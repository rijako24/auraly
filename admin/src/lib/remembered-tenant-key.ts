export const REMEMBERED_TENANT_KEY_STORAGE_KEY = "auraly:last-tenant-key";

type TenantKeyStorage = Pick<Storage, "getItem" | "setItem">;

function browserStorage(): TenantKeyStorage | null {
  if (typeof window === "undefined") return null;
  try {
    return window.localStorage;
  } catch {
    return null;
  }
}

export function readRememberedTenantKey(storage: TenantKeyStorage | null = browserStorage()): string {
  if (!storage) return "";
  try {
    return storage.getItem(REMEMBERED_TENANT_KEY_STORAGE_KEY)?.trim() ?? "";
  } catch {
    return "";
  }
}

export function rememberTenantKey(tenantKey: string, storage: TenantKeyStorage | null = browserStorage()): void {
  const normalized = tenantKey.trim();
  if (!storage || !normalized) return;
  try {
    storage.setItem(REMEMBERED_TENANT_KEY_STORAGE_KEY, normalized);
  } catch {
    // Browsers in private or hardened mode may disable storage; login must still work.
  }
}