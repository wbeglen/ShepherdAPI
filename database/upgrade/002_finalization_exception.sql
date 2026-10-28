-- Finalization with an unknown Execution end time (V11): dbo.FinalizationException.
-- One row per Execution whose end time is unknown and was explicitly accepted when its version was finalized, with
-- a mandatory explanation. Rows are written only by dbo.usp_FinalizeExperimentVersion, in the finalization
-- transaction, with RecordedUTC equal to the version's LockedUTC; dbo.trg_FinalizationException_Guard rejects any
-- other insert and every update or delete (programmability/092).
-- ExperimentVersionID repeats what the Execution already implies, as in dbo.ScoreObservationSnapshot, so the guard can
-- check the finalization token and lock the version directly; the guard also checks that the two agree.
-- Re-runnable: the table and each constraint are created only if they do not already exist.
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

IF OBJECT_ID(N'dbo.FinalizationException', N'U') IS NULL
    CREATE TABLE dbo.FinalizationException
    (
        ExperimentVersionID bigint NOT NULL,
        ExecutionID bigint NOT NULL,
        ExceptionCode varchar(40) NOT NULL,
        Explanation nvarchar(2000) NOT NULL,
        RecordedUTC datetime2(3) NOT NULL,
        CONSTRAINT PK_FinalizationException PRIMARY KEY CLUSTERED (ExecutionID, ExceptionCode)
    );
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_FinalizationException_ExperimentVersion'
               AND parent_object_id = OBJECT_ID(N'dbo.FinalizationException'))
    ALTER TABLE dbo.FinalizationException WITH CHECK
        ADD CONSTRAINT FK_FinalizationException_ExperimentVersion FOREIGN KEY (ExperimentVersionID)
            REFERENCES dbo.ExperimentVersion (ExperimentVersionID);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_FinalizationException_Execution'
               AND parent_object_id = OBJECT_ID(N'dbo.FinalizationException'))
    ALTER TABLE dbo.FinalizationException WITH CHECK
        ADD CONSTRAINT FK_FinalizationException_Execution FOREIGN KEY (ExecutionID)
            REFERENCES dbo.Execution (ExecutionID);
GO

-- The only exception kind so far. New kinds are added by widening this list.
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_FinalizationException_Code'
               AND parent_object_id = OBJECT_ID(N'dbo.FinalizationException'))
    ALTER TABLE dbo.FinalizationException WITH CHECK
        ADD CONSTRAINT CK_FinalizationException_Code CHECK (ExceptionCode IN ('end-time-unknown'));
GO

-- The explanation is mandatory and cannot be blank.
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_FinalizationException_Explanation'
               AND parent_object_id = OBJECT_ID(N'dbo.FinalizationException'))
    ALTER TABLE dbo.FinalizationException WITH CHECK
        ADD CONSTRAINT CK_FinalizationException_Explanation CHECK (LEN(LTRIM(RTRIM(Explanation))) > 0);
GO

-- The read path lists a version's exceptions.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FinalizationException_ExperimentVersionID'
               AND object_id = OBJECT_ID(N'dbo.FinalizationException'))
    CREATE NONCLUSTERED INDEX IX_FinalizationException_ExperimentVersionID
        ON dbo.FinalizationException (ExperimentVersionID);
GO
