export type ProductPurchaseTaxTreatment =
  | "DeductibleInputVat"
  | "CapitalizedCost"
  | "NotApplicable";

export function normalizeProductPurchaseTaxTreatment(
  purchaseTaxRate: number | undefined,
  current: ProductPurchaseTaxTreatment,
): ProductPurchaseTaxTreatment {
  if (purchaseTaxRate === undefined) return current;
  if (purchaseTaxRate === 0) return "NotApplicable";
  return current === "NotApplicable" ? "DeductibleInputVat" : current;
}
