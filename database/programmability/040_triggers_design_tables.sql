-- Issue #6 / Issue #4: guards on the design tables (Iteration, Parameter, ScoreRule). Re-runnable (CREATE OR ALTER).
-- Each trigger, in this order:
--   1. Rejects an UPDATE that changes an identity/ownership column (50101), locked or not.
--   2. Resolves the old and new owning versions set-wise, locks them in ascending id order under an
--      update lock, and rejects the whole statement if any is locked (50100).
--   3. Issue #4: rejects the whole statement if any owning version already has an Execution (50400,
--      design-frozen). Checked after the lock, so it is serialized against Execution creation.
-- ScoreRule has one carve-out (Issue #4 soft-delete): setting RowState from 'A' to 'D' and changing
-- nothing else is allowed on a version that is NOT live. A version is live when it has an Execution and is
-- unlocked; for a locked version the A->D change is the only admitted write, and for an unlocked version the
-- freeze in step 3 applies like any other write.
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE OR ALTER TRIGGER dbo.trg_Iteration_Guard
ON dbo.Iteration
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;

    IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
       AND EXISTS (SELECT IterationID, ExperimentVersionID FROM inserted
                   EXCEPT
                   SELECT IterationID, ExperimentVersionID FROM deleted)
        THROW 50101, N'Iteration.ExperimentVersionID cannot be changed.', 1;

    DECLARE @Owners dbo.VersionIdList;
    INSERT INTO @Owners (ExperimentVersionID)
        SELECT ExperimentVersionID FROM inserted
        UNION
        SELECT ExperimentVersionID FROM deleted;

    EXEC dbo.usp_AssertVersionsUnlocked @Owners;
    EXEC dbo.usp_AssertDesignNotFrozen @Owners;
END
GO

CREATE OR ALTER TRIGGER dbo.trg_Parameter_Guard
ON dbo.Parameter
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;

    IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
       AND EXISTS (SELECT ParameterID, ExperimentVersionID FROM inserted
                   EXCEPT
                   SELECT ParameterID, ExperimentVersionID FROM deleted)
        THROW 50101, N'Parameter.ExperimentVersionID cannot be changed.', 1;

    DECLARE @Owners dbo.VersionIdList;
    INSERT INTO @Owners (ExperimentVersionID)
        SELECT ExperimentVersionID FROM inserted
        UNION
        SELECT ExperimentVersionID FROM deleted;

    EXEC dbo.usp_AssertVersionsUnlocked @Owners;
    EXEC dbo.usp_AssertDesignNotFrozen @Owners;
END
GO

CREATE OR ALTER TRIGGER dbo.trg_ScoreRule_Guard
ON dbo.ScoreRule
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;

    DECLARE @IsUpdate bit = IIF(EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted), 1, 0);

    IF @IsUpdate = 1
       AND EXISTS (SELECT ScoreRuleID, ExperimentVersionID, PhaseID, ScoreCategoryID FROM inserted
                   EXCEPT
                   SELECT ScoreRuleID, ExperimentVersionID, PhaseID, ScoreCategoryID FROM deleted)
        THROW 50101, N'ScoreRule.ExperimentVersionID, PhaseID and ScoreCategoryID cannot be changed.', 1;

    DECLARE @Owners dbo.VersionIdList;
    INSERT INTO @Owners (ExperimentVersionID)
        SELECT ExperimentVersionID FROM inserted
        UNION
        SELECT ExperimentVersionID FROM deleted;

    EXEC dbo.usp_LockVersions @Versions = @Owners;

    DECLARE @Locked dbo.VersionIdList;
    INSERT INTO @Locked (ExperimentVersionID)
        SELECT ev.ExperimentVersionID
        FROM dbo.ExperimentVersion AS ev
        JOIN @Owners AS o ON o.ExperimentVersionID = ev.ExperimentVersionID
        WHERE ev.LockedUTC IS NOT NULL;

    -- A locked version admits only the soft-delete: an UPDATE whose every affected row goes
    -- RowState 'A' -> 'D' with no other column changing. Inserts and deletes are always rejected.
    IF EXISTS (SELECT 1 FROM @Locked)
    BEGIN
        IF @IsUpdate = 0
            THROW 50100, N'ExperimentVersion is locked; ScoreRules cannot be inserted or deleted.', 1;

        IF EXISTS
        (
            SELECT 1
            FROM inserted AS i
            JOIN deleted AS d ON d.ScoreRuleID = i.ScoreRuleID
            WHERE i.ExperimentVersionID IN (SELECT ExperimentVersionID FROM @Locked)
              AND NOT (    d.RowState = 'A'
                       AND i.RowState = 'D'
                       -- INTERSECT compares NULLs as equal, so a changed RuleNotes can never read as UNKNOWN.
                       AND EXISTS (SELECT i.WeightPercent, i.MinScore, i.MaxScore, i.Active, i.RuleNotes
                                   INTERSECT
                                   SELECT d.WeightPercent, d.MinScore, d.MaxScore, d.Active, d.RuleNotes))
        )
            THROW 50100, N'ExperimentVersion is locked; the only permitted ScoreRule change is soft-deleting it (RowState A to D).', 1;
    END

    -- Unlocked owners: the design freeze applies to every write, the soft-delete included.
    DECLARE @Unlocked dbo.VersionIdList;
    INSERT INTO @Unlocked (ExperimentVersionID)
        SELECT o.ExperimentVersionID
        FROM @Owners AS o
        WHERE NOT EXISTS (SELECT 1 FROM @Locked AS l WHERE l.ExperimentVersionID = o.ExperimentVersionID);

    EXEC dbo.usp_AssertDesignNotFrozen @Unlocked;
END
GO
