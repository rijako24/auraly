import test from "node:test";
import assert from "node:assert/strict";
import { resolvePosReceiptPrintRoute } from "./pos-receipt-print-routing";

test("an installed application prints online sales through the local POS printer", () => {
  assert.equal(resolvePosReceiptPrintRoute("edge-session"), "installed-app");
});

test("a browser-only POS keeps the browser print flow", () => {
  assert.equal(resolvePosReceiptPrintRoute(null), "browser");
});
