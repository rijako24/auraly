CREATE TABLE [dbo].[ServiceBundleItems] (
    [ServiceBundleItemId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BundleServiceId] UNIQUEIDENTIFIER NOT NULL,
    [IncludedServiceId] UNIQUEIDENTIFIER NOT NULL,
    [DisplayOrder] INT NOT NULL DEFAULT 1,
    CONSTRAINT [FK_ServiceBundleItems_BundleService] FOREIGN KEY ([BundleServiceId])
        REFERENCES [dbo].[Services] ([ServiceId])
        ON DELETE CASCADE,
    CONSTRAINT [FK_ServiceBundleItems_IncludedService] FOREIGN KEY ([IncludedServiceId])
        REFERENCES [dbo].[Services] ([ServiceId])
        ON DELETE NO ACTION
);

GO

CREATE UNIQUE INDEX [IX_ServiceBundleItems_BundleServiceId_IncludedServiceId] ON [dbo].[ServiceBundleItems] ([BundleServiceId], [IncludedServiceId]);

GO

CREATE INDEX [IX_ServiceBundleItems_IncludedServiceId] ON [dbo].[ServiceBundleItems] ([IncludedServiceId]);

GO
