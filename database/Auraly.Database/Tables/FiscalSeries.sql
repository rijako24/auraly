CREATE TABLE [dbo].[FiscalSeries]
(
    [SeriesId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [DeviceId] UNIQUEIDENTIFIER NULL,
    [EmitterKind] NVARCHAR(16) NOT NULL,
    [FiscalAuthorizationId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentType] NVARCHAR(32) NOT NULL,
    [Prefix] NVARCHAR(16) NOT NULL,
    [RangeStart] BIGINT NOT NULL,
    [RangeEnd] BIGINT NOT NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_FiscalSeries] PRIMARY KEY CLUSTERED ([SeriesId]),
    CONSTRAINT [FK_FiscalSeries_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_FiscalSeries_EnrolledDevices] FOREIGN KEY ([DeviceId]) REFERENCES [dbo].[EnrolledDevices] ([DeviceId]),
    CONSTRAINT [FK_FiscalSeries_FiscalAuthorizations] FOREIGN KEY ([FiscalAuthorizationId]) REFERENCES [dbo].[FiscalAuthorizations] ([FiscalAuthorizationId]),
    CONSTRAINT [CK_FiscalSeries_Emitter] CHECK (
        ([EmitterKind]=N'Server' AND [DeviceId] IS NULL)
        OR ([EmitterKind]=N'Device' AND [DeviceId] IS NOT NULL)),
    CONSTRAINT [CK_FiscalSeries_Range] CHECK ([RangeStart] > 0 AND [RangeEnd] >= [RangeStart])
);
GO

CREATE UNIQUE INDEX [UX_FiscalSeries_Online]
    ON [dbo].[FiscalSeries] ([BusinessId],[DocumentType],[FiscalAuthorizationId],[Prefix])
    WHERE [EmitterKind]=N'Server' AND [DeviceId] IS NULL AND [IsActive]=1;
GO

CREATE UNIQUE INDEX [UX_FiscalSeries_Device]
    ON [dbo].[FiscalSeries] ([BusinessId],[DeviceId],[DocumentType])
    WHERE [EmitterKind]=N'Device' AND [DeviceId] IS NOT NULL AND [IsActive]=1;
GO

CREATE INDEX [IX_FiscalSeries_Business_Device]
    ON [dbo].[FiscalSeries] ([BusinessId], [DeviceId]);
GO
