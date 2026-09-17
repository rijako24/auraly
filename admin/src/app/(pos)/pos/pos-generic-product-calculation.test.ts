import assert from "node:assert/strict";
import test from "node:test";

import {
  genericProductFromMargin,
  genericProductFromSalePrice,
  initializeGenericProductPrice,
  nextGenericProductFieldIndex,
} from "./pos-generic-product-calculation";

test("first sale price initializes zero margin on its tax-exclusive cost", () => {
  assert.deepEqual(initializeGenericProductPrice(119_000, 19), {
    publicSalePrice: 119_000,
    documentUnitCost: 100_000,
    marginPercent: 0,
  });
});

test("changing sale price preserves cost and recalculates margin", () => {
  assert.deepEqual(genericProductFromSalePrice(95_200, 100_000, 19), {
    publicSalePrice: 95_200,
    documentUnitCost: 100_000,
    marginPercent: -25,
  });
});

test("changing margin or cost preserves both and recalculates sale price", () => {
  assert.deepEqual(genericProductFromMargin(80_000, 20, 19), {
    publicSalePrice: 119_000,
    documentUnitCost: 80_000,
    marginPercent: 20,
  });
});

test("arrow keys move vertically through generic product fields without leaving the form", () => {
  assert.equal(nextGenericProductFieldIndex(0, 3, "ArrowDown"), 1);
  assert.equal(nextGenericProductFieldIndex(1, 3, "ArrowDown"), 2);
  assert.equal(nextGenericProductFieldIndex(2, 3, "ArrowDown"), 2);
  assert.equal(nextGenericProductFieldIndex(2, 3, "ArrowUp"), 1);
  assert.equal(nextGenericProductFieldIndex(0, 3, "ArrowUp"), 0);
  assert.equal(nextGenericProductFieldIndex(1, 3, "Enter"), null);
});
