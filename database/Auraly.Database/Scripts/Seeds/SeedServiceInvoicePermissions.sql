SET NOCOUNT ON;

DECLARE @ServiceInvoicePermissions TABLE
(
    [Action] NVARCHAR(50) NOT NULL,
    [Resource] NVARCHAR(100) NOT NULL,
    [Description] NVARCHAR(500) NOT NULL
);
INSERT @ServiceInvoicePermissions VALUES
    (N'Read',N'service-invoices.read',N'Consultar facturas y servicios facturables'),
    (N'Create',N'service-invoices.create',N'Crear facturas de servicios'),
    (N'OverridePrice',N'service-invoices.price.override',N'Modificar el precio de un servicio al facturar'),
    (N'Discount',N'service-invoices.discount',N'Aplicar descuentos a servicios'),
    (N'Issue',N'service-invoices.issue',N'Emitir facturas electrónicas de servicios'),
    (N'Print',N'service-invoices.print',N'Imprimir facturas de servicios');

INSERT dbo.Permissions(PermissionId,Module,Action,Resource,Description,CreatedAt)
SELECT NEWID(),N'ServiceInvoices',value.Action,value.Resource,value.Description,SYSUTCDATETIME()
FROM @ServiceInvoicePermissions value
WHERE NOT EXISTS(
    SELECT 1 FROM dbo.Permissions currentValue
    WHERE currentValue.Resource=value.Resource);

INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
SELECT NEWID(),roleValue.RoleId,permissionValue.PermissionId,SYSUTCDATETIME()
FROM dbo.AppRoles roleValue
JOIN dbo.Permissions permissionValue
  ON permissionValue.Resource IN(
    N'service-invoices.read',N'service-invoices.create',
    N'service-invoices.price.override',N'service-invoices.discount',
    N'service-invoices.issue',N'service-invoices.print')
WHERE roleValue.IsActive=1 AND roleValue.NormalizedName=N'ADMINISTRATOR'
  AND NOT EXISTS(
    SELECT 1 FROM dbo.RolePermissions assigned
    WHERE assigned.RoleId=roleValue.RoleId
      AND assigned.PermissionId=permissionValue.PermissionId);
GO
