import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { buildDispatchReportRows, dispatchReportColumns } from "./dispatch-report";
import type { DispatchReportRow } from "@/services/api/dispatches";

const base: DispatchReportRow = { dispatchNumber:"D1",scheduledDate:"2026-08-13",status:"Verified",driverName:"Ana",vehiclePlate:null,documentType:"SalesInvoice",documentNumber:"F1",customerName:"Cliente",deliveryAddress:null,sellerName:"Vendedor",productCode:"P1",productName:"Producto",assignedQuantity:2,verifiedQuantity:1,shortageQuantity:1,unitPrice:100,lineTotal:200 };

describe("dispatch reports", () => {
  it("consolidates quantities and amounts by product without multiplying documents", () => {
    const result = buildDispatchReportRows([base, { ...base, documentNumber:"F2", assignedQuantity:3, verifiedQuantity:3, shortageQuantity:0, lineTotal:300 }], "product");
    assert.equal(result.length, 1);
    assert.equal(result[0].assignedQuantity, 5);
    assert.equal(result[0].verifiedQuantity, 4);
    assert.equal(result[0].shortageQuantity, 1);
    assert.equal(result[0].lineTotal, 500);
    assert.equal(result[0].documentNumber, "Varios");
  });

  it("keeps detail rows unchanged and hides price columns without permission", () => {
    assert.equal(buildDispatchReportRows([base], "detail").length, 1);
    assert.equal(dispatchReportColumns(false).some((column) => column.key === "lineTotal"), false);
    assert.equal(dispatchReportColumns(true).some((column) => column.key === "lineTotal"), true);
  });
});
