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
    },
  ]);

  assert.deepEqual(lines, [
    {
      productId: "product-1",
      quantity: 3,
      unitPrice: 12_500,
      discountAmount: 2_500,
    },
  ]);
});
