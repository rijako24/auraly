export const SALES_OFFLINE_DATABASE = "auraly-sales-pwa";
export const SALES_OFFLINE_DATABASE_VERSION = 6;

const stores: Array<[string, IDBObjectStoreParameters]> = [
  ["seller-catalog", { keyPath: "key" }],
  ["seller-order-drafts", { keyPath: "key" }],
  ["seller-order-outbox", { keyPath: "id" }],
  ["seller-order-snapshots", { keyPath: "idempotencyKey" }],
  ["daily-route-snapshots", { keyPath: "key" }],
  ["route-visit-outbox", { keyPath: "idempotencyKey" }],
  ["seller-offline-preparations", { keyPath: "key" }],
  ["seller-workspaces", { keyPath: "key" }],
];

export function openSalesOfflineDatabase(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(SALES_OFFLINE_DATABASE, SALES_OFFLINE_DATABASE_VERSION);
    request.onupgradeneeded = () => {
      const database = request.result;
      if (database.objectStoreNames.contains("offline-logins"))
        database.deleteObjectStore("offline-logins");
      for (const [name, options] of stores)
        if (!database.objectStoreNames.contains(name)) database.createObjectStore(name, options);
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
    request.onblocked = () => reject(new Error("Cierra otras ventanas de Auraly y vuelve a intentar."));
  });
}
