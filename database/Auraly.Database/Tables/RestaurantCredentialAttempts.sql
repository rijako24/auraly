CREATE TABLE [authorization].[RestaurantCredentialAttempts]
(
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [FailedCount] INT NOT NULL CONSTRAINT [DF_RestaurantCredentialAttempts_FailedCount] DEFAULT (0),
    [LockedUntil] DATETIMEOFFSET(7) NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_RestaurantCredentialAttempts] PRIMARY KEY ([UserId]),
    CONSTRAINT [FK_RestaurantCredentialAttempts_User] FOREIGN KEY ([UserId],[TenantId])
        REFERENCES [dbo].[AppUsers] ([UserId],[TenantId]),
    CONSTRAINT [CK_RestaurantCredentialAttempts_FailedCount] CHECK ([FailedCount]>=0)
);
