import assert from "node:assert/strict";
import test from "node:test";

import { calculatePosDiscount } from "./pos-discount-calculation";

test("calcula el mismo descuento por valor y porcentaje", () => {
  const byValue = calculatePosDiscount("value", 1_250, 12_500, 19);
  const byPercentage = calculatePosDiscount("percentage", 10, 12_500, 19);

  assert.deepEqual(byValue, byPercentage);
  assert.deepEqual(byValue, {
    discount: 1_250,
    percentage: 10,
    net: 9_453.78,
    tax: 1_796.22,
    total: 11_250,
  });
});

test("rechaza porcentajes superiores a cien y valores mayores al bruto", () => {
  assert.equal(calculatePosDiscount("percentage", 100.01, 10_000, 19), null);
  assert.equal(calculatePosDiscount("value", 10_001, 10_000, 19), null);
});

test("permite retirar el descuento con cero", () => {
  assert.deepEqual(calculatePosDiscount("percentage", 0, 10_000, 5), {
    discount: 0,
    percentage: 0,
    net: 9_523.81,
    tax: 476.19,
    total: 10_000,
  });
});
