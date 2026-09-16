import assert from "node:assert/strict";
import test from "node:test";

import {
  canRequestOrderSave,
  orderSaveRequiresCustomerSelection,
  removingLastRecoveredOrderLineCancelsOrder,
  shouldSaveOrderAfterCustomerSelection,
} from "./pos-order-save-availability";

test("habilita guardar pedido desde el primer producto", () => {
  assert.equal(canRequestOrderSave({ lineCount: 1, busy: false }), true);
});

test("intenta la API sin prejuzgar conectividad y solo bloquea sin productos o durante otra operación", () => {
  assert.equal(canRequestOrderSave({ lineCount: 0, busy: false }), false);
  assert.equal(canRequestOrderSave({ lineCount: 1, busy: false }), true);
  assert.equal(canRequestOrderSave({ lineCount: 1, busy: true }), false);
});

test("guardar pedido sin cliente abre primero la selección de cliente", () => {
  assert.equal(orderSaveRequiresCustomerSelection(null), true);
  assert.equal(orderSaveRequiresCustomerSelection(undefined), true);
  assert.equal(orderSaveRequiresCustomerSelection("customer-1"), false);
});

test("después de seleccionar y valorizar el cliente continúa el guardado pendiente", () => {
  assert.equal(shouldSaveOrderAfterCustomerSelection({
    pendingOrderSave: true,
    selectedCustomerId: "customer-1",
  }), true);
  assert.equal(shouldSaveOrderAfterCustomerSelection({
    pendingOrderSave: true,
    selectedCustomerId: null,
  }), false);
  assert.equal(shouldSaveOrderAfterCustomerSelection({
    pendingOrderSave: false,
    selectedCustomerId: "customer-1",
  }), false);
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
