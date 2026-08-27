CREATE TABLE [reporting].[PayrollReportDefinitions]
(
    [Code] NVARCHAR(64) NOT NULL,
    [Name] NVARCHAR(160) NOT NULL,
    [Description] NVARCHAR(500) NOT NULL,
    [DatasetCode] NVARCHAR(64) NOT NULL,
    [ColumnsJson] NVARCHAR(MAX) NOT NULL,
    [SortOrder] INT NOT NULL,
    [IsActive] BIT NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_PayrollReportDefinitions] PRIMARY KEY ([Code]),
    CONSTRAINT [CK_PayrollReportDefinitions_Dataset] CHECK ([DatasetCode] IN
      (N'PayrollSummary',N'PayrollReceipt',N'ConceptDetail',N'Deductions',N'EmployerContributions',
       N'Provisions',N'LaborCost',N'Payments',N'ElectronicStatus',N'IncomeAndWithholding')),
    CONSTRAINT [CK_PayrollReportDefinitions_Columns] CHECK (ISJSON([ColumnsJson])=1),
    CONSTRAINT [CK_PayrollReportDefinitions_Order] CHECK ([SortOrder] BETWEEN 0 AND 9999)
);
GO
