import assert from "node:assert/strict";
import test from "node:test";

import { lineDiscountPercent, lineMarginPercent, nextFocusableIndex, salePriceForMargin } from "./pos-line-editor-calculation";

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
