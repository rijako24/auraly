import assert from "node:assert/strict";
import test from "node:test";

import { orderInvoiceFailureDetails } from "./order-invoice-result";

test("keeps the exact order and server reason for failed emissions", () => {
  assert.deepEqual(orderInvoiceFailureDetails([
    {
      orderNumber: "PED-20260917-4BF729C8",
      status: "Failed",
      error: "La sede no tiene una resolución fiscal activa y vigente.",
    },
    {
      orderNumber: "PED-OK",
      status: "Invoiced",
      error: null,
    },
  ]), [
    "PED-20260917-4BF729C8: La sede no tiene una resolución fiscal activa y vigente.",
  ]);
});

test("does not invent a failure detail when the server has none", () => {
  assert.deepEqual(orderInvoiceFailureDetails([
    { orderNumber: "PED-1", status: "Failed", error: "  " },
  ]), []);
});
