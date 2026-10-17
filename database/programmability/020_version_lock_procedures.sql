-- Issue #6: the parent-version lock protocol. The ExperimentVersion row is locked before any child
-- row, and the lock is held to the end of the caller's transaction.
--   * Controlled writes (Execution create, ScoreObservation create/update, design writes) take an
--     UPDATE lock. Two update locks conflict, so controlled writers to one version serialize.
--   * Finalization takes an EXCLUSIVE lock: it waits for in-flight writers and blocks new ones.
-- Explicit lock hints are used so the behavior does not depend on READ_COMMITTED_SNAPSHOT.
-- Re-runnable (CREATE OR ALTER).
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- Locks every listed version in ascending id order, so concurrent multi-version statements
-- acquire locks in the same order and cannot deadlock each other.
CREATE OR ALTER PROCEDURE dbo.usp_LockVersions
    @Versions dbo.VersionIdList READONLY,
    @Exclusive bit = 0
AS
BEGIN
    SET NOCOUNT ON;

    IF @@TRANCOUNT = 0
        THROW 50000, N'usp_LockVersions must be called inside a transaction so the lock is held.', 1;

    DECLARE @id bigint = (SELECT MIN(ExperimentVersionID) FROM @Versions);
    DECLARE @held bigint;

    WHILE @id IS NOT NULL
    BEGIN
        IF @Exclusive = 1
            SELECT @held = ExperimentVersionID FROM dbo.ExperimentVersion WITH (XLOCK, HOLDLOCK, ROWLOCK)
            WHERE ExperimentVersionID = @id;
        ELSE
            SELECT @held = ExperimentVersionID FROM dbo.ExperimentVersion WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
            WHERE ExperimentVersionID = @id;

        SET @id = (SELECT MIN(ExperimentVersionID) FROM @Versions WHERE ExperimentVersionID > @id);
    END
END
GO

-- Locks the versions (update lock) and rejects the statement if any of them is locked
-- (error 50100, version-locked). Used by the post-lock guard triggers.
CREATE OR ALTER PROCEDURE dbo.usp_AssertVersionsUnlocked
    @Versions dbo.VersionIdList READONLY
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.usp_LockVersions @Versions = @Versions;

    DECLARE @locked nvarchar(max) =
    (
        SELECT STRING_AGG(CAST(ev.ExperimentVersionID AS nvarchar(20)), N', ') WITHIN GROUP (ORDER BY ev.ExperimentVersionID)
        FROM dbo.ExperimentVersion AS ev
        JOIN @Versions AS v ON v.ExperimentVersionID = ev.ExperimentVersionID
        WHERE ev.LockedUTC IS NOT NULL
    );

    IF @locked IS NOT NULL
    BEGIN
        DECLARE @message nvarchar(2048) =
            CONCAT(N'ExperimentVersion ', @locked, N' is locked; its governed data is immutable through normal operations.');
        THROW 50100, @message, 1;
    END
END
GO

-- The primitive controlled-write procedures call first, inside their own transaction:
--   SET XACT_ABORT ON; BEGIN TRAN; EXEC dbo.usp_AcquireVersionLock ...; <write>; COMMIT;
-- Reports whether the version exists and its LockedUTC; the caller decides what to do with that.
-- With XACT_ABORT ON, a lock-wait timeout (1222) or deadlock (1205) rolls the whole transaction back.
CREATE OR ALTER PROCEDURE dbo.usp_AcquireVersionLock
    @ExperimentVersionID bigint,
    @Exclusive bit = 0,
    @Found bit OUTPUT,
    @LockedUTC datetime2(3) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF @@TRANCOUNT = 0
        THROW 50000, N'usp_AcquireVersionLock must be called inside a transaction so the lock is held.', 1;

    SET @Found = 0;
    SET @LockedUTC = NULL;

    IF @Exclusive = 1
        SELECT @Found = 1, @LockedUTC = LockedUTC
        FROM dbo.ExperimentVersion WITH (XLOCK, HOLDLOCK, ROWLOCK)
        WHERE ExperimentVersionID = @ExperimentVersionID;
    ELSE
        SELECT @Found = 1, @LockedUTC = LockedUTC
        FROM dbo.ExperimentVersion WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
        WHERE ExperimentVersionID = @ExperimentVersionID;
END
GO
