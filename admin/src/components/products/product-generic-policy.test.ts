import assert from "node:assert/strict";
import test from "node:test";
import {
  productTaxFieldVisibility,
  genericProductCatalogPrice,
  productModeCapabilities,
  setGenericMerchandisingMode,
  setGenericProductMode,
  validateProductCreateFields,
} from "./product-generic-policy";

const ordinaryProduct = {
  name: "Producto",
  baseUnitCode: "EA",
  salesTaxProfileId: "sales-tax",
  purchaseTaxProfileId: "purchase-tax",
  supplierId: "supplier",
  cost: 10_000,
  margin: 20,
  salePrice: 12_500,
  isGenericProduct: false,
  manageInventory: true,
  isWeighable: true,
  purchaseTaxTreatment: "DeductibleInputVat" as const,
  supplierProductCode: "SUP-1",
};

test("generic product requires identity and sales tax but not supplier, purchase tax or prices", () => {
  const generic = setGenericProductMode(ordinaryProduct, true);

  assert.deepEqual(validateProductCreateFields(generic), {});
  assert.equal(generic.manageInventory, false);
  assert.equal(generic.isWeighable, false);
  assert.equal(generic.purchaseTaxProfileId, "");
  assert.equal(generic.purchaseTaxTreatment, "NotApplicable");
  assert.equal(generic.supplierId, null);
  assert.equal(generic.cost, 0);
  assert.equal(generic.margin, 0);
  assert.equal(generic.salePrice, 0);
});

test("generic product still requires the fields that apply to every product", () => {
  const generic = setGenericProductMode(ordinaryProduct, true);
  const errors = validateProductCreateFields({
    ...generic,
    name: " ",
    baseUnitCode: "",
    salesTaxProfileId: "",
  });

  assert.deepEqual(errors, {
    name: "Este campo es requerido",
    baseUnitCode: "Este campo es requerido",
    salesTaxProfileId: "Este campo es requerido",
  });
});

test("changing a generic product back to ordinary restores ordinary validation", () => {
  const generic = setGenericProductMode(ordinaryProduct, true);
  const ordinaryAgain = setGenericProductMode(generic, false);
  const errors = validateProductCreateFields(ordinaryAgain);

  assert.deepEqual(Object.keys(errors).sort(), [
    "cost",
    "purchaseTaxProfileId",
    "salePrice",
    "supplierId",
  ]);
});

test("generic merchandising removes every family relationship", () => {
  const generic = setGenericMerchandisingMode({
    isGenericProduct: false,
    manageInventory: true,
    isWeighable: true,
    scale: { scaleCode: "1" },
    link: { parentProductId: "parent" },
    linkedProducts: [{ childProductId: "child" }],
    conversionMaximumLossPercent: 5,
  }, true);

  assert.equal(generic.manageInventory, false);
  assert.equal(generic.isWeighable, false);
  assert.equal(generic.scale, null);
  assert.equal(generic.link, null);
  assert.deepEqual(generic.linkedProducts, []);
  assert.equal(generic.conversionMaximumLossPercent, null);
});

test("generic tax editor keeps sales VAT and hides purchase VAT fields", () => {
  assert.deepEqual(productTaxFieldVisibility(true), {
    salesTax: true,
    purchaseTax: false,
    purchaseTaxTreatment: false,
  });
  assert.deepEqual(productTaxFieldVisibility(false), {
    salesTax: true,
    purchaseTax: true,
    purchaseTaxTreatment: true,
  });
  assert.deepEqual(productModeCapabilities(true), {
    supplier: false,
    purchaseTax: false,
    catalogPricing: false,
    inventory: false,
    scale: false,
    family: false,
  });
  assert.deepEqual(genericProductCatalogPrice(), {
    amount: 0,
    preparedAmount: 0,
    currencyCode: "COP",
    costBasisAmount: 0,
    targetMarginPercent: 0,
    inputMode: "Margin",
    roundingIncrement: 1,
    roundingMode: "Nearest",
  });
});
