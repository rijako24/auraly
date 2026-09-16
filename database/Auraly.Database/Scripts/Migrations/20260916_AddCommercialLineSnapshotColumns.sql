SET XACT_ABORT ON;

IF COL_LENGTH(N'dbo.OrderItems',N'DocumentUnitCost') IS NULL
    ALTER TABLE dbo.OrderItems ADD DocumentUnitCost DECIMAL(19,6) NULL;

IF COL_LENGTH(N'dbo.OrderDraftItems',N'DocumentUnitCost') IS NULL
    ALTER TABLE dbo.OrderDraftItems ADD DocumentUnitCost DECIMAL(19,6) NULL;

IF COL_LENGTH(N'dbo.SalesDraftLines',N'PublicUnitPrice') IS NULL
    ALTER TABLE dbo.SalesDraftLines ADD PublicUnitPrice DECIMAL(18,2) NULL;

IF COL_LENGTH(N'dbo.SalesDraftLines',N'PublicLineTotal') IS NULL
    ALTER TABLE dbo.SalesDraftLines ADD PublicLineTotal DECIMAL(18,2) NULL;
