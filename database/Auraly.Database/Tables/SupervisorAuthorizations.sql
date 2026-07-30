CREATE TABLE [dbo].[SupervisorCredentials]
(
    [CredentialId] UNIQUEIDENTIFIER NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [SecretSalt] VARBINARY(32) NOT NULL,
    [SecretHash] VARBINARY(32) NOT NULL,
    [SecretIterations] INT NOT NULL,
    [IsActive] BIT NOT NULL,
    [CreatedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RevokedByUserId] UNIQUEIDENTIFIER NULL,
    [RevokedAt] DATETIMEOFFSET(7) NULL,
    CONSTRAINT [PK_SupervisorCredentials] PRIMARY KEY CLUSTERED ([CredentialId]),
    CONSTRAINT [FK_SupervisorCredentials_User] FOREIGN KEY ([UserId])
        REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [FK_SupervisorCredentials_CreatedBy] FOREIGN KEY ([CreatedByUserId])
        REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [FK_SupervisorCredentials_RevokedBy] FOREIGN KEY ([RevokedByUserId])
        REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [CK_SupervisorCredentials_Iterations] CHECK ([SecretIterations] >= 100000),
    CONSTRAINT [CK_SupervisorCredentials_State] CHECK (
        ([IsActive]=1 AND [RevokedByUserId] IS NULL AND [RevokedAt] IS NULL)
        OR
        ([IsActive]=0 AND [RevokedByUserId] IS NOT NULL AND [RevokedAt] IS NOT NULL))
);
GO

CREATE UNIQUE INDEX [UX_SupervisorCredentials_User_Active]
    ON [dbo].[SupervisorCredentials] ([UserId]) WHERE [IsActive]=1;
GO

CREATE TABLE [dbo].[SupervisorAuthorizationGrants]
(
    [AuthorizationGrantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [RegisterId] UNIQUEIDENTIFIER NOT NULL,
    [RequestedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [AuthorizedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [PermissionCode] NVARCHAR(100) NOT NULL,
    [TokenHash] BINARY(32) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [ExpiresAt] DATETIMEOFFSET(7) NOT NULL,
    [ConsumedAt] DATETIMEOFFSET(7) NULL,
    [ConsumedByUserId] UNIQUEIDENTIFIER NULL,
    CONSTRAINT [PK_SupervisorAuthorizationGrants]
        PRIMARY KEY CLUSTERED ([AuthorizationGrantId]),
    CONSTRAINT [FK_SupervisorAuthorizationGrants_Business] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_SupervisorAuthorizationGrants_Register] FOREIGN KEY ([RegisterId])
        REFERENCES [dbo].[CashRegisters] ([RegisterId]),
    CONSTRAINT [FK_SupervisorAuthorizationGrants_RequestedBy] FOREIGN KEY ([RequestedByUserId])
        REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [FK_SupervisorAuthorizationGrants_AuthorizedBy] FOREIGN KEY ([AuthorizedByUserId])
        REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [FK_SupervisorAuthorizationGrants_ConsumedBy] FOREIGN KEY ([ConsumedByUserId])
        REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [CK_SupervisorAuthorizationGrants_Expiry] CHECK ([ExpiresAt] > [CreatedAt]),
    CONSTRAINT [CK_SupervisorAuthorizationGrants_Consumed] CHECK (
        ([ConsumedAt] IS NULL AND [ConsumedByUserId] IS NULL)
        OR
        ([ConsumedAt] IS NOT NULL AND [ConsumedByUserId] IS NOT NULL))
);
GO

CREATE UNIQUE INDEX [UX_SupervisorAuthorizationGrants_TokenHash]
    ON [dbo].[SupervisorAuthorizationGrants] ([TokenHash]);
GO

CREATE INDEX [IX_SupervisorAuthorizationGrants_Register_Requester]
    ON [dbo].[SupervisorAuthorizationGrants]
       ([RegisterId],[RequestedByUserId],[CreatedAt] DESC);
GO
