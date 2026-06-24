IF OBJECT_ID(N'[dbo].[BusinessAvailabilityBlocks]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[BusinessAvailabilityBlocks] (
        [BusinessAvailabilityBlockId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_BusinessAvailabilityBlocks] PRIMARY KEY DEFAULT NEWID(),
        [BusinessId]                  UNIQUEIDENTIFIER NOT NULL,
        [EmployeeId]                  UNIQUEIDENTIFIER NULL,
        [Date]                        DATE             NOT NULL,
        [StartTime]                   TIME             NULL,
        [EndTime]                     TIME             NULL,
        [Reason]                      NVARCHAR(500)    NOT NULL DEFAULT N'',
        [Source]                      NVARCHAR(50)     NOT NULL DEFAULT N'operations',
        [IsActive]                    BIT              NOT NULL DEFAULT 1,
        [CreatedAt]                   DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
        [UpdatedAt]                   DATETIME2        NULL,
        CONSTRAINT [FK_BusinessAvailabilityBlocks_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]) ON DELETE CASCADE,
        CONSTRAINT [FK_BusinessAvailabilityBlocks_Employees] FOREIGN KEY ([EmployeeId]) REFERENCES [dbo].[Employees] ([EmployeeId]) ON DELETE NO ACTION,
        CONSTRAINT [CK_BusinessAvailabilityBlocks_TimeRange] CHECK ([StartTime] IS NULL OR [EndTime] IS NULL OR [StartTime] < [EndTime])
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BusinessAvailabilityBlocks_BusinessId_Date' AND object_id = OBJECT_ID(N'[dbo].[BusinessAvailabilityBlocks]'))
    CREATE INDEX [IX_BusinessAvailabilityBlocks_BusinessId_Date] ON [dbo].[BusinessAvailabilityBlocks] ([BusinessId], [Date]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BusinessAvailabilityBlocks_BusinessId_EmployeeId_Date' AND object_id = OBJECT_ID(N'[dbo].[BusinessAvailabilityBlocks]'))
    CREATE INDEX [IX_BusinessAvailabilityBlocks_BusinessId_EmployeeId_Date] ON [dbo].[BusinessAvailabilityBlocks] ([BusinessId], [EmployeeId], [Date]);
