CREATE TABLE [dbo].[PromotionApplications] (
    [PromotionApplicationId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [PromotionId] UNIQUEIDENTIFIER NOT NULL,
    [OrderId] UNIQUEIDENTIFIER NULL,
    [ReservationId] UNIQUEIDENTIFIER NULL,
    [PaymentTransactionId] UNIQUEIDENTIFIER NULL,
    [DiscountAmount] DECIMAL(18, 2) NOT NULL DEFAULT 0,
    [SnapshotJson] NVARCHAR(MAX) NOT NULL DEFAULT N'{}',
    [AppliedAtUtc] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [FK_PromotionApplications_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_PromotionApplications_Promotions] FOREIGN KEY ([PromotionId])
        REFERENCES [dbo].[Promotions] ([PromotionId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_PromotionApplications_Orders] FOREIGN KEY ([OrderId])
        REFERENCES [dbo].[Orders] ([OrderId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_PromotionApplications_Reservations] FOREIGN KEY ([ReservationId])
        REFERENCES [dbo].[Reservations] ([ReservationId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_PromotionApplications_PaymentTransactions] FOREIGN KEY ([PaymentTransactionId])
        REFERENCES [dbo].[PaymentTransactions] ([PaymentTransactionId])
        ON DELETE NO ACTION
);

GO

CREATE INDEX [IX_PromotionApplications_BusinessId] ON [dbo].[PromotionApplications] ([BusinessId]);
GO
CREATE INDEX [IX_PromotionApplications_PromotionId] ON [dbo].[PromotionApplications] ([PromotionId]);
GO
CREATE INDEX [IX_PromotionApplications_OrderId] ON [dbo].[PromotionApplications] ([OrderId]);
GO
CREATE INDEX [IX_PromotionApplications_ReservationId] ON [dbo].[PromotionApplications] ([ReservationId]);
GO
CREATE INDEX [IX_PromotionApplications_PaymentTransactionId] ON [dbo].[PromotionApplications] ([PaymentTransactionId]);
GO
