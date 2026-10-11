CREATE TABLE [authorization].[RestaurantWaiterCredentials]
(
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [SecretSalt] VARBINARY(32) NOT NULL,
    [SecretHash] VARBINARY(32) NOT NULL,
    [SecretIterations] INT NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedByUserId] UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [PK_RestaurantWaiterCredentials] PRIMARY KEY ([UserId]),
    CONSTRAINT [FK_RestaurantWaiterCredentials_User] FOREIGN KEY ([UserId],[TenantId])
        REFERENCES [dbo].[AppUsers] ([UserId],[TenantId]),
    CONSTRAINT [FK_RestaurantWaiterCredentials_UpdatedBy] FOREIGN KEY ([UpdatedByUserId],[TenantId])
        REFERENCES [dbo].[AppUsers] ([UserId],[TenantId]),
    CONSTRAINT [CK_RestaurantWaiterCredentials_Iterations] CHECK ([SecretIterations]>=100000)
);
