const DATABASE = "auraly-offline-work";
const VERSION = 1;
const STORE = "operation-drafts";

export type DurableOperationLine = {
  productId: string;
  productCode: string;
  productName: string;
  unitCode: string;
  stock: number;
  quantity: string;
  preCount?: string;
  count?: string;
  recount?: string;
  cost: string;
  salePrice?: string;
  direction: "INPUT" | "OUTPUT";
  systemQuantity: number | null;
  familyRootProductId?: string;
  conversionFactor?: number;
  maximumLossPercent?: number;
};

export type DurableInventoryOperationDraft = {
  key: string;
  businessId: string;
  kind: "count" | "adjustment" | "transfer" | "conversion" | "damage";
  documentId: string;
  warehouseId: string;
  destinationId: string;
  reason: string;
  notes: string;
  countDocumentId: string | null;
  conversionType: "SPLIT" | "MERGE";
  valuationBasis?: "Cost" | "SalePrice";
  countCaptureStage?: "Count" | "Recount";
  lines: DurableOperationLine[];
  updatedAt: string;
};

export const inventoryDraftKey = (businessId: string, kind: string) =>
  `inventory:${businessId}:${kind}`;

const inventoryActiveKindKey = (businessId: string) =>
  `inventory:${businessId}:active-kind`;

type InventoryOperationKind = DurableInventoryOperationDraft["kind"];
type DurableInventoryOperationSelection = {
  key: string;
  businessId: string;
  kind: InventoryOperationKind;
  updatedAt: string;
};

function openDatabase(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DATABASE, VERSION);
    request.onupgradeneeded = () => {
      const database = request.result;
      if (!database.objectStoreNames.contains(STORE))
        database.createObjectStore(STORE, { keyPath: "key" });
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

async function transaction<T>(
  mode: IDBTransactionMode,
  action: (store: IDBObjectStore) => IDBRequest<T>,
): Promise<T> {
  const database = await openDatabase();
  try {
    return await new Promise<T>((resolve, reject) => {
      const request = action(database.transaction(STORE, mode).objectStore(STORE));
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
  } finally {
    database.close();
  }
}

export async function loadInventoryOperationDraft(key: string) {
  return (await transaction("readonly", (store) => store.get(key))) as
    | DurableInventoryOperationDraft
    | undefined;
}

export async function saveInventoryOperationDraft(
  draft: DurableInventoryOperationDraft,
) {
  await transaction("readwrite", (store) => store.put(draft));
}

export async function removeInventoryOperationDraft(key: string) {
  await transaction("readwrite", (store) => store.delete(key));
}

export async function loadActiveInventoryOperationKind(businessId: string) {
  const key = inventoryActiveKindKey(businessId);
  const selection = await transaction("readonly", (store) => store.get(key)) as
    | DurableInventoryOperationSelection
    | undefined;
  return selection?.businessId === businessId ? selection.kind : undefined;
}

export async function saveActiveInventoryOperationKind(
  businessId: string,
  kind: InventoryOperationKind,
) {
  const key = inventoryActiveKindKey(businessId);
  await transaction("readwrite", (store) => store.put({
    key,
    businessId,
    kind,
    updatedAt: new Date().toISOString(),
  }));
}
