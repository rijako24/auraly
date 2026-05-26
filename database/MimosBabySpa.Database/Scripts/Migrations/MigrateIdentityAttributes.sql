-- MigrateIdentityAttributes.sql — IdentityAttributesJson on Conversations and Leads

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.Conversations') AND name = N'IdentityAttributesJson'
)
BEGIN
    ALTER TABLE dbo.Conversations ADD IdentityAttributesJson NVARCHAR(MAX) NULL;
    PRINT 'Column IdentityAttributesJson added to Conversations.';
END

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.Leads') AND name = N'IdentityAttributesJson'
)
BEGIN
    ALTER TABLE dbo.Leads ADD IdentityAttributesJson NVARCHAR(MAX) NULL;
    PRINT 'Column IdentityAttributesJson added to Leads.';
END
