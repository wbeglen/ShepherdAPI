-- Issue #6: guards on the recording tables (Execution, ScoreObservation, Artifact,
-- ScoreObservationEvidence). Re-runnable (CREATE OR ALTER). Same two steps as the design tables:
--   1. An UPDATE that changes an identity/ownership column is rejected (50101), locked or not.
--   2. The old and new owning versions are resolved set-wise, locked in ascending id order under an
--      update lock, and the whole statement is rejected if any is locked (50100).
-- Indirect owners come from the dbo.vOwner* views, keyed by the parent id each row carries, so a
-- deleted row can still be resolved through its (still existing) parent.
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE OR ALTER TRIGGER dbo.trg_Execution_Guard
ON dbo.Execution
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;

    IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
       AND EXISTS (SELECT ExecutionID, IterationID, AgentID, PhaseID, AttemptNumber, StartUTC FROM inserted
                   EXCEPT
                   SELECT ExecutionID, IterationID, AgentID, PhaseID, AttemptNumber, StartUTC FROM deleted)
        THROW 50101, N'Execution.IterationID, AgentID, PhaseID, AttemptNumber and StartUTC cannot be changed.', 1;

    DECLARE @Owners dbo.VersionIdList;
    INSERT INTO @Owners (ExperimentVersionID)
        SELECT o.ExperimentVersionID FROM inserted AS x JOIN dbo.vOwnerIteration AS o ON o.IterationID = x.IterationID
        UNION
        SELECT o.ExperimentVersionID FROM deleted AS x JOIN dbo.vOwnerIteration AS o ON o.IterationID = x.IterationID;

    EXEC dbo.usp_AssertVersionsUnlocked @Owners;
END
GO

CREATE OR ALTER TRIGGER dbo.trg_ScoreObservation_Guard
ON dbo.ScoreObservation
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;

    IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
       AND EXISTS (SELECT ScoreObservationID, ExecutionID, ScoreRuleID, RaterID FROM inserted
                   EXCEPT
                   SELECT ScoreObservationID, ExecutionID, ScoreRuleID, RaterID FROM deleted)
        THROW 50101, N'ScoreObservation.ExecutionID, ScoreRuleID and RaterID cannot be changed.', 1;

    DECLARE @Owners dbo.VersionIdList;
    INSERT INTO @Owners (ExperimentVersionID)
        SELECT o.ExperimentVersionID FROM inserted AS x JOIN dbo.vOwnerExecution AS o ON o.ExecutionID = x.ExecutionID
        UNION
        SELECT o.ExperimentVersionID FROM deleted AS x JOIN dbo.vOwnerExecution AS o ON o.ExecutionID = x.ExecutionID;

    EXEC dbo.usp_AssertVersionsUnlocked @Owners;
END
GO

-- Artifact rows are insert-only: every column is an identity column.
CREATE OR ALTER TRIGGER dbo.trg_Artifact_Guard
ON dbo.Artifact
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;

    IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
       AND EXISTS (SELECT ArtifactID, ExecutionID, ArtifactTypeID, OriginalName, OriginalURI, StoredURI,
                          MimeType, ByteLength, SHA256, ReceivedUTC, StoredUTC FROM inserted
                   EXCEPT
                   SELECT ArtifactID, ExecutionID, ArtifactTypeID, OriginalName, OriginalURI, StoredURI,
                          MimeType, ByteLength, SHA256, ReceivedUTC, StoredUTC FROM deleted)
        THROW 50101, N'Artifact rows are insert-only; no column can be changed.', 1;

    DECLARE @Owners dbo.VersionIdList;
    INSERT INTO @Owners (ExperimentVersionID)
        SELECT o.ExperimentVersionID FROM inserted AS x JOIN dbo.vOwnerExecution AS o ON o.ExecutionID = x.ExecutionID
        UNION
        SELECT o.ExperimentVersionID FROM deleted AS x JOIN dbo.vOwnerExecution AS o ON o.ExecutionID = x.ExecutionID;

    EXEC dbo.usp_AssertVersionsUnlocked @Owners;
END
GO

-- An evidence link has two owners (its observation's version and its artifact's version); both are guarded.
CREATE OR ALTER TRIGGER dbo.trg_ScoreObservationEvidence_Guard
ON dbo.ScoreObservationEvidence
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;

    IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
       AND EXISTS (SELECT ScoreObservationID, ArtifactID FROM inserted
                   EXCEPT
                   SELECT ScoreObservationID, ArtifactID FROM deleted)
        THROW 50101, N'ScoreObservationEvidence.ScoreObservationID and ArtifactID cannot be changed.', 1;

    DECLARE @Owners dbo.VersionIdList;
    INSERT INTO @Owners (ExperimentVersionID)
        SELECT o.ExperimentVersionID FROM inserted AS x JOIN dbo.vOwnerScoreObservation AS o ON o.ScoreObservationID = x.ScoreObservationID
        UNION
        SELECT o.ExperimentVersionID FROM inserted AS x JOIN dbo.vOwnerArtifact AS o ON o.ArtifactID = x.ArtifactID
        UNION
        SELECT o.ExperimentVersionID FROM deleted AS x JOIN dbo.vOwnerScoreObservation AS o ON o.ScoreObservationID = x.ScoreObservationID
        UNION
        SELECT o.ExperimentVersionID FROM deleted AS x JOIN dbo.vOwnerArtifact AS o ON o.ArtifactID = x.ArtifactID;

    EXEC dbo.usp_AssertVersionsUnlocked @Owners;
END
GO
