import assert from "node:assert/strict";
import test from "node:test";
import { localPrintCompanyName, localPrintLogoSource } from "./pos-local-print-logo";

test("prepared printing reads the enrolled logo without a browser lookup", () => {
  assert.equal(localPrintLogoSource(null, true), null);
});

test("online printing uses the cached image or explicitly suppresses an old logo", () => {
  assert.equal(localPrintLogoSource({ logoUrl: "data:image/png;base64,AQ==" }, false),
    "data:image/png;base64,AQ==");
  assert.equal(localPrintLogoSource({ logoUrl: null }, false), "");
  assert.equal(localPrintLogoSource(null, false), "");
});

test("installed online printing uses the selected business when tenant branding is unavailable", () => {
  assert.equal(localPrintCompanyName(null, null, "Granja La Bendición", false),
    "Granja La Bendición");
  assert.equal(localPrintCompanyName(null, "  ", "Granja La Bendición", false),
    "Granja La Bendición");
  assert.equal(localPrintCompanyName({ displayName: "Empresa del tenant", legalName: null },
    null, "Granja La Bendición", false), "Empresa del tenant");
  assert.equal(localPrintCompanyName(null, "Nombre del comprobante",
    "Granja La Bendición", false), "Nombre del comprobante");
});

test("prepared printing keeps the locally enrolled company name", () => {
  assert.equal(localPrintCompanyName(null, null, "Sede principal", true), null);
  assert.equal(localPrintCompanyName(null, "Empresa enrolada", "Sede principal", true),
    "Empresa enrolada");
});
