CREATE TABLE [sales].[RestaurantDigitalMenus]
(
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [PublicCode] CHAR(32) NOT NULL,
    [Title] NVARCHAR(100) NOT NULL,
    [Introduction] NVARCHAR(500) NULL,
    [IsPublished] BIT NOT NULL CONSTRAINT [DF_RestaurantDigitalMenus_IsPublished] DEFAULT 0,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_RestaurantDigitalMenus] PRIMARY KEY ([BusinessId]),
    CONSTRAINT [UQ_RestaurantDigitalMenus_PublicCode] UNIQUE ([PublicCode]),
    CONSTRAINT [FK_RestaurantDigitalMenus_Business] FOREIGN KEY ([BusinessId],[TenantId])
        REFERENCES [dbo].[Businesses] ([BusinessId],[TenantId]),
    CONSTRAINT [CK_RestaurantDigitalMenus_Title] CHECK (LEN(LTRIM(RTRIM([Title])))>0)
);
GO
