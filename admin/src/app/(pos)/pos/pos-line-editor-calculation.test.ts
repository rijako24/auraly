import assert from "node:assert/strict";
import test from "node:test";

import { isPositiveWholeSaleValue, lineDiscountPercent, lineEconomicsFromDiscount, lineEconomicsFromDiscountPercent, lineEconomicsFromFinalPrice, lineEconomicsFromMargin, lineMarginPercent, nextFocusableIndex, nextGridPosition, prorateSaleDiscount, salePriceForMargin } from "./pos-line-editor-calculation";

test("keeps value and percentage discounts synchronized", () => {
  assert.equal(lineDiscountPercent(20_000, 2, 100_000), 10);
});

test("calculates margin from the net untaxed sale", () => {
  assert.equal(lineMarginPercent(50_000, 1, 119_000, 11_900, 19), 44.4444);
});

test("recalculates sale price from margin while preserving discount percentage", () => {
  assert.equal(salePriceForMargin(50_000, 50, 10, 19), 132_222.222222);
});

test("a lower final price creates a discount even when the original discount was zero", () => {
  assert.deepEqual(
    lineEconomicsFromFinalPrice(50_000, 2, 100_000, 80_000, 0),
    {
      finalUnitPrice: 80_000,
      documentUnitPrice: 100_000,
      discount: 40_000,
      discountPercent: 20,
      marginPercent: 37.5,
    },
  );
});

test("discount value percentage final price and margin remain mutually reactive", () => {
  const byValue = lineEconomicsFromDiscount(50_000, 2, 100_000, 30_000, 0);
  const byPercent = lineEconomicsFromDiscountPercent(50_000, 2, 100_000, 15, 0);
  assert.deepEqual(byPercent, byValue);
  assert.equal(byValue.finalUnitPrice, 85_000);
  assert.equal(byValue.marginPercent, 41.1765);
});

test("changing margin derives final price and its discount against the reference price", () => {
  assert.deepEqual(lineEconomicsFromMargin(50_000, 2, 120_000, 50, 0), {
    finalUnitPrice: 100_000,
    documentUnitPrice: 120_000,
    discount: 40_000,
    discountPercent: 16.6667,
    marginPercent: 50,
  });
});

test("raising final price never produces a negative discount", () => {
  assert.deepEqual(lineEconomicsFromFinalPrice(50_000, 1, 100_000, 130_000, 0), {
    finalUnitPrice: 130_000,
    documentUnitPrice: 130_000,
    discount: 0,
    discountPercent: 0,
    marginPercent: 61.5385,
  });
});

test("moves keyboard focus forward and backward with wraparound", () => {
  assert.equal(nextFocusableIndex(2, 6, false), 3);
  assert.equal(nextFocusableIndex(5, 6, false), 0);
  assert.equal(nextFocusableIndex(0, 6, true), 5);
  assert.equal(nextFocusableIndex(-1, 6, false), 0);
  assert.equal(nextFocusableIndex(-1, 6, true), 5);
});

test("the general discount accepts only positive whole values", () => {
  assert.equal(isPositiveWholeSaleValue(1), true);
  assert.equal(isPositiveWholeSaleValue(10_000), true);
  assert.equal(isPositiveWholeSaleValue(0), false);
  assert.equal(isPositiveWholeSaleValue(-1), false);
  assert.equal(isPositiveWholeSaleValue(1.5), false);
});

test("prorates a general discount by available line value and preserves the requested total", () => {
  const result = prorateSaleDiscount([
    { lineId: "small", quantity: 1, unitPrice: 10_000, discount: 0 },
    { lineId: "large", quantity: 2, unitPrice: 20_000, discount: 10_000 },
  ], 8_000);
  assert.deepEqual(result, [
    { lineId: "small", quantity: 1, unitPrice: 10_000, discount: 2_000, allocatedValue: 2_000 },
    { lineId: "large", quantity: 2, unitPrice: 20_000, discount: 16_000, allocatedValue: 6_000 },
  ]);
  assert.equal(result.reduce((sum, line) => sum + line.allocatedValue, 0), 8_000);
  assert.throws(() => prorateSaleDiscount(result, 33_000));
});

test("moves through editable columns and stops at the horizontal edges", () => {
  const columns = [[0, 2, 3, 4, 5]];
  assert.deepEqual(nextGridPosition(0, 3, columns, "ArrowRight"), { row: 0, column: 4 });
  assert.deepEqual(nextGridPosition(0, 3, columns, "ArrowLeft"), { row: 0, column: 2 });
  assert.equal(nextGridPosition(0, 0, columns, "ArrowLeft"), null);
  assert.equal(nextGridPosition(0, 5, columns, "ArrowRight"), null);
});

test("moves between sale lines, skips disabled cells and stops at the first and last line", () => {
  const columns = [[0, 2, 3, 4, 5], [0, 1, 2, 3, 4, 5], [], [0, 3, 4, 5]];
  assert.deepEqual(nextGridPosition(1, 1, columns, "ArrowUp"), { row: 0, column: 0 });
  assert.deepEqual(nextGridPosition(1, 4, columns, "ArrowDown"), { row: 3, column: 4 });
  assert.equal(nextGridPosition(0, 3, columns, "ArrowUp"), null);
  assert.equal(nextGridPosition(3, 3, columns, "ArrowDown"), null);
});
