import assert from "node:assert/strict";
import test from "node:test";

import {
  defaultInventoryOperationKind,
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
