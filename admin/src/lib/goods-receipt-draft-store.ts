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
      const indexedDbTransaction = database.transaction(STORE, mode);
      const request = action(indexedDbTransaction.objectStore(STORE));
      let result: T;
      request.onsuccess = () => { result = request.result; };
      request.onerror = () => reject(request.error);
      indexedDbTransaction.oncomplete = () => resolve(result);
      indexedDbTransaction.onerror = () => reject(indexedDbTransaction.error);
      indexedDbTransaction.onabort = () => reject(indexedDbTransaction.error);
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
