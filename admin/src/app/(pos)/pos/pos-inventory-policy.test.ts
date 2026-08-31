import assert from "node:assert/strict";
import test from "node:test";
import { posInventoryPolicyPresentation } from "./pos-inventory-policy";
import { isWorkspacePolicySynchronizationMessage } from "../../../services/pos/pos-workspace-synchronization";

test("distinguishes inventory-controlled warehouses from negative-stock sales", () => {
  assert.equal(posInventoryPolicyPresentation(false).setupLabel, "Controlando inventario");
  assert.equal(posInventoryPolicyPresentation(true).setupLabel, "Sin control de existencias");
});

test("recognizes the warehouse configuration push used by web POS", () => {
  assert.equal(isWorkspacePolicySynchronizationMessage(JSON.stringify({
    type: "message",
    data: { Stream: "Configuration" },
  })), true);
  assert.equal(isWorkspacePolicySynchronizationMessage(JSON.stringify({
    type: "message",
    data: { stream: "Customers" },
  })), false);
});
