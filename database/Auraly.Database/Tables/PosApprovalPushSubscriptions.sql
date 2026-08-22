CREATE TABLE [dbo].[PosApprovalPushSubscriptions]
(
    [SubscriptionId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [Endpoint] NVARCHAR(2000) NOT NULL,
    [EndpointHash] BINARY(32) NOT NULL,
    [P256dh] NVARCHAR(512) NOT NULL,
    [Auth] NVARCHAR(256) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_PosApprovalPushSubscriptions] PRIMARY KEY CLUSTERED ([SubscriptionId]),
    CONSTRAINT [FK_PosApprovalPushSubscriptions_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [FK_PosApprovalPushSubscriptions_Business] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [FK_PosApprovalPushSubscriptions_User] FOREIGN KEY ([UserId]) REFERENCES [dbo].[AppUsers]([UserId]),
    CONSTRAINT [UQ_PosApprovalPushSubscriptions_UserEndpoint] UNIQUE ([UserId],[EndpointHash])
);
GO

CREATE INDEX [IX_PosApprovalPushSubscriptions_Recipients]
    ON [dbo].[PosApprovalPushSubscriptions]([TenantId],[BusinessId],[UserId]);
GO
