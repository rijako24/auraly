export const inventoryOperationKinds = [
  "count",
  "adjustment",
  "transfer",
  "conversion",
  "damage",
] as const;

export type InventoryOperationKind = (typeof inventoryOperationKinds)[number];

export const defaultInventoryOperationKind: InventoryOperationKind = "count";
