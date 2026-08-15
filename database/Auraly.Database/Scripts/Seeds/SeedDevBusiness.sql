-- ============================================================

-- Script: SeedDevBusiness

-- Para el negocio 22222222-2222-2222-2222-222222222222:

--   1. Conserva permisos y rol del tenant de demostración sin crear identidades técnicas

--   2. Crea adjuntos (indicaciones, tÃ©rminos)

--   3. Crea adjuntos de confirmaciÃ³n (BusinessAttachments) para messageSequences del agente

-- ContraseÃ±a admin: Admin123!

-- ============================================================



SET QUOTED_IDENTIFIER ON;

SET NOCOUNT ON;



DECLARE @BusinessId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';

DECLARE @TenantId UNIQUEIDENTIFIER;

DECLARE @AdminUserId UNIQUEIDENTIFIER;

DECLARE @AdminRoleId UNIQUEIDENTIFIER;

DECLARE @AttachmentId UNIQUEIDENTIFIER = '8a1ec489-f1ba-4c7c-9576-382dfc9a55f1';

DECLARE @AttachmentId2 UNIQUEIDENTIFIER = '9b2fd590-a2cb-5d8d-a687-493efd0b66a2';



-- Hash BCrypt para "Admin123!" (work factor 12)

DECLARE @PasswordHash NVARCHAR(500) = NULLIF(N'$(BootstrapAdminPasswordHash)', N'');



-- Crear tenant y negocio si no existen

IF NOT EXISTS (SELECT 1 FROM [dbo].[Tenants] WHERE [Email] = N'admin2222@mimosbabyspa.com')

BEGIN

    SET @TenantId = NEWID();

    INSERT INTO [dbo].[Tenants] ([TenantId], [Name], [Email], [IsActive], [CreatedAt])

    VALUES (@TenantId, N'Mimos Baby Spa 2222', N'admin2222@mimosbabyspa.com', 1, GETUTCDATE());

END

ELSE

    SELECT @TenantId = [TenantId] FROM [dbo].[Tenants] WHERE [Email] = N'admin2222@mimosbabyspa.com';



IF NOT EXISTS (SELECT 1 FROM [dbo].[Businesses] WHERE [BusinessId] = @BusinessId)

BEGIN

    INSERT INTO [dbo].[Businesses] ([BusinessId], [TenantId], [Name], [Description], [Address], [Phone], [Email], [Website], [IsActive], [CreatedAt])

    VALUES (@BusinessId, @TenantId, N'Mimos Baby Spa Principal', N'Negocio principal', N'Por definir', N'+57 300 000 0000', N'admin2222@mimosbabyspa.com', N'https://mimosbabyspa.com', 1, GETUTCDATE());

    PRINT N'Negocio 22222222 creado.';

END

ELSE

    SELECT @TenantId = [TenantId] FROM [dbo].[Businesses] WHERE [BusinessId] = @BusinessId;



-- ============================================================

-- 1. Usuario admin y rol Administrator para el negocio

-- ============================================================



-- Permisos (si no existen)

DECLARE @Perms TABLE (Module NVARCHAR(50), Action NVARCHAR(50), Resource NVARCHAR(100), Description NVARCHAR(500));

INSERT INTO @Perms VALUES

(N'Users', N'Read', N'users.read', N'Ver listado de usuarios'),

(N'Users', N'Create', N'users.create', N'Crear usuarios'),

(N'Users', N'Update', N'users.update', N'Actualizar usuarios'),

(N'Users', N'Delete', N'users.delete', N'Desactivar usuarios'),

(N'Roles', N'Read', N'roles.read', N'Ver roles'),

(N'Roles', N'Create', N'roles.create', N'Crear roles'),

(N'Roles', N'Update', N'roles.update', N'Actualizar roles'),

(N'Businesses', N'Read', N'businesses.read', N'Ver negocios'),

(N'Businesses', N'Update', N'businesses.update', N'Actualizar negocios'),

(N'Services', N'Read', N'services.read', N'Ver servicios'),

(N'Services', N'Create', N'services.create', N'Crear servicios'),

(N'Services', N'Update', N'services.update', N'Actualizar servicios'),

