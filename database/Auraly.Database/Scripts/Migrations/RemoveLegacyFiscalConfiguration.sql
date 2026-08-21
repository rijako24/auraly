-- The fiscal onboarding flow replaces the former manual numbering model.
-- Keep this migration explicit because database publication intentionally uses
-- DropObjectsNotInSource=False.

DROP TABLE IF EXISTS [dbo].[SalesInvoiceNumberingConfigurations];

IF COL_LENGTH(N'dbo.FiscalAuthorizations', N'InitialConsecutive') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.CK_FiscalAuthorizations_Range', N'C') IS NOT NULL
        ALTER TABLE [dbo].[FiscalAuthorizations]
            DROP CONSTRAINT [CK_FiscalAuthorizations_Range];

    ALTER TABLE [dbo].[FiscalAuthorizations]
        DROP COLUMN [InitialConsecutive];

    ALTER TABLE [dbo].[FiscalAuthorizations]
        ADD CONSTRAINT [CK_FiscalAuthorizations_Range] CHECK (
            ([AuthorizedRangeStart] IS NULL AND [AuthorizedRangeEnd] IS NULL)
            OR ([AuthorizedRangeStart] > 0
                AND [AuthorizedRangeEnd] >= [AuthorizedRangeStart]));
END;
