-- Issue #4: controlled-write procedures for the design tables (Issue #6 lock protocol).
-- Each procedure runs ONE transaction: take the owning ExperimentVersion's update lock first, then
--   1. "not found" is reported through an OUTPUT flag (the API maps it to its existing 404),
--   2. a locked version is rejected                                  -> THROW 50100 (version-locked),
--   3. a version that already has any Execution is rejected          -> THROW 50400 (design-frozen),
--   4. the write happens.
-- Holding the update lock across the check and the write serializes these procedures against
-- usp_CreateExecution (Issue #1): a design write either commits before the first Execution or is rejected.
-- The guard triggers (040) remain as the defence for any write that bypasses these procedures.
-- Issue #7: ScoreRule follows the same rules as Iteration and Parameter, with no exceptions: create, update
-- and physical delete, all only while the version is unlocked and has no Execution.
-- Re-runnable (CREATE OR ALTER).
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- Shared tail used by the writers below: rejects a locked or frozen version. Expects the lock to be held.
CREATE OR ALTER PROCEDURE dbo.usp_AssertDesignWritable
    @ExperimentVersionID bigint,
    @LockedUTC datetime2(3)
AS
BEGIN
    SET NOCOUNT ON;

    IF @LockedUTC IS NOT NULL
    BEGIN
        DECLARE @lockedMessage nvarchar(2048) =
            CONCAT(N'ExperimentVersion ', @ExperimentVersionID, N' is locked; its governed data is immutable through normal operations.');
        THROW 50100, @lockedMessage, 1;
    END

    DECLARE @versions dbo.VersionIdList;
    INSERT INTO @versions (ExperimentVersionID) VALUES (@ExperimentVersionID);
    EXEC dbo.usp_AssertDesignNotFrozen @versions;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_UpdateExperimentVersion
    @ExperimentVersionID bigint,
    @Name nvarchar(200),
    @VersionLabel nvarchar(50),
    @EffectiveDateUTC datetime2(3),
    @Description nvarchar(max),
    @Found bit OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Found = 0;
    BEGIN TRANSACTION;

    DECLARE @lockedUtc datetime2(3);
    EXEC dbo.usp_AcquireVersionLock @ExperimentVersionID, 0, @Found OUTPUT, @lockedUtc OUTPUT;
    IF @Found = 0
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    EXEC dbo.usp_AssertDesignWritable @ExperimentVersionID, @lockedUtc;

    UPDATE dbo.ExperimentVersion
    SET Name = @Name, VersionLabel = @VersionLabel, EffectiveDateUTC = @EffectiveDateUTC, Description = @Description
    WHERE ExperimentVersionID = @ExperimentVersionID;

    COMMIT TRANSACTION;
END
GO

-- -- Iteration ---------------------------------------------------------------------------------

CREATE OR ALTER PROCEDURE dbo.usp_CreateIteration
    @ExperimentVersionID bigint,
    @Sequence int,
    @IterationLabel nvarchar(100),
    @Prompt nvarchar(max),
    @Scenario nvarchar(100) = NULL,
    @Notes nvarchar(max) = NULL,
    @IterationID bigint OUTPUT,
    @VersionFound bit OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @IterationID = NULL;
    BEGIN TRANSACTION;

    DECLARE @lockedUtc datetime2(3);
    EXEC dbo.usp_AcquireVersionLock @ExperimentVersionID, 0, @VersionFound OUTPUT, @lockedUtc OUTPUT;
    IF @VersionFound = 0
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    EXEC dbo.usp_AssertDesignWritable @ExperimentVersionID, @lockedUtc;

    INSERT INTO dbo.Iteration (ExperimentVersionID, Sequence, IterationLabel, Prompt, Scenario, Notes)
    VALUES (@ExperimentVersionID, @Sequence, @IterationLabel, @Prompt, @Scenario, @Notes);

    SET @IterationID = SCOPE_IDENTITY();
    COMMIT TRANSACTION;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_UpdateIteration
    @IterationID bigint,
    @Sequence int,
    @IterationLabel nvarchar(100),
    @Prompt nvarchar(max),
    @Scenario nvarchar(100) = NULL,
    @Notes nvarchar(max) = NULL,
    @Found bit OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Found = 0;
    BEGIN TRANSACTION;

    -- An iteration never changes version (identity columns are immutable), so reading it before the lock is safe.
    DECLARE @versionId bigint = (SELECT ExperimentVersionID FROM dbo.Iteration WHERE IterationID = @IterationID);
    IF @versionId IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    DECLARE @versionFound bit, @lockedUtc datetime2(3);
    EXEC dbo.usp_AcquireVersionLock @versionId, 0, @versionFound OUTPUT, @lockedUtc OUTPUT;

    -- Deleting the iteration takes the same lock, so re-check now that we hold it.
    IF NOT EXISTS (SELECT 1 FROM dbo.Iteration WHERE IterationID = @IterationID)
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END
    SET @Found = 1;

    EXEC dbo.usp_AssertDesignWritable @versionId, @lockedUtc;

    UPDATE dbo.Iteration
    SET Sequence = @Sequence, IterationLabel = @IterationLabel, Prompt = @Prompt, Scenario = @Scenario, Notes = @Notes
    WHERE IterationID = @IterationID;

    COMMIT TRANSACTION;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_DeleteIteration
    @IterationID bigint,
    @Found bit OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Found = 0;
    BEGIN TRANSACTION;

    DECLARE @versionId bigint = (SELECT ExperimentVersionID FROM dbo.Iteration WHERE IterationID = @IterationID);
    IF @versionId IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    DECLARE @versionFound bit, @lockedUtc datetime2(3);
    EXEC dbo.usp_AcquireVersionLock @versionId, 0, @versionFound OUTPUT, @lockedUtc OUTPUT;

    IF NOT EXISTS (SELECT 1 FROM dbo.Iteration WHERE IterationID = @IterationID)
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END
    SET @Found = 1;

    EXEC dbo.usp_AssertDesignWritable @versionId, @lockedUtc;

    DELETE FROM dbo.Iteration WHERE IterationID = @IterationID;

    COMMIT TRANSACTION;
END
GO

-- -- Parameter ---------------------------------------------------------------------------------

CREATE OR ALTER PROCEDURE dbo.usp_CreateParameter
    @ExperimentVersionID bigint,
    @Name nvarchar(200),
    @Value nvarchar(400) = NULL,
    @Unit nvarchar(50) = NULL,
    @Notes nvarchar(max) = NULL,
    @ParameterID bigint OUTPUT,
    @VersionFound bit OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @ParameterID = NULL;
    BEGIN TRANSACTION;

    DECLARE @lockedUtc datetime2(3);
    EXEC dbo.usp_AcquireVersionLock @ExperimentVersionID, 0, @VersionFound OUTPUT, @lockedUtc OUTPUT;
    IF @VersionFound = 0
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    EXEC dbo.usp_AssertDesignWritable @ExperimentVersionID, @lockedUtc;

    INSERT INTO dbo.Parameter (ExperimentVersionID, Name, Value, Unit, Notes)
    VALUES (@ExperimentVersionID, @Name, @Value, @Unit, @Notes);

    SET @ParameterID = SCOPE_IDENTITY();
    COMMIT TRANSACTION;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_UpdateParameter
    @ParameterID bigint,
    @Name nvarchar(200),
    @Value nvarchar(400) = NULL,
    @Unit nvarchar(50) = NULL,
    @Notes nvarchar(max) = NULL,
    @Found bit OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Found = 0;
    BEGIN TRANSACTION;

    DECLARE @versionId bigint = (SELECT ExperimentVersionID FROM dbo.Parameter WHERE ParameterID = @ParameterID);
    IF @versionId IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    DECLARE @versionFound bit, @lockedUtc datetime2(3);
    EXEC dbo.usp_AcquireVersionLock @versionId, 0, @versionFound OUTPUT, @lockedUtc OUTPUT;

    IF NOT EXISTS (SELECT 1 FROM dbo.Parameter WHERE ParameterID = @ParameterID)
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END
    SET @Found = 1;

    EXEC dbo.usp_AssertDesignWritable @versionId, @lockedUtc;

    UPDATE dbo.Parameter
    SET Name = @Name, Value = @Value, Unit = @Unit, Notes = @Notes
    WHERE ParameterID = @ParameterID;

    COMMIT TRANSACTION;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_DeleteParameter
    @ParameterID bigint,
    @Found bit OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Found = 0;
    BEGIN TRANSACTION;

    DECLARE @versionId bigint = (SELECT ExperimentVersionID FROM dbo.Parameter WHERE ParameterID = @ParameterID);
    IF @versionId IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    DECLARE @versionFound bit, @lockedUtc datetime2(3);
    EXEC dbo.usp_AcquireVersionLock @versionId, 0, @versionFound OUTPUT, @lockedUtc OUTPUT;

    IF NOT EXISTS (SELECT 1 FROM dbo.Parameter WHERE ParameterID = @ParameterID)
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END
    SET @Found = 1;

    EXEC dbo.usp_AssertDesignWritable @versionId, @lockedUtc;

    DELETE FROM dbo.Parameter WHERE ParameterID = @ParameterID;

    COMMIT TRANSACTION;
END
GO

-- -- ScoreRule ---------------------------------------------------------------------------------
-- Issue #7: the soft-delete procedure is replaced by usp_DeleteScoreRule, so remove it where it was deployed.
DROP PROCEDURE IF EXISTS dbo.usp_SoftDeleteScoreRule;
GO

CREATE OR ALTER PROCEDURE dbo.usp_CreateScoreRule
    @ExperimentVersionID bigint,
    @PhaseID int,
    @ScoreCategoryID int,
    @WeightPercent decimal(5, 2),
    @MinScore int,
    @MaxScore int,
    @Active bit,
    @RuleNotes nvarchar(300) = NULL,
    @ScoreRuleID int OUTPUT,
    @VersionFound bit OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @ScoreRuleID = NULL;
    BEGIN TRANSACTION;

    DECLARE @lockedUtc datetime2(3);
    EXEC dbo.usp_AcquireVersionLock @ExperimentVersionID, 0, @VersionFound OUTPUT, @lockedUtc OUTPUT;
    IF @VersionFound = 0
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    EXEC dbo.usp_AssertDesignWritable @ExperimentVersionID, @lockedUtc;

    INSERT INTO dbo.ScoreRule (ExperimentVersionID, PhaseID, ScoreCategoryID, WeightPercent, MinScore, MaxScore, Active, RuleNotes)
    VALUES (@ExperimentVersionID, @PhaseID, @ScoreCategoryID, @WeightPercent, @MinScore, @MaxScore, @Active, @RuleNotes);

    SET @ScoreRuleID = SCOPE_IDENTITY();
    COMMIT TRANSACTION;
END
GO

-- Changes the non-key configuration only. ExperimentVersionID, PhaseID and ScoreCategoryID are the rule's identity
-- and ownership and never change (trg_ScoreRule_Guard, 50101).
CREATE OR ALTER PROCEDURE dbo.usp_UpdateScoreRule
    @ScoreRuleID int,
    @WeightPercent decimal(5, 2),
    @MinScore int,
    @MaxScore int,
    @Active bit,
    @RuleNotes nvarchar(300) = NULL,
    @Found bit OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Found = 0;
    BEGIN TRANSACTION;

    -- A rule never changes version (identity columns are immutable), so reading it before the lock is safe.
    DECLARE @versionId bigint = (SELECT ExperimentVersionID FROM dbo.ScoreRule WHERE ScoreRuleID = @ScoreRuleID);
    IF @versionId IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    DECLARE @versionFound bit, @lockedUtc datetime2(3);
    EXEC dbo.usp_AcquireVersionLock @versionId, 0, @versionFound OUTPUT, @lockedUtc OUTPUT;

    -- Deleting the rule takes the same lock, so re-check now that we hold it.
    IF NOT EXISTS (SELECT 1 FROM dbo.ScoreRule WHERE ScoreRuleID = @ScoreRuleID)
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END
    SET @Found = 1;

    EXEC dbo.usp_AssertDesignWritable @versionId, @lockedUtc;

    UPDATE dbo.ScoreRule
    SET WeightPercent = @WeightPercent, MinScore = @MinScore, MaxScore = @MaxScore, Active = @Active, RuleNotes = @RuleNotes
    WHERE ScoreRuleID = @ScoreRuleID;

    COMMIT TRANSACTION;
END
GO

-- Physically deletes the rule, which frees its (ExperimentVersionID, PhaseID, ScoreCategoryID) combination.
-- Only a version that is unlocked and has no Execution is writable, so no ScoreObservation or snapshot row can
-- reference the rule; the foreign keys from both tables refuse the delete regardless (547).
CREATE OR ALTER PROCEDURE dbo.usp_DeleteScoreRule
    @ScoreRuleID int,
    @Found bit OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Found = 0;
    BEGIN TRANSACTION;

    DECLARE @versionId bigint = (SELECT ExperimentVersionID FROM dbo.ScoreRule WHERE ScoreRuleID = @ScoreRuleID);
    IF @versionId IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    DECLARE @versionFound bit, @lockedUtc datetime2(3);
    EXEC dbo.usp_AcquireVersionLock @versionId, 0, @versionFound OUTPUT, @lockedUtc OUTPUT;

    IF NOT EXISTS (SELECT 1 FROM dbo.ScoreRule WHERE ScoreRuleID = @ScoreRuleID)
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END
    SET @Found = 1;

    EXEC dbo.usp_AssertDesignWritable @versionId, @lockedUtc;

    DELETE FROM dbo.ScoreRule WHERE ScoreRuleID = @ScoreRuleID;

    COMMIT TRANSACTION;
END
GO
