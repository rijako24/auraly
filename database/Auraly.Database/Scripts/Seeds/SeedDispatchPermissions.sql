SET NOCOUNT ON;
DECLARE @Values TABLE([Action] NVARCHAR(50),[Resource] NVARCHAR(100),[Description] NVARCHAR(500));
INSERT @Values VALUES
(N'Read',N'dispatches.read',N'Consultar despachos'),
(N'ReadAll',N'dispatches.read-all',N'Consultar los despachos de todos los transportadores'),
(N'ExecuteDeliveries',N'dispatches.delivery.execute',N'Atender cargues y entregas asignadas al transportador'),
(N'Settle',N'dispatches.settle',N'Revisar novedades y liquidar despachos'),
(N'Create',N'dispatches.create',N'Crear despachos'),
(N'EditDraft',N'dispatches.edit-draft',N'Editar borradores de despacho'),
(N'AttachDocuments',N'dispatches.attach-documents',N'Agregar facturas y comprobantes de venta'),
(N'Prepare',N'dispatches.prepare',N'Preparar despachos'),
(N'Verify',N'dispatches.verify',N'Verificar mercancía cargada'),
(N'CorrectVerification',N'dispatches.correct-verification',N'Corregir verificación'),
(N'DeclareShortage',N'dispatches.declare-shortage',N'Declarar faltantes'),
(N'Release',N'dispatches.release',N'Liberar mercancía al transportador'),
(N'Cancel',N'dispatches.cancel',N'Cancelar despachos'),
(N'Reopen',N'dispatches.reopen',N'Reabrir verificación'),
(N'Reports',N'dispatches.reports.view',N'Consultar reportes de despacho'),
(N'Export',N'dispatches.reports.export',N'Exportar reportes de despacho'),
(N'ViewPrices',N'dispatches.view-prices',N'Ver precios en despacho y reportes');

INSERT dbo.Permissions(PermissionId,Module,Action,Resource,Description,CreatedAt)
SELECT NEWID(),N'Dispatching',v.Action,v.Resource,v.Description,SYSUTCDATETIME()
FROM @Values v WHERE NOT EXISTS(SELECT 1 FROM dbo.Permissions p WHERE p.Resource=v.Resource);

INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
SELECT NEWID(),r.RoleId,p.PermissionId,SYSUTCDATETIME()
FROM dbo.AppRoles r CROSS JOIN dbo.Permissions p
WHERE r.IsActive=1 AND r.NormalizedName=N'ADMINISTRATOR'
  AND p.Resource IN(SELECT Resource FROM @Values)
  AND NOT EXISTS(SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId=r.RoleId AND rp.PermissionId=p.PermissionId);
GO
