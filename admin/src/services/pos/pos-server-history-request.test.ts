import assert from "node:assert/strict";
import test from "node:test";
import {
  buildServerIssuedSalesSearchRequest,
  mapServerIssuedSalesPage,
} from "./pos-server-history-request";

test("server history sends absent date filters as null for browser and enrolled runtimes", () => {
  const request = buildServerIssuedSalesSearchRequest(
    {
      businessId: "10000000-0000-0000-0000-000000000001",
      warehouseId: "10000000-0000-0000-0000-000000000002",
      workSessionId: "10000000-0000-0000-0000-000000000003",
    },
    {
      search: "",
      customerId: null,
      partySiteId: null,
      from: "",
      to: "",
      productId: null,
      minimumTotal: null,
      maximumTotal: null,
    },
    0,
    20,
  );

  assert.equal(request.from, null);
  assert.equal(request.to, null);
  assert.equal(request.skip, 0);
  assert.equal(request.take, 20);
});

test("server history maps the API document id to the POS value object", () => {
  const page = mapServerIssuedSalesPage({
    items: [{
      documentId: "10000000-0000-0000-0000-000000000010",
      documentType: "SalesReceipt",
      documentNumber: "CVI08-00000001",
      fiscalNumber: null,
      issuedAt: "2026-09-17T19:00:00-05:00",
      total: 4200,
      customerIdentification: "222222222222",
      customerName: "Consumidor final",
      fiscalStatus: "LocallyIssuedPendingSync",
    }],
    hasMore: false,
    nextOffset: null,
  });

  assert.deepEqual(page.items[0].documentId, {
    value: "10000000-0000-0000-0000-000000000010",
  });
});
