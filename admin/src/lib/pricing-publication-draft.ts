import type { PriceRevisionListItem, PublishPriceItem } from "@/services/api/pricing";
import { recalculateProductPricing } from "./product-pricing-calculator";

export type PricePublicationDraft = {
  salePrice: number | null;
  margin: number | null;
  concurrencyToken: string;
};

export function createPricePublicationDraft(row: PriceRevisionListItem): PricePublicationDraft {
  return {
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

export function buildPricePublicationItem(
  row: PriceRevisionListItem,
  draft: PricePublicationDraft,
): PublishPriceItem {
  if (draft.salePrice === null || !Number.isFinite(draft.salePrice) || draft.salePrice < 0)
    throw new RangeError(`El precio preparado de ${row.productName} no es válido.`);

  // The prepared VAT-included price is authoritative. Publishing by margin
  // would calculate it again and could replace the amount approved by the user.
  return {
    proposalId: row.proposalId,
    inputMode: "SalePrice",
    targetMarginPercent: null,
    salePrice: draft.salePrice,
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
