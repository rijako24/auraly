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
    [ValidUntil] DATETIMEOFFSET(7) NULL,
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
    CONSTRAINT [CK_SupervisorCredentials_Validity] CHECK ([ValidUntil] IS NULL OR [ValidUntil] > [CreatedAt]),
    CONSTRAINT [CK_SupervisorCredentials_State] CHECK (
        ([IsActive]=1 AND [RevokedByUserId] IS NULL AND [RevokedAt] IS NULL)
        OR
        ([IsActive]=0 AND [RevokedByUserId] IS NOT NULL AND [RevokedAt] IS NOT NULL))
);
GO

CREATE UNIQUE INDEX [UX_SupervisorCredentials_User_Active]
    ON [dbo].[SupervisorCredentials] ([UserId]) WHERE [IsActive]=1;
GO
