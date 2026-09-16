import assert from "node:assert/strict";
import test from "node:test";

import { buildPosOrderUpdateLines } from "./pos-order-update-lines";

test("converts the online net line back to the public price when building an order", () => {
  const lines = buildPosOrderUpdateLines([
    {
      productId: { value: "product-1" },
      quantity: 1,
      unitPrice: 1_765.55,
      discount: 0,
      taxRate: 19,
      total: 2_101,
      priceSource: "PriceChannel",
      documentUnitCost: 7_250,
      publicUnitPrice: 2_101,
    },
  ]);

  assert.deepEqual(lines, [
    {
      productId: "product-1",
      quantity: 1,
      unitPrice: 2_101,
      discountAmount: 0,
      priceSource: "PriceChannel",
      documentUnitCost: 7_250,
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
      promotionDiscount: 500,
      taxRate: 0,
      total: 54_500,
      priceSource: "Promotion",
      documentUnitCost: 4_100,
    },
    {
      productId: { value: "new-product" },
      quantity: 2,
      unitPrice: 4_500,
      discount: 0,
      taxRate: 0,
      total: 9_000,
      priceSource: "Base",
      documentUnitCost: 2_250,
    },
  ]);

  assert.deepEqual(lines, [
    {
      productId: "kept-product",
      quantity: 7,
      unitPrice: 8_000,
      discountAmount: 1_500,
      priceSource: "Promotion",
      documentUnitCost: 4_100,
    },
    {
      productId: "new-product",
      quantity: 2,
      unitPrice: 4_500,
      discountAmount: 0,
      priceSource: "Base",
      documentUnitCost: 2_250,
    },
  ]);
  assert.equal(lines.some((line) => line.productId === "removed-product"), false);
});

test("preserves the exact public total instead of producing a negative cent discount", () => {
  const lines = buildPosOrderUpdateLines([
    {
      productId: { value: "taxed-product" },
      quantity: 3,
      unitPrice: 3_361.34,
      discount: 0,
      taxRate: 19,
      total: 12_000,
      priceSource: "Public",
      documentUnitCost: 2_000,
      publicUnitPrice: 4_000,
    },
  ]);

  assert.deepEqual(lines, [{
    productId: "taxed-product",
    quantity: 3,
    unitPrice: 4_000,
    discountAmount: 0,
    priceSource: "Public",
    documentUnitCost: 2_000,
  }]);
});
