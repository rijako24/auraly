import type { PosCreditTerms, PosCustomer, PosPaymentInput } from "@/services/pos/pos-edge-client";

export type PosPaymentSettlement = {
  isValid: boolean;
  received: number;
  missing: number;
  change: number;
  hasNonCashExcess: boolean;
  hasDuplicateCash: boolean;
  appliedPayments: PosPaymentInput[];
};

type PosPaymentAmountEnterEvent = {
  key: string;
  preventDefault: () => void;
  currentTarget: { form: { requestSubmit: () => void } | null };
};

export function handlePosPaymentAmountEnter(
  event: PosPaymentAmountEnterEvent,
  missing: number,
  addCashPayment: () => void,
): boolean {
  if (event.key !== "Enter") return false;
  event.preventDefault();
  if (missing > tolerance) addCashPayment();
  else event.currentTarget.form?.requestSubmit();
  return true;
}

export function chooseAdditionalPaymentMethod(
  methodCodes: readonly string[],
  usedMethodCodes: ReadonlySet<string>,
) {
  if (methodCodes.includes("Cash") && !usedMethodCodes.has("Cash")) return "Cash";
  return methodCodes.find((code) => !usedMethodCodes.has(code));
}

const precision = 100;
const tolerance = 0.005;

export function splitCreditCheckout(
  payments: PosPaymentInput[],
  customer: PosCustomer | null,
  now: Date = new Date(),
): { payments: PosPaymentInput[]; credit: PosCreditTerms | null } {
  const creditRows = payments.filter((payment) => payment.methodCode === "Credit");
  if (creditRows.length === 0) return { payments, credit: null };
  if (creditRows.length > 1) throw new Error("La venta admite una sola línea de crédito.");
  const creditRow = creditRows[0];
  if (!customer?.isCreditEnabled)
    throw new Error("El cliente no está habilitado para ventas a crédito.");
  if (customer.availableCredit != null && creditRow.amount - customer.availableCredit > tolerance)
    throw new Error("La venta supera el cupo disponible del cliente.");
  const dueDate = new Date(now.getTime() + (customer.defaultCreditDueDays ?? 0) * 86_400_000);
  return {
    payments: payments.filter((payment) => payment.methodCode !== "Credit"),
    credit: { amount: creditRow.amount, dueDate: dueDate.toISOString() },
  };
}

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
