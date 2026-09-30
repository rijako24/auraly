import assert from "node:assert/strict";
import test from "node:test";
import { hasPrintIdentity, receiptBrandMarkup, resolveReceiptCompanyName } from "./pos-receipt-brand";

test("printing requires a loaded tenant identity but accepts a profile without a logo", () => {
  assert.equal(hasPrintIdentity(null), false);
  assert.equal(hasPrintIdentity({ displayName: " ", legalName: null }), false);
  assert.equal(hasPrintIdentity({ displayName: "Aurali", legalName: null }), true);
});

test("uses the tenant name from POS bootstrap when a receipt and print cache have no name", () => {
  assert.equal(resolveReceiptCompanyName(null, null, "Aurali", "Sede centro"), "Aurali");
  assert.equal(resolveReceiptCompanyName(null, "", "Aurali", "Sede centro"), "Aurali");
  assert.equal(resolveReceiptCompanyName(null, null, null, "Sede centro"), "Sede centro");
});

test("uses and escapes the tenant logo and name on sales printouts", () => {
  const html = receiptBrandMarkup({
    displayName: "Comercial <Uno>",
    legalName: "Comercial Uno SAS",
    logoUrl: "https://media.test/logo.png?x=1&y=2",
  });

  assert.match(html, /class="brand-logo"/);
  assert.match(html, /Comercial &lt;Uno&gt;/);
  assert.match(html, /x=1&amp;y=2/);
  assert.doesNotMatch(html, />Auraly</);
});

test("falls back to a neutral company label without an Auraly logo", () => {
  assert.equal(
    receiptBrandMarkup(null),
    '<p class="brand-name">Empresa</p>',
  );
});
