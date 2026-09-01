import test from "node:test";
import assert from "node:assert/strict";
import { sellerOrderErrorMessage, shouldQueueSellerOrder } from "./seller-order-reliability";

test("seller orders queue transport and temporary server failures", () => {
  assert.equal(shouldQueueSellerOrder(new TypeError("Failed to fetch")), true);
  assert.equal(shouldQueueSellerOrder(Object.assign(new Error(""), { statusCode: 503 })), true);
  assert.equal(shouldQueueSellerOrder(Object.assign(new Error("Inválido"), { statusCode: 400 })), false);
});

test("seller order errors are never blank", () => {
  assert.equal(sellerOrderErrorMessage(new Error("   ")), "No fue posible guardar el pedido.");
  assert.match(sellerOrderErrorMessage(new TypeError("Failed to fetch")), /servidor no está disponible/);
});
