import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { calculatePaymentSettlement } from "./pos-payment-settlement";

describe("calculatePaymentSettlement", () => {
  it("applies the invoice total and returns cash change", () => {
    const result = calculatePaymentSettlement(55, [
      { methodCode: "Cash", amount: 60, reference: null },
    ]);

    assert.equal(result.isValid, true);
    assert.equal(result.received, 60);
    assert.equal(result.change, 5);
    assert.deepEqual(result.appliedPayments, [
      { methodCode: "Cash", amount: 55, reference: null },
    ]);
  });

  it("calculates change after applying a mixed payment", () => {
    const result = calculatePaymentSettlement(55, [
      { methodCode: "Cash", amount: 20, reference: null },
      { methodCode: "DebitCard", amount: 40, reference: "AUTH-1" },
    ]);

    assert.equal(result.isValid, true);
    assert.equal(result.change, 5);
    assert.deepEqual(result.appliedPayments, [
      { methodCode: "Cash", amount: 15, reference: null },
      { methodCode: "DebitCard", amount: 40, reference: "AUTH-1" },
    ]);
  });

  it("rejects an excess received without cash", () => {
    const result = calculatePaymentSettlement(55, [
      { methodCode: "CreditCard", amount: 60, reference: null },
    ]);

    assert.equal(result.isValid, false);
    assert.equal(result.hasNonCashExcess, true);
    assert.equal(result.change, 0);
  });

  it("reports the missing amount", () => {
    const result = calculatePaymentSettlement(55, [
      { methodCode: "Cash", amount: 20, reference: null },
    ]);

    assert.equal(result.isValid, false);
    assert.equal(result.missing, 35);
    assert.equal(result.change, 0);
  });

  it("rejects duplicate cash rows", () => {
    const result = calculatePaymentSettlement(55, [
      { methodCode: "Cash", amount: 30, reference: null },
      { methodCode: "Cash", amount: 30, reference: null },
    ]);

    assert.equal(result.isValid, false);
    assert.equal(result.hasDuplicateCash, true);
  });

  it("keeps cent precision deterministic", () => {
    const result = calculatePaymentSettlement(10.05, [
      { methodCode: "Cash", amount: 20, reference: null },
    ]);

    assert.equal(result.appliedPayments[0].amount, 10.05);
    assert.equal(result.change, 9.95);
  });
});
