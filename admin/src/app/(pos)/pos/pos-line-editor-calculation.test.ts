import assert from "node:assert/strict";
import test from "node:test";

import { lineDiscountPercent, lineMarginPercent, nextFocusableIndex, nextGridPosition, prorateAdditionalSaleValue, salePriceForMargin } from "./pos-line-editor-calculation";

test("keeps value and percentage discounts synchronized", () => {
  assert.equal(lineDiscountPercent(20_000, 2, 100_000), 10);
});

test("calculates margin from the net untaxed sale", () => {
  assert.equal(lineMarginPercent(50_000, 1, 119_000, 11_900, 19), 44.4444);
});

test("recalculates sale price from margin while preserving discount percentage", () => {
  assert.equal(salePriceForMargin(50_000, 50, 10, 19), 132_222.222222);
});

test("moves keyboard focus forward and backward with wraparound", () => {
  assert.equal(nextFocusableIndex(2, 6, false), 3);
  assert.equal(nextFocusableIndex(5, 6, false), 0);
  assert.equal(nextFocusableIndex(0, 6, true), 5);
  assert.equal(nextFocusableIndex(-1, 6, false), 0);
  assert.equal(nextFocusableIndex(-1, 6, true), 5);
});

test("prorates one additional value equally per unit across all sale lines", () => {
  const original = [
    { lineId: "one", quantity: 1, unitPrice: 10_000 },
    { lineId: "six", quantity: 6, unitPrice: 20_000 },
  ];
  const result = prorateAdditionalSaleValue(original, 7_000);
  assert.deepEqual(result, [
    { lineId: "one", quantity: 1, unitPrice: 11_000, allocatedValue: 1_000 },
    { lineId: "six", quantity: 6, unitPrice: 21_000, allocatedValue: 6_000 },
  ]);
  assert.equal(original[0].unitPrice, 10_000);
  assert.equal([...result, { lineId: "captured-later", quantity: 1, unitPrice: 30_000 }][2].unitPrice, 30_000);
});

test("preserves fractional quantities and the requested total within money precision", () => {
  const result = prorateAdditionalSaleValue([
    { lineId: "half", quantity: .5, unitPrice: 10_000 },
    { lineId: "two", quantity: 2, unitPrice: 20_000 },
  ], 1_000);
  assert.ok(Math.abs(result.reduce((sum, line) => sum + line.allocatedValue, 0) - 1_000) < .00001);
  assert.throws(() => prorateAdditionalSaleValue(result, 0));
});

test("moves through the editable sale-line grid and skips disabled cost cells", () => {
  const columns = [[0, 2, 3, 4, 5], [0, 1, 2, 3, 4, 5]];
  assert.deepEqual(nextGridPosition(0, 3, columns, "ArrowRight"), { row: 0, column: 4 });
  assert.deepEqual(nextGridPosition(0, 0, columns, "ArrowLeft"), { row: 0, column: 5 });
  assert.deepEqual(nextGridPosition(1, 1, columns, "ArrowUp"), { row: 0, column: 0 });
  assert.deepEqual(nextGridPosition(1, 4, columns, "ArrowDown"), { row: 0, column: 4 });
});
