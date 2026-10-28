-- Pre-deployment audit (Blocker #6): read-only checks of the data assumptions behind Issues #7-#10, and (section 8)
-- behind the V10 -> V11 upgrade scripts. Run against the Experiments database immediately before applying upgrade/ and
-- programmability/. Every statement is a SELECT: no DML, no DDL, no transaction.
--
-- Reading the results: section 1 (inventory) and section 7 (sessions) are informational. Every other result set
-- should be empty; any row is a problem to resolve before deploying. The finalization checks mirror
-- programmability/095_procedure_finalize_experiment_version.sql.
USE Experiments;
GO
SET NOCOUNT ON;

-- 0. Schema shape (#7, #9): v9 has no ScoreRule.RowState and has ExperimentVersion.LockedUTC.
SELECT 'ScoreRule.RowState still present' AS problem WHERE COL_LENGTH('dbo.ScoreRule', 'RowState') IS NOT NULL
UNION ALL SELECT 'ExperimentVersion.LockedUTC missing' WHERE COL_LENGTH('dbo.ExperimentVersion', 'LockedUTC') IS NULL
UNION ALL SELECT 'Old usp_SoftDeleteScoreRule present' WHERE OBJECT_ID('dbo.usp_SoftDeleteScoreRule') IS NOT NULL;

-- 1. Inventory (informational). A version with Executions has its design frozen once the scripts are applied (#7),
--    so fix its rules, parameters and iterations first.
SELECT ev.ExperimentVersionID, ev.LockedUTC,
    (SELECT COUNT(*) FROM dbo.Execution AS e JOIN dbo.Iteration AS i ON i.IterationID = e.IterationID
      WHERE i.ExperimentVersionID = ev.ExperimentVersionID) AS Executions,
    (SELECT COUNT(*) FROM dbo.ScoreRule AS r WHERE r.ExperimentVersionID = ev.ExperimentVersionID) AS ScoreRules,
    (SELECT COUNT(*) FROM dbo.ScoreObservationSnapshot AS s WHERE s.ExperimentVersionID = ev.ExperimentVersionID) AS SnapshotRows
FROM dbo.ExperimentVersion AS ev
ORDER BY ev.ExperimentVersionID;

-- 2. Lock integrity (#9): only finalization locks a version, so locked <=> has snapshot rows.
SELECT ev.ExperimentVersionID, 'locked without snapshot' AS problem
FROM dbo.ExperimentVersion AS ev
WHERE ev.LockedUTC IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM dbo.ScoreObservationSnapshot AS s WHERE s.ExperimentVersionID = ev.ExperimentVersionID)
UNION ALL
SELECT DISTINCT s.ExperimentVersionID, 'snapshot on unlocked version'
FROM dbo.ScoreObservationSnapshot AS s
JOIN dbo.ExperimentVersion AS ev ON ev.ExperimentVersionID = s.ExperimentVersionID
WHERE ev.LockedUTC IS NULL;

-- 3. ScoreRule validity (#7; the upgrade/001 CHECKs require the same of the snapshot copies).
SELECT ScoreRuleID, ExperimentVersionID, PhaseID, MinScore, MaxScore, WeightPercent
FROM dbo.ScoreRule
WHERE MinScore >= MaxScore OR WeightPercent < 0 OR WeightPercent > 100;

-- 3b. One rule per (version, phase, category).
SELECT ExperimentVersionID, PhaseID, ScoreCategoryID, COUNT(*) AS Dupes
FROM dbo.ScoreRule
GROUP BY ExperimentVersionID, PhaseID, ScoreCategoryID
HAVING COUNT(*) > 1;

-- 3c. Weights total 100 per phase (finalize 50505).
SELECT ExperimentVersionID, PhaseID, SUM(WeightPercent) AS TotalWeight
FROM dbo.ScoreRule
GROUP BY ExperimentVersionID, PhaseID
HAVING SUM(WeightPercent) <> 100.00;

-- 4. Observation integrity (finalize 50506; the snapshot ObservedScore CHECK).
SELECT o.ScoreObservationID, o.ExecutionID, o.ScoreRuleID, o.ObservedScore
FROM dbo.ScoreObservation AS o
JOIN dbo.Execution AS e ON e.ExecutionID = o.ExecutionID
JOIN dbo.Iteration AS i ON i.IterationID = e.IterationID
JOIN dbo.ScoreRule AS r ON r.ScoreRuleID = o.ScoreRuleID
WHERE r.ExperimentVersionID <> i.ExperimentVersionID OR r.PhaseID <> e.PhaseID
   OR o.ObservedScore < r.MinScore OR o.ObservedScore > r.MaxScore;

-- 5. Finalization readiness (#8 run-facts; finalize 50501-50503). An open Execution can still be ended after
--    deployment through usp_UpdateExecutionRunFacts, but it blocks finalization until it is.
SELECT i.ExperimentVersionID, e.ExecutionID, 'open (no EndUTC)' AS problem
FROM dbo.Execution AS e JOIN dbo.Iteration AS i ON i.IterationID = e.IterationID
WHERE e.EndUTC IS NULL
UNION ALL
SELECT i.ExperimentVersionID, e.ExecutionID, 'no observation'
FROM dbo.Execution AS e JOIN dbo.Iteration AS i ON i.IterationID = e.IterationID
WHERE NOT EXISTS (SELECT 1 FROM dbo.ScoreObservation AS o WHERE o.ExecutionID = e.ExecutionID)
UNION ALL
SELECT i.ExperimentVersionID, e.ExecutionID, 'phase has no rules'
FROM dbo.Execution AS e JOIN dbo.Iteration AS i ON i.IterationID = e.IterationID
WHERE NOT EXISTS (SELECT 1 FROM dbo.ScoreRule AS r
                  WHERE r.ExperimentVersionID = i.ExperimentVersionID AND r.PhaseID = e.PhaseID)
ORDER BY ExperimentVersionID, ExecutionID;

-- 6. Evidence links (#8): the service requires the same Execution on both sides; the database does not.
SELECT soe.ScoreObservationID, soe.ArtifactID, o.ExecutionID AS ObservationExecutionID, a.ExecutionID AS ArtifactExecutionID
FROM dbo.ScoreObservationEvidence AS soe
JOIN dbo.ScoreObservation AS o ON o.ScoreObservationID = soe.ScoreObservationID
JOIN dbo.Artifact AS a ON a.ArtifactID = soe.ArtifactID
WHERE o.ExecutionID <> a.ExecutionID;

-- 7. Other connections (informational; #10 is enforced in code, so this only confirms nothing else is writing).
--    SSMS's own Object Explorer and IntelliSense connections appear here and are harmless. Stop ShepherdAPI and any
--    other writer first; a non-SSMS row, or any row with open_transaction_count > 0, should be resolved.
SELECT session_id, login_name, host_name, program_name, status, open_transaction_count, last_request_end_time
FROM sys.dm_exec_sessions
WHERE database_id = DB_ID() AND is_user_process = 1 AND session_id <> @@SPID
ORDER BY session_id;

-- 8. V11 readiness. upgrade/003 recreates [artifact-bytelength-greater-equal-zero] as CHECK ([ByteLength] >= 0),
--    WITH CHECK, so an Artifact with a negative length would make it fail (the V10 constraint never rejected one).
SELECT ArtifactID, ExecutionID, ByteLength, 'negative ByteLength; upgrade/003 would fail' AS problem
FROM dbo.Artifact
WHERE ByteLength < 0;

-- 8b. Where V11 is already applied, FinalizationException rows may exist only for locked versions (re-running the
--     scripts is harmless; this catches a row written behind the guard's back). Empty on a V10 database.
IF OBJECT_ID(N'dbo.FinalizationException', N'U') IS NOT NULL
    EXEC (N'SELECT fe.ExperimentVersionID, fe.ExecutionID, ''exception row on an unlocked version'' AS problem
            FROM dbo.FinalizationException AS fe JOIN dbo.ExperimentVersion AS ev ON ev.ExperimentVersionID = fe.ExperimentVersionID
            WHERE ev.LockedUTC IS NULL;');
