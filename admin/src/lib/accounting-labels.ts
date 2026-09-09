const documentTypes:Record<string,string>={
  SalesInvoice:"Factura de venta",ServiceInvoice:"Factura de servicios",SalesReceipt:"Documento POS",
  SalesReturn:"Devolución de venta",SalesDebitNote:"Nota débito de venta",GoodsReceipt:"Recepción de compra",
  GoodsReceiptCostDocument:"Factura agregada a la compra",Expense:"Gasto",PurchaseReturn:"Devolución a proveedor",
  PayablePayment:"Pago a proveedor",ReceivablePayment:"Abono de cliente",CashReceipt:"Recibo de caja",
  CashDisbursement:"Comprobante de egreso",AccountAdjustment:"Nota contable",AccountingAccountAdjustment:"Nota contable",
  AccountingManualVoucher:"Comprobante manual",ManualAccountingVoucher:"Comprobante manual",
  AccountingOpeningBalance:"Saldo inicial",StockCount:"Conteo de inventario",InventoryAdjustment:"Ajuste de inventario",
  Damage:"Baja de inventario",ProductConversion:"Conversión de producto",WarehouseTransferReceipt:"Recepción de traslado",
  WarehouseTransfer:"Traslado entre bodegas",WorkSessionCashDifference:"Diferencia de caja",WorkSessionClosureReconciliation:"Conciliación de cierre",DispatchCashDifference:"Diferencia de recaudo",WorkSessionClosure:"Cierre de caja",CashMovement:"Movimiento de caja",
  PayrollAccrual:"Causación de nómina",PayrollPayment:"Pago de nómina",PayrollAdjustment:"Ajuste de nómina",SupplierDebitNote:"Nota débito de proveedor",
  Invoice:"Factura electrónica",CreditNote:"Nota crédito electrónica",DebitNote:"Nota débito electrónica",
  SupportDocument:"Documento soporte electrónico",Payroll:"Nómina electrónica",
};
const accountingStatuses:Record<string,string>={Posted:"Contabilizado",Pending:"Pendiente de contabilización",Processing:"En contabilización",CommercialEffectsApplied:"Efecto comercial sin asiento",AccountingPendingConfiguration:"Requiere configuración contable",MissingAccountingJob:"Ausencia contable",AccountingDisabled:"No requiere asiento",Failed:"Error contable",NotRequired:"No requiere asiento"};
const fiscalStatuses:Record<string,string>={Draft:"Borrador",Pending:"Pendiente",Queued:"En cola",Processing:"En proceso",Accepted:"Aceptado por la DIAN",DianAccepted:"Aceptado por la DIAN",Rejected:"Rechazado por la DIAN",DianRejected:"Rechazado por la DIAN",Failed:"Error fiscal",NotRequired:"No requiere envío fiscal",Issued:"Emitido",Voided:"Anulado",Validated:"Validado",Sent:"Enviado",Received:"Recibido",BlockedByQuota:"Bloqueado por cupo"};
const businessStatuses:Record<string,string>={Draft:"Borrador",Pending:"Pendiente",Processing:"En proceso",Confirmed:"Confirmado",Approved:"Aprobado",Calculated:"Calculado",Posted:"Contabilizado",Paid:"Pagado",Open:"Abierto",Closed:"Cerrado",Active:"Activo",Inactive:"Inactivo",Retired:"Retirado",Ready:"Listo",Blocked:"Bloqueado",Reopened:"Reabierto",Cancelled:"Cancelado",Completed:"Completado"};

export function accountingDocumentTypeLabel(value:string|null|undefined){return value?documentTypes[value]??humanize(value):"Documento"}
export function accountingStatusLabel(value:string|null|undefined){return value?accountingStatuses[value]??humanize(value):"Sin estado"}
export function fiscalStatusLabel(value:string|null|undefined){return value?fiscalStatuses[value]??humanize(value):"Sin envío fiscal"}
export function businessStatusLabel(value:string|null|undefined){return value?businessStatuses[value]??humanize(value):"Sin estado"}
export function humanize(value:string){return value.replace(/([a-záéíóúñ])([A-ZÁÉÍÓÚÑ])/g,"$1 $2").replace(/[_-]+/g," ").replace(/^./,letter=>letter.toLocaleUpperCase("es-CO"))}
