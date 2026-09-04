import test from "node:test";
import assert from "node:assert/strict";
import { resolvePosReceiptPrintRoute } from "./pos-receipt-print-routing";

test("an installed application prints online sales through the local POS printer", () => {
  assert.equal(resolvePosReceiptPrintRoute("edge-session", false), "installed-app");
});

test("a browser-only POS keeps the browser print flow", () => {
  assert.equal(resolvePosReceiptPrintRoute(null, false), "browser");
});

test("DIAN habilitation does not print the generated test document", () => {
  assert.equal(resolvePosReceiptPrintRoute("edge-session", true), "none");
  assert.equal(resolvePosReceiptPrintRoute(null, true), "none");
});
