CREATE TABLE [dbo].[DocumentWithholdingSnapshots]
(
    [DocumentId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentType] NVARCHAR(32) NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [GrossAmount] DECIMAL(19,4) NOT NULL,
    [WithholdingTotal] DECIMAL(19,4) NOT NULL,
    [NetAmount] DECIMAL(19,4) NOT NULL,
    [RecognizedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_DocumentWithholdingSnapshots] PRIMARY KEY ([DocumentId],[DocumentType]),
    CONSTRAINT [FK_DocumentWithholdingSnapshots_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [CK_DocumentWithholdingSnapshots_Reconcile] CHECK ([GrossAmount]-[WithholdingTotal]=[NetAmount]),
    CONSTRAINT [CK_DocumentWithholdingSnapshots_Amounts] CHECK ([GrossAmount]>=0 AND [WithholdingTotal]>=0 AND [NetAmount]>=0)
);
GO
CREATE TABLE [dbo].[DocumentWithholdingLines]
(
    [DocumentId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentType] NVARCHAR(32) NOT NULL,
    [LineNumber] INT NOT NULL,
    [RuleId] UNIQUEIDENTIFIER NOT NULL,
    [RuleVersion] INT NOT NULL,
    [RuleCode] NVARCHAR(32) NOT NULL,
    [Name] NVARCHAR(120) NOT NULL,
    [Kind] NVARCHAR(32) NOT NULL,
    [BaseKind] NVARCHAR(32) NOT NULL,
    [TaxableBase] DECIMAL(19,4) NOT NULL,
    [Rate] DECIMAL(9,6) NOT NULL,
    [Amount] DECIMAL(19,4) NOT NULL,
    [JurisdictionCode] NVARCHAR(16) NULL,
    CONSTRAINT [PK_DocumentWithholdingLines] PRIMARY KEY ([DocumentId],[DocumentType],[LineNumber]),
    CONSTRAINT [FK_DocumentWithholdingLines_Snapshot] FOREIGN KEY ([DocumentId],[DocumentType])
      REFERENCES [dbo].[DocumentWithholdingSnapshots] ([DocumentId],[DocumentType]),
    CONSTRAINT [FK_DocumentWithholdingLines_Rule] FOREIGN KEY ([RuleId],[RuleVersion])
      REFERENCES [dbo].[WithholdingRules] ([RuleId],[Version]),
    CONSTRAINT [CK_DocumentWithholdingLines_Amount] CHECK ([TaxableBase]>=0 AND [Amount]>0 AND [Rate]>0)
);
GO
