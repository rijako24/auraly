import assert from "node:assert/strict";
import test from "node:test";
import {
  orderReceiptsFromEmission,
  resolvePosOrderPrintRoute,
} from "./pos-order-print-routing";

test("la aplicación instalada imprime pedidos directamente, esté enrolada o no", () => {
  assert.equal(resolvePosOrderPrintRoute("edge-session"), "installed-app");
});

test("el navegador conserva la vista previa para pedidos", () => {
  assert.equal(resolvePosOrderPrintRoute(null), "browser");
});

test("pedidos imprime la tirilla devuelta al emitir sin una segunda consulta fiscal", () => {
  const receipt = { documentType: "SalesReceipt", cufe: null, qrPayload: null };
  assert.deepEqual(orderReceiptsFromEmission([
    { receipt },
    { receipt: null },
    {},
  ]), [receipt]);
});