(N'Employees', N'Read', N'employees.read', N'Ver empleados'),

(N'Employees', N'Create', N'employees.create', N'Crear empleados'),

(N'Reservations', N'Read', N'reservations.read', N'Ver reservas'),

(N'Reservations', N'Update', N'reservations.update', N'Actualizar reservas'),

(N'Leads', N'Read', N'leads.read', N'Ver leads'),

(N'Campaigns', N'Read', N'campaigns.read', N'Ver campañas'),

(N'Campaigns', N'Create', N'campaigns.create', N'Crear campañas'),

(N'Campaigns', N'Send', N'campaigns.send', N'Enviar campañas'),

(N'Campaigns', N'Cancel', N'campaigns.cancel', N'Cancelar campañas'),

(N'BusinessConfig', N'Read', N'business_config.read', N'Ver configuraciÃ³n'),

(N'BusinessConfig', N'Update', N'business_config.update', N'Actualizar configuraciÃ³n');



INSERT INTO [dbo].[Permissions] ([PermissionId], [Module], [Action], [Resource], [Description], [CreatedAt])

SELECT NEWID(), p.Module, p.Action, p.Resource, p.Description, GETUTCDATE()

FROM @Perms p WHERE NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Resource] = p.Resource);



-- Rol Administrator para el tenant

IF NOT EXISTS (SELECT 1 FROM [dbo].[AppRoles] WHERE [TenantId] = @TenantId AND [NormalizedName] = N'ADMINISTRATOR')

BEGIN

    SET @AdminRoleId = NEWID();

    INSERT INTO [dbo].[AppRoles] ([RoleId], [TenantId], [Name], [NormalizedName], [Description], [IsActive], [IsSystemRole], [CreatedAt])

    VALUES (@AdminRoleId, @TenantId, N'Administrator', N'ADMINISTRATOR', N'Acceso total', 1, 1, GETUTCDATE());

    INSERT INTO [dbo].[RolePermissions] ([RolePermissionId], [RoleId], [PermissionId], [AssignedAt])

    SELECT NEWID(), @AdminRoleId, [PermissionId], GETUTCDATE() FROM [dbo].[Permissions];

END

ELSE

    SELECT @AdminRoleId = [RoleId] FROM [dbo].[AppRoles] WHERE [TenantId] = @TenantId AND [NormalizedName] = N'ADMINISTRATOR';



-- La identidad administrativa de plataforma se aprovisiona exclusivamente en el tenant @auraly.

-- ============================================================

-- 2. Adjuntos (BusinessAttachments)

-- ============================================================



IF NOT EXISTS (SELECT 1 FROM [dbo].[BusinessAttachments] WHERE [BusinessAttachmentId] = @AttachmentId)

BEGIN

    INSERT INTO [dbo].[BusinessAttachments] (BusinessAttachmentId, BusinessId, BlobPath, MediaType, Filename, Description, IsActive, CreatedAt)

    VALUES (@AttachmentId, @BusinessId, N'confirmations/indicaciones-para-tu-visita.pdf', N'document', N'Indicaciones-para-tu-visita.pdf', N'Indicaciones para la visita', 1, GETUTCDATE());

    PRINT N'Adjunto creado: indicaciones-para-tu-visita.pdf';

END



IF NOT EXISTS (SELECT 1 FROM [dbo].[BusinessAttachments] WHERE [BusinessId] = @BusinessId AND [BlobPath] = N'confirmations/terminos-y-condiciones.pdf')

BEGIN

    INSERT INTO [dbo].[BusinessAttachments] (BusinessAttachmentId, BusinessId, BlobPath, MediaType, Filename, Description, IsActive, CreatedAt)

    VALUES (@AttachmentId2, @BusinessId, N'confirmations/terminos-y-condiciones.pdf', N'document', N'Terminos-y-condiciones.pdf', N'TÃ©rminos y condiciones', 1, GETUTCDATE());

    PRINT N'Adjunto creado: terminos-y-condiciones.pdf';

END



PRINT N'Seed completado para negocio 22222222-2222-2222-2222-222222222222.';

GO

