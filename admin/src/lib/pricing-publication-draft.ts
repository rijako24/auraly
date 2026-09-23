import type {
  PriceInputMode,
  PriceRevisionListItem,
  ReviewPriceProposalRequest,
} from "@/services/api/pricing";
import { recalculateProductPricing } from "./product-pricing-calculator";

export type PricePublicationDraft = {
  proposalId: string;
  salePrice: number | null;
  margin: number | null;
  concurrencyToken: string;
};

export function createPricePublicationDraft(row: PriceRevisionListItem): PricePublicationDraft {
  return {
    proposalId: row.proposalId,
    salePrice: row.suggestedSalePrice,
    margin: deriveMargin(row, row.suggestedSalePrice),
    concurrencyToken: row.concurrencyToken,
  };
}

export function changeDraftMargin(
  row: PriceRevisionListItem,
  current: PricePublicationDraft,
  margin: number | null,
): PricePublicationDraft {
  if (margin === null) return { ...current, margin };
  const calculated = recalculateProductPricing("margin", margin, {
    cost: row.observedUnitCost,
    margin: current.margin ?? 0,
    salePrice: current.salePrice ?? 0,
    salesTaxRate: row.salesTaxRate,
  });
  return { ...current, margin: calculated.margin, salePrice: calculated.salePrice };
}

export function changeDraftSalePrice(
  row: PriceRevisionListItem,
  current: PricePublicationDraft,
  salePrice: number | null,
): PricePublicationDraft {
  if (salePrice === null) return { ...current, salePrice };
  const calculated = recalculateProductPricing("salePrice", salePrice, {
    cost: row.observedUnitCost,
    margin: current.margin ?? 0,
    salePrice: current.salePrice ?? 0,
    salesTaxRate: row.salesTaxRate,
  });
  return { ...current, margin: calculated.margin, salePrice: calculated.salePrice };
}

export function buildPriceReviewRequest(
  row: PriceRevisionListItem,
  draft: PricePublicationDraft,
  inputMode: PriceInputMode,
): ReviewPriceProposalRequest {
  if (draft.salePrice === null || !Number.isFinite(draft.salePrice) || draft.salePrice <= 0)
    throw new RangeError(`El precio preparado de ${row.productName} no es válido.`);

  // The server persists this edit as the next authoritative preparation before
  // any selection can publish it.
  return {
    inputMode,
    targetMarginPercent: inputMode === "Margin" ? draft.margin : null,
    salePrice: inputMode === "SalePrice" ? draft.salePrice : null,
    roundingIncrement: 1,
    roundingMode: "Nearest",
    concurrencyToken: draft.concurrencyToken,
  };
}

function deriveMargin(row: PriceRevisionListItem, salePrice: number): number | null {
  if (!Number.isFinite(salePrice) || salePrice <= 0) return row.targetMarginPercent;
  return recalculateProductPricing("salePrice", salePrice, {
    cost: row.observedUnitCost,
    margin: row.targetMarginPercent ?? row.currentMarginPercent ?? 0,
    salePrice,
    salesTaxRate: row.salesTaxRate,
  }).margin;
}
