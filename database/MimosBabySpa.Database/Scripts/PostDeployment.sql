/*
Post-Deployment Script Template
--------------------------------------------------------------------------------------
 This file contains SQL statements that will be appended to the build script.
 Use SQLCMD syntax to include a file in the post-deployment script.
 Example:      :r .\myfile.sql
 Use SQLCMD syntax to reference a variable in the post-deployment script.
 Example:      :setvar TableName MyTable
               SELECT * FROM [$(TableName)]
--------------------------------------------------------------------------------------
*/

-- Scripts de post-despliegue
-- Esquema: columna requerida por EF / admin (lista de conversaciones)
:r .\033_ConversationsLastMessage.sql
:r .\SeedServiceCategoriesForNewBusinesses.sql
:r .\SeedAdminAttachmentsAndKey8ForBusiness2222.sql
:r .\035_FlowNodeCatalog.sql
:r .\036_FlowModernNodes.sql

PRINT 'Post-deployment script ejecutado correctamente.';

GO
