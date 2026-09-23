import assert from "node:assert/strict";
import { describe, it } from "node:test";
import type { PriceRevisionListItem } from "@/services/api/pricing";
import { buildPricePublicationRequest } from "./pricing-publication-selection";

const row = (productId: string): PriceRevisionListItem => ({
  proposalId: `proposal-${productId}`,
  productId,
  productCode: productId,
  productName: productId,
  sourceDocumentId: "00000000-0000-0000-0000-000000000000",
  sourceLineNumber: 0,
  supplierName: "Proveedor",
  previousObservedUnitCost: null,
  observedUnitCost: 10,
  currentSalePrice: 12,
  currentPricePublishedAt: null,
  currentMarginPercent: null,
  salesTaxRate: 0,
  targetMarginPercent: null,
  suggestedSalePrice: 13,
  effectiveMarginAfterRounding: null,
  status: "Approved",
  createdAt: "2026-09-23T00:00:00Z",
  concurrencyToken: "token",
  origin: "Product",
});

describe("pricing publication selection", () => {
  it("represents select all as the complete server-side filtered set", () => {
    const request = buildPricePublicationRequest(
      Array.from({ length: 25 }, (_, index) => row(`visible-${index}`)),
      true,
      { search: "aceite", supplierId: "supplier-1" },
    );

    assert.deepEqual(request, {
      allMatching: { search: "aceite", supplierId: "supplier-1" },
    });
    assert.equal("productIds" in request, false);
  });

  it("publishes only the distinct products selected explicitly", () => {
    assert.deepEqual(
      buildPricePublicationRequest([row("one"), row("two"), row("one")], false, {}),
      { productIds: ["one", "two"] },
    );
  });
});
