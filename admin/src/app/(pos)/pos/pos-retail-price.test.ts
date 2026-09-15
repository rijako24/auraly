import assert from "node:assert/strict";
import test from "node:test";

import "./pos-temporary-name.test";

import {
  calculateReceiptRetailUnitPrice,
  calculateRetailUnitPrice,
} from "./pos-retail-price";

test("muestra el precio de venta unitario con IVA incluido", () => {
  assert.equal(calculateRetailUnitPrice(1_765.55, 19, true), 2_101);
  assert.equal(calculateRetailUnitPrice(2_101, 19, false), 2_101);
});

test("reconstruye el precio de venta en una tirilla historica", () => {
  assert.equal(
    calculateReceiptRetailUnitPrice(12_500),
    12_500,
  );
});

test("no presenta precios invalidos", () => {
  assert.equal(calculateRetailUnitPrice(Number.NaN), 0);
  assert.equal(calculateRetailUnitPrice(100, Number.NaN, true), 0);
});
