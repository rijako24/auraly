import assert from "node:assert/strict";
import test from "node:test";

import { canRequestOrderSave } from "./pos-order-save-availability";

test("habilita guardar pedido desde el primer producto", () => {
  assert.equal(canRequestOrderSave({ connected: true, lineCount: 1, busy: false }), true);
});

test("mantiene guardar pedido bloqueado sin productos, sin conexión o durante otra operación", () => {
  assert.equal(canRequestOrderSave({ connected: true, lineCount: 0, busy: false }), false);
  assert.equal(canRequestOrderSave({ connected: false, lineCount: 1, busy: false }), false);
  assert.equal(canRequestOrderSave({ connected: true, lineCount: 1, busy: true }), false);
});
