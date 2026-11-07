import assert from "node:assert/strict";
import test from "node:test";

import { consumeOrderRecoveryUrl, isOrderRecoveryWorkspace } from "./pos-order-recovery-url";

test("consumes a recovery request once and preserves unrelated navigation state", () => {
  assert.deepEqual(
    consumeOrderRecoveryUrl("https://dev.auraly.test/pos?recoverOrder=order-1&panel=orders#sale"),
    { orderId: "order-1", nextUrl: "/pos?panel=orders#sale" },
  );
});

test("returns no recovery when the URL does not request one", () => {
  assert.deepEqual(
    consumeOrderRecoveryUrl("https://dev.auraly.test/pos?panel=orders"),
    { orderId: null, nextUrl: "/pos?panel=orders" },
  );
});

test("restaurant order entry survives consuming the recovery URL and refreshing", () => {
  const start = "https://dev.auraly.test/pos?recoverOrder=order-1";
  assert.equal(isOrderRecoveryWorkspace(start), true);
  const consumed = consumeOrderRecoveryUrl(start, true);
  assert.deepEqual(consumed, { orderId: "order-1", nextUrl: "/pos?workspace=orders" });
  assert.equal(isOrderRecoveryWorkspace(new URL(consumed.nextUrl, start).href), true);
  assert.equal(isOrderRecoveryWorkspace("https://dev.auraly.test/pos"), false);
  assert.equal(isOrderRecoveryWorkspace("https://dev.auraly.test/pos?workspace=change"), false);
});
