import assert from "node:assert/strict";
import test from "node:test";

import {
  canConfirmWarehouseTransferReceipt,
  defaultInventoryOperationKind,
  inventoryDocumentLabel,
  inventoryDocumentTypeForKind,
  inventoryMovementLabel,
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
  ]);
});

test("presenta en español los movimientos y documentos internos de traslado", () => {
  assert.equal(inventoryMovementLabel("TransferDispatchOut"), "Salida por traslado");
  assert.equal(inventoryMovementLabel("TransferReceiptIn"), "Entrada por traslado");
  assert.equal(inventoryDocumentLabel("WarehouseTransferReceipt"), "Recepción de traslado");
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
});
