import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  calculateGoodsReceiptLine,
  calculateBaseQuantity,
  calculateGoodsReceiptTotals,
  goodsReceiptUnitLabel,
  nextGoodsReceiptQuantityIndex,
  summarizeGoodsReceipt,
} from "./goods-receipt-calculator";

describe("goods receipt calculator", () => {
  it("separates all supplier documents from landed inventory cost without taxing freight twice", () => {
    assert.deepEqual(summarizeGoodsReceipt({
      currencyCode: "COP", grandTotal: 4760, taxAmount: 760,
      additionalCostDocuments: [{ currencyCode: "COP", grandTotal: 2380, taxAmount: 380,
        withholding: { withholdingTotal: 50, netAmount: 2330 } }],
      lines: [
        { allocatedLandedCostAmount: 1500, recognizedInventoryCostAmount: 4500 },
        { allocatedLandedCostAmount: 500, recognizedInventoryCostAmount: 1500 },
      ],
    }), { principal: 4760, additional: 2380, tax: 1140, gross: 7140,
      withholding: 50, net: 7090, landedCost: 2000, inventory: 6000 });
  });

  it("uses confirmed COP amounts when documents have different currencies", () => {
    const summary = summarizeGoodsReceipt({ currencyCode: "USD", grandTotal: 10,
      taxAmount: 0, functionalGrandTotal: 40000, functionalTaxAmount: 0,
      additionalCostDocuments: [{ currencyCode: "COP", grandTotal: 5000, taxAmount: 0 }],
      lines: [{ allocatedLandedCostAmount: 5000, recognizedInventoryCostAmount: 45000 }],
    });
    assert.equal(summary.gross, 45000);
    assert.equal(summary.net, 45000);
    assert.equal(summary.inventory, 45000);
  });

  it("does not invent zero totals or convert missing historical currency and inventory values", () => {
    const summary = summarizeGoodsReceipt({ currencyCode: "USD", grandTotal: 10,
      taxAmount: 0, lines: [{}] });
    assert.equal(summary.principal, null);
    assert.equal(summary.gross, null);
    assert.equal(summary.tax, null);
    assert.equal(summary.net, null);
    assert.equal(summary.inventory, null);
    assert.equal(summary.additional, 0);
  });

  it("preserves a simple receipt and a fully withheld document", () => {
    const summary = summarizeGoodsReceipt({ currencyCode: "COP", grandTotal: 119,
      taxAmount: 19, withholding: { withholdingTotal: 119, netAmount: 0 },
      lines: [{ allocatedLandedCostAmount: 0, recognizedInventoryCostAmount: 100 }],
    });
    assert.equal(summary.net, 0);
    assert.equal(summary.inventory, 100);
    assert.equal(summary.additional, 0);
  });
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
