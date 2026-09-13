import assert from "node:assert/strict";
import test from "node:test";

import {
  canRequestOrderSave,
  removingLastRecoveredOrderLineCancelsOrder,
} from "./pos-order-save-availability";

test("habilita guardar pedido desde el primer producto", () => {
  assert.equal(canRequestOrderSave({ connected: true, lineCount: 1, busy: false }), true);
});

test("mantiene guardar pedido bloqueado sin productos, sin conexión o durante otra operación", () => {
  assert.equal(canRequestOrderSave({ connected: true, lineCount: 0, busy: false }), false);
  assert.equal(canRequestOrderSave({ connected: false, lineCount: 1, busy: false }), false);
  assert.equal(canRequestOrderSave({ connected: true, lineCount: 1, busy: true }), false);
});

test("eliminar la última línea de un pedido recuperado usa la cancelación del pedido", () => {
  assert.equal(removingLastRecoveredOrderLineCancelsOrder({
    sourceOrderId: "order-1",
    lineCount: 1,
  }), true);
  assert.equal(removingLastRecoveredOrderLineCancelsOrder({
    sourceOrderId: "order-1",
    lineCount: 2,
  }), false);
  assert.equal(removingLastRecoveredOrderLineCancelsOrder({
    sourceOrderId: null,
    lineCount: 1,
  }), false);
});
