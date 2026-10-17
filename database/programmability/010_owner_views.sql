-- Issue #6: set-based owner resolution. Every governed table maps to the ExperimentVersion that
-- owns it. Re-runnable (CREATE OR ALTER; the table type is created only if missing).
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

IF TYPE_ID(N'dbo.VersionIdList') IS NULL
    CREATE TYPE dbo.VersionIdList AS TABLE (ExperimentVersionID bigint NOT NULL PRIMARY KEY);
GO

-- Direct owners (the table carries ExperimentVersionID).
CREATE OR ALTER VIEW dbo.vOwnerIteration AS
SELECT i.IterationID, i.ExperimentVersionID
FROM dbo.Iteration AS i;
GO

CREATE OR ALTER VIEW dbo.vOwnerParameter AS
SELECT p.ParameterID, p.ExperimentVersionID
FROM dbo.Parameter AS p;
GO

CREATE OR ALTER VIEW dbo.vOwnerScoreRule AS
SELECT r.ScoreRuleID, r.ExperimentVersionID
FROM dbo.ScoreRule AS r;
GO

CREATE OR ALTER VIEW dbo.vOwnerScoreObservationSnapshot AS
SELECT s.ExperimentVersionID, s.ExecutionID, s.PhaseID, s.ScoreCategoryID, s.ScoreRuleID, s.ScoreObservationID
FROM dbo.ScoreObservationSnapshot AS s;
GO

-- Indirect owners.
CREATE OR ALTER VIEW dbo.vOwnerExecution AS
SELECT e.ExecutionID, i.ExperimentVersionID
FROM dbo.Execution AS e
JOIN dbo.Iteration AS i ON i.IterationID = e.IterationID;
GO

CREATE OR ALTER VIEW dbo.vOwnerArtifact AS
SELECT a.ArtifactID, o.ExperimentVersionID
FROM dbo.Artifact AS a
JOIN dbo.vOwnerExecution AS o ON o.ExecutionID = a.ExecutionID;
GO

CREATE OR ALTER VIEW dbo.vOwnerScoreObservation AS
SELECT so.ScoreObservationID, o.ExperimentVersionID
FROM dbo.ScoreObservation AS so
JOIN dbo.vOwnerExecution AS o ON o.ExecutionID = so.ExecutionID;
GO

-- An evidence link joins an observation to an artifact, so it has two owning versions
-- (the API requires them to be the same; the database does not, so both are guarded).
CREATE OR ALTER VIEW dbo.vOwnerScoreObservationEvidence AS
SELECT soe.ScoreObservationID, soe.ArtifactID,
       obs.ExperimentVersionID AS ObservationVersionID,
       art.ExperimentVersionID AS ArtifactVersionID
FROM dbo.ScoreObservationEvidence AS soe
JOIN dbo.vOwnerScoreObservation AS obs ON obs.ScoreObservationID = soe.ScoreObservationID
JOIN dbo.vOwnerArtifact AS art ON art.ArtifactID = soe.ArtifactID;
GO
