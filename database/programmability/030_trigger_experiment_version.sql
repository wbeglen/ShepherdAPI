-- Issue #6 / Issue #4: guards on the ExperimentVersion row itself. Re-runnable (CREATE OR ALTER).
--   * LockedUTC may only be set from NULL; once set it can never be cleared or changed.          (50100)
--   * A locked version cannot be deleted.                                                         (50100)
--   * After lock only StatusID may change (Name, VersionLabel, EffectiveDateUTC, Description are frozen). (50100)
--   * Issue #4: once any Execution exists, Name, VersionLabel, EffectiveDateUTC and Description are
--     frozen too, locked or not. StatusID and LockedUTC (set by finalization) stay writable.       (50400)
-- A locked version reports 50100 before the freeze is considered. The old values come from the deleted
-- pseudo-table, read under the lock the UPDATE itself holds, which also serializes it against Execution creation.
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE OR ALTER TRIGGER dbo.trg_ExperimentVersion_Guard
ON dbo.ExperimentVersion
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM deleted) RETURN;

    IF NOT EXISTS (SELECT 1 FROM inserted)
    BEGIN
        IF EXISTS (SELECT 1 FROM deleted WHERE LockedUTC IS NOT NULL)
            THROW 50100, N'A locked ExperimentVersion cannot be deleted.', 1;
        RETURN;
    END

    IF EXISTS
    (
        SELECT 1
        FROM inserted AS i
        JOIN deleted AS d ON d.ExperimentVersionID = i.ExperimentVersionID
        WHERE d.LockedUTC IS NOT NULL
          AND (   i.LockedUTC IS NULL
               OR i.LockedUTC <> d.LockedUTC
               OR i.Name <> d.Name
               OR i.VersionLabel <> d.VersionLabel
               OR i.EffectiveDateUTC <> d.EffectiveDateUTC
               OR i.Description <> d.Description)
    )
        THROW 50100, N'A locked ExperimentVersion cannot be changed; only StatusID may change after lock, and LockedUTC can never be cleared or changed.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM inserted AS i
        JOIN deleted AS d ON d.ExperimentVersionID = i.ExperimentVersionID
        WHERE (   i.Name <> d.Name
               OR i.VersionLabel <> d.VersionLabel
               OR i.EffectiveDateUTC <> d.EffectiveDateUTC
               OR i.Description <> d.Description)
          AND dbo.fn_VersionHasExecutions(i.ExperimentVersionID) = 1
    )
        THROW 50400, N'The design of this ExperimentVersion is frozen because it has Executions; Name, VersionLabel, EffectiveDateUTC and Description can no longer change.', 1;
END
GO
