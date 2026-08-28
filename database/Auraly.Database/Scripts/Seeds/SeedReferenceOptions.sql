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
('11000000-0000-0000-0000-000000000001',N'card-franchise',N'Visa',N'Visa',NULL,10),
('11000000-0000-0000-0000-000000000002',N'card-franchise',N'Mastercard',N'Mastercard',NULL,20),
('11000000-0000-0000-0000-000000000003',N'card-franchise',N'AmericanExpress',N'American Express',NULL,30),
('11000000-0000-0000-0000-000000000004',N'card-franchise',N'DinersClub',N'Diners Club',NULL,40),
('12000000-0000-0000-0000-000000000001',N'sales-return-resolution-method',N'Cash',N'Efectivo',N'Reintegra dinero desde la sesión de caja y abre el cajón.',10),
('12000000-0000-0000-0000-000000000002',N'sales-return-resolution-method',N'CustomerCredit',N'Abono a cartera',N'Resta el saldo de la cuenta por cobrar de la venta.',20),
('12000000-0000-0000-0000-000000000003',N'sales-return-resolution-method',N'Transfer',N'Transferencia',N'Reintegro por transferencia bancaria.',30),
('12000000-0000-0000-0000-000000000004',N'sales-return-resolution-method',N'DebitCard',N'Tarjeta débito',N'Reversión al pago original con tarjeta débito.',40),
('12000000-0000-0000-0000-000000000005',N'sales-return-resolution-method',N'CreditCard',N'Tarjeta crédito',N'Reversión al pago original con tarjeta crédito.',50),
('12100000-0000-0000-0000-000000000001',N'sales-return-scope',N'FullCancellation',N'Anulación / devolución total',N'Devuelve automáticamente todas las cantidades aún disponibles de la venta.',10),
('12100000-0000-0000-0000-000000000002',N'sales-return-scope',N'Partial',N'Devolución parcial',N'Permite seleccionar productos y cantidades específicas.',20),
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
('50000000-0000-0000-0000-000000000004',N'agent-bot-type',N'4',N'Validador de pagos',N'Consulta pagos pendientes y confirma transacciones autorizadas.',40),
('60000000-0000-0000-0000-000000000001',N'accounting-account-type',N'Asset',N'Activo',NULL,10),
('60000000-0000-0000-0000-000000000002',N'accounting-account-type',N'Liability',N'Pasivo',NULL,20),
('60000000-0000-0000-0000-000000000003',N'accounting-account-type',N'Equity',N'Patrimonio',NULL,30),
('60000000-0000-0000-0000-000000000004',N'accounting-account-type',N'Revenue',N'Ingreso',NULL,40),
('60000000-0000-0000-0000-000000000005',N'accounting-account-type',N'Expense',N'Gasto',NULL,50),
('60000000-0000-0000-0000-000000000006',N'accounting-account-type',N'ContraRevenue',N'Contraingreso',NULL,60),
('61000000-0000-0000-0000-000000000001',N'accounting-subledger-kind',N'Receivable',N'Cuenta por cobrar',NULL,10),
('61000000-0000-0000-0000-000000000002',N'accounting-subledger-kind',N'Payable',N'Cuenta por pagar',NULL,20),
('62000000-0000-0000-0000-000000000001',N'accounting-adjustment-direction',N'Increase',N'Aumentar saldo',NULL,10),
('62000000-0000-0000-0000-000000000002',N'accounting-adjustment-direction',N'Decrease',N'Disminuir saldo',NULL,20),
('63000000-0000-0000-0000-000000000001',N'accounting-manual-concept',N'ACCOUNT_ADJUSTMENT',N'Ajuste de cartera',NULL,10),
('63000000-0000-0000-0000-000000000002',N'accounting-manual-concept',N'MANUAL_VOUCHER',N'Comprobante manual',NULL,20),
('64000000-0000-0000-0000-000000000001',N'accounting-report-type',N'ledger',N'Mayor y balances',NULL,10),
('64000000-0000-0000-0000-000000000002',N'accounting-report-type',N'journal',N'Libro diario',NULL,20),
('64000000-0000-0000-0000-000000000003',N'accounting-report-type',N'balance',N'Situación financiera',NULL,30),
('64000000-0000-0000-0000-000000000004',N'accounting-report-type',N'income',N'Estado de resultados',NULL,40),
('64000000-0000-0000-0000-000000000005',N'accounting-report-type',N'exceptions',N'Sin contabilizar',NULL,50),
('65000000-0000-0000-0000-000000000001',N'accounting-withholding-kind',N'IncomeTax',N'Retefuente',NULL,10),
('65000000-0000-0000-0000-000000000002',N'accounting-withholding-kind',N'Vat',N'ReteIVA',NULL,20),
('65000000-0000-0000-0000-000000000003',N'accounting-withholding-kind',N'IndustryCommerce',N'ReteICA',NULL,30),
('66000000-0000-0000-0000-000000000001',N'accounting-opening-balance-mode',N'ZeroDeclared',N'Iniciar en cero',N'La contabilidad comienza únicamente con documentos posteriores a la fecha efectiva.',10),
('66000000-0000-0000-0000-000000000002',N'accounting-opening-balance-mode',N'ImportedAndApproved',N'Usar saldos aprobados',N'Genera el asiento de apertura aprobado antes de contabilizar movimientos posteriores.',20),
('67000000-0000-0000-0000-000000000001',N'tenant-entity-type',N'NaturalPerson',N'Persona natural',NULL,10),
('67000000-0000-0000-0000-000000000002',N'tenant-entity-type',N'Organization',N'Persona jurídica',NULL,20),
('68000000-0000-0000-0000-000000000001',N'tenant-identification-type',N'CC',N'Cédula de ciudadanía',NULL,10),
('68000000-0000-0000-0000-000000000002',N'tenant-identification-type',N'NIT',N'NIT',NULL,20),
('69000000-0000-0000-0000-000000000001',N'purchase-evidence-type',N'SupplierElectronicInvoice',N'Factura electrónica del proveedor',N'La factura la emite el proveedor; Auraly no genera un documento fiscal.',10),
('69000000-0000-0000-0000-000000000002',N'purchase-evidence-type',N'BuyerElectronicSupportDocument',N'Documento soporte electrónico',N'Auraly genera, firma y envía el documento soporte a la DIAN.',20),
('69000000-0000-0000-0000-000000000003',N'purchase-evidence-type',N'InternalReceiptVoucher',N'Comprobante interno',N'Registra la entrada y sus efectos contables sin emitir un documento fiscal.',30);

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
       (N'payment-method',N'card-franchise',N'sales-return-resolution-method',N'sales-return-scope',N'sales-document-type',N'purchase-presentation',
        N'inventory-operation-type',N'agent-bot-type',N'accounting-account-type',
        N'accounting-subledger-kind',N'accounting-adjustment-direction',
        N'accounting-manual-concept',N'accounting-report-type',
        N'accounting-withholding-kind',N'accounting-opening-balance-mode',
        N'tenant-entity-type',N'tenant-identification-type',N'purchase-evidence-type')
THEN UPDATE SET target.IsActive=0,target.UpdatedAt=@Now;
