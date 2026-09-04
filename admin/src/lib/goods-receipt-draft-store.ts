const DATABASE = "auraly-purchasing-work";
const VERSION = 1;
const STORE = "goods-receipt-drafts";

type StoredGoodsReceiptDraft<T> = {
  key: string;
  value: T;
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

export const goodsReceiptDraftKey = (userId: string, businessId: string) =>
  `goods-receipt:${userId}:${businessId}`;

export async function loadGoodsReceiptDraft<T>(key: string) {
  const stored = await transaction("readonly", (store) => store.get(key)) as
    | StoredGoodsReceiptDraft<T>
    | undefined;
  return stored?.value;
}

export async function saveGoodsReceiptDraft<T>(key: string, value: T) {
  await transaction("readwrite", (store) => store.put({
    key,
    value,
    updatedAt: new Date().toISOString(),
  }));
}

export async function removeGoodsReceiptDraft(key: string) {
  await transaction("readwrite", (store) => store.delete(key));
}
