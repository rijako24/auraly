export const inventoryOperationKinds = [
  "count",
  "adjustment",
  "transfer",
  "conversion",
  "damage",
] as const;

export type InventoryOperationKind = (typeof inventoryOperationKinds)[number];

export const defaultInventoryOperationKind: InventoryOperationKind = "count";

const inventoryDocumentTypeByKind: Record<InventoryOperationKind, string> = {
  count: "StockCount",
  adjustment: "InventoryAdjustment",
  transfer: "WarehouseTransfer",
  conversion: "ProductConversion",
  damage: "Damage",
};

export const inventoryDocumentTypeForKind = (kind: InventoryOperationKind) =>
  inventoryDocumentTypeByKind[kind];

const inventoryDocumentLabels: Record<string, string> = {
  StockCount: "Conteo físico",
  InventoryAdjustment: "Movimiento de mercancía",
  WarehouseTransfer: "Traslado",
  WarehouseTransferReceipt: "Recepción de traslado",
  ProductConversion: "Conversión",
  Damage: "Avería",
  SalesInvoice: "Venta",
  GoodsReceipt: "Recepción",
};

const inventoryMovementLabels: Record<string, string> = {
  GoodsReceipt: "Recepción de mercancía",
  InventoryAdjustment: "Movimiento de mercancía",
  StockCount: "Ajuste por conteo físico",
  StockCountAdjustment: "Ajuste por conteo físico",
  WarehouseTransfer: "Traslado entre bodegas",
  TransferOut: "Salida por traslado",
  TransferDispatchOut: "Salida por traslado",
  TransferIn: "Entrada por traslado",
  TransferReceiptIn: "Entrada por traslado",
  ProductConversion: "Conversión de producto",
  ConversionInput: "Consumo por conversión",
  ConversionOutput: "Producción por conversión",
  Damage: "Salida por avería",
  InventoryDamage: "Salida por avería",
  SalesInvoice: "Venta",
  Sale: "Venta",
  SalesReturn: "Devolución de venta",
};

export const inventoryDocumentLabel = (value: string) =>
  inventoryDocumentLabels[value] ?? value.replace(/([a-z])([A-Z])/g, "$1 $2");

export const inventoryMovementLabel = (value: string) =>
  inventoryMovementLabels[value] ?? value.replace(/([a-z])([A-Z])/g, "$1 $2");

export const canConfirmWarehouseTransferReceipt = (status: string) =>
  status === "Dispatched" || status === "PartiallyReceived";
