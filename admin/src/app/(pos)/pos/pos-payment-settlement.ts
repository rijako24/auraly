import type { PosPaymentInput } from "@/services/pos/pos-edge-client";

export type PosPaymentSettlement = {
  isValid: boolean;
  received: number;
  missing: number;
  change: number;
  hasNonCashExcess: boolean;
  hasDuplicateCash: boolean;
  appliedPayments: PosPaymentInput[];
};

export function chooseAdditionalPaymentMethod(
  methodCodes: readonly string[],
  usedMethodCodes: ReadonlySet<string>,
) {
  if (methodCodes.includes("Cash") && !usedMethodCodes.has("Cash")) return "Cash";
  return methodCodes.find((code) => !usedMethodCodes.has(code));
}

const precision = 100;
const tolerance = 0.005;

function round(value: number) {
  return Math.round(value * precision) / precision;
}

export function shouldShowCashChange(settlement: Pick<PosPaymentSettlement, "change">) {
  return settlement.change > tolerance;
}

export function calculatePaymentSettlement(
  total: number,
  payments: PosPaymentInput[],
): PosPaymentSettlement {
  const validAmounts =
    payments.length > 0 &&
    payments.every(
      (payment) => Number.isFinite(payment.amount) && payment.amount > 0,
    );
  const cashPayments = payments.filter((payment) => payment.methodCode === "Cash");
  const nonCashPayments = payments.filter((payment) => payment.methodCode !== "Cash");
  const cashTendered = round(
    cashPayments.reduce((sum, payment) => sum + payment.amount, 0),
  );
  const nonCashTotal = round(
    nonCashPayments.reduce((sum, payment) => sum + payment.amount, 0),
  );
  const received = round(cashTendered + nonCashTotal);
  const missing = round(Math.max(0, total - received));
  const cashApplied = round(Math.min(cashTendered, Math.max(0, total - nonCashTotal)));
  const change = round(Math.max(0, cashTendered - cashApplied));
  const hasNonCashExcess = nonCashTotal - total > tolerance;
  const hasDuplicateCash = cashPayments.length > 1;
  const isValid =
    validAmounts &&
    missing < tolerance &&
    !hasNonCashExcess &&
    !hasDuplicateCash;

  let remainingCash = cashApplied;
  const appliedPayments = payments
    .map((payment) => {
      if (payment.methodCode !== "Cash") return payment;
      const amount = round(Math.min(payment.amount, remainingCash));
      remainingCash = round(remainingCash - amount);
      return { ...payment, amount };
    })
    .filter((payment) => payment.amount > tolerance);

  return {
    isValid,
    received,
    missing,
    change,
    hasNonCashExcess,
    hasDuplicateCash,
    appliedPayments,
  };
}
