-- V11: correct the Artifact.ByteLength CHECK constraint.
-- Up to V10, [artifact-bytelength-greater-equal-zero] was CHECK (datalength([ByteLength]) >= 0). DATALENGTH of a bigint
-- is always 8, so it accepted any value, including negative lengths; only the API enforced ByteLength >= 0.
-- The constraint is dropped and recreated, under the same name, as CHECK ([ByteLength] >= 0), WITH CHECK, so existing
-- rows are validated (the script fails, changing nothing, if a row has a negative length; pre-deploy-audit.sql reports
-- such rows first).
-- Re-runnable: the old definition is dropped only when it is present, and the new one is added only when missing.
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'artifact-bytelength-greater-equal-zero'
           AND parent_object_id = OBJECT_ID(N'dbo.Artifact')
           AND definition <> N'([ByteLength]>=(0))')
    ALTER TABLE dbo.Artifact DROP CONSTRAINT [artifact-bytelength-greater-equal-zero];
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'artifact-bytelength-greater-equal-zero'
               AND parent_object_id = OBJECT_ID(N'dbo.Artifact'))
    ALTER TABLE dbo.Artifact WITH CHECK
        ADD CONSTRAINT [artifact-bytelength-greater-equal-zero] CHECK ([ByteLength] >= 0);
GO
