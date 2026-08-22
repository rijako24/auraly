CREATE TABLE [dbo].[ComplianceReportDefinitions]
(
    [AuthorityCode] NVARCHAR(24) NOT NULL,
    [TaxYear] SMALLINT NOT NULL,
    [FormatCode] NVARCHAR(24) NOT NULL,
    [FormatVersion] SMALLINT NOT NULL,
    [Name] NVARCHAR(240) NOT NULL,
    [ReportKind] NVARCHAR(24) NOT NULL,
    [ResolutionNumber] NVARCHAR(80) NOT NULL,
    [ResolutionDate] DATE NOT NULL,
    [TechnicalAnnex] NVARCHAR(80) NOT NULL,
    [SourceUrl] NVARCHAR(1000) NOT NULL,
    [SourceSha256] CHAR(64) NOT NULL,
    [SchemaJson] NVARCHAR(MAX) NOT NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_ComplianceReportDefinitions_Active] DEFAULT (1),
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_ComplianceReportDefinitions] PRIMARY KEY
      ([AuthorityCode],[TaxYear],[FormatCode],[FormatVersion]),
    CONSTRAINT [CK_ComplianceReportDefinitions_Year] CHECK ([TaxYear] BETWEEN 2000 AND 2200),
    CONSTRAINT [CK_ComplianceReportDefinitions_Version] CHECK ([FormatVersion]>0),
    CONSTRAINT [CK_ComplianceReportDefinitions_Kind] CHECK ([ReportKind] IN (N'Exogenous',N'FiscalDraft')),
    CONSTRAINT [CK_ComplianceReportDefinitions_Hash] CHECK ([SourceSha256] NOT LIKE '%[^0-9A-F]%'),
    CONSTRAINT [CK_ComplianceReportDefinitions_Schema] CHECK (ISJSON([SchemaJson])=1)
);
GO

