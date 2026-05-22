-- ============================================================
-- Script: SeedDevBusiness
-- Para el negocio 22222222-2222-2222-2222-222222222222:
--   1. Crea usuario admin con rol Administrator asignado al negocio
--   2. Crea adjuntos (indicaciones, términos)
--   3. Crea/actualiza configuración Key=1 (PaymentConfirmationMessages)
-- Contraseña admin: Admin123!
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
DECLARE @PasswordHash NVARCHAR(500) = N'$2a$12$.lNc5ybjDXuH3fevIkTyb.L.OpvHnO4oZ2/kyx.HtUtRJ5cpCKtPi';

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
(N'BusinessConfig', N'Read', N'business_config.read', N'Ver configuración'),
(N'BusinessConfig', N'Update', N'business_config.update', N'Actualizar configuración');

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

-- Usuario admin (username único por tenant, usar admin2222 para evitar conflicto con admin global)
DECLARE @AdminUsername NVARCHAR(100) = N'admin2222';
DECLARE @AdminEmail NVARCHAR(256) = N'admin2222@mimosbabyspa.com';

IF NOT EXISTS (SELECT 1 FROM [dbo].[AppUsers] WHERE [TenantId] = @TenantId AND [NormalizedUsername] = UPPER(@AdminUsername))
BEGIN
    SET @AdminUserId = NEWID();
    INSERT INTO [dbo].[AppUsers] ([UserId], [TenantId], [Username], [NormalizedUsername], [Email], [NormalizedEmail], [PasswordHash], [FirstName], [LastName], [AccessFailedCount], [EmailConfirmed], [IsActive], [CreatedAt])
    VALUES (@AdminUserId, @TenantId, @AdminUsername, UPPER(@AdminUsername), @AdminEmail, UPPER(@AdminEmail), @PasswordHash, N'Admin', N'Negocio 2222', 0, 1, 1, GETUTCDATE());
    INSERT INTO [dbo].[UserRoles] ([UserRoleId], [UserId], [RoleId], [BusinessId], [AssignedAt])
    VALUES (NEWID(), @AdminUserId, @AdminRoleId, @BusinessId, GETUTCDATE());
    PRINT N'Usuario admin creado: ' + @AdminUsername + N' / Admin123! (asignado al negocio 22222222)';
END
ELSE
    PRINT N'Usuario admin para tenant ya existe.';

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
    VALUES (@AttachmentId2, @BusinessId, N'confirmations/terminos-y-condiciones.pdf', N'document', N'Terminos-y-condiciones.pdf', N'Términos y condiciones', 1, GETUTCDATE());
    PRINT N'Adjunto creado: terminos-y-condiciones.pdf';
END

-- ============================================================
-- 3. Configuración Key=1 (PaymentConfirmationMessages)
-- ============================================================

DECLARE @ConfirmationMessagesValue NVARCHAR(MAX) = N'{
  "messages": [
    {"body": "✅ ¡Tu pago ha sido confirmado y tu reserva creada!"},
    {"body": "📋 Adjuntamos las indicaciones para tu visita:", "attachmentId": "8a1ec489-f1ba-4c7c-9576-382dfc9a55f1"},
    {"body": "Estos son los términos y condiciones:", "attachmentId": "9b2fd590-a2cb-5d8d-a687-493efd0b66a2"}
  ]
}';

MERGE dbo.BusinessConfigurations AS target
USING (SELECT @BusinessId AS BusinessId, 1 AS [Key]) AS src
   ON target.BusinessId = src.BusinessId AND target.[Key] = src.[Key]
WHEN MATCHED THEN
    UPDATE SET [Value] = @ConfirmationMessagesValue, UpdatedAt = GETUTCDATE()
WHEN NOT MATCHED THEN
    INSERT (BusinessConfigurationId, BusinessId, [Key], [Value], IsActive, CreatedAt)
    VALUES (NEWID(), @BusinessId, 1, @ConfirmationMessagesValue, 1, GETUTCDATE());

PRINT N'Key=1 (PaymentConfirmationMessages) configurada.';

PRINT N'Seed completado para negocio 22222222-2222-2222-2222-222222222222.';
GO
