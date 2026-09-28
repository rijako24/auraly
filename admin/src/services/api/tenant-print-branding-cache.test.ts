import assert from "node:assert/strict";
import test from "node:test";
import { createTenantPrintBrandingCache } from "./tenant-print-branding-cache";

test("repeated invoice prints reuse one logo load within the same session", async () => {
  let reads = 0;
  let clock = 0;
  const cache = createTenantPrintBrandingCache(async () => ({ logo: ++reads }), () => clock);
  const first = await Promise.all([cache.get("session-one"), cache.get("session-one")]);
  assert.deepEqual(first, [{ logo: 1 }, { logo: 1 }]);
  assert.deepEqual(await cache.get("session-one"), { logo: 1 });
  assert.equal(reads, 1);

  clock += 10 * 60 * 1000;
  assert.deepEqual(await cache.get("session-one"), { logo: 2 });
  assert.deepEqual(await cache.get("session-two"), { logo: 3 });
  cache.clear();
  assert.deepEqual(await cache.get("session-two"), { logo: 4 });
});

test("a failed logo load can be retried", async () => {
  let reads = 0;
  const cache = createTenantPrintBrandingCache(async () => {
    if (++reads === 1) throw new Error("storage unavailable");
    return "data:image/png;base64,AQID";
  });
  await assert.rejects(cache.get("session-one"));
  assert.equal(await cache.get("session-one"), "data:image/png;base64,AQID");
  assert.equal(reads, 2);
});

test("releases superseded browser logo data after a session change", async () => {
  const released: string[] = [];
  const cache = createTenantPrintBrandingCache(
    async () => "blob:local-logo", Date.now, value => released.push(value));
  await cache.get("first");
  await cache.get("second");
  await Promise.resolve();
  assert.deepEqual(released, ["blob:local-logo"]);
});
