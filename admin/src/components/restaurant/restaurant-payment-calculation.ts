import { parseMoneyDraft } from "../../lib/money-input";
import type { PosPaymentInput } from "@/services/pos/pos-edge-client";

const round = (value: number) => Math.round(value * 10000) / 10000;

export function finalRestaurantPayments(collections: ReadonlyArray<{
  payments: PosPaymentInput[];
}>): PosPaymentInput[] {
  const captured = collections.flatMap((entry) => entry.payments);
  const cash = captured.filter((payment) => payment.methodCode === "Cash");
  const other = captured.filter((payment) => payment.methodCode !== "Cash");
  if (cash.length <= 1) return captured;
  const groups = new Map<string | null, PosPaymentInput[]>();
  for (const payment of cash) {
    const key = payment.paymentMethodId ?? null;
    const group = groups.get(key);
    if (group) group.push(payment);
    else groups.set(key, [payment]);
  }
  return [...groups.values()].map<PosPaymentInput>((group) => {
    const roundingAdjustment = round(group.reduce((sum, item) => sum + (item.roundingAdjustment ?? 0), 0));
    const first = { ...group[0] };
    delete first.roundingAdjustment;
    return { ...first, amount: round(group.reduce((sum, item) => sum + item.amount, 0)),
      tenderedAmount: round(group.reduce((sum, item) =>
        sum + (item.tenderedAmount ?? item.amount + (item.roundingAdjustment ?? 0)), 0)),
      ...(roundingAdjustment !== 0 ? { roundingAdjustment } : {}) };
  }).concat(other);
}

export function settleRestaurantPayment(total: number, rows: ReadonlyArray<{
  methodCode: string; amount: string;
}>, cashTipIncluded = 0, roundingAdjustment = 0) {
  const payable = round(total + roundingAdjustment);
  const nonCashReceived = round(rows.filter((row) => row.methodCode !== "Cash")
    .reduce((sum, row) => sum + parseMoneyDraft(row.amount), 0));
  const cashTendered = round(rows.filter((row) => row.methodCode === "Cash")
    .reduce((sum, row) => sum + parseMoneyDraft(row.amount), 0));
  const cashSaleTendered = round(cashTendered - cashTipIncluded);
  const cashApplied = round(Math.min(Math.max(0, cashSaleTendered),
    Math.max(0, payable - nonCashReceived)));
  return {
    cashTendered,
    cashSaleTendered,
    cashApplied,
    due: round(payable - nonCashReceived - cashApplied),
    change: round(nonCashReceived > payable ? 0 :
      Math.max(0, cashSaleTendered - (payable - nonCashReceived))),
  };
}
