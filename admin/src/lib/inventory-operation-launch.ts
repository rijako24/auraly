export const inventoryOperationKinds = [
  "count",
  "adjustment",
  "transfer",
  "conversion",
  "damage",
  "production",
] as const;

export type InventoryOperationKind = (typeof inventoryOperationKinds)[number];

export const defaultInventoryOperationKind: InventoryOperationKind = "count";

const inventoryDocumentTypeByKind: Record<InventoryOperationKind, string> = {
  count: "StockCount",
  adjustment: "InventoryAdjustment",
  transfer: "WarehouseTransfer",
  conversion: "ProductConversion",
  damage: "Damage",
  production: "Production",
};

export const inventoryDocumentTypeForKind = (kind: InventoryOperationKind) =>
  inventoryDocumentTypeByKind[kind];

export const canConfirmWarehouseTransferReceipt = (status: string) =>
  status === "Dispatched" || status === "PartiallyReceived";

// Visibility follows the existing server capabilities. The API remains authoritative.
export function canReadInventoryOperation(permissions: ReadonlySet<string>, documentType: string): boolean {
  const any = (...codes: string[]) => codes.some((code) => permissions.has(code));
  switch (documentType) {
    case "all": return ["StockCount", "InventoryAdjustment", "WarehouseTransfer", "ProductConversion", "Production", "Damage", "GoodsReceipt"]
      .some((type) => canReadInventoryOperation(permissions, type));
    case "StockCount": return any("inventory.counts.confirm", "inventory.physical-counts.manage", "inventory.physical-counts.capture");
    case "InventoryAdjustment": return any("inventory.adjustments.confirm");
    case "WarehouseTransfer": return any("inventory.transfers.dispatch", "inventory.transfers.receive", "inventory.transfers.resolve-difference");
    case "ProductConversion": return any("inventory.conversions.confirm");
    case "Production": return any("inventory.production.confirm");
    case "Damage": return any("inventory.damages.confirm");
    case "GoodsReceipt": return any("purchasing.goods-receipts.read");
    default: return false;
  }
}

export function canCreateInventoryOperation(permissions: ReadonlySet<string>, kind: InventoryOperationKind): boolean {
  const code = {
    count: "inventory.physical-counts.manage", adjustment: "inventory.adjustments.confirm",
    transfer: "inventory.transfers.dispatch", conversion: "inventory.conversions.confirm",
    production: "inventory.production.confirm", damage: "inventory.damages.confirm",
  }[kind];
  return permissions.has(code);
}
