SET NOCOUNT ON;

DECLARE @Now DATETIMEOFFSET(7)=SYSUTCDATETIME();
DECLARE @Source TABLE
(
    OptionId UNIQUEIDENTIFIER NOT NULL,
    CatalogCode NVARCHAR(64) NOT NULL,
    Code NVARCHAR(64) NOT NULL,
    Label NVARCHAR(160) NOT NULL,
    Description NVARCHAR(500) NULL,
    SortOrder INT NOT NULL
);

INSERT @Source(OptionId,CatalogCode,Code,Label,Description,SortOrder)
VALUES
('10000000-0000-0000-0000-000000000001',N'payment-method',N'Cash',N'Efectivo',NULL,10),
('10000000-0000-0000-0000-000000000002',N'payment-method',N'DebitCard',N'Tarjeta débito',NULL,20),
('10000000-0000-0000-0000-000000000003',N'payment-method',N'CreditCard',N'Tarjeta crédito',NULL,30),
('10000000-0000-0000-0000-000000000004',N'payment-method',N'Transfer',N'Transferencia',NULL,40),
('10000000-0000-0000-0000-000000000005',N'payment-method',N'Credit',N'Crédito cliente',NULL,50),
('10000000-0000-0000-0000-000000000006',N'payment-method',N'BankTransfer',N'Transferencia bancaria',NULL,60),
('10000000-0000-0000-0000-000000000007',N'payment-method',N'Deposit',N'Consignación o depósito',NULL,70),
('20000000-0000-0000-0000-000000000001',N'sales-document-type',N'SalesInvoice',N'Factura electrónica',N'Usa numeración DIAN, CUFE y código QR.',10),
('20000000-0000-0000-0000-000000000002',N'sales-document-type',N'SalesReceipt',N'Comprobante de venta',N'Usa la numeración operativa CVI.',20),
('30000000-0000-0000-0000-000000000001',N'purchase-presentation',N'Unidad',N'Unidad',NULL,10),
('30000000-0000-0000-0000-000000000002',N'purchase-presentation',N'Caja',N'Caja',NULL,20),
('30000000-0000-0000-0000-000000000003',N'purchase-presentation',N'Bulto',N'Bulto',NULL,30),
('30000000-0000-0000-0000-000000000004',N'purchase-presentation',N'Paquete',N'Paquete',NULL,40),
('30000000-0000-0000-0000-000000000005',N'purchase-presentation',N'Bolsa',N'Bolsa',NULL,50),
('30000000-0000-0000-0000-000000000006',N'purchase-presentation',N'Rollo',N'Rollo',NULL,60),
('30000000-0000-0000-0000-000000000007',N'purchase-presentation',N'Canasta',N'Canasta',NULL,70),
('40000000-0000-0000-0000-000000000001',N'inventory-operation-type',N'StockCount',N'Conteo físico',NULL,10),
('40000000-0000-0000-0000-000000000002',N'inventory-operation-type',N'InventoryAdjustment',N'Ajuste',NULL,20),
('40000000-0000-0000-0000-000000000003',N'inventory-operation-type',N'WarehouseTransfer',N'Traslado',NULL,30),
('40000000-0000-0000-0000-000000000004',N'inventory-operation-type',N'ProductConversion',N'Conversión',NULL,40),
('40000000-0000-0000-0000-000000000005',N'inventory-operation-type',N'Damage',N'Avería',NULL,50),
('50000000-0000-0000-0000-000000000001',N'agent-bot-type',N'1',N'Reservas',N'Agenda servicios, consulta disponibilidad y confirma citas.',10),
('50000000-0000-0000-0000-000000000002',N'agent-bot-type',N'2',N'Pedidos',N'Vende productos, arma pedidos y gestiona entrega y pago.',20),
('50000000-0000-0000-0000-000000000003',N'agent-bot-type',N'3',N'Domicilios',N'Recibe, acepta y actualiza pedidos asignados a domiciliarios.',30),
('50000000-0000-0000-0000-000000000004',N'agent-bot-type',N'4',N'Validador de pagos',N'Consulta pagos pendientes y confirma transacciones autorizadas.',40);

MERGE [reference].[Options] AS target
USING @Source AS source
ON target.CatalogCode=source.CatalogCode AND target.Code=source.Code
WHEN MATCHED THEN UPDATE SET
    target.Label=source.Label,target.Description=source.Description,
    target.SortOrder=source.SortOrder,target.IsActive=1,target.UpdatedAt=@Now
WHEN NOT MATCHED THEN
    INSERT(OptionId,CatalogCode,Code,Label,Description,IsActive,SortOrder,CreatedAt,UpdatedAt)
    VALUES(source.OptionId,source.CatalogCode,source.Code,source.Label,
           source.Description,1,source.SortOrder,@Now,@Now)
WHEN NOT MATCHED BY SOURCE
     AND target.CatalogCode IN
       (N'payment-method',N'sales-document-type',N'purchase-presentation',
        N'inventory-operation-type',N'agent-bot-type')
THEN UPDATE SET target.IsActive=0,target.UpdatedAt=@Now;
