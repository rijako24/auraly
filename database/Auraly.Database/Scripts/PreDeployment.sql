/*
Pre-Deployment Script Template
--------------------------------------------------------------------------------------
 This file contains SQL statements that will be prepended to the build script.
 Use SQLCMD syntax to include a file in the pre-deployment script.
 Example:      :r .\myfile.sql
 Use SQLCMD syntax to reference a variable in the pre-deployment script.
 Example:      :setvar TableName MyTable
               SELECT * FROM [$(TableName)]
--------------------------------------------------------------------------------------
*/

:r .\Migrations\20260730_CollapseOrganizationScope.sql
GO
:r .\Migrations\20260801_CreateFiscalDocumentRoot.sql
GO
:r .\Migrations\20260802_RemoveCashRegisterContext.sql
GO
:r .\Migrations\20260811_NormalizeGoodsReceiptPresentations.sql
GO
:r .\Migrations\20260811_ExpandAuditAction.sql
GO
:r .\Migrations\20260817_NormalizeAuralyPlatformTenantKey.sql
GO
:r .\Migrations\20260823_MoveBusinessLogoToTenant.sql
GO
:r .\Migrations\20260824_MoveFiscalCredentialsToTenant.sql
GO
:r .\Migrations\20260825_AddPurchaseEvidence.sql
GO
:r .\Migrations\20260828_NormalizePriceChannelValues.sql
GO
:r .\Migrations\20260828_ReplaceAverageCostMarkupWithLatestCostMargin.sql
GO
:r .\Migrations\20260829_BackfillProductTenant.sql
GO
:r .\Migrations\20260829_RemoveFiscalSeriesAllocationState.sql
:r .\Migrations\20260831_EnforceExclusiveUserSessions.sql
GO
:r .\Migrations\20260902_ScopeWorkSessionsByTenant.sql
GO
:r .\Migrations\MoveDispatchReasonsToOwnedSchema.sql
GO

-- Scripts de pre-despliegue
-- Aquí puedes agregar validaciones, limpieza, etc.

IF OBJECT_ID(N'dbo.PriceChannels', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.PriceChannels', N'Strategy') IS NOT NULL
   AND COL_LENGTH(N'dbo.PriceChannels', N'Value') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql N'
        UPDATE dbo.PriceChannels
        SET Strategy = N''TieredProductPrice'', Value = NULL
        WHERE Strategy = N''FixedSpecialPrice'';

        UPDATE dbo.PriceChannels
        SET Strategy = N''PercentageOverBasePrice'',
            Value = -ABS(COALESCE(Value, 0))
        WHERE Strategy = N''PercentageBelowBasePrice'';';
END;

-- La caja conserva cada captura como una línea independiente. El índice
-- histórico por producto impedía agregar el mismo producto dos veces y debe
-- retirarse explícitamente porque DEV publica conservando objetos ajenos al DACPAC.
PRINT 'Pre-deployment script ejecutado correctamente.';

GO
