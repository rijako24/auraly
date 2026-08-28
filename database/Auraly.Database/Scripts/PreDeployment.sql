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
:r .\Migrations\20260801_CreateFiscalDocumentRoot.sql
:r .\Migrations\20260802_RemoveCashRegisterContext.sql
:r .\Migrations\20260811_NormalizeGoodsReceiptPresentations.sql
:r .\Migrations\20260811_ExpandAuditAction.sql
:r .\Migrations\20260817_NormalizeAuralyPlatformTenantKey.sql
:r .\Migrations\20260823_MoveBusinessLogoToTenant.sql
:r .\Migrations\20260824_MoveFiscalCredentialsToTenant.sql
:r .\Migrations\20260825_AddPurchaseEvidence.sql
:r .\Migrations\20260828_NormalizePriceChannelValues.sql
:r .\Migrations\MoveDispatchReasonsToOwnedSchema.sql

-- Scripts de pre-despliegue
-- Aquí puedes agregar validaciones, limpieza, etc.

IF OBJECT_ID(N'dbo.PriceChannels', N'U') IS NOT NULL
BEGIN
    UPDATE dbo.PriceChannels
    SET Strategy = N'TieredProductPrice',
        Value = NULL
    WHERE Strategy = N'FixedSpecialPrice';

    -- PercentageBelowBasePrice era una segunda representación del mismo
    -- cálculo que PercentageOverBasePrice con signo negativo. Se normaliza
    -- antes de reemplazar los checks para conservar todos los canales.
    UPDATE dbo.PriceChannels
    SET Strategy = N'PercentageOverBasePrice',
        Value = -ABS(COALESCE(Value, 0))
    WHERE Strategy = N'PercentageBelowBasePrice';
END;

-- La caja conserva cada captura como una línea independiente. El índice
-- histórico por producto impedía agregar el mismo producto dos veces y debe
-- retirarse explícitamente porque DEV publica conservando objetos ajenos al DACPAC.
PRINT 'Pre-deployment script ejecutado correctamente.';

GO
