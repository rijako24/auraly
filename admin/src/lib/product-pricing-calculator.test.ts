import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { marginFromCostAndSale, recalculateProductPricing } from "./product-pricing-calculator";

describe("product pricing calculator", () => {
  it("keeps margin and recalculates sale price when cost changes", () => {
    assert.deepEqual(
      recalculateProductPricing("cost", 80, { cost: 50, margin: 20, salePrice: 62.5 }),
      { cost: 80, margin: 20, salePrice: 100, salesTaxRate: 0 },
    );
  });

  it("keeps cost and recalculates sale price when margin changes", () => {
    assert.deepEqual(
      recalculateProductPricing("margin", 25, { cost: 75, margin: 10, salePrice: 83.33 }),
      { cost: 75, margin: 25, salePrice: 100, salesTaxRate: 0 },
    );
  });

  it("keeps cost and recalculates margin when sale price changes", () => {
    assert.deepEqual(
      recalculateProductPricing("salePrice", 200, { cost: 80, margin: 30, salePrice: 114.29 }),
      { cost: 80, margin: 60, salePrice: 200, salesTaxRate: 0 },
    );
  });

  it("calculates the public sale price including sales VAT when margin changes", () => {
    assert.deepEqual(
      recalculateProductPricing("margin", 20, {
        cost: 80, margin: 10, salePrice: 100, salesTaxRate: 19,
      }),
      { cost: 80, margin: 20, salePrice: 119, salesTaxRate: 19 },
    );
  });

  it("recalculates margin from a VAT-included sale price without changing cost", () => {
    assert.deepEqual(
      recalculateProductPricing("salePrice", 119, {
        cost: 80, margin: 10, salePrice: 100, salesTaxRate: 19,
      }),
      { cost: 80, margin: 20, salePrice: 119, salesTaxRate: 19 },
    );
  });
  it("allows an explicit sale price to recover from an old negative margin", () => {
    assert.deepEqual(
      recalculateProductPricing("salePrice", 95.2, {
        cost: 100, margin: -25, salePrice: 80, salesTaxRate: 19,
      }),
      { cost: 100, margin: -25, salePrice: 95.2, salesTaxRate: 19 },
    );
  });

  it("derives a margin for legacy prices that have cost but no saved margin", () => {
    assert.equal(marginFromCostAndSale(80, 100), 20);
  });

  it("rejects impossible margins", () => {
    assert.throws(
      () => recalculateProductPricing("margin", 100, { cost: 80, margin: 20, salePrice: 100 }),
      RangeError,
    );
  });
});