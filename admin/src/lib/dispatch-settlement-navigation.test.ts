import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { shouldCloseDispatchAfterSettlement } from "./dispatch-settlement-navigation";

describe("dispatch settlement navigation", () => {
  it("closes the detail after settlement was submitted or completed", () => {
    assert.equal(shouldCloseDispatchAfterSettlement("SettlementProcessing"), true);
    assert.equal(shouldCloseDispatchAfterSettlement("SettlementAttention"), true);
    assert.equal(shouldCloseDispatchAfterSettlement("Closed"), true);
  });

  it("keeps the settlement open while the receiver still has work", () => {
    assert.equal(shouldCloseDispatchAfterSettlement("PendingSettlement"), false);
    assert.equal(shouldCloseDispatchAfterSettlement("InDelivery"), false);
  });
});
