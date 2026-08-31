SET NOCOUNT ON;
DECLARE @PurchasingPermissions TABLE([Action] NVARCHAR(50) NOT NULL,[Resource] NVARCHAR(100) NOT NULL,[Description] NVARCHAR(500) NOT NULL);
INSERT @PurchasingPermissions VALUES
    (N'Read',N'purchasing.purchase-orders.read',N'Consultar órdenes de compra'),
    (N'Create',N'purchasing.purchase-orders.create',N'Crear y guardar órdenes de compra'),
    (N'Confirm',N'purchasing.purchase-orders.confirm',N'Confirmar órdenes de compra'),
    (N'Close',N'purchasing.purchase-orders.close',N'Cerrar saldos pendientes de órdenes de compra'),
    (N'Authorize',N'purchasing.goods-receipts.over-receive',N'Autorizar cantidades recibidas superiores a la orden'),
    (N'Read',N'purchasing.goods-receipts.read',N'Consultar recepciones de compra'),
    (N'Create',N'purchasing.goods-receipts.create',N'Crear y guardar recepciones de compra'),
    (N'Confirm',N'purchasing.goods-receipts.confirm',N'Confirmar recepciones de compra'),
    (N'Read',N'purchasing.purchase-returns.read',N'Consultar devoluciones de compra'),
    (N'Create',N'purchasing.purchase-returns.create',N'Crear devoluciones de compra'),
    (N'Confirm',N'purchasing.purchase-returns.confirm',N'Confirmar devoluciones de compra');
INSERT dbo.Permissions(PermissionId,Module,Action,Resource,Description,CreatedAt)
SELECT NEWID(),N'Purchasing',p.Action,p.Resource,p.Description,SYSUTCDATETIME()
FROM @PurchasingPermissions p
WHERE NOT EXISTS(SELECT 1 FROM dbo.Permissions x WHERE x.Resource=p.Resource);
INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
SELECT NEWID(),r.RoleId,p.PermissionId,SYSUTCDATETIME()
FROM dbo.AppRoles r
JOIN dbo.Permissions p ON p.Resource IN
  (N'purchasing.purchase-orders.read',N'purchasing.purchase-orders.create',N'purchasing.purchase-orders.confirm',N'purchasing.purchase-orders.close',
   N'purchasing.goods-receipts.over-receive',N'purchasing.goods-receipts.read',N'purchasing.goods-receipts.create',N'purchasing.goods-receipts.confirm',
   N'purchasing.purchase-returns.read',N'purchasing.purchase-returns.create',N'purchasing.purchase-returns.confirm')
WHERE r.IsActive=1 AND r.NormalizedName=N'ADMINISTRATOR'
AND NOT EXISTS(SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId=r.RoleId AND rp.PermissionId=p.PermissionId);
