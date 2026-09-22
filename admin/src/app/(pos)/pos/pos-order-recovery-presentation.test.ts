import assert from "node:assert/strict";
import test from "node:test";

import { completedOrderRecoveryPresentation } from "./pos-order-recovery-presentation";

test("una recuperación exitosa limpia cualquier error anterior del POS", () => {
  assert.deepEqual(completedOrderRecoveryPresentation(["line-1", "line-2"]), {
    error: null,
    selectedLineId: "line-1",
    message: "Pedido recuperado · 2 líneas",
  });
});
