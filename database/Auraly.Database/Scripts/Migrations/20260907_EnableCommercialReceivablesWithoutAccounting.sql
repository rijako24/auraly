IF COL_LENGTH(N'dbo.AccountingPostingJobs',N'AccountingEntryRequired') IS NULL
BEGIN
    ALTER TABLE dbo.AccountingPostingJobs
      ADD AccountingEntryRequired BIT NOT NULL
          CONSTRAINT DF_AccountingPostingJobs_AccountingEntryRequired DEFAULT (1) WITH VALUES;
END;
GO

IF EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.AccountingPostingJobs')
      AND name=N'CK_AccountingPostingJobs_Status'
)
    ALTER TABLE dbo.AccountingPostingJobs
      DROP CONSTRAINT CK_AccountingPostingJobs_Status;
GO

ALTER TABLE dbo.AccountingPostingJobs WITH CHECK
  ADD CONSTRAINT CK_AccountingPostingJobs_Status
  CHECK (Status IN
    (N'Pending',N'AccountingPendingConfiguration',N'CommercialEffectsApplied',N'Posted'));
GO
