import assert from "node:assert/strict";
import test from "node:test";

import {
  calculateReceiptRetailUnitPrice,
  calculateRetailUnitPrice,
} from "./pos-retail-price";

test("muestra el precio de venta unitario con IVA incluido", () => {
  assert.equal(calculateRetailUnitPrice(12_500, 19), 12_500);
  assert.equal(calculateRetailUnitPrice(12_500, 0), 12_500);
});

test("reconstruye el precio de venta en una tirilla historica", () => {
  assert.equal(
    calculateReceiptRetailUnitPrice(12_500, 2, 1_000, 4_560),
    12_500,
  );
});

test("no presenta precios invalidos", () => {
  assert.equal(calculateRetailUnitPrice(Number.NaN, 19), 0);
});
