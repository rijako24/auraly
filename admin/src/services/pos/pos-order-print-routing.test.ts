import assert from "node:assert/strict";
import test from "node:test";
import { resolvePosOrderPrintRoute } from "./pos-order-print-routing";

test("la aplicación instalada factura pedidos mediante la impresora local", () => {
  assert.equal(resolvePosOrderPrintRoute("edge-session"), "installed-pos");
});

test("el navegador conserva la vista previa para pedidos", () => {
  assert.equal(resolvePosOrderPrintRoute(null), "browser");
});
