-- Post-deployment audit (V10): run against the Experiments database right after upgrade/ and programmability/ are
-- applied. Read-only: SELECTs into table variables, plus a RAISERROR at the end when something failed.
--
-- V10 here means the v9 baseline plus every script in upgrade/ and programmability/ (Issues #1-#10).
-- It returns three result sets:
--   1. Inventory    - counts per ExperimentVersion (informational).
--   2. Findings     - one row per finding, worst first:
--        FAIL  a deployment failure: an expected object, constraint or setting is missing, disabled or out of date,
--              or the existing data no longer matches what the pre-deployment audit recorded. Resolve before use.
--        WARN  a finalization-readiness warning: the deployment is fine, but usp_FinalizeExperimentVersion would
--              refuse the version (error in Detail) until the data is completed.
--        INFO  worth knowing; no action required.
--   3. Verdict      - one row summarizing the counts.
-- When any FAIL is found, the script ends with RAISERROR severity 16, so sqlcmd -b exits non-zero.
USE Experiments;
GO
SET NOCOUNT ON;

-- Expected data, from the pre-deployment audit of 2026-10-23. The V10 scripts change no data, so these still hold
-- right after deployment. If the data has since been changed on purpose (a version or Execution added), update them.
DECLARE @ExpectedVersions int = 1,
        @ExpectedExecutions int = 16,
        @ExpectedScoreRules int = 9,
        @ExpectedSnapshotRows int = 0,
        @KnownOpenExecutionID bigint = 9;

DECLARE @Findings TABLE (
    Seq int IDENTITY(1, 1) PRIMARY KEY,
    Severity varchar(4) NOT NULL,
    Area nvarchar(60) NOT NULL,
    Item nvarchar(256) NOT NULL,
    Detail nvarchar(1000) NULL);

-- Every object the V10 scripts create. Controlled = controlled-write procedure (Issue #10 standalone guard).
DECLARE @Expected TABLE (
    Name sysname PRIMARY KEY,
    TypeCode char(2) NOT NULL,
    ParentTable sysname NULL,
    Controlled bit NOT NULL);
INSERT INTO @Expected (Name, TypeCode, ParentTable, Controlled) VALUES
    -- 010 owner views
    (N'vOwnerIteration', 'V', NULL, 0),
    (N'vOwnerParameter', 'V', NULL, 0),
    (N'vOwnerScoreRule', 'V', NULL, 0),
    (N'vOwnerScoreObservationSnapshot', 'V', NULL, 0),
    (N'vOwnerExecution', 'V', NULL, 0),
    (N'vOwnerArtifact', 'V', NULL, 0),
    (N'vOwnerScoreObservation', 'V', NULL, 0),
    (N'vOwnerScoreObservationEvidence', 'V', NULL, 0),
    -- 015 design freeze; 020 lock primitives and assertion helpers
    (N'fn_VersionHasExecutions', 'FN', NULL, 0),
    (N'usp_AssertDesignNotFrozen', 'P', NULL, 0),
    (N'usp_AssertDesignWritable', 'P', NULL, 0),
    (N'usp_AssertVersionsUnlocked', 'P', NULL, 0),
    (N'usp_AcquireVersionLock', 'P', NULL, 0),
    (N'usp_LockVersions', 'P', NULL, 0),
    -- 030, 040, 050, 090 guard triggers
    (N'trg_ExperimentVersion_Guard', 'TR', N'ExperimentVersion', 0),
    (N'trg_Iteration_Guard', 'TR', N'Iteration', 0),
    (N'trg_Parameter_Guard', 'TR', N'Parameter', 0),
    (N'trg_ScoreRule_Guard', 'TR', N'ScoreRule', 0),
    (N'trg_Execution_Guard', 'TR', N'Execution', 0),
    (N'trg_ScoreObservation_Guard', 'TR', N'ScoreObservation', 0),
    (N'trg_Artifact_Guard', 'TR', N'Artifact', 0),
    (N'trg_ScoreObservationEvidence_Guard', 'TR', N'ScoreObservationEvidence', 0),
    (N'trg_ScoreObservationSnapshot_Guard', 'TR', N'ScoreObservationSnapshot', 0),
    -- 060, 070, 080, 085, 095 controlled-write procedures
    (N'usp_CreateExecution', 'P', NULL, 1),
    (N'usp_CreateScoreObservation', 'P', NULL, 1),
    (N'usp_UpdateScoreObservationContent', 'P', NULL, 1),
    (N'usp_UpdateExperimentVersion', 'P', NULL, 1),
    (N'usp_CreateIteration', 'P', NULL, 1),
    (N'usp_UpdateIteration', 'P', NULL, 1),
    (N'usp_DeleteIteration', 'P', NULL, 1),
    (N'usp_CreateParameter', 'P', NULL, 1),
    (N'usp_UpdateParameter', 'P', NULL, 1),
    (N'usp_DeleteParameter', 'P', NULL, 1),
    (N'usp_CreateScoreRule', 'P', NULL, 1),
    (N'usp_UpdateScoreRule', 'P', NULL, 1),
    (N'usp_DeleteScoreRule', 'P', NULL, 1),
    (N'usp_UpdateExecutionRunFacts', 'P', NULL, 1),
    (N'usp_CreateArtifact', 'P', NULL, 1),
    (N'usp_AddEvidence', 'P', NULL, 1),
    (N'usp_RemoveEvidence', 'P', NULL, 1),
    (N'usp_FinalizeExperimentVersion', 'P', NULL, 1),
    -- upgrade/001 snapshot CHECK constraints, and the baseline uniqueness the procedures rely on
    (N'CK_ScoreObservationSnapshot_MinScore_LT_MaxScore', 'C', N'ScoreObservationSnapshot', 0),
    (N'CK_ScoreObservationSnapshot_WeightPercent', 'C', N'ScoreObservationSnapshot', 0),
    (N'CK_ScoreObservationSnapshot_ObservedScore_InRange', 'C', N'ScoreObservationSnapshot', 0),
    (N'UC_ScoreRule_ExperimentVersionID_PhaseID_ScoreCategoryID', 'UQ', N'ScoreRule', 0),
    (N'UC_ScoreObservation_ExecutionID_ScoreRuleID_RaterID', 'UQ', N'ScoreObservation', 0);

-- Code fingerprints: text each deployed module must contain, proving it is the current version and not an older one.
DECLARE @Fingerprints TABLE (Name sysname NOT NULL, MustContain nvarchar(200) NOT NULL, Reason nvarchar(200) NOT NULL);
INSERT INTO @Fingerprints (Name, MustContain, Reason) VALUES
    (N'trg_ExperimentVersion_Guard', N'50509', N'Issue #9: LockedUTC requires the finalization token'),
    (N'usp_FinalizeExperimentVersion', N'FinalizingVersionID', N'Issue #9: finalization sets the token'),
    (N'usp_UpdateExecutionRunFacts', N'50201', N'Issue #8: run-facts frozen once EndUTC is set'),
    (N'usp_AddEvidence', N'usp_LockVersions', N'Issue #8: locks both owning versions in order'),
    (N'usp_RemoveEvidence', N'usp_LockVersions', N'Issue #8: locks both owning versions in order');

----------------------------------------------------------------------------------------------------------------------
-- A. Deployment: schema shape, objects, triggers, constraints, module settings, code versions (FAIL)
----------------------------------------------------------------------------------------------------------------------

-- A1. Schema shape (#7, #9).
IF COL_LENGTH('dbo.ScoreRule', 'RowState') IS NOT NULL
    INSERT INTO @Findings (Severity, Area, Item, Detail)
    VALUES ('FAIL', N'Deployment: schema', N'ScoreRule.RowState', N'column still present; the database is not on the v9 baseline');
IF COL_LENGTH('dbo.ExperimentVersion', 'LockedUTC') IS NULL
    INSERT INTO @Findings (Severity, Area, Item, Detail)
    VALUES ('FAIL', N'Deployment: schema', N'ExperimentVersion.LockedUTC', N'column missing; the database is not on the v9 baseline');

-- A2. Every expected object exists with the expected type.
INSERT INTO @Findings (Severity, Area, Item, Detail)
SELECT 'FAIL', N'Deployment: objects', x.Name,
       CASE WHEN o.object_id IS NULL THEN N'missing'
            ELSE CONCAT(N'is ', o.type_desc, N', expected type ', x.TypeCode) END
FROM @Expected AS x
LEFT JOIN sys.objects AS o ON o.object_id = OBJECT_ID(N'dbo.' + x.Name)
WHERE o.object_id IS NULL OR o.type COLLATE DATABASE_DEFAULT <> x.TypeCode;

IF NOT EXISTS (SELECT 1 FROM sys.table_types WHERE schema_id = SCHEMA_ID(N'dbo') AND name = N'VersionIdList')
    INSERT INTO @Findings (Severity, Area, Item, Detail)
    VALUES ('FAIL', N'Deployment: objects', N'VersionIdList', N'table type missing');

IF OBJECT_ID(N'dbo.usp_SoftDeleteScoreRule') IS NOT NULL
    INSERT INTO @Findings (Severity, Area, Item, Detail)
    VALUES ('FAIL', N'Deployment: objects', N'usp_SoftDeleteScoreRule', N'retired by Issue #7 but still present');

-- A3. Triggers: enabled, AFTER, on the right table, firing on INSERT, UPDATE and DELETE.
INSERT INTO @Findings (Severity, Area, Item, Detail)
SELECT 'FAIL', N'Deployment: triggers', x.Name,
       CONCAT_WS(N'; ',
           CASE WHEN t.is_disabled = 1 THEN N'disabled' END,
           CASE WHEN t.is_instead_of_trigger = 1 THEN N'INSTEAD OF, expected AFTER' END,
           CASE WHEN t.parent_id <> OBJECT_ID(N'dbo.' + x.ParentTable)
                THEN CONCAT(N'on ', OBJECT_NAME(t.parent_id), N', expected ', x.ParentTable) END,
           CASE WHEN ev.Events <> 3 THEN CONCAT(N'fires on ', ev.Events, N' of INSERT/UPDATE/DELETE') END)
FROM @Expected AS x
JOIN sys.triggers AS t ON t.object_id = OBJECT_ID(N'dbo.' + x.Name)
CROSS APPLY (SELECT COUNT(DISTINCT te.type_desc) AS Events
             FROM sys.trigger_events AS te
             WHERE te.object_id = t.object_id AND te.type_desc IN (N'INSERT', N'UPDATE', N'DELETE')) AS ev
WHERE x.TypeCode = 'TR'
  AND (t.is_disabled = 1 OR t.is_instead_of_trigger = 1 OR t.parent_id <> OBJECT_ID(N'dbo.' + x.ParentTable) OR ev.Events <> 3);

-- A4. Expected constraints are on the right table; every CHECK and FOREIGN KEY in dbo is enabled and trusted.
INSERT INTO @Findings (Severity, Area, Item, Detail)
SELECT 'FAIL', N'Deployment: constraints', x.Name, CONCAT(N'on ', OBJECT_NAME(o.parent_object_id), N', expected ', x.ParentTable)
FROM @Expected AS x
JOIN sys.objects AS o ON o.object_id = OBJECT_ID(N'dbo.' + x.Name)
WHERE x.TypeCode IN ('C', 'UQ') AND o.parent_object_id <> OBJECT_ID(N'dbo.' + x.ParentTable);

INSERT INTO @Findings (Severity, Area, Item, Detail)
SELECT 'FAIL', N'Deployment: constraints', c.name,
       CONCAT(OBJECT_NAME(c.parent_object_id), N': ',
              CONCAT_WS(N'; ', CASE WHEN c.is_disabled = 1 THEN N'disabled' END,
                               CASE WHEN c.is_not_trusted = 1 THEN N'not trusted (existing rows were not checked)' END))
FROM (SELECT name, parent_object_id, schema_id, is_disabled, is_not_trusted FROM sys.check_constraints
      UNION ALL
      SELECT name, parent_object_id, schema_id, is_disabled, is_not_trusted FROM sys.foreign_keys) AS c
WHERE c.schema_id = SCHEMA_ID(N'dbo') AND (c.is_disabled = 1 OR c.is_not_trusted = 1);

-- A5. Modules were created with QUOTED_IDENTIFIER and ANSI_NULLS ON (sqlcmd leaves QUOTED_IDENTIFIER off without -I).
INSERT INTO @Findings (Severity, Area, Item, Detail)
SELECT 'FAIL', N'Deployment: module settings', x.Name,
       CONCAT_WS(N'; ', CASE WHEN m.uses_quoted_identifier = 0 THEN N'created with QUOTED_IDENTIFIER OFF' END,
                        CASE WHEN m.uses_ansi_nulls = 0 THEN N'created with ANSI_NULLS OFF' END)
FROM @Expected AS x
JOIN sys.sql_modules AS m ON m.object_id = OBJECT_ID(N'dbo.' + x.Name)
WHERE m.uses_quoted_identifier = 0 OR m.uses_ansi_nulls = 0;

-- A6. Code versions: controlled-write procedures carry the Issue #10 standalone guard; the fingerprints match;
--     no deployed module still refers to RowState (#7).
INSERT INTO @Findings (Severity, Area, Item, Detail)
SELECT 'FAIL', N'Deployment: code version', x.Name, N'no standalone-transaction guard; an older version (before Issue #10) is deployed'
FROM @Expected AS x
JOIN sys.sql_modules AS m ON m.object_id = OBJECT_ID(N'dbo.' + x.Name)
WHERE x.Controlled = 1 AND m.definition NOT LIKE N'%must be called outside a transaction%';

INSERT INTO @Findings (Severity, Area, Item, Detail)
SELECT 'FAIL', N'Deployment: code version', f.Name, CONCAT(N'does not contain ''', f.MustContain, N'''; expected ', f.Reason)
FROM @Fingerprints AS f
JOIN sys.sql_modules AS m ON m.object_id = OBJECT_ID(N'dbo.' + f.Name)
WHERE CHARINDEX(f.MustContain, m.definition) = 0;

INSERT INTO @Findings (Severity, Area, Item, Detail)
SELECT 'FAIL', N'Deployment: code version', x.Name, N'still refers to RowState; an older version (before Issue #7) is deployed'
FROM @Expected AS x
JOIN sys.sql_modules AS m ON m.object_id = OBJECT_ID(N'dbo.' + x.Name)
WHERE m.definition LIKE N'%RowState%';

-- A7. Other user modules in dbo that the V10 scripts do not create (INFO: leftovers or unrelated objects).
INSERT INTO @Findings (Severity, Area, Item, Detail)
SELECT 'INFO', N'Deployment: objects', o.name, CONCAT(o.type_desc, N' not created by the V10 scripts')
FROM sys.objects AS o
WHERE o.schema_id = SCHEMA_ID(N'dbo') AND o.is_ms_shipped = 0
  AND o.type IN ('P', 'V', 'TR', 'FN', 'IF', 'TF')
  AND o.name <> N'usp_SoftDeleteScoreRule'
  AND NOT EXISTS (SELECT 1 FROM @Expected AS x WHERE x.Name = o.name);

----------------------------------------------------------------------------------------------------------------------
-- B. Data intact: matches the pre-deployment audit, and the integrity checks still pass (FAIL)
----------------------------------------------------------------------------------------------------------------------

DECLARE @Versions int = (SELECT COUNT(*) FROM dbo.ExperimentVersion),
        @Executions int = (SELECT COUNT(*) FROM dbo.Execution),
        @ScoreRules int = (SELECT COUNT(*) FROM dbo.ScoreRule),
        @SnapshotRows int = (SELECT COUNT(*) FROM dbo.ScoreObservationSnapshot);

INSERT INTO @Findings (Severity, Area, Item, Detail)
SELECT 'FAIL', N'Data intact: counts', c.Item, CONCAT(N'found ', c.Actual, N', expected ', c.Expected)
FROM (VALUES (N'ExperimentVersion rows', @Versions, @ExpectedVersions),
             (N'Execution rows', @Executions, @ExpectedExecutions),
             (N'ScoreRule rows', @ScoreRules, @ExpectedScoreRules),
             (N'ScoreObservationSnapshot rows', @SnapshotRows, @ExpectedSnapshotRows)) AS c (Item, Actual, Expected)
WHERE c.Actual <> c.Expected;

-- B1. The known open Execution: still present. Open is a finalization warning; ended since is informational.
IF NOT EXISTS (SELECT 1 FROM dbo.Execution WHERE ExecutionID = @KnownOpenExecutionID)
    INSERT INTO @Findings (Severity, Area, Item, Detail)
    VALUES ('FAIL', N'Data intact: Execution', CONCAT(N'Execution ', @KnownOpenExecutionID), N'missing');

INSERT INTO @Findings (Severity, Area, Item, Detail)
SELECT CASE WHEN e.EndUTC IS NULL THEN 'WARN' ELSE 'INFO' END,
       CASE WHEN e.EndUTC IS NULL THEN N'Finalization readiness' ELSE N'Data intact: Execution' END,
       CONCAT(N'Execution ', e.ExecutionID),
       CASE WHEN e.EndUTC IS NULL
            THEN CONCAT(N'known open Execution, still present with EndUTC NULL; version ', i.ExperimentVersionID,
                        N' cannot be finalized until it is ended through usp_UpdateExecutionRunFacts (50501)')
            ELSE CONCAT(N'known open Execution has since been ended at ', CONVERT(nvarchar(30), e.EndUTC, 126)) END
FROM dbo.Execution AS e
JOIN dbo.Iteration AS i ON i.IterationID = e.IterationID
WHERE e.ExecutionID = @KnownOpenExecutionID;

-- B2. Lock integrity (#9): locked <=> has snapshot rows.
INSERT INTO @Findings (Severity, Area, Item, Detail)
SELECT 'FAIL', N'Data intact: lock integrity', CONCAT(N'ExperimentVersion ', ev.ExperimentVersionID), N'locked without snapshot rows'
FROM dbo.ExperimentVersion AS ev
WHERE ev.LockedUTC IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM dbo.ScoreObservationSnapshot AS s WHERE s.ExperimentVersionID = ev.ExperimentVersionID);

INSERT INTO @Findings (Severity, Area, Item, Detail)
SELECT DISTINCT 'FAIL', N'Data intact: lock integrity', CONCAT(N'ExperimentVersion ', s.ExperimentVersionID), N'snapshot rows on an unlocked version'
FROM dbo.ScoreObservationSnapshot AS s
JOIN dbo.ExperimentVersion AS ev ON ev.ExperimentVersionID = s.ExperimentVersionID
WHERE ev.LockedUTC IS NULL;

-- B3. ScoreRule validity and uniqueness.
INSERT INTO @Findings (Severity, Area, Item, Detail)
SELECT 'FAIL', N'Data intact: ScoreRule', CONCAT(N'ScoreRule ', ScoreRuleID),
       CONCAT(N'MinScore ', MinScore, N', MaxScore ', MaxScore, N', WeightPercent ', WeightPercent)
FROM dbo.ScoreRule
WHERE MinScore >= MaxScore OR WeightPercent < 0 OR WeightPercent > 100;

INSERT INTO @Findings (Severity, Area, Item, Detail)
SELECT 'FAIL', N'Data intact: ScoreRule',
       CONCAT(N'Version ', ExperimentVersionID, N', phase ', PhaseID, N', category ', ScoreCategoryID),
       CONCAT(COUNT(*), N' rules for one slot')
FROM dbo.ScoreRule
GROUP BY ExperimentVersionID, PhaseID, ScoreCategoryID
HAVING COUNT(*) > 1;

-- B4. Observation integrity: the rule belongs to the Execution's version and phase, and the score is in range (50506).
INSERT INTO @Findings (Severity, Area, Item, Detail)
SELECT 'FAIL', N'Data intact: ScoreObservation', CONCAT(N'ScoreObservation ', o.ScoreObservationID),
       CONCAT_WS(N'; ',
           CASE WHEN r.ExperimentVersionID <> i.ExperimentVersionID THEN N'rule belongs to another version' END,
           CASE WHEN r.PhaseID <> e.PhaseID THEN N'rule belongs to another phase' END,
           CASE WHEN o.ObservedScore < r.MinScore OR o.ObservedScore > r.MaxScore
                THEN CONCAT(N'score ', o.ObservedScore, N' outside ', r.MinScore, N'-', r.MaxScore) END)
FROM dbo.ScoreObservation AS o
JOIN dbo.Execution AS e ON e.ExecutionID = o.ExecutionID
JOIN dbo.Iteration AS i ON i.IterationID = e.IterationID
JOIN dbo.ScoreRule AS r ON r.ScoreRuleID = o.ScoreRuleID
WHERE r.ExperimentVersionID <> i.ExperimentVersionID OR r.PhaseID <> e.PhaseID
   OR o.ObservedScore < r.MinScore OR o.ObservedScore > r.MaxScore;

-- B5. Evidence links join an observation and an artifact of the same Execution (#8; enforced by the service).
INSERT INTO @Findings (Severity, Area, Item, Detail)
SELECT 'FAIL', N'Data intact: evidence', CONCAT(N'ScoreObservation ', soe.ScoreObservationID, N' / Artifact ', soe.ArtifactID),
       CONCAT(N'observation Execution ', o.ExecutionID, N', artifact Execution ', a.ExecutionID)
FROM dbo.ScoreObservationEvidence AS soe
JOIN dbo.ScoreObservation AS o ON o.ScoreObservationID = soe.ScoreObservationID
JOIN dbo.Artifact AS a ON a.ArtifactID = soe.ArtifactID
WHERE o.ExecutionID <> a.ExecutionID;

----------------------------------------------------------------------------------------------------------------------
-- C. Finalization readiness of unlocked versions that have Executions (WARN; mirrors usp_FinalizeExperimentVersion)
----------------------------------------------------------------------------------------------------------------------

DECLARE @Execs TABLE (ExecutionID bigint PRIMARY KEY, ExperimentVersionID bigint NOT NULL, PhaseID int NOT NULL, EndUTC datetime2(3) NULL);
INSERT INTO @Execs (ExecutionID, ExperimentVersionID, PhaseID, EndUTC)
SELECT e.ExecutionID, i.ExperimentVersionID, e.PhaseID, e.EndUTC
FROM dbo.Execution AS e
JOIN dbo.Iteration AS i ON i.IterationID = e.IterationID
JOIN dbo.ExperimentVersion AS ev ON ev.ExperimentVersionID = i.ExperimentVersionID
WHERE ev.LockedUTC IS NULL;

-- 50501: other open Executions (the known one is reported in B1).
INSERT INTO @Findings (Severity, Area, Item, Detail)
SELECT 'WARN', N'Finalization readiness', CONCAT(N'Execution ', ExecutionID),
       CONCAT(N'open (EndUTC NULL); version ', ExperimentVersionID, N' cannot be finalized until it is ended (50501)')
FROM @Execs
WHERE EndUTC IS NULL AND ExecutionID <> @KnownOpenExecutionID;

-- 50502: Executions without an observation.
INSERT INTO @Findings (Severity, Area, Item, Detail)
SELECT 'WARN', N'Finalization readiness', CONCAT(N'Execution ', x.ExecutionID),
       CONCAT(N'has no ScoreObservation; version ', x.ExperimentVersionID, N' (50502)')
FROM @Execs AS x
WHERE NOT EXISTS (SELECT 1 FROM dbo.ScoreObservation AS o WHERE o.ExecutionID = x.ExecutionID);

-- 50503: Executions in a phase with no ScoreRules.
INSERT INTO @Findings (Severity, Area, Item, Detail)
SELECT 'WARN', N'Finalization readiness', CONCAT(N'Execution ', x.ExecutionID),
       CONCAT(N'phase ', x.PhaseID, N' has no ScoreRules in version ', x.ExperimentVersionID, N' (50503)')
FROM @Execs AS x
WHERE NOT EXISTS (SELECT 1 FROM dbo.ScoreRule AS r WHERE r.ExperimentVersionID = x.ExperimentVersionID AND r.PhaseID = x.PhaseID);

-- 50504: configured phases with no observation.
INSERT INTO @Findings (Severity, Area, Item, Detail)
SELECT DISTINCT 'WARN', N'Finalization readiness', CONCAT(N'Version ', r.ExperimentVersionID, N', phase ', r.PhaseID),
       N'phase has ScoreRules but no ScoreObservation (50504)'
FROM dbo.ScoreRule AS r
WHERE r.ExperimentVersionID IN (SELECT ExperimentVersionID FROM @Execs)
  AND NOT EXISTS (SELECT 1 FROM dbo.ScoreObservation AS o JOIN @Execs AS x ON x.ExecutionID = o.ExecutionID
                  WHERE x.ExperimentVersionID = r.ExperimentVersionID AND x.PhaseID = r.PhaseID);

-- 50505: weights per phase total 100.
INSERT INTO @Findings (Severity, Area, Item, Detail)
SELECT 'WARN', N'Finalization readiness', CONCAT(N'Version ', ExperimentVersionID, N', phase ', PhaseID),
       CONCAT(N'weights total ', SUM(WeightPercent), N', expected 100.00 (50505)')
FROM dbo.ScoreRule
WHERE ExperimentVersionID IN (SELECT ExperimentVersionID FROM @Execs)
GROUP BY ExperimentVersionID, PhaseID
HAVING SUM(WeightPercent) <> 100.00;

----------------------------------------------------------------------------------------------------------------------
-- Results
----------------------------------------------------------------------------------------------------------------------

-- 1. Inventory.
SELECT ev.ExperimentVersionID, ev.LockedUTC,
    (SELECT COUNT(*) FROM dbo.Execution AS e JOIN dbo.Iteration AS i ON i.IterationID = e.IterationID
      WHERE i.ExperimentVersionID = ev.ExperimentVersionID) AS Executions,
    (SELECT COUNT(*) FROM dbo.Execution AS e JOIN dbo.Iteration AS i ON i.IterationID = e.IterationID
      WHERE i.ExperimentVersionID = ev.ExperimentVersionID AND e.EndUTC IS NULL) AS OpenExecutions,
    (SELECT COUNT(*) FROM dbo.ScoreObservation AS o JOIN dbo.Execution AS e ON e.ExecutionID = o.ExecutionID
      JOIN dbo.Iteration AS i ON i.IterationID = e.IterationID
      WHERE i.ExperimentVersionID = ev.ExperimentVersionID) AS ScoreObservations,
    (SELECT COUNT(*) FROM dbo.ScoreRule AS r WHERE r.ExperimentVersionID = ev.ExperimentVersionID) AS ScoreRules,
    (SELECT COUNT(*) FROM dbo.ScoreObservationSnapshot AS s WHERE s.ExperimentVersionID = ev.ExperimentVersionID) AS SnapshotRows
FROM dbo.ExperimentVersion AS ev
ORDER BY ev.ExperimentVersionID;

-- 2. Findings, worst first.
SELECT Severity, Area, Item, Detail
FROM @Findings
ORDER BY CASE Severity WHEN 'FAIL' THEN 1 WHEN 'WARN' THEN 2 ELSE 3 END, Seq;

-- 3. Verdict.
DECLARE @Fails int = (SELECT COUNT(*) FROM @Findings WHERE Severity = 'FAIL'),
        @Warns int = (SELECT COUNT(*) FROM @Findings WHERE Severity = 'WARN');
SELECT CASE WHEN @Fails > 0 THEN N'DEPLOYMENT FAILED' ELSE N'DEPLOYMENT VERIFIED' END AS Verdict,
       @Fails AS DeploymentFailures,
       @Warns AS FinalizationWarnings,
       (SELECT COUNT(*) FROM @Expected) AS ObjectsChecked;

IF @Fails > 0
    RAISERROR(N'Post-deployment audit: %d deployment failure(s). See the FAIL rows in the findings.', 16, 1, @Fails);
