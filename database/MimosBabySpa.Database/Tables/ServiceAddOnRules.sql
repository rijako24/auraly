CREATE TABLE [dbo].[ServiceAddOnRules] (
    [ServiceAddOnRuleId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [AddOnServiceId] UNIQUEIDENTIFIER NOT NULL,
    [CompatibleServiceId] UNIQUEIDENTIFIER NULL,
    [DisplayOrder] INT NOT NULL DEFAULT 1,
    CONSTRAINT [FK_ServiceAddOnRules_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_ServiceAddOnRules_AddOnService] FOREIGN KEY ([AddOnServiceId])
        REFERENCES [dbo].[Services] ([ServiceId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_ServiceAddOnRules_CompatibleService] FOREIGN KEY ([CompatibleServiceId])
        REFERENCES [dbo].[Services] ([ServiceId])
        ON DELETE NO ACTION
);

GO

CREATE UNIQUE INDEX [IX_ServiceAddOnRules_BusinessId_AddOnServiceId_CompatibleServiceId] 
    ON [dbo].[ServiceAddOnRules] ([BusinessId], [AddOnServiceId], [CompatibleServiceId])
    WHERE [CompatibleServiceId] IS NOT NULL;

GO

CREATE INDEX [IX_ServiceAddOnRules_BusinessId] ON [dbo].[ServiceAddOnRules] ([BusinessId]);

GO

CREATE INDEX [IX_ServiceAddOnRules_AddOnServiceId] ON [dbo].[ServiceAddOnRules] ([AddOnServiceId]);

GO

CREATE INDEX [IX_ServiceAddOnRules_CompatibleServiceId] ON [dbo].[ServiceAddOnRules] ([CompatibleServiceId]);

GO
