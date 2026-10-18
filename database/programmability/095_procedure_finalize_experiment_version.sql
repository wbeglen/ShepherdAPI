-- Issue #2: ExperimentVersion finalization, atomically. The procedure owns the transaction (XACT_ABORT ON,
-- structured TRY/CATCH): any failure leaves no changes from that attempt.
--
-- Sequence:
--   1. Acquire the EXCLUSIVE parent-version lock (Issue #6). It waits for in-flight controlled writers and
--      blocks new ones until commit.
--   2. The version must exist (@Found = 0 otherwise; the API maps that to 404) and be unlocked      -> 50100
--   3. No ScoreObservationSnapshot rows may already exist for the version                          -> 50507
--   4. Completeness. The configured phases are the distinct PhaseIDs of the version's ScoreRules with
--      RowState = 'A' (ScoreRule.Active is deliberately ignored everywhere):
--        the version has Executions                                                                -> 50500
--        every Execution has EndUTC                                                                -> 50501
--        every Execution has at least one observation                                              -> 50502
--        every Execution's phase is a configured phase                                             -> 50503
--        every configured phase has at least one observation                                       -> 50504
--        every configured phase's active rules total exactly 100.00                                -> 50505
--   5. Observation integrity: each observation's rule belongs to the Execution's version and phase, and
--      ObservedScore lies within the rule's MinScore..MaxScore inclusive                           -> 50506
--   6. One snapshot row per ScoreObservation (all raters, no calculation), inserted with the session-context
--      token set so dbo.trg_ScoreObservationSnapshot_Guard admits them.
--   7. LockedUTC on the version and SnapshotUTC on every row are ONE database-generated UTC timestamp.
--   8. Commit.
-- Each failed check raises its own catalog error naming the offending Executions, phases or observations: the
-- message ends with a machine-readable suffix "[Name=1,2,3;Total=n]" (at most 50 ids) which the API turns into
-- ProblemDetails members. Re-runnable (CREATE OR ALTER).
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE OR ALTER PROCEDURE dbo.usp_FinalizeExperimentVersion
    @ExperimentVersionID bigint,
    @Found bit OUTPUT,
    @LockedUTC datetime2(3) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Found = 0;
    SET @LockedUTC = NULL;

    DECLARE @ids nvarchar(max), @total int, @more nvarchar(40), @msg nvarchar(2048);

    BEGIN TRY
        BEGIN TRANSACTION;

        -- 1-2. Exclusive lock, then existence and lock state.
        DECLARE @currentLocked datetime2(3);
        EXEC dbo.usp_AcquireVersionLock @ExperimentVersionID, 1, @Found OUTPUT, @currentLocked OUTPUT;

        IF @Found = 0
        BEGIN
            ROLLBACK TRANSACTION;
            RETURN;
        END

        IF @currentLocked IS NOT NULL
        BEGIN
            SET @msg = CONCAT(N'ExperimentVersion ', @ExperimentVersionID, N' is already locked; it cannot be finalized again.');
            THROW 50100, @msg, 1;
        END

        -- 3. Pre-existing snapshot rows.
        SELECT @total = COUNT(*) FROM dbo.ScoreObservationSnapshot WHERE ExperimentVersionID = @ExperimentVersionID;
        IF @total > 0
        BEGIN
            SET @msg = CONCAT(N'ExperimentVersion ', @ExperimentVersionID, N' already has ', @total,
                N' ScoreObservationSnapshot row(s); finalization will not run over existing snapshots. [Total=', @total, N']');
            THROW 50507, @msg, 1;
        END

        -- Working sets, read under the exclusive lock.
        DECLARE @Execs TABLE (ExecutionID bigint NOT NULL PRIMARY KEY, PhaseID int NOT NULL, EndUTC datetime2(3) NULL);
        INSERT INTO @Execs (ExecutionID, PhaseID, EndUTC)
            SELECT e.ExecutionID, e.PhaseID, e.EndUTC
            FROM dbo.Execution AS e
            JOIN dbo.Iteration AS i ON i.IterationID = e.IterationID
            WHERE i.ExperimentVersionID = @ExperimentVersionID;

        DECLARE @Obs TABLE (ScoreObservationID bigint NOT NULL PRIMARY KEY, ExecutionID bigint NOT NULL, ScoreRuleID int NOT NULL, ObservedScore decimal(5, 2) NOT NULL);
        INSERT INTO @Obs (ScoreObservationID, ExecutionID, ScoreRuleID, ObservedScore)
            SELECT so.ScoreObservationID, so.ExecutionID, so.ScoreRuleID, so.ObservedScore
            FROM dbo.ScoreObservation AS so
            JOIN @Execs AS e ON e.ExecutionID = so.ExecutionID;

        DECLARE @Phases TABLE (PhaseID int NOT NULL PRIMARY KEY, RuleCount int NOT NULL, TotalWeight decimal(38, 2) NOT NULL);
        INSERT INTO @Phases (PhaseID, RuleCount, TotalWeight)
            SELECT PhaseID, COUNT(*), SUM(WeightPercent)
            FROM dbo.ScoreRule
            WHERE ExperimentVersionID = @ExperimentVersionID AND RowState = 'A'
            GROUP BY PhaseID;

        -- 4a. The version has Executions.
        IF NOT EXISTS (SELECT 1 FROM @Execs)
        BEGIN
            SET @msg = CONCAT(N'ExperimentVersion ', @ExperimentVersionID, N' has no Executions; there is nothing to finalize.');
            THROW 50500, @msg, 1;
        END

        -- 4b. Every Execution has EndUTC.
        SELECT @total = COUNT(*) FROM @Execs WHERE EndUTC IS NULL;
        IF @total > 0
        BEGIN
            SELECT @ids = STRING_AGG(CAST(ExecutionID AS nvarchar(20)), N',') WITHIN GROUP (ORDER BY ExecutionID)
            FROM (SELECT TOP (50) ExecutionID FROM @Execs WHERE EndUTC IS NULL ORDER BY ExecutionID) AS t;
            SET @more = CASE WHEN @total > 50 THEN CONCAT(N' (and ', @total - 50, N' more)') ELSE N'' END;
            SET @msg = CONCAT(N'ExperimentVersion ', @ExperimentVersionID, N' cannot be finalized: ', @total,
                N' Execution(s) have no EndUTC: ', @ids, @more, N'. [ExecutionIDs=', @ids, N';Total=', @total, N']');
            THROW 50501, @msg, 1;
        END

        -- 4c. Every Execution has at least one observation.
        SELECT @total = COUNT(*) FROM @Execs AS e WHERE NOT EXISTS (SELECT 1 FROM @Obs AS o WHERE o.ExecutionID = e.ExecutionID);
        IF @total > 0
        BEGIN
            SELECT @ids = STRING_AGG(CAST(ExecutionID AS nvarchar(20)), N',') WITHIN GROUP (ORDER BY ExecutionID)
            FROM (SELECT TOP (50) e.ExecutionID FROM @Execs AS e
                  WHERE NOT EXISTS (SELECT 1 FROM @Obs AS o WHERE o.ExecutionID = e.ExecutionID) ORDER BY e.ExecutionID) AS t;
            SET @more = CASE WHEN @total > 50 THEN CONCAT(N' (and ', @total - 50, N' more)') ELSE N'' END;
            SET @msg = CONCAT(N'ExperimentVersion ', @ExperimentVersionID, N' cannot be finalized: ', @total,
                N' Execution(s) have no ScoreObservation: ', @ids, @more, N'. [ExecutionIDs=', @ids, N';Total=', @total, N']');
            THROW 50502, @msg, 1;
        END

        -- 4d. Every Execution's phase is a configured phase.
        SELECT @total = COUNT(*) FROM @Execs AS e WHERE NOT EXISTS (SELECT 1 FROM @Phases AS p WHERE p.PhaseID = e.PhaseID);
        IF @total > 0
        BEGIN
            SELECT @ids = STRING_AGG(CAST(ExecutionID AS nvarchar(20)), N',') WITHIN GROUP (ORDER BY ExecutionID)
            FROM (SELECT TOP (50) e.ExecutionID FROM @Execs AS e
                  WHERE NOT EXISTS (SELECT 1 FROM @Phases AS p WHERE p.PhaseID = e.PhaseID) ORDER BY e.ExecutionID) AS t;
            DECLARE @phaseIds nvarchar(max) =
            (
                SELECT STRING_AGG(CAST(PhaseID AS nvarchar(20)), N',') WITHIN GROUP (ORDER BY PhaseID)
                FROM (SELECT DISTINCT TOP (50) e.PhaseID FROM @Execs AS e
                      WHERE NOT EXISTS (SELECT 1 FROM @Phases AS p WHERE p.PhaseID = e.PhaseID) ORDER BY e.PhaseID) AS t
            );
            SET @more = CASE WHEN @total > 50 THEN CONCAT(N' (and ', @total - 50, N' more)') ELSE N'' END;
            SET @msg = CONCAT(N'ExperimentVersion ', @ExperimentVersionID, N' cannot be finalized: ', @total,
                N' Execution(s) are in phase(s) with no active ScoreRule (phase ', @phaseIds, N'): ', @ids, @more,
                N'. [ExecutionIDs=', @ids, N';PhaseIDs=', @phaseIds, N';Total=', @total, N']');
            THROW 50503, @msg, 1;
        END

        -- 4e. Every configured phase has at least one observation.
        SELECT @total = COUNT(*) FROM @Phases AS p
        WHERE NOT EXISTS (SELECT 1 FROM @Obs AS o JOIN @Execs AS e ON e.ExecutionID = o.ExecutionID WHERE e.PhaseID = p.PhaseID);
        IF @total > 0
        BEGIN
            SELECT @ids = STRING_AGG(CAST(PhaseID AS nvarchar(20)), N',') WITHIN GROUP (ORDER BY PhaseID)
            FROM (SELECT TOP (50) p.PhaseID FROM @Phases AS p
                  WHERE NOT EXISTS (SELECT 1 FROM @Obs AS o JOIN @Execs AS e ON e.ExecutionID = o.ExecutionID WHERE e.PhaseID = p.PhaseID)
                  ORDER BY p.PhaseID) AS t;
            SET @more = CASE WHEN @total > 50 THEN CONCAT(N' (and ', @total - 50, N' more)') ELSE N'' END;
            SET @msg = CONCAT(N'ExperimentVersion ', @ExperimentVersionID, N' cannot be finalized: ', @total,
                N' configured phase(s) have no ScoreObservation: ', @ids, @more, N'. [PhaseIDs=', @ids, N';Total=', @total, N']');
            THROW 50504, @msg, 1;
        END

        -- 4f. Every configured phase's active rules total exactly 100.00.
        SELECT @total = COUNT(*) FROM @Phases WHERE TotalWeight <> 100.00;
        IF @total > 0
        BEGIN
            SELECT @ids = STRING_AGG(CAST(PhaseID AS nvarchar(20)), N',') WITHIN GROUP (ORDER BY PhaseID)
            FROM (SELECT TOP (50) PhaseID FROM @Phases WHERE TotalWeight <> 100.00 ORDER BY PhaseID) AS t;
            DECLARE @weights nvarchar(max) =
            (
                SELECT STRING_AGG(CONCAT(N'phase ', PhaseID, N' = ', TotalWeight), N'; ') WITHIN GROUP (ORDER BY PhaseID)
                FROM (SELECT TOP (10) PhaseID, TotalWeight FROM @Phases WHERE TotalWeight <> 100.00 ORDER BY PhaseID) AS t
            );
            SET @more = CASE WHEN @total > 50 THEN CONCAT(N' (and ', @total - 50, N' more)') ELSE N'' END;
            SET @msg = CONCAT(N'ExperimentVersion ', @ExperimentVersionID, N' cannot be finalized: ', @total,
                N' configured phase(s) do not have active rules totaling exactly 100.00 (', @weights, N'): ', @ids, @more,
                N'. [PhaseIDs=', @ids, N';Total=', @total, N']');
            THROW 50505, @msg, 1;
        END

        -- 5. Observation integrity.
        SELECT @total = COUNT(*)
        FROM @Obs AS o
        JOIN @Execs AS e ON e.ExecutionID = o.ExecutionID
        JOIN dbo.ScoreRule AS r ON r.ScoreRuleID = o.ScoreRuleID
        WHERE r.ExperimentVersionID <> @ExperimentVersionID OR r.PhaseID <> e.PhaseID
           OR o.ObservedScore < r.MinScore OR o.ObservedScore > r.MaxScore;
        IF @total > 0
        BEGIN
            SELECT @ids = STRING_AGG(CAST(ScoreObservationID AS nvarchar(20)), N',') WITHIN GROUP (ORDER BY ScoreObservationID)
            FROM (SELECT TOP (50) o.ScoreObservationID
                  FROM @Obs AS o
                  JOIN @Execs AS e ON e.ExecutionID = o.ExecutionID
                  JOIN dbo.ScoreRule AS r ON r.ScoreRuleID = o.ScoreRuleID
                  WHERE r.ExperimentVersionID <> @ExperimentVersionID OR r.PhaseID <> e.PhaseID
                     OR o.ObservedScore < r.MinScore OR o.ObservedScore > r.MaxScore
                  ORDER BY o.ScoreObservationID) AS t;
            SET @more = CASE WHEN @total > 50 THEN CONCAT(N' (and ', @total - 50, N' more)') ELSE N'' END;
            SET @msg = CONCAT(N'ExperimentVersion ', @ExperimentVersionID, N' cannot be finalized: ', @total,
                N' ScoreObservation(s) have a ScoreRule of another version or phase, or a score outside the rule''s range: ',
                @ids, @more, N'. [ScoreObservationIDs=', @ids, N';Total=', @total, N']');
            THROW 50506, @msg, 1;
        END

        -- 6-7. Snapshot, then lock, with one database-generated timestamp.
        DECLARE @now datetime2(3) = SYSUTCDATETIME();

        EXEC sys.sp_set_session_context @key = N'ShepherdAPI.FinalizingVersionID', @value = @ExperimentVersionID;

        INSERT INTO dbo.ScoreObservationSnapshot
            (ExperimentVersionID, ExecutionID, PhaseID, ScoreCategoryID, ScoreRuleID, ScoreObservationID,
             ObservedScore, MinScore, MaxScore, WeightPercent, HigherIsBetter, SnapshotUTC)
        SELECT @ExperimentVersionID, e.ExecutionID, e.PhaseID, r.ScoreCategoryID, r.ScoreRuleID, o.ScoreObservationID,
               o.ObservedScore, r.MinScore, r.MaxScore, r.WeightPercent, c.HigherIsBetter, @now
        FROM @Obs AS o
        JOIN @Execs AS e ON e.ExecutionID = o.ExecutionID
        JOIN dbo.ScoreRule AS r ON r.ScoreRuleID = o.ScoreRuleID
        JOIN dbo.ScoreCategory AS c ON c.ScoreCategoryID = r.ScoreCategoryID;

        EXEC sys.sp_set_session_context @key = N'ShepherdAPI.FinalizingVersionID', @value = NULL;

        UPDATE dbo.ExperimentVersion SET LockedUTC = @now WHERE ExperimentVersionID = @ExperimentVersionID;

        -- 8. Commit.
        COMMIT TRANSACTION;
        SET @LockedUTC = @now;
    END TRY
    BEGIN CATCH
        -- The token is session state, not transactional: always clear it so a pooled connection never keeps it.
        EXEC sys.sp_set_session_context @key = N'ShepherdAPI.FinalizingVersionID', @value = NULL;

        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        SET @LockedUTC = NULL;
        THROW;
    END CATCH
END
GO
