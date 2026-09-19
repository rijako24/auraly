import test from "node:test";
import assert from "node:assert/strict";
import { resolvePosReceiptPrintRoute } from "./pos-receipt-print-routing";
import { resolveSalePrintEffect } from "./pos-print-routing";

test("an installed application prints online sales through the local POS printer", () => {
  assert.equal(resolvePosReceiptPrintRoute("edge-session"), "installed-app");
});

test("a browser-only POS keeps the browser print flow", () => {
  assert.equal(resolvePosReceiptPrintRoute(null), "browser");
});

test("a completed sale retry repeats no printing or cash drawer effects", () => {
  assert.deepEqual(resolveSalePrintEffect(true), {
    dispatchCopy: false,
    openCashDrawer: false,
  });
  assert.deepEqual(resolveSalePrintEffect(false), {
    dispatchCopy: true,
    openCashDrawer: true,
  });
});
