import assert from "node:assert/strict";
import test from "node:test";
import { normalizeProductPurchaseTaxTreatment } from "./product-purchase-tax-treatment";

test("IVA de compra cero siempre usa No aplica", () => {
  assert.equal(
    normalizeProductPurchaseTaxTreatment(0, "DeductibleInputVat"),
    "NotApplicable",
  );
  assert.equal(
    normalizeProductPurchaseTaxTreatment(0, "CapitalizedCost"),
    "NotApplicable",
  );
});

test("un IVA positivo recupera un tratamiento guardable", () => {
  assert.equal(
    normalizeProductPurchaseTaxTreatment(19, "NotApplicable"),
    "DeductibleInputVat",
  );
  assert.equal(
    normalizeProductPurchaseTaxTreatment(19, "CapitalizedCost"),
    "CapitalizedCost",
  );
});

test("no cambia el tratamiento mientras el catálogo tributario está cargando", () => {
  assert.equal(
    normalizeProductPurchaseTaxTreatment(undefined, "CapitalizedCost"),
    "CapitalizedCost",
  );
});
