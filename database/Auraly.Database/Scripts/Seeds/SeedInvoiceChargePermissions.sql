SET NOCOUNT ON;
INSERT dbo.Permissions(PermissionId,Module,Action,Resource,Description,CreatedAt)
SELECT NEWID(),N'Sales',p.Action,p.Resource,p.Description,SYSUTCDATETIME()
FROM (VALUES
  (N'Read',N'invoice-charges.read',N'Consultar cargos de facturación'),
  (N'Configure',N'invoice-charges.configure',N'Configurar cargos de facturación')
) p(Action,Resource,Description)
WHERE NOT EXISTS(SELECT 1 FROM dbo.Permissions currentPermission WHERE currentPermission.Resource=p.Resource);
-- Administrator assignment is owned by the final canonical role synchronization.
GO
