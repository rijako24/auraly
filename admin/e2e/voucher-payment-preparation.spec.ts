import { expect, test } from "@playwright/test";
import { prepareVoucherPayment } from "../src/lib/voucher-payment-preparation";
import type { AccountingOpenPosition } from "../src/services/api/accounting";

function position(overrides: Partial<AccountingOpenPosition> = {}): AccountingOpenPosition {
  return { subledgerKind: "Payable", subledgerId: "invoice", partyId: "party", partyName: "Proveedor",
    partyIdentification: "1001", sourceDocumentId: "source", sourceDocumentType: "GoodsReceipt", documentNumber: "FV-1",
    occurredAt: "2026-11-19T12:00:00Z", dueDate: null, currencyCode: "COP", originalAmount: 1000, outstandingAmount: 1000,
    pendingPaymentAmount: 0, pendingAdjustmentAmount: 0, availableAmount: 1000, openingEntryId: "entry",
    openingEntryLineNumber: 1, accountId: "payable-account", accountCode: "220505", accountName: "Proveedor", accountIsActive: true,
    openingDebit: 0, openingCredit: 1000, openingLinkIssue: null, counterpartyRoleId: "supplier", status: "Open",
    financialDiscountEligibleAmount: 100, financialDiscountAnticipated: false, financialDiscountDecided: false,
    financialDiscountPendingReturn: false, financialDiscountEarnedAccountId: "earned", financialDiscountLostAccountId: "lost", ...overrides };
}

for (const scenario of [
  { name: "ganado sin anticipar", anticipated: false, earned: true, available: 1000, cash: 900, debit: 1000, adjustment: -100 },
  { name: "ganado ya anticipado", anticipated: true, earned: true, available: 900, cash: 900, debit: 900, adjustment: 0 },
  { name: "perdido anticipado", anticipated: true, earned: false, available: 900, cash: 1000, debit: 900, adjustment: 100 },
]) test(`preparar egreso conserva dinero y descuento ${scenario.name}`, () => {
  const result = prepareVoucherPayment("Payable", "supplier", [{ id: "invoice", amount: scenario.cash, financialDiscountEarned: scenario.earned }],
    [position({ financialDiscountAnticipated: scenario.anticipated, availableAmount: scenario.available, outstandingAmount: scenario.available })]);
  expect(result.mainPartyId).toBe("party");
  expect(result.lines[0].value).toMatchObject({ debit: scenario.debit, credit: 0, existingPosition: { subledgerId: "invoice" }, financialDiscountEarned: scenario.earned });
  expect(result.lines.reduce((sum, line) => sum + line.value.debit - line.value.credit, 0)).toBe(scenario.cash);
  expect(result.lines.length).toBe(scenario.adjustment === 0 ? 1 : 2);
  if (scenario.adjustment !== 0) expect(result.lines[1].value.debit - result.lines[1].value.credit).toBe(scenario.adjustment);
});

test("preparar recibo conserva tercero y abonos distintos en todas las facturas", () => {
  const rows = ["a", "b"].map(subledgerId => position({ subledgerId, subledgerKind: "Receivable", counterpartyRoleId: "customer", openingDebit: 1000, openingCredit: 0 }));
  const result = prepareVoucherPayment("Receivable", "customer", [{ id: "b", amount: 125 }, { id: "a", amount: 75 }], rows);
  expect(result.lines.map(line => [line.value.existingPosition?.subledgerId, line.value.credit, line.value.debit])).toEqual([["b", 125, 0], ["a", 75, 0]]);
  expect(result.lines.every(line => line.value.partyId === "party")).toBe(true);
});

test("preparar no sustituye una factura ausente, de otro tercero, bloqueada o sin saldo suficiente", () => {
  const selected = [{ id: "invoice", amount: 125 }];
  expect(() => prepareVoucherPayment("Payable", "supplier", selected, [])).toThrow(/ya no está disponible/);
  expect(() => prepareVoucherPayment("Payable", "supplier", selected, [position({ counterpartyRoleId: "other" })])).toThrow(/tercero seleccionado/);
  expect(() => prepareVoucherPayment("Payable", "supplier", selected, [position({ openingLinkIssue: "Apertura sin conciliar" })])).toThrow("Apertura sin conciliar");
  expect(() => prepareVoucherPayment("Payable", "supplier", selected, [position({ availableAmount: 100 })])).toThrow(/supera el saldo disponible/);
  expect(() => prepareVoucherPayment("Payable", "supplier", [{ ...selected[0], financialDiscountEarned: true }],
    [position({ financialDiscountDecided: true })])).toThrow(/descuento financiero cambió/);
});
