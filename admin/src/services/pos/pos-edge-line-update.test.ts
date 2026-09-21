import assert from "node:assert/strict";
import test from "node:test";
import { buildCompatibleEdgeLineUpdates } from "./pos-edge-line-update";

test("edge line updates preserve the canonical price and the installed-app alias", () => {
  const [serialized] = buildCompatibleEdgeLineUpdates([{
    lineId: "10000000-0000-0000-0000-000000000001",
    description: "Producto con descuento F2",
    publicUnitPrice: 10_000,
    discount: 500,
    documentUnitCost: 7_000,
  }]);

  assert.deepEqual(serialized, {
    lineId: "10000000-0000-0000-0000-000000000001",
    description: "Producto con descuento F2",
    publicUnitPrice: 10_000,
    unitPrice: 10_000,
    discount: 500,
    documentUnitCost: 7_000,
  });
});
