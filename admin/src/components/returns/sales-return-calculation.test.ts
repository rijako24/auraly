import assert from "node:assert/strict";
import test from "node:test";

import {
  calculateSalesReturnSelection,
  salesReturnPurchasedUnitPrice,
} from "./sales-return-calculation";

const lines = [
  { originalLineNumber: 1, soldQuantity: 2, availableQuantity: 1.5, lineTotal: 23_800 },
  { originalLineNumber: 2, soldQuantity: 1, availableQuantity: 1, lineTotal: 5_000 },
];

test("expone el precio efectivamente comprado desde el snapshot", () => {
  assert.equal(salesReturnPurchasedUnitPrice(lines[0]), 11_900);
});

test("estima una devolución parcial conservando la proporción del snapshot", () => {
  assert.deepEqual(calculateSalesReturnSelection(lines, { 1: .5, 2: 1 }), {
    selectedLineNumbers: [1, 2],
    lineTotals: { 1: 5_950, 2: 5_000 },
    estimatedTotal: 10_950,
    isValid: true,
  });
});

test("rechaza cantidades que superan el saldo disponible", () => {
  assert.deepEqual(calculateSalesReturnSelection(lines, { 1: 1.500001 }), {
    selectedLineNumbers: [],
    lineTotals: {},
    estimatedTotal: 0,
    isValid: false,
  });
});

test("ignora líneas sin cantidad y rechaza valores no numéricos", () => {
  assert.deepEqual(calculateSalesReturnSelection(lines, { 1: Number.NaN }), {
    selectedLineNumbers: [],
    lineTotals: {},
    estimatedTotal: 0,
    isValid: false,
  });
});
