-- V11: the unknown-end acknowledgments passed to dbo.usp_FinalizeExperimentVersion (095): one row per Execution whose
-- end time is unknown and is accepted at finalization, with its mandatory explanation. Re-runnable (created only if
-- missing; a table type cannot be altered while a procedure references it).
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

IF TYPE_ID(N'dbo.FinalizationExceptionList') IS NULL
    CREATE TYPE dbo.FinalizationExceptionList AS TABLE
    (
        ExecutionID bigint NOT NULL PRIMARY KEY,
        Explanation nvarchar(2000) NOT NULL
    );
GO
