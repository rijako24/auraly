import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
  calculatePaymentSettlement,
  chooseAdditionalPaymentMethod,
  splitCreditCheckout,
  shouldShowCashChange,
} from "./pos-payment-settlement";

describe("chooseAdditionalPaymentMethod", () => {
  it("uses cash first when a partial payment still has a balance", () => {
    assert.equal(
      chooseAdditionalPaymentMethod(["DebitCard", "Cash", "CreditCard"], new Set(["DebitCard"])),
      "Cash",
    );
  });

  it("uses the next unused catalog method when cash is already present", () => {
    assert.equal(
      chooseAdditionalPaymentMethod(["DebitCard", "Cash", "CreditCard"], new Set(["Cash"])),
      "DebitCard",
    );
  });
});

describe("splitCreditCheckout", () => {
  const customer = {
    customerId: "customer-1", identification: "9001", name: "Cliente crédito",
    priceChannelId: null, requiresElectronicInvoice: false, isActive: true,
    isCreditEnabled: true, defaultCreditDueDays: 30, availableCredit: 500,
  };

  it("converts customer credit into financed terms instead of received money", () => {
    const value = splitCreditCheckout([
      { methodCode: "Cash", amount: 40, reference: null },
      { methodCode: "Credit", amount: 60, reference: null },
    ], customer, new Date("2026-08-23T12:00:00.000Z"));
    assert.deepEqual(value.payments, [{ methodCode: "Cash", amount: 40, reference: null }]);
    assert.deepEqual(value.credit, { amount: 60, dueDate: "2026-09-22T12:00:00.000Z" });
  });

  it("blocks disabled credit and insufficient available credit", () => {
    assert.throws(() => splitCreditCheckout([{ methodCode: "Credit", amount: 60, reference: null }], { ...customer, isCreditEnabled: false }), /no está habilitado/);
    assert.throws(() => splitCreditCheckout([{ methodCode: "Credit", amount: 600, reference: null }], customer), /supera el cupo/);
  });
});

describe("calculatePaymentSettlement", () => {
  it("applies the invoice total and returns cash change", () => {
    const result = calculatePaymentSettlement(55, [
      { methodCode: "Cash", amount: 60, reference: null },
    ]);

    assert.equal(result.isValid, true);
    assert.equal(result.received, 60);
    assert.equal(result.change, 5);
    assert.equal(shouldShowCashChange(result), true);
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

  it("keeps the regular layout for an exact card payment", () => {
    const result = calculatePaymentSettlement(55, [
      { methodCode: "CreditCard", amount: 55, reference: "AUTH-2" },
    ]);

    assert.equal(result.isValid, true);
    assert.equal(result.change, 0);
    assert.equal(shouldShowCashChange(result), false);
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
