import assert from "node:assert/strict";
import test from "node:test";
import { localPrintLogoSource } from "./pos-local-print-logo";

test("prepared printing reads the enrolled logo without a browser lookup", () => {
  assert.equal(localPrintLogoSource(null, true), null);
});

test("online printing uses the cached image or explicitly suppresses an old logo", () => {
  assert.equal(localPrintLogoSource({ logoUrl: "data:image/png;base64,AQ==" }, false),
    "data:image/png;base64,AQ==");
  assert.equal(localPrintLogoSource({ logoUrl: null }, false), "");
  assert.equal(localPrintLogoSource(null, false), "");
});
