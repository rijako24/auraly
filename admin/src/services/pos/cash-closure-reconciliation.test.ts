import assert from "node:assert/strict";
import test from "node:test";

import {
  cashClosurePaymentGroups,
  cashClosureVerificationDecisions,
  correctedCashClosureAmount,
  isCashClosureMethodConfirmed,
} from "./cash-closure-reconciliation";

const items = [
  { verificationKey: "sale", paymentMethodCode: "Cash", movementType: "Sale", amount: 100_000 },
  { verificationKey: "refund", paymentMethodCode: "Cash", movementType: "Refund", amount: -10_000 },
  { verificationKey: "receivable", paymentMethodCode: "Cash", movementType: "ReceivablePayment", amount: 8_000 },
  { verificationKey: "payable", paymentMethodCode: "Cash", movementType: "PayablePayment", amount: -4_000 },
  { verificationKey: "in", paymentMethodCode: "Cash", movementType: "CashIn", amount: 20_000 },
  { verificationKey: "out", paymentMethodCode: "Cash", movementType: "CashOut", amount: -5_000 },
  { verificationKey: "credit", paymentMethodCode: "Credit", movementType: "CreditSale", amount: 30_000 },
  { verificationKey: "transfer-sale", paymentMethodCode: "Transfer", movementType: "Sale", amount: 9_000 },
  { verificationKey: "transfer-refund", paymentMethodCode: "Transfer", movementType: "Refund", amount: -2_000 },
];

test("cash groups its six source types and keeps individual verification where required", () => {
  assert.deepEqual(
    cashClosureVerificationDecisions(items).map(item => item.verificationKey),
    ["receivable", "payable", "in", "out", "credit"],
  );
  assert.deepEqual(
    cashClosurePaymentGroups(items.filter(item => item.paymentMethodCode === "Cash"), true).map(group => [group.label, group.items.length]),
    [["Facturas y comprobantes", 1], ["Devoluciones", 1], ["Abonos a cartera", 1], ["Pagos a proveedores", 1], ["Entradas de dinero", 1], ["Salidas de dinero", 1]],
  );
  assert.deepEqual(
    cashClosurePaymentGroups(items.filter(item => item.paymentMethodCode === "Transfer"), false).map(group => [group.label, group.items.length]),
    [["Facturas y comprobantes", 1], ["Devoluciones", 1], ["Abonos a cartera", 0], ["Pagos a proveedores", 0]],
  );
});

test("source correction moves a confirmed tender without changing physical cash", () => {
  const tender = [
    { verificationKey: "sale-cash", paymentMethodCode: "Cash", movementType: "Sale", amount: 5_000 },
    { verificationKey: "sale-transfer", paymentMethodCode: "Transfer", movementType: "Sale", amount: 9_000 },
  ];
  const corrections = { "sale-cash": { verificationKey: "sale-cash", paymentMethodCode: "Transfer", amount: 7_000 } };
  assert.equal(correctedCashClosureAmount("Cash", tender, { "sale-transfer": "Verified" }, corrections, 0), 0);
  assert.equal(correctedCashClosureAmount("Transfer", tender, {}, corrections, 0), 16_000);
  assert.equal(isCashClosureMethodConfirmed("Transfer", tender, {}, false), true);
  assert.equal(correctedCashClosureAmount("Transfer", [], {}, {}, 0, { Transfer: 9_000 }), 9_000);
  assert.equal(correctedCashClosureAmount("Transfer", tender, {}, corrections, 0, { Transfer: 9_000 }), 16_000);
  assert.equal(correctedCashClosureAmount("Transfer", tender, {}, {
    "sale-transfer": { verificationKey: "sale-transfer", paymentMethodCode: "Cash", amount: 9_000 },
  }, 0, { Transfer: 9_000 }), 0);
});

test("cash uses the corrected physical count while movements still require a decision", () => {
  assert.equal(correctedCashClosureAmount("Cash", items, {}, {}, 85_000), 85_000);
  assert.equal(correctedCashClosureAmount("Cash", items, { in: "Verified" }, {}, 85_000), 85_000);
  assert.equal(correctedCashClosureAmount("Cash", items, { in: "Verified", out: "Verified" }, {}, 85_000), 85_000);
  assert.equal(isCashClosureMethodConfirmed("Cash", items, { in: "Verified" }, false), false);
  assert.equal(isCashClosureMethodConfirmed(
    "Cash",
    items,
    { receivable: "Verified", payable: "Verified", in: "Verified", out: "Missing" },
    false,
  ), true);
  assert.equal(isCashClosureMethodConfirmed("Credit", items, {}, false), false);
  assert.equal(isCashClosureMethodConfirmed("Credit", items, { credit: "Verified" }, false), true);
});
