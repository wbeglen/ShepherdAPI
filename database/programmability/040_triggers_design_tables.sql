-- Issue #6 / Issue #4: guards on the design tables (Iteration, Parameter, ScoreRule). Re-runnable (CREATE OR ALTER).
-- Each trigger, in this order:
--   1. Rejects an UPDATE that changes an identity/ownership column (50101), locked or not.
--   2. Resolves the old and new owning versions set-wise, locks them in ascending id order under an
--      update lock, and rejects the whole statement if any is locked (50100).
--   3. Issue #4: rejects the whole statement if any owning version already has an Execution (50400,
--      design-frozen). Checked after the lock, so it is serialized against Execution creation.
-- Issue #7: ScoreRule has no exceptions. Its rows are inserted, updated and physically deleted only while the
-- version is unlocked and has no Execution; the foreign keys from ScoreObservation and ScoreObservationSnapshot
-- additionally refuse the delete of any rule that is referenced.
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

    IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
       AND EXISTS (SELECT ScoreRuleID, ExperimentVersionID, PhaseID, ScoreCategoryID FROM inserted
                   EXCEPT
                   SELECT ScoreRuleID, ExperimentVersionID, PhaseID, ScoreCategoryID FROM deleted)
        THROW 50101, N'ScoreRule.ExperimentVersionID, PhaseID and ScoreCategoryID cannot be changed.', 1;

    DECLARE @Owners dbo.VersionIdList;
    INSERT INTO @Owners (ExperimentVersionID)
        SELECT ExperimentVersionID FROM inserted
        UNION
        SELECT ExperimentVersionID FROM deleted;

    EXEC dbo.usp_AssertVersionsUnlocked @Owners;
    EXEC dbo.usp_AssertDesignNotFrozen @Owners;
END
GO
