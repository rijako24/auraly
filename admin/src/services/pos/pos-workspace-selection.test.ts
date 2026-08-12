import test from "node:test";
import assert from "node:assert/strict";

import { resolvePosWorkspaceSelection } from "./pos-workspace-selection";

const option = (
  businessId: string,
  warehouseId: string,
): {
  businessId: string;
  businessName: string;
  warehouseId: string;
  warehouseCode: string;
  warehouseName: string;
  warehouseAllowsNegativeStockSales: boolean;
  hasActiveEdgeEnrollment: boolean;
} => ({
  businessId,
  businessName: `Sede ${businessId}`,
  warehouseId,
  warehouseCode: warehouseId,
  warehouseName: `Bodega ${warehouseId}`,
  warehouseAllowsNegativeStockSales: false,
  hasActiveEdgeEnrollment: false,
});

test("selects the only available business and warehouse", () => {
  assert.deepEqual(
    resolvePosWorkspaceSelection([option("business-1", "warehouse-1")], "", ""),
    { businessId: "business-1", warehouseId: "warehouse-1" },
  );
});

test("discards a remembered selection that no longer exists", () => {
  assert.deepEqual(
    resolvePosWorkspaceSelection(
      [option("business-1", "warehouse-1")],
      "removed-business",
      "removed-warehouse",
    ),
    { businessId: "business-1", warehouseId: "warehouse-1" },
  );
});

test("requires a choice when several businesses are available", () => {
  assert.deepEqual(
    resolvePosWorkspaceSelection(
      [option("business-1", "warehouse-1"), option("business-2", "warehouse-2")],
      "",
      "",
    ),
    { businessId: "", warehouseId: "" },
  );
});

test("keeps a valid explicit selection", () => {
  assert.deepEqual(
    resolvePosWorkspaceSelection(
      [option("business-1", "warehouse-1"), option("business-1", "warehouse-2")],
      "business-1",
      "warehouse-2",
    ),
    { businessId: "business-1", warehouseId: "warehouse-2" },
  );
});
