import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  calculateGoodsReceiptLine,
  calculateBaseQuantity,
  calculateGoodsReceiptTotals,
  goodsReceiptUnitLabel,
  nextGoodsReceiptQuantityIndex,
} from "./goods-receipt-calculator";

describe("goods receipt calculator", () => {
  it("recalculates quantity, discount, purchase VAT and line total", () => {
    assert.deepEqual(calculateGoodsReceiptLine({
      quantity: 3,
      unitCost: 10_000,
      discountAmount: 3_000,
      taxRate: 19,
    }), { net: 27_000, tax: 5_130, total: 32_130 });
  });

  it("totals lines with different purchase VAT rates", () => {
    assert.deepEqual(calculateGoodsReceiptTotals([
      { quantity: 2, unitCost: 10_000, discountAmount: 0, taxRate: 19 },
      { quantity: 4, unitCost: 5_000, discountAmount: 2_000, taxRate: 5 },
    ]), { net: 38_000, tax: 4_700, total: 42_700 });
  });

  it("converts purchasing presentations into inventory units", () => {
    assert.equal(calculateBaseQuantity(3, 24), 72);
    assert.throws(() => calculateBaseQuantity(1, 0));
  });

  it("shows the technical EA inventory unit in Spanish", () => {
    assert.equal(goodsReceiptUnitLabel("EA", 1), "unidad");
    assert.equal(goodsReceiptUnitLabel("EA", 10), "unidades");
    assert.equal(goodsReceiptUnitLabel("NIU", 2), "unidades");
    assert.equal(goodsReceiptUnitLabel("KGM", 2), "KGM");
  });

  it("moves through quantity cells without changing the quantity", () => {
    assert.equal(nextGoodsReceiptQuantityIndex(0, 1, 3), 1);
    assert.equal(nextGoodsReceiptQuantityIndex(1, 1, 3), 2);
    assert.equal(nextGoodsReceiptQuantityIndex(2, 1, 3), 2);
    assert.equal(nextGoodsReceiptQuantityIndex(1, -1, 3), 0);
    assert.equal(nextGoodsReceiptQuantityIndex(0, -1, 3), 0);
  });
});
