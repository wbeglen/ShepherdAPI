-- Issue #4: the design-freeze check. Once ANY Execution exists for an ExperimentVersion, its design
-- configuration (Iterations, Parameters, ScoreRules, and the version's Name/VersionLabel/EffectiveDateUTC/
-- Description) is frozen for good, whatever the Execution's phase, completion state or observations.
-- This script holds only the shared check; the guard triggers (030/040/050) and the controlled-write
-- procedures (080) call it AFTER taking the parent-version lock, so it is serialized against Execution
-- creation. Re-runnable (CREATE OR ALTER).
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE OR ALTER FUNCTION dbo.fn_VersionHasExecutions (@ExperimentVersionID bigint)
RETURNS bit
AS
BEGIN
    RETURN CASE WHEN EXISTS
    (
        SELECT 1
        FROM dbo.Execution AS e
        JOIN dbo.Iteration AS i ON i.IterationID = e.IterationID
        WHERE i.ExperimentVersionID = @ExperimentVersionID
    ) THEN 1 ELSE 0 END;
END
GO

-- Rejects the statement (error 50400, design-frozen) if any listed version has an Execution.
-- The caller must already hold the version locks (usp_AssertVersionsUnlocked / usp_LockVersions).
CREATE OR ALTER PROCEDURE dbo.usp_AssertDesignNotFrozen
    @Versions dbo.VersionIdList READONLY
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @frozen nvarchar(max) =
    (
        SELECT STRING_AGG(CAST(v.ExperimentVersionID AS nvarchar(20)), N', ') WITHIN GROUP (ORDER BY v.ExperimentVersionID)
        FROM @Versions AS v
        WHERE dbo.fn_VersionHasExecutions(v.ExperimentVersionID) = 1
    );

    IF @frozen IS NOT NULL
    BEGIN
        DECLARE @message nvarchar(2048) = CONCAT(
            N'The design of ExperimentVersion ', @frozen,
            N' is frozen because it has Executions; its Iterations, Parameters, ScoreRules and descriptive fields can no longer change.');
        THROW 50400, @message, 1;
    END
END
GO
