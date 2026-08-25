import assert from "node:assert/strict";
import test from "node:test";
import { completePaymentBreakdown, type PaymentBreakdownRow } from "./sales-payment-breakdown";

const row = (key: string, netSales: number): PaymentBreakdownRow => ({
  key,
  label: key,
  documentCount: 1,
  quantity: 0,
  grossSales: netSales,
  discounts: 0,
  returns: 0,
  netUntaxedSales: netSales,
  tax: 0,
  netSales,
  recognizedCost: 0,
  grossProfit: netSales,
  grossMarginPercent: 100,
  participationPercent: 100,
});

test("shows every configured payment method and preserves reported totals", () => {
  const result = completePaymentBreakdown([
    { code: "Cash", label: "Efectivo", sortOrder: 10 },
    { code: "Card", label: "Tarjeta", sortOrder: 20 },
    { code: "Transfer", label: "Transferencia", sortOrder: 30 },
  ], [row("Card", 25000)]);
  assert.deepEqual(result.map((item) => [item.key, item.label, item.netSales]), [
    ["Cash", "Efectivo", 0],
    ["Card", "Tarjeta", 25000],
    ["Transfer", "Transferencia", 0],
  ]);
});

test("keeps a reported legacy method that is not in the current catalog", () => {
  const result = completePaymentBreakdown([], [row("LegacyVoucher", 5000)]);
  assert.equal(result[0].key, "LegacyVoucher");
  assert.equal(result[0].netSales, 5000);
});
