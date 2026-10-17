-- Issue #6: guards on the design tables (Iteration, Parameter, ScoreRule). Re-runnable (CREATE OR ALTER).
-- Each trigger, in order:
--   1. Rejects an UPDATE that changes an identity/ownership column (50101), locked or not.
--   2. Resolves the old and new owning versions set-wise, locks them in ascending id order under an
--      update lock, and rejects the whole statement if any is locked (50100).
-- ScoreRule has one carve-out on a locked version: setting RowState from 'A' to 'D' and changing
-- nothing else (the soft-delete defined in Issue #4).
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

    IF NOT EXISTS (SELECT 1 FROM @Locked) RETURN;

    -- A locked version admits only the soft-delete: an UPDATE whose every affected row goes
    -- RowState 'A' -> 'D' with no other column changing. Inserts and deletes are always rejected.
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
GO
