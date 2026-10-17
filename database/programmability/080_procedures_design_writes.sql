-- Issue #4: controlled-write procedures for the design tables (Issue #6 lock protocol).
-- Each procedure runs ONE transaction: take the owning ExperimentVersion's update lock first, then
--   1. "not found" is reported through an OUTPUT flag (the API maps it to its existing 404),
--   2. a locked version is rejected                                  -> THROW 50100 (version-locked),
--   3. a version that already has any Execution is rejected          -> THROW 50400 (design-frozen),
--   4. the write happens.
-- Holding the update lock across the check and the write serializes these procedures against
-- usp_CreateExecution (Issue #1): a design write either commits before the first Execution or is rejected.
-- The guard triggers (040) remain as the defence for any write that bypasses these procedures.
-- The ScoreRule soft-delete is the one narrow exception: see usp_SoftDeleteScoreRule.
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
-- ScoreRule is add/delete-only: there is no update procedure.

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

    -- RowState is left to its database default ('A').
    INSERT INTO dbo.ScoreRule (ExperimentVersionID, PhaseID, ScoreCategoryID, WeightPercent, MinScore, MaxScore, Active, RuleNotes)
    VALUES (@ExperimentVersionID, @PhaseID, @ScoreCategoryID, @WeightPercent, @MinScore, @MaxScore, @Active, @RuleNotes);

    SET @ScoreRuleID = SCOPE_IDENTITY();
    COMMIT TRANSACTION;
END
GO

-- The dedicated ScoreRule soft-delete: the only write permitted to touch a ScoreRule of a locked version,
-- and it changes nothing but RowState ('A' -> 'D'). Precedence, after the parent-version lock is held:
--   1. lifecycle eligibility: a LIVE version (has an Execution and LockedUTC IS NULL) -> THROW 50400
--      (design-frozen); a version with no Execution, or a locked one, is eligible
--   2. RowState: 'A' -> soft-delete; 'D' -> THROW 50401 (already deleted); anything else -> THROW 50402.
CREATE OR ALTER PROCEDURE dbo.usp_SoftDeleteScoreRule
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

    -- ScoreRules are never physically deleted, but re-read the row under the lock for its current RowState.
    DECLARE @rowState char(1) = (SELECT RowState FROM dbo.ScoreRule WHERE ScoreRuleID = @ScoreRuleID);
    IF @rowState IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END
    SET @Found = 1;

    IF @lockedUtc IS NULL AND dbo.fn_VersionHasExecutions(@versionId) = 1
    BEGIN
        DECLARE @liveMessage nvarchar(2048) = CONCAT(
            N'ExperimentVersion ', @versionId,
            N' is live (it has Executions and is not locked); its ScoreRules cannot be deleted until it is finalized.');
        THROW 50400, @liveMessage, 1;
    END

    IF @rowState = 'D'
    BEGIN
        DECLARE @deletedMessage nvarchar(2048) = CONCAT(N'ScoreRule ', @ScoreRuleID, N' is already deleted.');
        THROW 50401, @deletedMessage, 1;
    END

    IF @rowState <> 'A'
    BEGIN
        DECLARE @invalidMessage nvarchar(2048) = CONCAT(N'ScoreRule ', @ScoreRuleID, N' has an invalid RowState ''', @rowState, N'''; only ''A'' can be deleted.');
        THROW 50402, @invalidMessage, 1;
    END

    UPDATE dbo.ScoreRule SET RowState = 'D' WHERE ScoreRuleID = @ScoreRuleID AND RowState = 'A';

    COMMIT TRANSACTION;
END
GO
