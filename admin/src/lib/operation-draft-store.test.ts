import assert from "node:assert/strict";
import test from "node:test";

import { inventoryOperationOccurredAt } from "./operation-draft-store";

test("conserva la fecha original al reintentar una operación de inventario", () => {
  assert.equal(inventoryOperationOccurredAt({
    occurredAt: "2026-09-18T14:00:00.000Z",
    updatedAt: "2026-09-18T14:05:00.000Z",
  }), "2026-09-18T14:00:00.000Z");
});

test("migra un borrador local anterior usando su fecha persistida", () => {
  assert.equal(inventoryOperationOccurredAt({
    updatedAt: "2026-09-18T14:05:00.000Z",
  }), "2026-09-18T14:05:00.000Z");
});
