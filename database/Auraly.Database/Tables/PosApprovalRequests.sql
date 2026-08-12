CREATE TABLE [dbo].[PosApprovalRequests]
(
    [ApprovalRequestId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [DeviceId] UNIQUEIDENTIFIER NULL,
    [WorkSessionId] UNIQUEIDENTIFIER NULL,
    [DraftId] UNIQUEIDENTIFIER NOT NULL,
    [LineId] UNIQUEIDENTIFIER NULL,
    [PermissionResource] NVARCHAR(100) NOT NULL,
    [RequestedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [ContextJson] NVARCHAR(MAX) NOT NULL,
    [Status] NVARCHAR(20) NOT NULL,
    [RequestedAt] DATETIMEOFFSET(7) NOT NULL,
    [ExpiresAt] DATETIMEOFFSET(7) NOT NULL,
    [DecidedByUserId] UNIQUEIDENTIFIER NULL,
    [DecisionMethod] NVARCHAR(20) NULL,
    [DecidedAt] DATETIMEOFFSET(7) NULL,
    [ReservedAt] DATETIMEOFFSET(7) NULL,
    [ConsumedAt] DATETIMEOFFSET(7) NULL,
    [ConsumedByOperationId] UNIQUEIDENTIFIER NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_PosApprovalRequests] PRIMARY KEY CLUSTERED ([ApprovalRequestId]),
    CONSTRAINT [FK_PosApprovalRequests_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [FK_PosApprovalRequests_Business] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [FK_PosApprovalRequests_Device] FOREIGN KEY ([DeviceId]) REFERENCES [dbo].[EnrolledDevices]([DeviceId]),
    CONSTRAINT [FK_PosApprovalRequests_WorkSession] FOREIGN KEY ([WorkSessionId]) REFERENCES [dbo].[WorkSessions]([WorkSessionId]),
    CONSTRAINT [FK_PosApprovalRequests_RequestedBy] FOREIGN KEY ([RequestedByUserId]) REFERENCES [dbo].[AppUsers]([UserId]),
    CONSTRAINT [FK_PosApprovalRequests_DecidedBy] FOREIGN KEY ([DecidedByUserId]) REFERENCES [dbo].[AppUsers]([UserId]),
    CONSTRAINT [CK_PosApprovalRequests_Status] CHECK ([Status] IN (N'Pending',N'Approved',N'Reserved',N'Rejected',N'Expired',N'Consumed')),
    CONSTRAINT [CK_PosApprovalRequests_Decision] CHECK (
        ([Status]=N'Pending' AND [DecidedByUserId] IS NULL AND [DecidedAt] IS NULL AND [DecisionMethod] IS NULL)
        OR ([Status] IN(N'Approved',N'Reserved',N'Rejected',N'Consumed') AND [DecidedByUserId] IS NOT NULL AND [DecidedAt] IS NOT NULL AND [DecisionMethod] IN(N'Remote',N'LocalSecret'))
        OR ([Status]=N'Expired' AND [ConsumedAt] IS NULL)),
    CONSTRAINT [CK_PosApprovalRequests_Consumption] CHECK (
        ([Status]=N'Reserved' AND [ReservedAt] IS NOT NULL AND [ConsumedAt] IS NULL AND [ConsumedByOperationId] IS NOT NULL)
        OR ([Status]=N'Consumed' AND [ReservedAt] IS NOT NULL AND [ConsumedAt] IS NOT NULL AND [ConsumedByOperationId] IS NOT NULL)
        OR ([Status] NOT IN(N'Reserved',N'Consumed') AND [ReservedAt] IS NULL AND [ConsumedAt] IS NULL AND [ConsumedByOperationId] IS NULL)),
    CONSTRAINT [CK_PosApprovalRequests_ContextJson] CHECK (ISJSON([ContextJson])=1)
);
GO

CREATE INDEX [IX_PosApprovalRequests_SupervisorInbox]
    ON [dbo].[PosApprovalRequests]([TenantId],[BusinessId],[Status],[RequestedAt] DESC)
    INCLUDE([PermissionResource],[DraftId],[LineId],[RequestedByUserId],[ExpiresAt]);
GO

CREATE UNIQUE INDEX [UX_PosApprovalRequests_Consumption]
    ON [dbo].[PosApprovalRequests]([ConsumedByOperationId])
    WHERE [ConsumedByOperationId] IS NOT NULL;
GO
