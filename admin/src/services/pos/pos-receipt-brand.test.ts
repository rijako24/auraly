import assert from "node:assert/strict";
import test from "node:test";
import { receiptBrandMarkup } from "./pos-receipt-brand";

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
