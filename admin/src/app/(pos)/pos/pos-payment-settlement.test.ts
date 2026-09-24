import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
  calculatePaymentSettlement,
  chooseAdditionalPaymentMethod,
  handlePosPaymentAmountEnter,
  splitCreditCheckout,
  shouldShowCashChange,
} from "./pos-payment-settlement";

describe("handlePosPaymentAmountEnter", () => {
  it("does not invent another payment while the written total is incomplete", () => {
    let submitted = false;
    handlePosPaymentAmountEnter({
      key: "Enter",
      preventDefault: () => undefined,
      currentTarget: { form: { requestSubmit: () => { submitted = true; } } },
    }, 35);

    assert.equal(submitted, false);
  });

  it("submits the canonical payment form when the total is complete", () => {
    let submitted = false;
    handlePosPaymentAmountEnter({
      key: "Enter",
      preventDefault: () => undefined,
      currentTarget: { form: { requestSubmit: () => { submitted = true; } } },
    }, 0);

    assert.equal(submitted, true);
  });
});

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
    isCreditEnabled: true, availableCredit: 500,
  };

  it("converts customer credit into financed terms instead of received money", () => {
    const value = splitCreditCheckout([
      { methodCode: "Cash", amount: 40, reference: null },
      { methodCode: "Credit", amount: 60, reference: null },
    ], customer);
    assert.deepEqual(value.payments, [{ methodCode: "Cash", amount: 40, reference: null }]);
    assert.deepEqual(value.credit, { amount: 60 });
  });

  it("leaves credit authorization to the current server balance", () => {
    assert.doesNotThrow(() => splitCreditCheckout(
      [{ methodCode: "Credit", amount: 600, reference: null }],
      { ...customer, isCreditEnabled: false, availableCredit: 0 },
    ));
    assert.throws(
      () => splitCreditCheckout(
        [{ methodCode: "Credit", amount: 60, reference: null }],
        null,
      ),
      /seleccionar un cliente/,
    );
  });
});

describe("calculatePaymentSettlement", () => {
  it("keeps the exact amount for a portfolio payment", () => {
    const result = calculatePaymentSettlement(10_450.45, [
      { methodCode: "Cash", amount: 10_450.45, reference: null },
    ], false);
    assert.equal(result.isValid, true);
    assert.equal(result.paymentTotal, 10_450.45);
    assert.equal(result.appliedPayments[0].amount, 10_450.45);
    assert.equal(result.appliedPayments[0].roundingAdjustment, undefined);
  });
  it("never completes a sale after a cashier writes a negative correction", () => {
    const settlement = calculatePaymentSettlement(20_000, [
      { methodCode: "DebitCard", amount: 15_000, reference: null },
      { methodCode: "Cash", amount: -2_000, reference: null },
    ]);
    assert.equal(settlement.received, 13_000);
    assert.equal(settlement.missing, 7_000);
    assert.equal(settlement.isValid, false);
  });
  it("applies the invoice total and returns cash change", () => {
    const result = calculatePaymentSettlement(100, [
      { methodCode: "Cash", amount: 110, reference: null },
    ]);

    assert.equal(result.isValid, true);
    assert.equal(result.received, 110);
    assert.equal(result.cashTendered, 110);
    assert.equal(result.change, 10);
    assert.equal(shouldShowCashChange(result), true);
    assert.deepEqual(result.appliedPayments, [
      { methodCode: "Cash", amount: 100, reference: null, tenderedAmount: 110 },
    ]);
  });

  it("calculates change after applying a mixed payment", () => {
    const result = calculatePaymentSettlement(100, [
      { methodCode: "Cash", amount: 30, reference: null },
      { methodCode: "DebitCard", amount: 80, reference: "AUTH-1" },
    ]);

    assert.equal(result.isValid, true);
    assert.equal(result.change, 10);
    assert.deepEqual(result.appliedPayments, [
      { methodCode: "Cash", amount: 20, reference: null, tenderedAmount: 30 },
      { methodCode: "DebitCard", amount: 80, reference: "AUTH-1" },
    ]);
  });

  it("rejects an excess received without cash", () => {
    const result = calculatePaymentSettlement(100, [
      { methodCode: "CreditCard", amount: 110, reference: null },
    ]);

    assert.equal(result.isValid, false);
    assert.equal(result.hasNonCashExcess, true);
    assert.equal(result.change, 0);
  });

  it("keeps the regular layout for an exact card payment", () => {
    const result = calculatePaymentSettlement(100, [
      { methodCode: "CreditCard", amount: 100, reference: "AUTH-2" },
    ]);

    assert.equal(result.isValid, true);
    assert.equal(result.change, 0);
    assert.equal(shouldShowCashChange(result), false);
  });

  it("reports the missing amount", () => {
    const result = calculatePaymentSettlement(100, [
      { methodCode: "Cash", amount: 20, reference: null },
    ]);

    assert.equal(result.isValid, false);
    assert.equal(result.missing, 80);
    assert.equal(result.change, 0);
  });

  it("rejects duplicate cash rows", () => {
    const result = calculatePaymentSettlement(50, [
      { methodCode: "Cash", amount: 30, reference: null },
      { methodCode: "Cash", amount: 30, reference: null },
    ]);

    assert.equal(result.isValid, false);
    assert.equal(result.hasDuplicateCash, true);
  });

  it("rounds the invoice to the nearest hundred and keeps a positive adjustment separate", () => {
    const result = calculatePaymentSettlement(10_450.45, [
      { methodCode: "Cash", amount: 20_000, reference: null },
    ]);

    assert.equal(result.paymentTotal, 10_500);
    assert.equal(result.appliedPayments[0].amount, 10_450.45);
    assert.equal(result.appliedPayments[0].roundingAdjustment, 49.55);
    assert.equal(result.appliedPayments[0].tenderedAmount, 20_000);
    assert.equal(result.change, 9_500);
  });

  it("rounds down to the nearest hundred and keeps a negative adjustment separate", () => {
    const result = calculatePaymentSettlement(10_749, [
      { methodCode: "DebitCard", amount: 10_700, reference: "AUTH-3" },
    ]);

    assert.equal(result.paymentTotal, 10_700);
    assert.deepEqual(result.appliedPayments, [
      {
        methodCode: "DebitCard",
        amount: 10_749,
        reference: "AUTH-3",
        roundingAdjustment: -49,
      },
    ]);
  });

  it("rounds an exact midpoint upward", () => {
    const result = calculatePaymentSettlement(10_750, [
      { methodCode: "Cash", amount: 10_800, reference: null },
    ]);

    assert.equal(result.paymentTotal, 10_800);
    assert.equal(result.appliedPayments[0].roundingAdjustment, 50);
  });

  it("uses the rounded total for a sale financed completely on customer credit", () => {
    const result = calculatePaymentSettlement(10_450.45, [
      { methodCode: "Credit", amount: 10_500, reference: null },
    ]);

    assert.equal(result.paymentTotal, 10_500);
    assert.deepEqual(result.appliedPayments, [
      { methodCode: "Credit", amount: 10_500, reference: null },
    ]);
  });
});
