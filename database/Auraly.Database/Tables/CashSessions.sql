CREATE TABLE [dbo].[CashSessions]
(
    [CashSessionId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [LocationId] UNIQUEIDENTIFIER NOT NULL,
    [RegisterId] UNIQUEIDENTIFIER NOT NULL,
    [OpenedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [OpenedAt] DATETIMEOFFSET(7) NOT NULL,
    [OpeningFloat] DECIMAL(19,4) NOT NULL,
    [Status] NVARCHAR(16) NOT NULL,
    [ClosedByUserId] UNIQUEIDENTIFIER NULL,
    [OpenIdempotencyKey] NVARCHAR(128) NOT NULL,
    [ClosedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_CashSessions] PRIMARY KEY CLUSTERED ([CashSessionId]),
    CONSTRAINT [FK_CashSessions_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_CashSessions_Locations] FOREIGN KEY ([LocationId]) REFERENCES [dbo].[BusinessLocations] ([LocationId]),
    CONSTRAINT [FK_CashSessions_Registers] FOREIGN KEY ([RegisterId]) REFERENCES [dbo].[CashRegisters] ([RegisterId]),
    CONSTRAINT [FK_CashSessions_OpenedBy] FOREIGN KEY ([OpenedByUserId]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [FK_CashSessions_ClosedBy] FOREIGN KEY ([ClosedByUserId]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [CK_CashSessions_OpeningFloat] CHECK ([OpeningFloat] >= 0),
    CONSTRAINT [CK_CashSessions_Status] CHECK (
        ([Status]=N'Open' AND [ClosedByUserId] IS NULL AND [ClosedAt] IS NULL)
        OR
        ([Status]=N'Closed' AND [ClosedByUserId] IS NOT NULL AND [ClosedAt] IS NOT NULL))
);
GO

CREATE UNIQUE INDEX [UX_CashSessions_Register_Open]
    ON [dbo].[CashSessions] ([RegisterId]) WHERE [Status]=N'Open';
GO

CREATE UNIQUE INDEX [UX_CashSessions_Register_OpenKey]
    ON [dbo].[CashSessions] ([RegisterId],[OpenIdempotencyKey]);
GO

CREATE INDEX [IX_CashSessions_Business_Register_Opened]
    ON [dbo].[CashSessions] ([BusinessId],[RegisterId],[OpenedAt] DESC);
GO

CREATE TABLE [dbo].[CashierShifts]
(
    [CashierShiftId] UNIQUEIDENTIFIER NOT NULL,
    [CashSessionId] UNIQUEIDENTIFIER NOT NULL,
    [RegisterId] UNIQUEIDENTIFIER NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [StartedAt] DATETIMEOFFSET(7) NOT NULL,
    [Status] NVARCHAR(16) NOT NULL,
    [EndedAt] DATETIMEOFFSET(7) NULL,
    [EndReason] NVARCHAR(32) NULL,
    [EndedByUserId] UNIQUEIDENTIFIER NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_CashierShifts] PRIMARY KEY CLUSTERED ([CashierShiftId]),
    CONSTRAINT [FK_CashierShifts_Sessions] FOREIGN KEY ([CashSessionId]) REFERENCES [dbo].[CashSessions] ([CashSessionId]),
    CONSTRAINT [FK_CashierShifts_Registers] FOREIGN KEY ([RegisterId]) REFERENCES [dbo].[CashRegisters] ([RegisterId]),
    CONSTRAINT [FK_CashierShifts_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [FK_CashierShifts_EndedBy] FOREIGN KEY ([EndedByUserId]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [UQ_CashierShifts_Session_Shift] UNIQUE ([CashSessionId],[CashierShiftId]),
    CONSTRAINT [CK_CashierShifts_Status] CHECK (
        ([Status]=N'Active' AND [EndedAt] IS NULL AND [EndReason] IS NULL AND [EndedByUserId] IS NULL)
        OR
        ([Status]=N'Ended' AND [EndedAt] IS NOT NULL AND
         [EndReason] IN (N'Handoff',N'SessionClosed',N'UserChanged') AND
         [EndedByUserId] IS NOT NULL))
);
GO

CREATE UNIQUE INDEX [UX_CashierShifts_Register_User_Active]
    ON [dbo].[CashierShifts] ([RegisterId],[UserId]) WHERE [Status]=N'Active';
GO

CREATE INDEX [IX_CashierShifts_Session_User_Started]
    ON [dbo].[CashierShifts] ([CashSessionId],[UserId],[StartedAt]);
GO

CREATE TABLE [dbo].[CashCountNumberCursors]
(
    [RegisterId] UNIQUEIDENTIFIER NOT NULL,
    [NextConsecutive] BIGINT NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_CashCountNumberCursors] PRIMARY KEY CLUSTERED ([RegisterId]),
    CONSTRAINT [FK_CashCountNumberCursors_Registers] FOREIGN KEY ([RegisterId]) REFERENCES [dbo].[CashRegisters] ([RegisterId]),
    CONSTRAINT [CK_CashCountNumberCursors_Next] CHECK ([NextConsecutive] > 0)
);
GO
