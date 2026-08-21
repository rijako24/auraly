export const SALES_OFFLINE_DATABASE = "auraly-sales-pwa";
export const SALES_OFFLINE_DATABASE_VERSION = 3;

const stores: Array<[string, IDBObjectStoreParameters]> = [
  ["seller-catalog", { keyPath: "key" }],
  ["seller-order-drafts", { keyPath: "key" }],
  ["seller-order-outbox", { keyPath: "id" }],
  ["daily-route-snapshots", { keyPath: "key" }],
  ["route-visit-outbox", { keyPath: "idempotencyKey" }],
  ["seller-offline-preparations", { keyPath: "key" }],
];

export function openSalesOfflineDatabase(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(SALES_OFFLINE_DATABASE, SALES_OFFLINE_DATABASE_VERSION);
    request.onupgradeneeded = () => {
      const database = request.result;
      for (const [name, options] of stores)
        if (!database.objectStoreNames.contains(name)) database.createObjectStore(name, options);
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
    request.onblocked = () => reject(new Error("Cierra otras ventanas de Auraly y vuelve a intentar."));
  });
}
