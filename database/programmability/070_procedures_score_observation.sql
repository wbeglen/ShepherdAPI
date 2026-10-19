-- Issue #3: ScoreObservation write validation, as controlled-write procedures (Issue #6 protocol).
-- Each procedure takes the owning version's update lock and validates and writes in ONE transaction,
-- so a concurrent rule change or finalization cannot slip in between the check and the write.
--
-- usp_CreateScoreObservation, order of checks:
--   1. Execution not found                         -> @ExecutionFound = 0 (the API maps this to its existing 404).
--   2. ExperimentVersion locked                    -> THROW 50100 (version-locked).
--   3. Rule belongs to another version or phase    -> THROW 50300 (version-phase-mismatch).
--   4. ObservedScore outside [MinScore, MaxScore]  -> THROW 50302 (out-of-range-score), inclusive bounds.
--   (50301, unavailable-rule, is retired with ScoreRule.RowState in Issue #7: every existing rule is usable.)
--   A rule or rater that does not exist is left to the foreign key, which reports it as it always did (547).
--   A duplicate (ExecutionID, ScoreRuleID, RaterID) is rejected by the existing unique constraint (2627).
-- usp_UpdateScoreObservationContent: content only (ObservedScore, ObservedUTC, ObservationNotes); needs an
--   unlocked version and an ObservedScore within the observation's rule range. The identity columns are
--   immutable (Issue #6 trigger). ScoreRule.Active is never consulted. Re-runnable (CREATE OR ALTER).
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE OR ALTER PROCEDURE dbo.usp_CreateScoreObservation
    @ExecutionID bigint,
    @ScoreRuleID int,
    @RaterID int,
    @ObservedScore decimal(5, 2),
    @ObservedUTC datetime2(3) = NULL,
    @ObservationNotes nvarchar(max) = NULL,
    @ScoreObservationID bigint OUTPUT,
    @ExecutionFound bit OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @ScoreObservationID = NULL;
    SET @ExecutionFound = 0;

    BEGIN TRANSACTION;

    -- An execution never changes version or phase (identity columns are immutable), so reading it before the lock is safe.
    DECLARE @VersionID bigint, @ExecutionPhaseID int;
    SELECT @VersionID = i.ExperimentVersionID, @ExecutionPhaseID = e.PhaseID
    FROM dbo.Execution AS e
    JOIN dbo.Iteration AS i ON i.IterationID = e.IterationID
    WHERE e.ExecutionID = @ExecutionID;

    IF @VersionID IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    DECLARE @found bit, @lockedUtc datetime2(3);
    EXEC dbo.usp_AcquireVersionLock @VersionID, 0, @found OUTPUT, @lockedUtc OUTPUT;

    -- Re-check now that the lock is held.
    IF NOT EXISTS (SELECT 1 FROM dbo.Execution WHERE ExecutionID = @ExecutionID)
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END
    SET @ExecutionFound = 1;

    IF @lockedUtc IS NOT NULL
    BEGIN
        DECLARE @lockedMessage nvarchar(2048) =
            CONCAT(N'ExperimentVersion ', @VersionID, N' is locked; ScoreObservations cannot be created.');
        THROW 50100, @lockedMessage, 1;
    END

    DECLARE @RuleVersionID bigint, @RulePhaseID int, @MinScore int, @MaxScore int;
    SELECT @RuleVersionID = ExperimentVersionID, @RulePhaseID = PhaseID,
           @MinScore = MinScore, @MaxScore = MaxScore
    FROM dbo.ScoreRule
    WHERE ScoreRuleID = @ScoreRuleID;

    IF @RuleVersionID IS NOT NULL
    BEGIN
        IF @RuleVersionID <> @VersionID OR @RulePhaseID <> @ExecutionPhaseID
        BEGIN
            DECLARE @mismatchMessage nvarchar(2048) = CONCAT(
                N'ScoreRule ', @ScoreRuleID, N' (version ', @RuleVersionID, N', phase ', @RulePhaseID,
                N') does not match Execution ', @ExecutionID, N' (version ', @VersionID, N', phase ', @ExecutionPhaseID, N').');
            THROW 50300, @mismatchMessage, 1;
        END

        IF @ObservedScore < @MinScore OR @ObservedScore > @MaxScore
        BEGIN
            DECLARE @rangeMessage nvarchar(2048) = CONCAT(
                N'ObservedScore ', @ObservedScore, N' is outside the range ', @MinScore, N' to ', @MaxScore,
                N' of ScoreRule ', @ScoreRuleID, N'.');
            THROW 50302, @rangeMessage, 1;
        END
    END

    INSERT INTO dbo.ScoreObservation (ExecutionID, ScoreRuleID, RaterID, ObservedScore, ObservedUTC, ObservationNotes)
    VALUES (@ExecutionID, @ScoreRuleID, @RaterID, @ObservedScore, @ObservedUTC, @ObservationNotes);

    SET @ScoreObservationID = SCOPE_IDENTITY();

    COMMIT TRANSACTION;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_UpdateScoreObservationContent
    @ScoreObservationID bigint,
    @ObservedScore decimal(5, 2),
    @ObservedUTC datetime2(3) = NULL,
    @ObservationNotes nvarchar(max) = NULL,
    @Found bit OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Found = 0;

    BEGIN TRANSACTION;

    DECLARE @VersionID bigint =
        (SELECT ExperimentVersionID FROM dbo.vOwnerScoreObservation WHERE ScoreObservationID = @ScoreObservationID);

    IF @VersionID IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    DECLARE @found2 bit, @lockedUtc datetime2(3);
    EXEC dbo.usp_AcquireVersionLock @VersionID, 0, @found2 OUTPUT, @lockedUtc OUTPUT;

    DECLARE @ScoreRuleID int, @MinScore int, @MaxScore int;
    SELECT @ScoreRuleID = so.ScoreRuleID, @MinScore = r.MinScore, @MaxScore = r.MaxScore
    FROM dbo.ScoreObservation AS so
    JOIN dbo.ScoreRule AS r ON r.ScoreRuleID = so.ScoreRuleID
    WHERE so.ScoreObservationID = @ScoreObservationID;

    IF @ScoreRuleID IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END
    SET @Found = 1;

    IF @lockedUtc IS NOT NULL
    BEGIN
        DECLARE @lockedMessage nvarchar(2048) =
            CONCAT(N'ExperimentVersion ', @VersionID, N' is locked; ScoreObservations cannot be changed.');
        THROW 50100, @lockedMessage, 1;
    END

    IF @ObservedScore < @MinScore OR @ObservedScore > @MaxScore
    BEGIN
        DECLARE @rangeMessage nvarchar(2048) = CONCAT(
            N'ObservedScore ', @ObservedScore, N' is outside the range ', @MinScore, N' to ', @MaxScore,
            N' of ScoreRule ', @ScoreRuleID, N'.');
        THROW 50302, @rangeMessage, 1;
    END

    UPDATE dbo.ScoreObservation
    SET ObservedScore = @ObservedScore, ObservedUTC = @ObservedUTC, ObservationNotes = @ObservationNotes
    WHERE ScoreObservationID = @ScoreObservationID;

    COMMIT TRANSACTION;
END
GO
