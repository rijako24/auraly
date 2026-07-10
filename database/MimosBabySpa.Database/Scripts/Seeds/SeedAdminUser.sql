/*

  Script para crear un usuario administrador con todos los permisos.

  ContraseÃ±a por defecto: Admin123!  (CÃMBIELA tras el primer inicio de sesiÃ³n)

*/



SET QUOTED_IDENTIFIER ON;

SET NOCOUNT ON;



DECLARE @TenantId UNIQUEIDENTIFIER;

DECLARE @AdminUserId UNIQUEIDENTIFIER;

DECLARE @AdminRoleId UNIQUEIDENTIFIER;



-- Hash BCrypt para "Admin123!" (work factor 12)

DECLARE @PasswordHash NVARCHAR(500) = N'$2a$12$.lNc5ybjDXuH3fevIkTyb.L.OpvHnO4oZ2/kyx.HtUtRJ5cpCKtPi';



-- 1. Tenant por defecto

IF NOT EXISTS (SELECT 1 FROM [dbo].[Tenants] WHERE [Email] = N'admin@mimosbabyspa.com')

BEGIN

    SET @TenantId = NEWID();

    INSERT INTO [dbo].[Tenants] ([TenantId], [Name], [Email], [IsActive], [CreatedAt])

    VALUES (@TenantId, N'Mimos Baby Spa', N'admin@mimosbabyspa.com', 1, GETUTCDATE());

END

ELSE

    SELECT @TenantId = [TenantId] FROM [dbo].[Tenants] WHERE [Email] = N'admin@mimosbabyspa.com';



-- 2. Negocio por defecto

-- SeedDevBusiness.sql crea el negocio canonico con BusinessId estable

-- 22222222-2222-2222-2222-222222222222. No crear un negocio aqui con NEWID(),

-- porque duplica "Mimos Baby Spa Principal" en cada ambiente donde no exista

-- ese registro bajo el tenant admin@mimosbabyspa.com.



-- 3. Permisos

DECLARE @Perms TABLE (Module NVARCHAR(50), Action NVARCHAR(50), Resource NVARCHAR(100), Description NVARCHAR(500));

INSERT INTO @Perms VALUES

(N'Users', N'Read', N'users.read', N'Ver listado de usuarios'),

(N'Users', N'Create', N'users.create', N'Crear nuevos usuarios'),

(N'Users', N'Update', N'users.update', N'Actualizar usuarios'),

(N'Users', N'Delete', N'users.delete', N'Desactivar usuarios'),

(N'Users', N'AssignRole', N'users.assign_role', N'Asignar roles'),

(N'Users', N'RemoveRole', N'users.remove_role', N'Remover roles'),

(N'Roles', N'Read', N'roles.read', N'Ver roles'),

(N'Roles', N'Create', N'roles.create', N'Crear roles'),

(N'Roles', N'Update', N'roles.update', N'Actualizar roles'),

(N'Roles', N'Delete', N'roles.delete', N'Desactivar roles'),

(N'Roles', N'AssignPermissions', N'roles.assign_permissions', N'Asignar permisos'),

(N'Permissions', N'Read', N'permissions.read', N'Ver permisos'),

(N'Tenants', N'Read', N'tenants.read', N'Ver tenants'),

(N'Tenants', N'Create', N'tenants.create', N'Crear tenants'),

(N'Tenants', N'Update', N'tenants.update', N'Actualizar tenants'),

(N'Businesses', N'Read', N'businesses.read', N'Ver negocios'),

(N'Businesses', N'Create', N'businesses.create', N'Crear negocios'),

(N'Businesses', N'Update', N'businesses.update', N'Actualizar negocios'),

(N'Businesses', N'Delete', N'businesses.delete', N'Eliminar negocios'),

(N'Services', N'Read', N'services.read', N'Ver servicios'),

(N'Services', N'Create', N'services.create', N'Crear servicios'),

(N'Services', N'Update', N'services.update', N'Actualizar servicios'),

(N'Services', N'Delete', N'services.delete', N'Eliminar servicios'),

(N'Promotions', N'Read', N'promotions.read', N'Ver promociones'),

(N'Promotions', N'Create', N'promotions.create', N'Crear promociones'),

(N'Promotions', N'Update', N'promotions.update', N'Actualizar promociones'),

(N'Promotions', N'Delete', N'promotions.delete', N'Desactivar promociones'),

(N'Employees', N'Read', N'employees.read', N'Ver empleados'),

(N'Employees', N'Create', N'employees.create', N'Crear empleados'),

(N'Employees', N'Update', N'employees.update', N'Actualizar empleados'),

(N'Employees', N'Delete', N'employees.delete', N'Eliminar empleados'),

(N'Reservations', N'Read', N'reservations.read', N'Ver reservas'),

(N'Reservations', N'Create', N'reservations.create', N'Crear reservas'),

(N'Reservations', N'Update', N'reservations.update', N'Actualizar reservas'),

