-- Issue #2: guard on dbo.ScoreObservationSnapshot. Re-runnable (CREATE OR ALTER).
--   * Rows are created only by dbo.usp_FinalizeExperimentVersion, which sets the session-context key
--     'ShepherdAPI.FinalizingVersionID' to the version it is finalizing around its inserts. An insert whose rows
--     do not all belong to that version is rejected with 50508 (snapshot-insert-not-permitted). This stops
--     out-of-procedure writes by accident; strong protection depends on the deferred permissions work, because
--     any session able to set session context can set the key.
--   * An insert for a locked version is rejected with 50100 (version-locked).
--   * Updates and deletes are always rejected with 50100: a snapshot is immutable once written.
-- The token is checked before the lock, so an insert that is both out of procedure and for a locked version
-- reports 50508.
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE OR ALTER TRIGGER dbo.trg_ScoreObservationSnapshot_Guard
ON dbo.ScoreObservationSnapshot
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;

    IF EXISTS (SELECT 1 FROM deleted)
        THROW 50100, N'ScoreObservationSnapshot rows are immutable: they cannot be updated or deleted.', 1;

    DECLARE @token bigint = TRY_CONVERT(bigint, SESSION_CONTEXT(N'ShepherdAPI.FinalizingVersionID'));

    IF @token IS NULL OR EXISTS (SELECT 1 FROM inserted WHERE ExperimentVersionID <> @token)
        THROW 50508, N'ScoreObservationSnapshot rows can only be created by ExperimentVersion finalization (dbo.usp_FinalizeExperimentVersion).', 1;

    DECLARE @Owners dbo.VersionIdList;
    INSERT INTO @Owners (ExperimentVersionID)
        SELECT DISTINCT ExperimentVersionID FROM inserted;

    EXEC dbo.usp_AssertVersionsUnlocked @Owners;
END
GO
