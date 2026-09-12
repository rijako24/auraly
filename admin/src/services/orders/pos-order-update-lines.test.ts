import assert from "node:assert/strict";
import test from "node:test";

import { buildPosOrderUpdateLines } from "./pos-order-update-lines";

test("preserves the recovered order price and discount when building update lines", () => {
  const lines = buildPosOrderUpdateLines([
    {
      productId: { value: "product-1" },
      quantity: 3,
      unitPrice: 12_500,
      discount: 2_500,
      priceSource: "PriceChannel",
    },
  ]);

  assert.deepEqual(lines, [
    {
      productId: "product-1",
      quantity: 3,
      unitPrice: 12_500,
      discountAmount: 2_500,
      priceSource: "PriceChannel",
    },
  ]);
});

test("builds the complete replacement from only the lines that remain in the recovered sale", () => {
  const lines = buildPosOrderUpdateLines([
    {
      productId: { value: "kept-product" },
      quantity: 7,
      unitPrice: 8_000,
      discount: 1_000,
      priceSource: "Promotion",
    },
    {
      productId: { value: "new-product" },
      quantity: 2,
      unitPrice: 4_500,
      discount: 0,
      priceSource: "Base",
    },
  ]);

  assert.deepEqual(lines, [
    {
      productId: "kept-product",
      quantity: 7,
      unitPrice: 8_000,
      discountAmount: 1_000,
      priceSource: "Promotion",
    },
    {
      productId: "new-product",
      quantity: 2,
      unitPrice: 4_500,
      discountAmount: 0,
      priceSource: "Base",
    },
  ]);
  assert.equal(lines.some((line) => line.productId === "removed-product"), false);
});
