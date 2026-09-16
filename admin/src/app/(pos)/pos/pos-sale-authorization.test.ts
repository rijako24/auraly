import assert from "node:assert/strict";
import test from "node:test";

import { saleRequiresBelowCostAuthorization } from "./pos-sale-authorization";

test("requires approval when the tax-exclusive line net is below its document cost", () => {
  assert.equal(saleRequiresBelowCostAuthorization([
    { quantity: 2.35, net: 56_400, documentUnitCost: 24_001 },
  ]), true);
});

test("does not require approval at cost, above cost, or without a positive cost", () => {
  assert.equal(saleRequiresBelowCostAuthorization([
    { quantity: 2, net: 48_000, documentUnitCost: 24_000 },
    { quantity: 1, net: 30_600, documentUnitCost: 24_000 },
    { quantity: 1, net: 1, documentUnitCost: 0 },
  ]), false);
});
