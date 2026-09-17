import assert from "node:assert/strict";
import test from "node:test";

import { consumeOrderRecoveryUrl } from "./pos-order-recovery-url";

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
