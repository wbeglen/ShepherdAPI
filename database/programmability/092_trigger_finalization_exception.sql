-- V11: guard on dbo.FinalizationException (upgrade/002). Re-runnable (CREATE OR ALTER). The same rules as the snapshot
-- guard (090):
--   * Rows are created only by dbo.usp_FinalizeExperimentVersion, which sets the session-context key
--     'ShepherdAPI.FinalizingVersionID' to the version it is finalizing around its inserts. An insert whose rows do not
--     all belong to that version, or whose Execution does not belong to the row's ExperimentVersionID, is rejected with
--     50511 (finalization-exception-insert-not-permitted). This stops out-of-procedure writes by accident; strong
--     protection depends on the deferred permissions work (Issue #11).
--   * An insert for a locked version is rejected with 50100 (version-locked).
--   * Updates and deletes are always rejected with 50100: an accepted exception is immutable once written.
-- The token is checked before the lock, so an insert that is both out of procedure and for a locked version reports 50511.
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE OR ALTER TRIGGER dbo.trg_FinalizationException_Guard
ON dbo.FinalizationException
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;

    IF EXISTS (SELECT 1 FROM deleted)
        THROW 50100, N'FinalizationException rows are immutable: they cannot be updated or deleted.', 1;

    DECLARE @token bigint = TRY_CONVERT(bigint, SESSION_CONTEXT(N'ShepherdAPI.FinalizingVersionID'));

    IF @token IS NULL
       OR EXISTS (SELECT 1 FROM inserted WHERE ExperimentVersionID <> @token)
       OR EXISTS (SELECT 1 FROM inserted AS x
                  LEFT JOIN dbo.vOwnerExecution AS o ON o.ExecutionID = x.ExecutionID
                  WHERE o.ExperimentVersionID IS NULL OR o.ExperimentVersionID <> x.ExperimentVersionID)
        THROW 50511, N'FinalizationException rows can only be created by ExperimentVersion finalization (dbo.usp_FinalizeExperimentVersion), for an Execution of the version being finalized.', 1;

    DECLARE @Owners dbo.VersionIdList;
    INSERT INTO @Owners (ExperimentVersionID)
        SELECT DISTINCT ExperimentVersionID FROM inserted;

    EXEC dbo.usp_AssertVersionsUnlocked @Owners;
END
GO
