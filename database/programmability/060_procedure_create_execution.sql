-- Issue #1: the Execution insert guard, as a controlled-write procedure (Issue #6 protocol).
-- Validation and insert happen in ONE transaction under the owning version's update lock, so a
-- concurrent rule change or finalization cannot slip in between the check and the insert.
--
-- Order of checks:
--   1. Iteration not found      -> returns @IterationFound = 0 (the API maps this to its existing 404).
--   2. ExperimentVersion locked -> THROW 50100 (version-locked).
--   3. The phase's ScoreRules must be non-empty and total exactly 100.00
--      -> THROW 50200 (invalid-rule-configuration).
-- Every existing rule is usable (Issue #7); ScoreRule.Active is deliberately ignored. Rules of other
-- versions or other phases never contribute. Re-runnable (CREATE OR ALTER).
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE OR ALTER PROCEDURE dbo.usp_CreateExecution
    @IterationID bigint,
    @AgentID int,
    @PhaseID int,
    @StatusID int,
    @StartUTC datetime2(3),
    @EndUTC datetime2(3) = NULL,
    @ModelUsed nvarchar(200) = NULL,
    @InterfaceUsed nvarchar(100) = NULL,
    @InterventionCount int,
    @OutcomeNotes nvarchar(max) = NULL,
    @AttemptNumber int,
    @ExecutionID bigint OUTPUT,
    @IterationFound bit OUTPUT
AS
BEGIN
    -- Issue #10: controlled-write procedures own their transaction and are called standalone. This must stay the first
    -- statement, before SET XACT_ABORT ON: with XACT_ABORT ON the THROW would doom the caller's transaction.
    IF @@TRANCOUNT > 0
        THROW 50000, N'dbo.usp_CreateExecution must be called outside a transaction; it owns the transaction it begins.', 1;

    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @ExecutionID = NULL;
    SET @IterationFound = 0;

    BEGIN TRANSACTION;

    -- An iteration never changes version (identity columns are immutable), so reading it before the lock is safe.
    DECLARE @VersionID bigint = (SELECT ExperimentVersionID FROM dbo.Iteration WHERE IterationID = @IterationID);
    IF @VersionID IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    DECLARE @found bit, @lockedUtc datetime2(3);
    EXEC dbo.usp_AcquireVersionLock @VersionID, 0, @found OUTPUT, @lockedUtc OUTPUT;

    -- Deleting an iteration takes the same lock, so re-check now that we hold it.
    IF NOT EXISTS (SELECT 1 FROM dbo.Iteration WHERE IterationID = @IterationID)
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END
    SET @IterationFound = 1;

    IF @lockedUtc IS NOT NULL
    BEGIN
        DECLARE @lockedMessage nvarchar(2048) =
            CONCAT(N'ExperimentVersion ', @VersionID, N' is locked; Executions cannot be created.');
        THROW 50100, @lockedMessage, 1;
    END

    DECLARE @ruleCount int, @total decimal(38, 2);
    SELECT @ruleCount = COUNT(*), @total = SUM(WeightPercent)
    FROM dbo.ScoreRule
    WHERE ExperimentVersionID = @VersionID AND PhaseID = @PhaseID;

    IF @ruleCount = 0 OR @total <> 100.00
    BEGIN
        DECLARE @ruleMessage nvarchar(2048) = CONCAT(
            N'ExperimentVersion ', @VersionID, N' phase ', @PhaseID, N' has ', @ruleCount,
            N' ScoreRule(s) totaling ', ISNULL(@total, 0), N'; exactly 100.00 is required to create an Execution.');
        THROW 50200, @ruleMessage, 1;
    END

    INSERT INTO dbo.Execution
        (IterationID, AgentID, PhaseID, StatusID, StartUTC, EndUTC, ModelUsed, InterfaceUsed,
         InterventionCount, OutcomeNotes, AttemptNumber)
    VALUES
        (@IterationID, @AgentID, @PhaseID, @StatusID, @StartUTC, @EndUTC, @ModelUsed, @InterfaceUsed,
         @InterventionCount, @OutcomeNotes, @AttemptNumber);

    SET @ExecutionID = SCOPE_IDENTITY();

    COMMIT TRANSACTION;
END
GO