(N'Reservations', N'Cancel', N'reservations.cancel', N'Cancelar reservas'),

(N'Reservations', N'Export', N'reservations.export', N'Exportar reservas'),

(N'Leads', N'Read', N'leads.read', N'Ver leads'),

(N'Leads', N'Create', N'leads.create', N'Crear leads'),

(N'Leads', N'Update', N'leads.update', N'Actualizar leads'),

(N'Leads', N'Export', N'leads.export', N'Exportar leads'),

(N'Campaigns', N'Read', N'campaigns.read', N'Ver campañas'),

(N'Campaigns', N'Create', N'campaigns.create', N'Crear campañas'),

(N'Campaigns', N'Send', N'campaigns.send', N'Enviar campañas'),

(N'Campaigns', N'Cancel', N'campaigns.cancel', N'Cancelar campañas'),

(N'Conversations', N'Read', N'conversations.read', N'Ver conversaciones'),

(N'Agents', N'Read', N'agents.read', N'Ver agentes IA'),

(N'Agents', N'Update', N'agents.update', N'Configurar agente IA'),

(N'Catalog', N'Import', N'catalog.import', N'Importar catÃ¡logo desde documento'),

(N'BusinessConfig', N'Read', N'business_config.read', N'Ver configuraciÃ³n'),

(N'BusinessConfig', N'Update', N'business_config.update', N'Actualizar configuraciÃ³n'),

(N'AuditLogs', N'Read', N'audit_logs.read', N'Ver auditorÃ­a'),

(N'Dashboard', N'Read', N'dashboard.read', N'Ver dashboard'),

(N'Payments', N'Read', N'payments.read', N'Ver transacciones de pago'),

(N'Payments', N'ConfirmManual', N'payments.confirm_manual', N'Confirmar pagos manualmente');



INSERT INTO [dbo].[Permissions] ([PermissionId], [Module], [Action], [Resource], [Description], [CreatedAt])

SELECT NEWID(), p.Module, p.Action, p.Resource, p.Description, GETUTCDATE()

FROM @Perms p WHERE NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Resource] = p.Resource);



-- 4. Rol Administrador

IF NOT EXISTS (SELECT 1 FROM [dbo].[AppRoles] WHERE [TenantId] = @TenantId AND [NormalizedName] = N'ADMINISTRATOR')

BEGIN

    SET @AdminRoleId = NEWID();

    INSERT INTO [dbo].[AppRoles] ([RoleId], [TenantId], [Name], [NormalizedName], [Description], [IsActive], [IsSystemRole], [CreatedAt])

    VALUES (@AdminRoleId, @TenantId, N'Administrator', N'ADMINISTRATOR', N'Acceso total', 1, 1, GETUTCDATE());

    INSERT INTO [dbo].[RolePermissions] ([RolePermissionId], [RoleId], [PermissionId], [AssignedAt])

    SELECT NEWID(), @AdminRoleId, [PermissionId], GETUTCDATE() FROM [dbo].[Permissions];

END

ELSE

BEGIN

    SELECT @AdminRoleId = [RoleId] FROM [dbo].[AppRoles] WHERE [TenantId] = @TenantId AND [NormalizedName] = N'ADMINISTRATOR';

    INSERT INTO [dbo].[RolePermissions] ([RolePermissionId], [RoleId], [PermissionId], [AssignedAt])

    SELECT NEWID(), @AdminRoleId, p.[PermissionId], GETUTCDATE()

    FROM [dbo].[Permissions] p

    WHERE NOT EXISTS (SELECT 1 FROM [dbo].[RolePermissions] rp WHERE rp.[RoleId] = @AdminRoleId AND rp.[PermissionId] = p.[PermissionId]);

END



-- 5. Usuario admin

IF NOT EXISTS (SELECT 1 FROM [dbo].[AppUsers] WHERE [NormalizedUsername] = N'ADMIN')

BEGIN

    SET @AdminUserId = NEWID();

    INSERT INTO [dbo].[AppUsers] ([UserId], [TenantId], [Username], [NormalizedUsername], [Email], [NormalizedEmail], [PasswordHash], [FirstName], [LastName], [AccessFailedCount], [EmailConfirmed], [IsActive], [CreatedAt])

    VALUES (@AdminUserId, @TenantId, N'admin', N'ADMIN', N'admin@mimosbabyspa.com', N'ADMIN@MIMOSBABYSPA.COM', @PasswordHash, N'Administrador', N'Sistema', 0, 1, 1, GETUTCDATE());

    INSERT INTO [dbo].[UserRoles] ([UserRoleId], [UserId], [RoleId], [BusinessId], [AssignedAt])

    VALUES (NEWID(), @AdminUserId, @AdminRoleId, NULL, GETUTCDATE());

    PRINT N'Usuario creado: admin / Admin123!';

END

ELSE

    PRINT N'Usuario admin ya existe.';



SET NOCOUNT OFF;

GO



