import assert from "node:assert/strict";
import { describe, it } from "node:test";
import type { PriceRevisionListItem } from "@/services/api/pricing";
import {
  buildPricePublicationItem,
  changeDraftMargin,
  changeDraftSalePrice,
  createPricePublicationDraft,
} from "./pricing-publication-draft";

const proposal: PriceRevisionListItem = {
  proposalId: "proposal-1",
  productId: "product-1",
  productCode: "ACEITE",
  productName: "Aceite",
  sourceDocumentId: "source-1",
  sourceLineNumber: 1,
  supplierName: "Proveedor",
  previousObservedUnitCost: null,
  observedUnitCost: 20_000,
  currentSalePrice: 29_500,
  currentMarginPercent: 15,
  salesTaxRate: 19,
  targetMarginPercent: 15,
  suggestedSalePrice: 28_000,
  effectiveMarginAfterRounding: 15,
  status: "Approved",
  createdAt: "2026-08-11T00:00:00Z",
  concurrencyToken: "token-1",
  origin: "Product",
};

describe("price publication draft", () => {
  it("keeps the exact price prepared from product as the publication authority", () => {
    const draft = createPricePublicationDraft(proposal);
    assert.equal(draft.salePrice, 28_000);
    assert.deepEqual(buildPricePublicationItem(proposal, draft), {
      proposalId: "proposal-1",
      inputMode: "SalePrice",
      targetMarginPercent: null,
      salePrice: 28_000,
      roundingIncrement: 1,
      roundingMode: "Nearest",
      concurrencyToken: "token-1",
    });
  });

  it("recalculates the margin when the grid price changes", () => {
    const draft = changeDraftSalePrice(proposal, createPricePublicationDraft(proposal), 30_000);
    assert.equal(draft.salePrice, 30_000);
    assert.equal(draft.margin, 20.6667);
  });

  it("recalculates the VAT-included sale price when the grid margin changes", () => {
    const draft = changeDraftMargin(proposal, createPricePublicationDraft(proposal), 20);
    assert.equal(draft.salePrice, 29_750);
  });
});
