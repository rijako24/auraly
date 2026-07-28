CREATE TABLE [dbo].[Promotions] (
    [PromotionId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [Description] NVARCHAR(1000) NULL,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [StartsAtUtc] DATETIME2 NULL,
    [EndsAtUtc] DATETIME2 NULL,
    [Priority] INT NOT NULL DEFAULT 0,
    [IsCombinable] BIT NOT NULL DEFAULT 0,
    [CouponCode] NVARCHAR(80) NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_Promotions_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION
);

GO

CREATE INDEX [IX_Promotions_BusinessId] ON [dbo].[Promotions] ([BusinessId]);
GO
CREATE INDEX [IX_Promotions_BusinessId_Active_Window]
    ON [dbo].[Promotions] ([BusinessId], [IsActive], [StartsAtUtc], [EndsAtUtc]);
GO
CREATE INDEX [IX_Promotions_BusinessId_CouponCode]
    ON [dbo].[Promotions] ([BusinessId], [CouponCode])
    WHERE [CouponCode] IS NOT NULL;
GO
