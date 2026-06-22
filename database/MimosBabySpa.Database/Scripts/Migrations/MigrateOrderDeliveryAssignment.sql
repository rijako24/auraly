SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF OBJECT_ID(N'[dbo].[Orders]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'[dbo].[Orders]', N'DeliveryAssignmentStatus') IS NULL
        ALTER TABLE [dbo].[Orders] ADD [DeliveryAssignmentStatus] INT NOT NULL CONSTRAINT [DF_Orders_DeliveryAssignmentStatus] DEFAULT 0;

    IF COL_LENGTH(N'[dbo].[Orders]', N'DeliveryExternalEscalationAttemptId') IS NULL
        ALTER TABLE [dbo].[Orders] ADD [DeliveryExternalEscalationAttemptId] UNIQUEIDENTIFIER NULL;

    IF COL_LENGTH(N'[dbo].[Orders]', N'DeliveryAssigneeKeySnapshot') IS NULL
        ALTER TABLE [dbo].[Orders] ADD [DeliveryAssigneeKeySnapshot] NVARCHAR(100) NULL;

    IF COL_LENGTH(N'[dbo].[Orders]', N'DeliveryAssigneeNameSnapshot') IS NULL
        ALTER TABLE [dbo].[Orders] ADD [DeliveryAssigneeNameSnapshot] NVARCHAR(200) NULL;

    IF COL_LENGTH(N'[dbo].[Orders]', N'DeliveryAssigneeRoleSnapshot') IS NULL
        ALTER TABLE [dbo].[Orders] ADD [DeliveryAssigneeRoleSnapshot] NVARCHAR(100) NULL;

    IF COL_LENGTH(N'[dbo].[Orders]', N'DeliveryAssigneePhoneSnapshot') IS NULL
        ALTER TABLE [dbo].[Orders] ADD [DeliveryAssigneePhoneSnapshot] NVARCHAR(50) NULL;

    IF COL_LENGTH(N'[dbo].[Orders]', N'DeliveryAssignmentRequestedAt') IS NULL
        ALTER TABLE [dbo].[Orders] ADD [DeliveryAssignmentRequestedAt] DATETIME2 NULL;

    IF COL_LENGTH(N'[dbo].[Orders]', N'DeliveryAssignmentAcceptedAt') IS NULL
        ALTER TABLE [dbo].[Orders] ADD [DeliveryAssignmentAcceptedAt] DATETIME2 NULL;

    IF COL_LENGTH(N'[dbo].[Orders]', N'DeliveryAssignmentDeclinedAt') IS NULL
        ALTER TABLE [dbo].[Orders] ADD [DeliveryAssignmentDeclinedAt] DATETIME2 NULL;

    IF COL_LENGTH(N'[dbo].[Orders]', N'DeliveryAssignmentTimedOutAt') IS NULL
        ALTER TABLE [dbo].[Orders] ADD [DeliveryAssignmentTimedOutAt] DATETIME2 NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Orders_DeliveryAssignmentStatus')
        EXEC(N'ALTER TABLE [dbo].[Orders] ADD CONSTRAINT [CK_Orders_DeliveryAssignmentStatus] CHECK ([DeliveryAssignmentStatus] IN (0, 1, 2, 3, 4));');

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Orders_BusinessId_DeliveryAssignmentStatus' AND object_id = OBJECT_ID(N'[dbo].[Orders]'))
        EXEC(N'CREATE INDEX [IX_Orders_BusinessId_DeliveryAssignmentStatus] ON [dbo].[Orders] ([BusinessId], [DeliveryAssignmentStatus]);');
END;