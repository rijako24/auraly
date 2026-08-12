CREATE TABLE [dbo].[DocumentSeries]
(
    [DocumentSeriesId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [DeviceId] UNIQUEIDENTIFIER NULL,
    [DocumentType] NVARCHAR(32) NOT NULL,
    [Prefix] NVARCHAR(8) NOT NULL,
    [SeriesCode] NVARCHAR(16) NOT NULL,
    [Padding] TINYINT NOT NULL,
    [RangeStart] BIGINT NOT NULL,
    [RangeEnd] BIGINT NOT NULL,
    [IsOfflineCapable] BIT NOT NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_DocumentSeries] PRIMARY KEY CLUSTERED ([DocumentSeriesId]),
    CONSTRAINT [FK_DocumentSeries_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_DocumentSeries_EnrolledDevices] FOREIGN KEY ([DeviceId])
        REFERENCES [dbo].[EnrolledDevices] ([DeviceId]),
    CONSTRAINT [UQ_DocumentSeries_Business_Type_Code]
        UNIQUE ([BusinessId], [DocumentType], [Prefix], [SeriesCode]),
    CONSTRAINT [CK_DocumentSeries_Emitter] CHECK (
        ([DeviceId] IS NULL AND [IsOfflineCapable]=0 AND ([IsActive]=0 OR [SeriesCode]=N'00'))
        OR ([DeviceId] IS NOT NULL AND [SeriesCode]<>N'00' AND [IsOfflineCapable]=1)),
    CONSTRAINT [CK_DocumentSeries_Padding] CHECK ([Padding] = 8),
    CONSTRAINT [CK_DocumentSeries_Range] CHECK ([RangeStart] > 0 AND [RangeEnd] >= [RangeStart] AND [RangeEnd] <= 99999999)
);
GO

CREATE INDEX [IX_DocumentSeries_Business_Type_Active]
    ON [dbo].[DocumentSeries] ([BusinessId], [DocumentType], [IsActive]);
GO

CREATE INDEX [IX_DocumentSeries_Device_Type]
    ON [dbo].[DocumentSeries] ([DeviceId], [DocumentType])
    WHERE [DeviceId] IS NOT NULL;
GO