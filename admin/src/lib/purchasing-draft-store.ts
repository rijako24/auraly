const DATABASE = "auraly-purchasing-work";
const VERSION = 2;
const GOODS_RECEIPT_STORE = "goods-receipt-drafts";
const PURCHASE_ORDER_STORE = "purchase-order-drafts";

type StoredPurchasingDraft<T> = {
  key: string;
  value: T;
  updatedAt: string;
};

function openDatabase(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DATABASE, VERSION);
    request.onupgradeneeded = () => {
      const database = request.result;
      for (const store of [GOODS_RECEIPT_STORE, PURCHASE_ORDER_STORE]) {
        if (!database.objectStoreNames.contains(store))
          database.createObjectStore(store, { keyPath: "key" });
      }
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

async function transaction<T>(
  storeName: string,
  mode: IDBTransactionMode,
  action: (store: IDBObjectStore) => IDBRequest<T>,
): Promise<T> {
  const database = await openDatabase();
  try {
    return await new Promise<T>((resolve, reject) => {
      const indexedDbTransaction = database.transaction(storeName, mode);
      const request = action(indexedDbTransaction.objectStore(storeName));
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
  const stored = await transaction(GOODS_RECEIPT_STORE, "readonly", (store) => store.get(key)) as
    | StoredPurchasingDraft<T>
    | undefined;
  return stored?.value;
}

export async function saveGoodsReceiptDraft<T>(key: string, value: T) {
  await transaction(GOODS_RECEIPT_STORE, "readwrite", (store) => store.put({
    key,
    value,
    updatedAt: new Date().toISOString(),
  }));
}

export async function removeGoodsReceiptDraft(key: string) {
  await transaction(GOODS_RECEIPT_STORE, "readwrite", (store) => store.delete(key));
}

export const purchaseOrderDraftKey = (userId: string, businessId: string) =>
  `purchase-order:${userId}:${businessId}`;

export async function loadPurchaseOrderDraft<T>(key: string) {
  const stored = await transaction(PURCHASE_ORDER_STORE, "readonly", (store) => store.get(key)) as
    | StoredPurchasingDraft<T>
    | undefined;
  return stored?.value;
}

export async function savePurchaseOrderDraft<T>(key: string, value: T) {
  await transaction(PURCHASE_ORDER_STORE, "readwrite", (store) => store.put({
    key,
    value,
    updatedAt: new Date().toISOString(),
  }));
}

export async function removePurchaseOrderDraft(key: string) {
  await transaction(PURCHASE_ORDER_STORE, "readwrite", (store) => store.delete(key));
}
