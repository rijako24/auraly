import assert from "node:assert/strict";
import test from "node:test";
import {
  invoiceOrdersInSequence,
  orderInvoiceIdempotencyKey,
  orderReceiptsFromEmission,
  orderReceiptsForPrinting,
  resolvePosOrderPrintRoute,
} from "./pos-order-print-routing";

test("la aplicación instalada imprime pedidos directamente, esté enrolada o no", () => {
  assert.equal(resolvePosOrderPrintRoute("edge-session"), "installed-app");
});

test("el navegador conserva la vista previa para pedidos", () => {
  assert.equal(resolvePosOrderPrintRoute(null), "browser");
});

test("pedidos conserva factura y comprobante de cartera devueltos al emitir", () => {
  const receipt = {
    documentType: "SalesReceipt",
    cufe: null,
    qrPayload: null,
    creditAcknowledgement: { documentNumber: "FV-1", customerName: "Cliente" },
  };
  assert.deepEqual(orderReceiptsFromEmission([
    { receipt },
    { receipt: null },
    {},
  ]), [receipt]);
  assert.equal(orderReceiptsForPrinting([receipt], false)[0].creditAcknowledgement, undefined);
  assert.deepEqual(
    orderReceiptsForPrinting([receipt], true)[0].creditAcknowledgement,
    receipt.creditAcknowledgement,
  );
});

test("factura e imprime cada pedido antes de comenzar el siguiente y reporta el contador", async () => {
  const calls: string[] = [];
  const completedProgress: number[] = [];
  const response = await invoiceOrdersInSequence(
    ["order-1", "order-2", "order-3"],
    "batch-attempt",
    {
      invoiceOne: async (orderId, idempotencyKey) => {
        calls.push(`invoice:${orderId}:${idempotencyKey}`);
        return {
          operationId: `operation-${orderId}`,
          status: "Completed",
          requestedCount: 1,
          completedCount: 1,
          failedCount: 0,
          isReplay: false,
          results: [{
            orderId,
            orderNumber: orderId.replace("order", "PED"),
            status: "Invoiced",
            documentId: `document-${orderId}`,
            documentNumber: `FV-${orderId}`,
            error: null,
            receipt: { documentNumber: `FV-${orderId}` } as never,
          }],
        };
      },
      printOne: async (receipts) => {
        calls.push(`print:${receipts[0].documentNumber}`);
      },
      onProgress: (progress) => {
        if (progress.phase === "completed") completedProgress.push(progress.processed);
      },
    },
  );

  assert.deepEqual(calls, [
    `invoice:order-1:${orderInvoiceIdempotencyKey("batch-attempt", "order-1")}`,
    "print:FV-order-1",
    `invoice:order-2:${orderInvoiceIdempotencyKey("batch-attempt", "order-2")}`,
    "print:FV-order-2",
    `invoice:order-3:${orderInvoiceIdempotencyKey("batch-attempt", "order-3")}`,
    "print:FV-order-3",
  ]);
  assert.deepEqual(completedProgress, [1, 2, 3]);
  assert.equal(response.completedCount, 3);
  assert.equal(response.printStatus, "Sent");
});

test("un error de impresión queda visible y no impide intentar el siguiente pedido", async () => {
  const calls: string[] = [];
  const response = await invoiceOrdersInSequence(["order-1", "order-2"], "batch", {
    invoiceOne: async (orderId) => ({
      operationId: `operation-${orderId}`,
      status: "Completed",
      requestedCount: 1,
      completedCount: 1,
      failedCount: 0,
      isReplay: false,
      results: [{
        orderId,
        orderNumber: orderId,
        status: "Invoiced",
        documentId: orderId,
        documentNumber: orderId,
        error: null,
        receipt: { documentNumber: orderId } as never,
      }],
    }),
    printOne: async (receipts) => {
      calls.push(receipts[0].documentNumber);
      if (receipts[0].documentNumber === "order-1") throw new Error("Impresora sin papel");
    },
  });

  assert.deepEqual(calls, ["order-1", "order-2"]);
  assert.equal(response.completedCount, 2);
  assert.equal(response.printStatus, "Failed");
  assert.match(response.printError ?? "", /order-1: Impresora sin papel/);
});
