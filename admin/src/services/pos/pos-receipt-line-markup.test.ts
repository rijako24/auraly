import test from "node:test";
import assert from "node:assert/strict";
import { receiptLineMarkup } from "./pos-receipt-line-markup";

const currency = new Intl.NumberFormat("es-CO", {
  style: "currency",
  currency: "COP",
  maximumFractionDigits: 0,
});

test("invoice and receipt lines show the applied discount", () => {
  const html = receiptLineMarkup({
    description: "Certificación sync <57>",
    quantity: 1,
    unitPrice: 6_645,
    discount: 1_329,
    total: 5_316,
  }, currency);

  assert.match(html, /Certificación sync &lt;57&gt;/);
  assert.match(html, /Descuento/);
  assert.match(html, /-\$\s?1\.329/);
  assert.match(html, /\$\s?5\.316/);
});

test("a line without a discount does not add an empty row", () => {
  const html = receiptLineMarkup({
    description: "Producto",
    quantity: 1,
    unitPrice: 2_500,
    discount: 0,
    total: 2_500,
  }, currency);

  assert.doesNotMatch(html, /Descuento/);
});
