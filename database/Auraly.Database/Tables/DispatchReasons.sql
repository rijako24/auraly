CREATE TABLE [dbo].[DispatchReasons] (
    [DispatchReasonId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ReasonType] NVARCHAR(32) NOT NULL,
    [Code] NVARCHAR(40) NOT NULL,
    [Name] NVARCHAR(160) NOT NULL,
    [IsSystem] BIT NOT NULL CONSTRAINT [DF_DispatchReasons_IsSystem] DEFAULT (0),
    [IsActive] BIT NOT NULL CONSTRAINT [DF_DispatchReasons_IsActive] DEFAULT (1),
    [DisplayOrder] INT NOT NULL CONSTRAINT [DF_DispatchReasons_DisplayOrder] DEFAULT (0),
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_DispatchReasons] PRIMARY KEY ([DispatchReasonId]),
    CONSTRAINT [FK_DispatchReasons_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [CK_DispatchReasons_Type] CHECK ([ReasonType] IN (N'NotDelivered',N'DeliveryReturn')),
    CONSTRAINT [CK_DispatchReasons_Order] CHECK ([DisplayOrder] BETWEEN 0 AND 9999)
);
GO
CREATE UNIQUE INDEX [UX_DispatchReasons_Business_Type_Code] ON [dbo].[DispatchReasons] ([BusinessId],[ReasonType],[Code]);
GO
CREATE INDEX [IX_DispatchReasons_Business_Type_Active] ON [dbo].[DispatchReasons] ([BusinessId],[ReasonType],[IsActive],[DisplayOrder]);
GO
