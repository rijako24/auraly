import assert from "node:assert/strict";
import test from "node:test";
import { toPrintableOrder } from "./pos-order-print-document";

test("an order print document preserves captured prices and has no fiscal or payment data", () => {
  const receipt = toPrintableOrder({
    orderId: "order-1",
    orderNumber: "PED-0042",
    createdAt: "2026-09-11T15:00:00Z",
    customerName: "Cliente pedido",
    customerIdentification: "900123",
    customerPhone: "3001234567",
    deliveryAddress: "Calle 10 # 20-30 · Apartamento 402",
    total: 195,
    lines: [{
      productCode: "P-1",
      productName: "Producto capturado",
      quantity: 2,
      unitPrice: 100,
      discountAmount: 5,
      lineTotal: 195,
    }],
  }, { businessName: "Sede", warehouseName: "Pedidos" });

  assert.equal(receipt.documentType, "Order");
  assert.equal(receipt.documentNumber, "PED-0042");
  assert.equal(receipt.customerName, "Cliente pedido");
  assert.equal(receipt.customerPhone, "3001234567");
  assert.equal(receipt.customerAddress, "Calle 10 # 20-30 · Apartamento 402");
  assert.equal(receipt.lines[0]?.unitPrice, 100);
  assert.equal(receipt.lines[0]?.discount, 5);
  assert.equal(receipt.lines[0]?.total, 195);
  assert.equal(receipt.payableAmount, 195);
  assert.equal(receipt.taxAmount, 0);
  assert.deepEqual(receipt.payments, []);
  assert.deepEqual(receipt.withholdings, []);
  assert.equal(receipt.cufe, null);
  assert.equal(receipt.qrPayload, null);
});
