import assert from "node:assert/strict";
import test from "node:test";

import {
  cashClosureCashGroups,
  cashClosureVerificationDecisions,
  isCashClosureMethodConfirmed,
  verifiedCashClosureAmount,
} from "./cash-closure-reconciliation";

const items = [
  { verificationKey: "sale", paymentMethodCode: "Cash", movementType: "Sale", amount: 100_000 },
  { verificationKey: "refund", paymentMethodCode: "Cash", movementType: "Refund", amount: -10_000 },
  { verificationKey: "in", paymentMethodCode: "Cash", movementType: "CashIn", amount: 20_000 },
  { verificationKey: "out", paymentMethodCode: "Cash", movementType: "CashOut", amount: -5_000 },
  { verificationKey: "credit", paymentMethodCode: "Credit", movementType: "CreditSale", amount: 30_000 },
];

test("cash shows only entries and exits while credit sales remain independently verifiable", () => {
  assert.deepEqual(
    cashClosureVerificationDecisions(items).map(item => item.verificationKey),
    ["in", "out", "credit"],
  );
  assert.deepEqual(
    cashClosureCashGroups(items).map(group => [group.label, group.items.length]),
    [["Entradas de dinero", 1], ["Salidas de dinero", 1]],
  );
});

test("cash verified total trusts hidden sales and changes as movements are checked", () => {
  assert.equal(verifiedCashClosureAmount("Cash", items, {}, 0), 90_000);
  assert.equal(verifiedCashClosureAmount("Cash", items, { in: "Verified" }, 0), 110_000);
  assert.equal(verifiedCashClosureAmount("Cash", items, { in: "Verified", out: "Verified" }, 0), 105_000);
  assert.equal(isCashClosureMethodConfirmed("Cash", items, { in: "Verified" }, false), false);
  assert.equal(isCashClosureMethodConfirmed(
    "Cash",
    items,
    { in: "Verified", out: "Missing" },
    false,
  ), true);
  assert.equal(isCashClosureMethodConfirmed("Credit", items, {}, false), false);
  assert.equal(isCashClosureMethodConfirmed("Credit", items, { credit: "Verified" }, false), true);
});
