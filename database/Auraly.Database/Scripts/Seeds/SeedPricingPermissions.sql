SET NOCOUNT ON;
DECLARE @PricingPermissions TABLE(
    [Action] NVARCHAR(50) NOT NULL,
    [Resource] NVARCHAR(100) NOT NULL,
    [Description] NVARCHAR(500) NOT NULL);
INSERT @PricingPermissions VALUES
    (N'Read',N'pricing.read',N'Consultar propuestas y precios publicados'),
    (N'ReadCostBasis',N'pricing.cost-basis.read',N'Consultar costos usados para fijar precios'),
    (N'Review',N'pricing.proposals.review',N'Revisar y rechazar propuestas de precio'),
    (N'Prepare',N'pricing.prices.prepare',N'Preparar costos, margen y precio antes de publicar'),
    (N'Publish',N'pricing.prices.publish',N'Publicar precios de venta'),
    (N'BulkPublish',N'pricing.bulk-publish',N'Publicar varios precios en una operacion'),
    (N'ManageRounding',N'pricing.rounding.manage',N'Configurar reglas de redondeo'),
    (N'ReadHistory',N'pricing.history.read',N'Consultar historial de precios');
INSERT dbo.Permissions(PermissionId,Module,Action,Resource,Description,CreatedAt)
SELECT NEWID(),N'Pricing',p.Action,p.Resource,p.Description,SYSUTCDATETIME()
FROM @PricingPermissions p
WHERE NOT EXISTS(SELECT 1 FROM dbo.Permissions x WHERE x.Resource=p.Resource);
INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
SELECT NEWID(),r.RoleId,p.PermissionId,SYSUTCDATETIME()
FROM dbo.AppRoles r
JOIN dbo.Permissions p ON p.Resource LIKE N'pricing.%'
WHERE r.IsActive=1 AND r.NormalizedName=N'ADMINISTRATOR'
AND NOT EXISTS(
    SELECT 1 FROM dbo.RolePermissions rp
    WHERE rp.RoleId=r.RoleId AND rp.PermissionId=p.PermissionId);