CREATE TABLE [dbo].[ComplianceConceptMappings]
(
    [MappingId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NULL,
    [AuthorityCode] NVARCHAR(24) NOT NULL,
    [TaxYear] SMALLINT NOT NULL,
    [FormatCode] NVARCHAR(24) NOT NULL,
    [FormatVersion] SMALLINT NOT NULL,
    [AccountId] UNIQUEIDENTIFIER NOT NULL,
    [ConceptCode] NVARCHAR(24) NOT NULL,
    [TargetField] NVARCHAR(64) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_ComplianceConceptMappings] PRIMARY KEY ([MappingId]),
    CONSTRAINT [FK_ComplianceConceptMappings_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [FK_ComplianceConceptMappings_Business] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [FK_ComplianceConceptMappings_Definition] FOREIGN KEY
      ([AuthorityCode],[TaxYear],[FormatCode],[FormatVersion]) REFERENCES [dbo].[ComplianceReportDefinitions]
      ([AuthorityCode],[TaxYear],[FormatCode],[FormatVersion]),
    CONSTRAINT [FK_ComplianceConceptMappings_Account] FOREIGN KEY ([TenantId],[AccountId]) REFERENCES [dbo].[AccountingAccounts]([TenantId],[AccountId])
);
GO
CREATE UNIQUE INDEX [UX_ComplianceConceptMappings_Scope]
    ON [dbo].[ComplianceConceptMappings]
      ([TenantId],[BusinessId],[AuthorityCode],[TaxYear],[FormatCode],[FormatVersion],[AccountId],[ConceptCode],[TargetField]);
GO

CREATE TABLE [dbo].[ComplianceReportRuns]
(
    [RunId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [AuthorityCode] NVARCHAR(24) NOT NULL,
    [TaxYear] SMALLINT NOT NULL,
    [FormatCode] NVARCHAR(24) NOT NULL,
    [FormatVersion] SMALLINT NOT NULL,
    [PeriodFrom] DATE NOT NULL,
    [PeriodTo] DATE NOT NULL,
    [Status] NVARCHAR(16) NOT NULL,
    [ResolutionNumber] NVARCHAR(80) NOT NULL,
    [SourceUrl] NVARCHAR(1000) NOT NULL,
    [SourceSha256] CHAR(64) NOT NULL,
    [MappingSnapshotJson] NVARCHAR(MAX) NOT NULL,
    [RowCount] INT NOT NULL,
    [ControlTotal] DECIMAL(19,4) NOT NULL,
    [CreatedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [CompletedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_ComplianceReportRuns] PRIMARY KEY ([RunId]),
    CONSTRAINT [FK_ComplianceReportRuns_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [FK_ComplianceReportRuns_Business] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [FK_ComplianceReportRuns_User] FOREIGN KEY ([CreatedByUserId]) REFERENCES [dbo].[AppUsers]([UserId]),
    CONSTRAINT [FK_ComplianceReportRuns_Definition] FOREIGN KEY
      ([AuthorityCode],[TaxYear],[FormatCode],[FormatVersion]) REFERENCES [dbo].[ComplianceReportDefinitions]
      ([AuthorityCode],[TaxYear],[FormatCode],[FormatVersion]),
    CONSTRAINT [CK_ComplianceReportRuns_Range] CHECK ([PeriodTo]>=[PeriodFrom]),
    CONSTRAINT [CK_ComplianceReportRuns_Status] CHECK ([Status] IN (N'Blocked',N'Ready')),
    CONSTRAINT [CK_ComplianceReportRuns_Mapping] CHECK (ISJSON([MappingSnapshotJson])=1),
    CONSTRAINT [CK_ComplianceReportRuns_Count] CHECK ([RowCount]>=0)
);
GO
CREATE INDEX [IX_ComplianceReportRuns_Scope]
    ON [dbo].[ComplianceReportRuns]([TenantId],[BusinessId],[TaxYear],[FormatCode],[CreatedAt] DESC);
GO

CREATE TABLE [dbo].[ComplianceReportRows]
(
    [RunId] UNIQUEIDENTIFIER NOT NULL,
    [RowNumber] INT NOT NULL,
    [PartyId] UNIQUEIDENTIFIER NULL,
    [ConceptCode] NVARCHAR(24) NOT NULL,
    [RowJson] NVARCHAR(MAX) NOT NULL,
    [ControlAmount] DECIMAL(19,4) NOT NULL,
    CONSTRAINT [PK_ComplianceReportRows] PRIMARY KEY ([RunId],[RowNumber]),
    CONSTRAINT [FK_ComplianceReportRows_Run] FOREIGN KEY ([RunId]) REFERENCES [dbo].[ComplianceReportRuns]([RunId]),
    CONSTRAINT [FK_ComplianceReportRows_Party] FOREIGN KEY ([PartyId]) REFERENCES [dbo].[Parties]([PartyId]),
    CONSTRAINT [CK_ComplianceReportRows_Number] CHECK ([RowNumber]>0),
    CONSTRAINT [CK_ComplianceReportRows_Json] CHECK (ISJSON([RowJson])=1)
);
GO

CREATE TABLE [dbo].[ComplianceReportValidations]
(
    [RunId] UNIQUEIDENTIFIER NOT NULL,
    [ValidationNumber] INT NOT NULL,
    [Severity] NVARCHAR(12) NOT NULL,
    [Code] NVARCHAR(64) NOT NULL,
    [Message] NVARCHAR(1000) NOT NULL,
    [PartyId] UNIQUEIDENTIFIER NULL,
    [AccountId] UNIQUEIDENTIFIER NULL,
    CONSTRAINT [PK_ComplianceReportValidations] PRIMARY KEY ([RunId],[ValidationNumber]),
    CONSTRAINT [FK_ComplianceReportValidations_Run] FOREIGN KEY ([RunId]) REFERENCES [dbo].[ComplianceReportRuns]([RunId]),
    CONSTRAINT [CK_ComplianceReportValidations_Severity] CHECK ([Severity] IN (N'Error',N'Warning'))
);
GO

CREATE TABLE [dbo].[ComplianceReportArtifacts]
(
    [RunId] UNIQUEIDENTIFIER NOT NULL,
    [FileName] NVARCHAR(200) NOT NULL,
    [MediaType] NVARCHAR(100) NOT NULL,
    [Content] VARBINARY(MAX) NOT NULL,
    [ContentSha256] BINARY(32) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_ComplianceReportArtifacts] PRIMARY KEY ([RunId]),
    CONSTRAINT [FK_ComplianceReportArtifacts_Run] FOREIGN KEY ([RunId]) REFERENCES [dbo].[ComplianceReportRuns]([RunId])
);
GO
