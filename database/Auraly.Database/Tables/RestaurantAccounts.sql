CREATE TABLE [sales].[RestaurantAccounts]
(
    [AccountId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [TableId] UNIQUEIDENTIFIER NOT NULL,
    [WarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [PriceChannelId] UNIQUEIDENTIFIER NULL,
    [IsCustomerPriceChannel] BIT NOT NULL CONSTRAINT [DF_RestaurantAccounts_IsCustomerPriceChannel] DEFAULT (0),
    [CustomerId] UNIQUEIDENTIFIER NULL,
    [CustomerPartySiteId] UNIQUEIDENTIFIER NULL,
    [ResponsibleUserId] UNIQUEIDENTIFIER NOT NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [Version] BIGINT NOT NULL CONSTRAINT [DF_RestaurantAccounts_Version] DEFAULT (1),
    [OpenedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [InvoicedAt] DATETIMEOFFSET(7) NULL,
    [CancelledAt] DATETIMEOFFSET(7) NULL,
    [ReleasedAt] DATETIMEOFFSET(7) NULL,
    CONSTRAINT [PK_RestaurantAccounts] PRIMARY KEY ([AccountId]),
    CONSTRAINT [UQ_RestaurantAccounts_Business_Id] UNIQUE ([BusinessId],[AccountId]),
    CONSTRAINT [FK_RestaurantAccounts_Table] FOREIGN KEY ([BusinessId],[TableId])
        REFERENCES [sales].[RestaurantTables] ([BusinessId],[TableId]),
    CONSTRAINT [FK_RestaurantAccounts_Business] FOREIGN KEY ([BusinessId],[TenantId])
        REFERENCES [dbo].[Businesses] ([BusinessId],[TenantId]),
    CONSTRAINT [FK_RestaurantAccounts_Warehouse] FOREIGN KEY ([BusinessId],[WarehouseId])
        REFERENCES [dbo].[Warehouses] ([BusinessId],[WarehouseId]),
    CONSTRAINT [FK_RestaurantAccounts_PriceChannel] FOREIGN KEY ([PriceChannelId])
        REFERENCES [dbo].[PriceChannels] ([PriceChannelId]),
    CONSTRAINT [FK_RestaurantAccounts_Customer] FOREIGN KEY ([TenantId],[CustomerId])
        REFERENCES [dbo].[Customers] ([TenantId],[CustomerId]),
    CONSTRAINT [FK_RestaurantAccounts_CustomerSite] FOREIGN KEY ([CustomerPartySiteId])
        REFERENCES [dbo].[PartySites] ([PartySiteId]),
    CONSTRAINT [FK_RestaurantAccounts_ResponsibleUser] FOREIGN KEY ([ResponsibleUserId],[TenantId])
        REFERENCES [dbo].[AppUsers] ([UserId],[TenantId]),
    CONSTRAINT [CK_RestaurantAccounts_Status] CHECK ([Status] IN
        (N'Open',N'Issuing',N'Invoiced',N'Cancelled',N'Released')),
    CONSTRAINT [CK_RestaurantAccounts_Version] CHECK ([Version]>0),
    CONSTRAINT [CK_RestaurantAccounts_CustomerPriceChannel] CHECK
        ([IsCustomerPriceChannel]=0 OR ([CustomerId] IS NOT NULL AND [PriceChannelId] IS NOT NULL)),
    CONSTRAINT [CK_RestaurantAccounts_Terminal] CHECK
        (([Status]=N'Cancelled' AND [CancelledAt] IS NOT NULL AND
          [InvoicedAt] IS NULL AND [ReleasedAt] IS NOT NULL)
         OR ([Status]=N'Released' AND [InvoicedAt] IS NOT NULL AND
             [CancelledAt] IS NULL AND [ReleasedAt] IS NOT NULL)
         OR ([Status] IN (N'Open',N'Issuing') AND [InvoicedAt] IS NULL AND
             [CancelledAt] IS NULL AND [ReleasedAt] IS NULL)
         OR ([Status]=N'Invoiced' AND [InvoicedAt] IS NOT NULL AND
             [CancelledAt] IS NULL AND [ReleasedAt] IS NULL))
);
GO
CREATE UNIQUE INDEX [UX_RestaurantAccounts_TableOccupied]
    ON [sales].[RestaurantAccounts] ([BusinessId],[TableId])
    WHERE [ReleasedAt] IS NULL;
GO
CREATE INDEX [IX_RestaurantAccounts_Business_Status_Updated]
    ON [sales].[RestaurantAccounts] ([BusinessId],[Status],[UpdatedAt] DESC);
GO
