IF COL_LENGTH(N'dbo.PricePublicationAudits', N'PublicationOrigin') IS NULL
BEGIN
    ALTER TABLE dbo.PricePublicationAudits
        ADD PublicationOrigin NVARCHAR(24) NOT NULL
            CONSTRAINT DF_PricePublicationAudits_PublicationOrigin DEFAULT N'ReceiptProposal' WITH VALUES;
END;
GO

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_PricePublicationAudits_Proposals' AND parent_object_id=OBJECT_ID(N'dbo.PricePublicationAudits'))
    ALTER TABLE dbo.PricePublicationAudits DROP CONSTRAINT FK_PricePublicationAudits_Proposals;
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.PricePublicationAudits') AND name=N'ProposalId' AND is_nullable=0)
    ALTER TABLE dbo.PricePublicationAudits ALTER COLUMN ProposalId UNIQUEIDENTIFIER NULL;
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.PricePublicationAudits') AND name=N'CostBasisAmount' AND is_nullable=0)
    ALTER TABLE dbo.PricePublicationAudits ALTER COLUMN CostBasisAmount DECIMAL(19,6) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_PricePublicationAudits_Proposals' AND parent_object_id=OBJECT_ID(N'dbo.PricePublicationAudits'))
    ALTER TABLE dbo.PricePublicationAudits WITH CHECK
        ADD CONSTRAINT FK_PricePublicationAudits_Proposals FOREIGN KEY (ProposalId)
        REFERENCES dbo.PriceRevisionProposals (PriceRevisionProposalId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'CK_PricePublicationAudits_Origin' AND parent_object_id=OBJECT_ID(N'dbo.PricePublicationAudits'))
    ALTER TABLE dbo.PricePublicationAudits WITH CHECK
        ADD CONSTRAINT CK_PricePublicationAudits_Origin
        CHECK (PublicationOrigin IN (N'ReceiptProposal',N'Manual'));
GO