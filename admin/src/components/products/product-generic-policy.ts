export interface ProductCreateValidationState {
  name: string;
  baseUnitCode: string;
  salesTaxProfileId: string;
  purchaseTaxProfileId: string;
  supplierId: string | null;
  cost: number;
  margin: number;
  salePrice: number;
  isGenericProduct: boolean;
}

export interface GenericProductFields {
  isGenericProduct: boolean;
  manageInventory: boolean;
  isWeighable: boolean;
  purchaseTaxProfileId: string;
  purchaseTaxTreatment: "DeductibleInputVat" | "CapitalizedCost" | "NotApplicable";
  supplierId: string | null;
  supplierProductCode: string;
  cost: number;
  margin: number;
  salePrice: number;
}

export function validateProductCreateFields(
  form: ProductCreateValidationState,
): Record<string, string> {
  const errors: Record<string, string> = {};
  if (!form.name.trim()) errors.name = "Este campo es requerido";
  if (!form.baseUnitCode) errors.baseUnitCode = "Este campo es requerido";
  if (!form.salesTaxProfileId) errors.salesTaxProfileId = "Este campo es requerido";

  if (form.isGenericProduct) return errors;

  if (!form.purchaseTaxProfileId) errors.purchaseTaxProfileId = "Este campo es requerido";
  if (!form.supplierId) errors.supplierId = "Este campo es requerido";
  if (!(form.cost > 0)) errors.cost = "Este campo es requerido";
  if (form.margin < 0) errors.margin = "No puede ser negativo";
  else if (form.margin >= 100) errors.margin = "Debe ser menor que 100 %";
  if (!(form.salePrice > 0)) errors.salePrice = "Este campo es requerido";
  return errors;
}

export function setGenericProductMode<T extends GenericProductFields>(
  form: T,
  enabled: boolean,
): T {
  if (!enabled) return { ...form, isGenericProduct: false };
  return {
    ...form,
    isGenericProduct: true,
    manageInventory: false,
    isWeighable: false,
    purchaseTaxProfileId: "",
    purchaseTaxTreatment: "NotApplicable",
    supplierId: null,
    supplierProductCode: "",
    cost: 0,
    margin: 0,
    salePrice: 0,
  };
}

export interface ProductFamilyFields {
  isGenericProduct: boolean;
  manageInventory: boolean;
  isWeighable: boolean;
  scale: unknown | null;
  link: unknown | null;
  linkedProducts: unknown[];
  conversionMaximumLossPercent: number | null;
}

export function setGenericMerchandisingMode<T extends ProductFamilyFields>(
  form: T,
  enabled: boolean,
): T {
  if (!enabled) return { ...form, isGenericProduct: false };
  return {
    ...form,
    isGenericProduct: true,
    manageInventory: false,
    isWeighable: false,
    scale: null,
    link: null,
    linkedProducts: [],
    conversionMaximumLossPercent: null,
  };
}

export function productTaxFieldVisibility(isGenericProduct: boolean) {
  const capabilities = productModeCapabilities(isGenericProduct);
  return { salesTax: true, purchaseTax: capabilities.purchaseTax, purchaseTaxTreatment: capabilities.purchaseTax };
}

export function productModeCapabilities(isGenericProduct: boolean) {
  return {
    supplier: !isGenericProduct,
    purchaseTax: !isGenericProduct,
    catalogPricing: !isGenericProduct,
    inventory: !isGenericProduct,
    scale: !isGenericProduct,
    family: !isGenericProduct,
  };
}

export function genericProductCatalogPrice(currencyCode = "COP") {
  return {
    amount: 0,
    preparedAmount: 0,
    currencyCode,
    costBasisAmount: 0,
    targetMarginPercent: 0,
    inputMode: "Margin" as const,
    roundingIncrement: 1,
    roundingMode: "Nearest" as const,
  };
}
