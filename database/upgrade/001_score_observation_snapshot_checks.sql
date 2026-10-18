-- Issue #2 (toward the v9 baseline): CHECK constraints on dbo.ScoreObservationSnapshot.
--   * MinScore < MaxScore
--   * WeightPercent between 0 and 100
--   * ObservedScore between MinScore and MaxScore (inclusive)
-- Re-runnable: each constraint is added only if it does not already exist. They are added WITH CHECK, so the
-- script fails (and changes nothing for that constraint) if existing snapshot rows violate it.
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_ScoreObservationSnapshot_MinScore_LT_MaxScore'
               AND parent_object_id = OBJECT_ID(N'dbo.ScoreObservationSnapshot'))
    ALTER TABLE dbo.ScoreObservationSnapshot WITH CHECK
        ADD CONSTRAINT CK_ScoreObservationSnapshot_MinScore_LT_MaxScore CHECK (MinScore < MaxScore);
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_ScoreObservationSnapshot_WeightPercent'
               AND parent_object_id = OBJECT_ID(N'dbo.ScoreObservationSnapshot'))
    ALTER TABLE dbo.ScoreObservationSnapshot WITH CHECK
        ADD CONSTRAINT CK_ScoreObservationSnapshot_WeightPercent CHECK (WeightPercent >= 0 AND WeightPercent <= 100);
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_ScoreObservationSnapshot_ObservedScore_InRange'
               AND parent_object_id = OBJECT_ID(N'dbo.ScoreObservationSnapshot'))
    ALTER TABLE dbo.ScoreObservationSnapshot WITH CHECK
        ADD CONSTRAINT CK_ScoreObservationSnapshot_ObservedScore_InRange CHECK (ObservedScore >= MinScore AND ObservedScore <= MaxScore);
GO
