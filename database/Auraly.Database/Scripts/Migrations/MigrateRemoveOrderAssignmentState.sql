SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID(N'[dbo].[Orders]', N'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Orders_BusinessId_DeliveryAssignmentStatus' AND object_id = OBJECT_ID(N'[dbo].[Orders]'))
        DROP INDEX [IX_Orders_BusinessId_DeliveryAssignmentStatus] ON [dbo].[Orders];

    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Orders_DeliveryAssignmentStatus')
        ALTER TABLE [dbo].[Orders] DROP CONSTRAINT [CK_Orders_DeliveryAssignmentStatus];

    IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_Orders_DeliveryAssignmentStatus')
        ALTER TABLE [dbo].[Orders] DROP CONSTRAINT [DF_Orders_DeliveryAssignmentStatus];

    IF COL_LENGTH(N'[dbo].[Orders]', N'DeliveryAssignmentStatus') IS NOT NULL
        ALTER TABLE [dbo].[Orders] DROP COLUMN [DeliveryAssignmentStatus];

    IF COL_LENGTH(N'[dbo].[Orders]', N'DeliveryExternalEscalationAttemptId') IS NOT NULL
        ALTER TABLE [dbo].[Orders] DROP COLUMN [DeliveryExternalEscalationAttemptId];

    IF COL_LENGTH(N'[dbo].[Orders]', N'DeliveryAssigneeKeySnapshot') IS NOT NULL
        ALTER TABLE [dbo].[Orders] DROP COLUMN [DeliveryAssigneeKeySnapshot];

    IF COL_LENGTH(N'[dbo].[Orders]', N'DeliveryAssigneeNameSnapshot') IS NOT NULL
        ALTER TABLE [dbo].[Orders] DROP COLUMN [DeliveryAssigneeNameSnapshot];

    IF COL_LENGTH(N'[dbo].[Orders]', N'DeliveryAssigneeRoleSnapshot') IS NOT NULL
        ALTER TABLE [dbo].[Orders] DROP COLUMN [DeliveryAssigneeRoleSnapshot];

    IF COL_LENGTH(N'[dbo].[Orders]', N'DeliveryAssigneePhoneSnapshot') IS NOT NULL
        ALTER TABLE [dbo].[Orders] DROP COLUMN [DeliveryAssigneePhoneSnapshot];

    IF COL_LENGTH(N'[dbo].[Orders]', N'DeliveryAssignmentRequestedAt') IS NOT NULL
        ALTER TABLE [dbo].[Orders] DROP COLUMN [DeliveryAssignmentRequestedAt];

    IF COL_LENGTH(N'[dbo].[Orders]', N'DeliveryAssignmentAcceptedAt') IS NOT NULL
        ALTER TABLE [dbo].[Orders] DROP COLUMN [DeliveryAssignmentAcceptedAt];

    IF COL_LENGTH(N'[dbo].[Orders]', N'DeliveryAssignmentDeclinedAt') IS NOT NULL
        ALTER TABLE [dbo].[Orders] DROP COLUMN [DeliveryAssignmentDeclinedAt];
END
GO
