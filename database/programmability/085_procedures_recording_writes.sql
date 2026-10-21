-- Issue #8: controlled-write procedures for the remaining recording writes the API makes: Execution run-facts,
-- Artifact creation and evidence-link addition and removal. They follow the Issue #6 lock protocol: each procedure
-- owns ONE atomic transaction, is called standalone (Issue #10), takes the owning ExperimentVersion's update lock
-- FIRST, and only then reads the state it validates and writes. Parent-first ordering means they can no longer
-- deadlock against finalization (which holds the exclusive lock) the way a plain DML statement followed by its AFTER
-- trigger could.
--
-- Order of checks (after the lock is held):
--   1. target not found -> @Found / @ExecutionFound = 0 (the API maps this to its existing 404)
--   2. owning version locked                                  -> THROW 50100 (version-locked)
--   3. usp_UpdateExecutionRunFacts only: Execution has EndUTC -> THROW 50201 (execution-run-facts-frozen)
--   4. the write
-- The same-Execution rule for evidence stays in the service: an Artifact's and an observation's Execution never
-- change (identity guards), so it cannot race. Re-runnable (CREATE OR ALTER).
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- Replaces the full set of run-facts. They remain mutable only until EndUTC is set (docs/ARCHITECTURE.md §5.1); the
-- check is made here, under the lock, so a concurrent update that sets EndUTC cannot be overwritten.
CREATE OR ALTER PROCEDURE dbo.usp_UpdateExecutionRunFacts
    @ExecutionID bigint,
    @ModelUsed nvarchar(200) = NULL,
    @InterfaceUsed nvarchar(100) = NULL,
    @InterventionCount int,
    @OutcomeNotes nvarchar(max) = NULL,
    @EndUTC datetime2(3) = NULL,
    @Found bit OUTPUT
AS
BEGIN
    -- Issue #10: controlled-write procedures own their transaction and are called standalone. This must stay the first
    -- statement, before SET XACT_ABORT ON: with XACT_ABORT ON the THROW would doom the caller's transaction.
    IF @@TRANCOUNT > 0
        THROW 50000, N'dbo.usp_UpdateExecutionRunFacts must be called outside a transaction; it owns the transaction it begins.', 1;

    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Found = 0;
    BEGIN TRANSACTION;

    -- An Execution never changes version (identity columns are immutable), so reading its owner before the lock is safe.
    DECLARE @versionId bigint = (SELECT ExperimentVersionID FROM dbo.vOwnerExecution WHERE ExecutionID = @ExecutionID);
    IF @versionId IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    DECLARE @versionFound bit, @lockedUtc datetime2(3);
    EXEC dbo.usp_AcquireVersionLock @versionId, 0, @versionFound OUTPUT, @lockedUtc OUTPUT;

    -- Re-read under the lock: this is the state the restriction is evaluated against.
    DECLARE @currentEnd datetime2(3), @exists bit = 0;
    SELECT @exists = 1, @currentEnd = EndUTC FROM dbo.Execution WHERE ExecutionID = @ExecutionID;
    IF @exists = 0
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END
    SET @Found = 1;

    IF @lockedUtc IS NOT NULL
    BEGIN
        DECLARE @lockedMessage nvarchar(2048) =
            CONCAT(N'ExperimentVersion ', @versionId, N' is locked; Execution ', @ExecutionID, N' cannot be changed.');
        THROW 50100, @lockedMessage, 1;
    END

    IF @currentEnd IS NOT NULL
    BEGIN
        DECLARE @endedMessage nvarchar(2048) =
            CONCAT(N'Execution ', @ExecutionID, N' completed at ', CONVERT(nvarchar(30), @currentEnd, 126),
                   N'; its run-facts are immutable through normal ShepherdAPI operations.');
        THROW 50201, @endedMessage, 1;
    END

    UPDATE dbo.Execution
    SET ModelUsed = @ModelUsed, InterfaceUsed = @InterfaceUsed, InterventionCount = @InterventionCount,
        OutcomeNotes = @OutcomeNotes, EndUTC = @EndUTC
    WHERE ExecutionID = @ExecutionID;

    COMMIT TRANSACTION;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_CreateArtifact
    @ExecutionID bigint,
    @ArtifactTypeID int,
    @OriginalName nvarchar(200),
    @OriginalURI nvarchar(2048),
    @StoredURI nvarchar(2048),
    @MimeType nvarchar(100),
    @ByteLength bigint,
    @SHA256 varchar(64),
    @ReceivedUTC datetime2(3),
    @StoredUTC datetime2(3) = NULL,
    @ArtifactID bigint OUTPUT,
    @ExecutionFound bit OUTPUT
AS
BEGIN
    -- Issue #10: controlled-write procedures own their transaction and are called standalone. This must stay the first
    -- statement, before SET XACT_ABORT ON: with XACT_ABORT ON the THROW would doom the caller's transaction.
    IF @@TRANCOUNT > 0
        THROW 50000, N'dbo.usp_CreateArtifact must be called outside a transaction; it owns the transaction it begins.', 1;

    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @ArtifactID = NULL;
    SET @ExecutionFound = 0;
    BEGIN TRANSACTION;

    DECLARE @versionId bigint = (SELECT ExperimentVersionID FROM dbo.vOwnerExecution WHERE ExecutionID = @ExecutionID);
    IF @versionId IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    DECLARE @versionFound bit, @lockedUtc datetime2(3);
    EXEC dbo.usp_AcquireVersionLock @versionId, 0, @versionFound OUTPUT, @lockedUtc OUTPUT;

    IF NOT EXISTS (SELECT 1 FROM dbo.Execution WHERE ExecutionID = @ExecutionID)
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END
    SET @ExecutionFound = 1;

    IF @lockedUtc IS NOT NULL
    BEGIN
        DECLARE @lockedMessage nvarchar(2048) =
            CONCAT(N'ExperimentVersion ', @versionId, N' is locked; Artifacts cannot be added to Execution ', @ExecutionID, N'.');
        THROW 50100, @lockedMessage, 1;
    END

    INSERT INTO dbo.Artifact
        (ExecutionID, ArtifactTypeID, OriginalName, OriginalURI, StoredURI, MimeType, ByteLength, SHA256, ReceivedUTC, StoredUTC)
    VALUES
        (@ExecutionID, @ArtifactTypeID, @OriginalName, @OriginalURI, @StoredURI, @MimeType, @ByteLength, @SHA256, @ReceivedUTC, @StoredUTC);

    SET @ArtifactID = SCOPE_IDENTITY();
    COMMIT TRANSACTION;
END
GO

-- An evidence link joins an observation to an artifact, so two versions are involved (normally the same one; the
-- service requires the same Execution, the database does not). Both are locked, in ascending id order, through
-- usp_LockVersions, so a link that spans versions cannot take its second lock out of order inside the trigger.
CREATE OR ALTER PROCEDURE dbo.usp_AddEvidence
    @ScoreObservationID bigint,
    @ArtifactID bigint,
    @EvidenceNotes nvarchar(500) = NULL,
    @Found bit OUTPUT
AS
BEGIN
    -- Issue #10: controlled-write procedures own their transaction and are called standalone. This must stay the first
    -- statement, before SET XACT_ABORT ON: with XACT_ABORT ON the THROW would doom the caller's transaction.
    IF @@TRANCOUNT > 0
        THROW 50000, N'dbo.usp_AddEvidence must be called outside a transaction; it owns the transaction it begins.', 1;

    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Found = 0;
    BEGIN TRANSACTION;

    DECLARE @observationVersion bigint = (SELECT ExperimentVersionID FROM dbo.vOwnerScoreObservation WHERE ScoreObservationID = @ScoreObservationID);
    DECLARE @artifactVersion bigint = (SELECT ExperimentVersionID FROM dbo.vOwnerArtifact WHERE ArtifactID = @ArtifactID);
    IF @observationVersion IS NULL OR @artifactVersion IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    DECLARE @Versions dbo.VersionIdList;
    INSERT INTO @Versions (ExperimentVersionID) VALUES (@observationVersion);
    IF @artifactVersion <> @observationVersion
        INSERT INTO @Versions (ExperimentVersionID) VALUES (@artifactVersion);

    EXEC dbo.usp_LockVersions @Versions = @Versions;

    IF NOT EXISTS (SELECT 1 FROM dbo.ScoreObservation WHERE ScoreObservationID = @ScoreObservationID)
       OR NOT EXISTS (SELECT 1 FROM dbo.Artifact WHERE ArtifactID = @ArtifactID)
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END
    SET @Found = 1;

    -- Throws 50100 if either version is locked.
    EXEC dbo.usp_AssertVersionsUnlocked @Versions = @Versions;

    INSERT INTO dbo.ScoreObservationEvidence (ScoreObservationID, ArtifactID, EvidenceNotes)
    VALUES (@ScoreObservationID, @ArtifactID, @EvidenceNotes);

    COMMIT TRANSACTION;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_RemoveEvidence
    @ScoreObservationID bigint,
    @ArtifactID bigint,
    @Found bit OUTPUT
AS
BEGIN
    -- Issue #10: controlled-write procedures own their transaction and are called standalone. This must stay the first
    -- statement, before SET XACT_ABORT ON: with XACT_ABORT ON the THROW would doom the caller's transaction.
    IF @@TRANCOUNT > 0
        THROW 50000, N'dbo.usp_RemoveEvidence must be called outside a transaction; it owns the transaction it begins.', 1;

    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Found = 0;
    BEGIN TRANSACTION;

    DECLARE @observationVersion bigint, @artifactVersion bigint;
    SELECT @observationVersion = ObservationVersionID, @artifactVersion = ArtifactVersionID
    FROM dbo.vOwnerScoreObservationEvidence
    WHERE ScoreObservationID = @ScoreObservationID AND ArtifactID = @ArtifactID;

    IF @observationVersion IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    DECLARE @Versions dbo.VersionIdList;
    INSERT INTO @Versions (ExperimentVersionID) VALUES (@observationVersion);
    IF @artifactVersion <> @observationVersion
        INSERT INTO @Versions (ExperimentVersionID) VALUES (@artifactVersion);

    EXEC dbo.usp_LockVersions @Versions = @Versions;

    -- Re-check under the lock: another remover may have taken the link in the meantime.
    IF NOT EXISTS (SELECT 1 FROM dbo.ScoreObservationEvidence WHERE ScoreObservationID = @ScoreObservationID AND ArtifactID = @ArtifactID)
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END
    SET @Found = 1;

    EXEC dbo.usp_AssertVersionsUnlocked @Versions = @Versions;

    DELETE FROM dbo.ScoreObservationEvidence WHERE ScoreObservationID = @ScoreObservationID AND ArtifactID = @ArtifactID;

    COMMIT TRANSACTION;
END
GO
