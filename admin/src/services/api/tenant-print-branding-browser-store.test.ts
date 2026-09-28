import assert from "node:assert/strict";
import test from "node:test";
import { loadTenantPrintBrandingFromBrowserStore,
  type ConditionalBranding } from "./tenant-print-branding-browser-store";

test("one conditional request per POS entry reuses browser disk logo when unchanged", async () => {
  const values = new Map<string, Response>();
  const storage = {
    open: async () => ({
      match: async (key: URL) => values.get(key.href)?.clone(),
      put: async (key: URL, response: Response) => { values.set(key.href, response.clone()); },
    }),
  } as unknown as CacheStorage;
  let requests = 0;
  let downloads = 0;
  let currentVersion = '"v1"';
  const fetchBranding = async (etag: string | null): Promise<ConditionalBranding<{
    tenantId: string; logoUrl: string | null;
  }>> => {
    requests += 1;
    if (etag === currentVersion) return { notModified: true as const };
    downloads += 1;
    return { notModified: false as const, etag: currentVersion,
      value: { tenantId: "tenant-one", logoUrl: `data:image/png;base64,${downloads}` } };
  };
  const read = () => loadTenantPrintBrandingFromBrowserStore(
    "tenant-one", fetchBranding, storage, "https://auraly.example");

  assert.equal((await read()).logoUrl, "data:image/png;base64,1");
  assert.equal((await read()).logoUrl, "data:image/png;base64,1");
  assert.equal(requests, 2);
  assert.equal(downloads, 1);
  currentVersion = '"v2"';
  assert.equal((await read()).logoUrl, "data:image/png;base64,2");
  assert.equal(downloads, 2);
});

test("no configured logo uses one request and never downloads an image", async () => {
  let requests = 0;
  const branding = await loadTenantPrintBrandingFromBrowserStore(
    "tenant-one", async () => {
      requests += 1;
      return { notModified: false, etag: '"empty"',
        value: { tenantId: "tenant-one", logoUrl: null } };
    }, undefined, "https://auraly.example");
  assert.equal(requests, 1);
  assert.equal(branding.logoUrl, null);
});

test("browser cache never serves another tenant's logo", async () => {
  await assert.rejects(loadTenantPrintBrandingFromBrowserStore(
    "tenant-one", async () => ({ notModified: false, etag: '"v1"',
      value: { tenantId: "tenant-two" } }), undefined,
    "https://auraly.example"), /cambió de empresa/);
});
