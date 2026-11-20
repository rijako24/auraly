import type { Page } from "@playwright/test";

// Read-only evidence from the browser's real IndexedDB. Never creates a database
// before the application has created its store.
export async function readLocalCatalogDraft<T>(page: Page, key: string) {
  return page.evaluate(async key => {
    if (!(await indexedDB.databases()).some(database => database.name === "auraly-catalog-work")) return null;
    const database = await new Promise<IDBDatabase>((resolve, reject) => {
      const request = indexedDB.open("auraly-catalog-work");
      request.onsuccess = () => resolve(request.result); request.onerror = () => reject(request.error);
    });
    try {
      if (!database.objectStoreNames.contains("catalog-drafts")) return null;
      return await new Promise<T | null>((resolve, reject) => {
        const request = database.transaction("catalog-drafts").objectStore("catalog-drafts").get(key);
        request.onsuccess = () => resolve(request.result?.value ?? null); request.onerror = () => reject(request.error);
      });
    } finally { database.close(); }
  }, key);
}
