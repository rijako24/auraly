import assert from "node:assert/strict";
import test from "node:test";

import {
  canConfirmWarehouseTransferReceipt,
  defaultInventoryOperationKind,
  inventoryDocumentTypeForKind,
  inventoryOperationKinds,
} from "./inventory-operation-launch";

test("nueva operación abre conteo y conserva todos los procesos disponibles", () => {
  assert.equal(defaultInventoryOperationKind, "count");
  assert.deepEqual(inventoryOperationKinds, [
    "count",
    "adjustment",
    "transfer",
    "conversion",
    "damage",
    "production",
  ]);
});

test("solo permite confirmar entrada cuando el traslado tiene cantidad pendiente", () => {
  assert.equal(canConfirmWarehouseTransferReceipt("Dispatched"), true);
  assert.equal(canConfirmWarehouseTransferReceipt("PartiallyReceived"), true);
  assert.equal(canConfirmWarehouseTransferReceipt("DispatchPending"), false);
  assert.equal(canConfirmWarehouseTransferReceipt("ReceiptPending"), false);
  assert.equal(canConfirmWarehouseTransferReceipt("Received"), false);
});

test("al completar una operación abre el historial del mismo tipo", () => {
  assert.equal(inventoryDocumentTypeForKind("transfer"), "WarehouseTransfer");
  assert.equal(inventoryDocumentTypeForKind("damage"), "Damage");
  assert.equal(inventoryDocumentTypeForKind("production"), "Production");
});
