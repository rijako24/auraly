import assert from "node:assert/strict";
import test from "node:test";
import type { CommerceOrderListItem } from "./commerce-orders-client";
import {
  limitInvoiceBatch,
  loadAllMatchingOrders,
  ORDER_INVOICE_BATCH_LIMIT,
} from "./order-batch-selection";

function order(index: number): CommerceOrderListItem {
  return {
    orderId: `order-${index}`,
    orderNumber: `PED-${index}`,
    status: "Available",
    source: 1,
    customerName: `Cliente ${index}`,
    customerIdentification: null,
    customerPhone: null,
    currency: "COP",
    total: index,
    lineCount: 1,
    createdAt: new Date(2026, 8, 2).toISOString(),
    canInvoice: true,
  invoiceDocumentId: null,
  claim: null,
  customerId: "customer-1",
  partySiteId: "site-1",
  partySiteName: "Sede principal",
};
}

test("seleccionar todos conserva los pedidos que no están en la página visible", async () => {
  const source = Array.from({ length: 50 }, (_, index) => order(index + 1));
  const calls: number[] = [];
  const selected = await loadAllMatchingOrders(async ({ page, pageSize }) => {
    calls.push(page!);
    const start = (page! - 1) * pageSize!;
    const items = source.slice(start, start + pageSize!);
    return {
      items,
      page: page!,
      pageSize: pageSize!,
      totalCount: source.length,
      hasMore: start + items.length < source.length,
    };
  }, {}, 10);

  assert.equal(selected.length, 50);
  assert.deepEqual(calls, [1, 2, 3, 4, 5]);
  assert.equal(selected[49].orderId, "order-50");
});

test("detiene una paginación defectuosa que repite la misma página", async () => {
  const repeated = [order(1), order(2)];
  await assert.rejects(
    loadAllMatchingOrders(async ({ page, pageSize }) => ({
      items: repeated,
      page: page!,
      pageSize: pageSize!,
      totalCount: 4,
      hasMore: true,
    }), {}, 2),
    /no avanzó/,
  );
});

test("limita la selección masiva al máximo aceptado por el endpoint", () => {
  const selected = limitInvoiceBatch(
    Array.from({ length: ORDER_INVOICE_BATCH_LIMIT + 25 }, (_, index) => order(index + 1)),
  );

  assert.equal(selected.length, ORDER_INVOICE_BATCH_LIMIT);
  assert.equal(selected[0].orderId, "order-1");
  assert.equal(selected.at(-1)?.orderId, `order-${ORDER_INVOICE_BATCH_LIMIT}`);
});
