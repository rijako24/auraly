import assert from "node:assert/strict";
import test from "node:test";
import { readRememberedTenantKey, rememberTenantKey, REMEMBERED_TENANT_KEY_STORAGE_KEY } from "./remembered-tenant-key";

class MemoryStorage {
  private readonly values = new Map<string, string>();
  getItem(key: string): string | null { return this.values.get(key) ?? null; }
  setItem(key: string, value: string): void { this.values.set(key, value); }
}

test("remembers only the normalized tenant key", () => {
  const storage = new MemoryStorage();
  rememberTenantKey("  @auraly  ", storage);
  assert.equal(storage.getItem(REMEMBERED_TENANT_KEY_STORAGE_KEY), "@auraly");
  assert.equal(readRememberedTenantKey(storage), "@auraly");
});

test("does not overwrite the remembered tenant with an empty value", () => {
  const storage = new MemoryStorage();
  rememberTenantKey("@auraly", storage);
  rememberTenantKey("   ", storage);
  assert.equal(readRememberedTenantKey(storage), "@auraly");
});