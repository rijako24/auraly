CREATE TABLE [fiscal].[DianNumberingRanges]
(
    [DianNumberingRangeId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [AuthorizationNumber] NVARCHAR(64) NOT NULL,
    [ResolutionDate] DATE NULL,
    [Prefix] NVARCHAR(16) NOT NULL,
    [RangeStart] BIGINT NOT NULL,
    [RangeEnd] BIGINT NOT NULL,
    [ValidFrom] DATE NOT NULL,
    [ValidUntil] DATE NOT NULL,
    [ProtectedTechnicalKey] VARBINARY(MAX) NOT NULL,
    [AssignedBusinessId] UNIQUEIDENTIFIER NULL,
    [AssignedAt] DATETIMEOFFSET(7) NULL,
    [AssignedByUserId] UNIQUEIDENTIFIER NULL,
    [ImportedAt] DATETIMEOFFSET(7) NOT NULL,
    [LastSeenAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_DianNumberingRanges] PRIMARY KEY ([DianNumberingRangeId]),
    CONSTRAINT [FK_DianNumberingRanges_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [FK_DianNumberingRanges_Businesses] FOREIGN KEY ([AssignedBusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [FK_DianNumberingRanges_Users] FOREIGN KEY ([AssignedByUserId]) REFERENCES [dbo].[AppUsers]([UserId]),
    CONSTRAINT [UQ_DianNumberingRanges_Tenant_Range] UNIQUE
        ([TenantId],[AuthorizationNumber],[Prefix],[RangeStart],[RangeEnd]),
    CONSTRAINT [CK_DianNumberingRanges_Range] CHECK ([RangeStart] > 0 AND [RangeEnd] >= [RangeStart]),
    CONSTRAINT [CK_DianNumberingRanges_Validity] CHECK ([ValidUntil] >= [ValidFrom]),
    CONSTRAINT [CK_DianNumberingRanges_Assignment] CHECK
        (([AssignedBusinessId] IS NULL AND [AssignedAt] IS NULL AND [AssignedByUserId] IS NULL)
         OR ([AssignedBusinessId] IS NOT NULL AND [AssignedAt] IS NOT NULL AND [AssignedByUserId] IS NOT NULL))
);
GO

CREATE INDEX [IX_DianNumberingRanges_Tenant_Available]
    ON [fiscal].[DianNumberingRanges]([TenantId],[AssignedBusinessId],[ValidUntil],[Prefix]);
GO
