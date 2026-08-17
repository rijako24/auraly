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

-- Scripts de pre-despliegue
-- Aquí puedes agregar validaciones, limpieza, etc.

PRINT 'Pre-deployment script ejecutado correctamente.';

GO
