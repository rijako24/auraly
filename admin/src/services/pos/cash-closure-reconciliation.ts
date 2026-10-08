export type CashClosureVerification = {
  verificationKey: string;
  paymentMethodCode: string;
  movementType: string;
  amount: number;
};

export type CashClosureVerificationStatus = "Verified" | "Missing";
export type CashClosureCorrection = { verificationKey: string; paymentMethodCode: string; amount: number };

export function requiresIndividualCashClosureVerification(
  item: CashClosureVerification,
) {
  if (item.movementType === "Sale" || item.movementType === "Refund") return false;
  return item.paymentMethodCode !== "Cash" ||
    item.movementType === "CashIn" ||
    item.movementType === "CashOut" ||
    item.movementType === "ReceivablePayment" ||
    item.movementType === "PayablePayment" ||
    item.movementType === "CreditSale";
}

export function cashClosureVerificationDecisions<T extends CashClosureVerification>(
  items: readonly T[],
) {
  return items.filter(requiresIndividualCashClosureVerification);
}

export function correctedCashClosureAmount(
  paymentMethodCode: string,
  items: readonly CashClosureVerification[],
  statuses: Readonly<Record<string, CashClosureVerificationStatus | undefined>>,
  corrections: Readonly<Record<string, CashClosureCorrection | undefined>>,
  cashCount: number,
  informationalTotals?: Readonly<Record<string, number>>,
) {
  if (paymentMethodCode === "Cash") return cashCount;
  const informationalAmount = informationalTotals?.[paymentMethodCode] ?? 0;
  return items.reduce((sum, item) => {
    const correction = corrections[item.verificationKey];
    const informational = item.movementType === "Sale" || item.movementType === "Refund";
    if (informationalTotals && informational) {
      if (!correction) return sum;
      if (item.paymentMethodCode === paymentMethodCode) sum -= item.amount;
      if (correction.paymentMethodCode === paymentMethodCode) sum += correction.amount;
      return sum;
    }
    const confirmed = !requiresIndividualCashClosureVerification(item) ||
      statuses[item.verificationKey] === "Verified";
    if (!confirmed && !(correction && item.paymentMethodCode === "Cash")) return sum;
    const method = correction?.paymentMethodCode ?? item.paymentMethodCode;
    return method === paymentMethodCode ? sum + (correction?.amount ?? item.amount) : sum;
  }, informationalAmount);
}

export function isCashClosureMethodConfirmed(
  paymentMethodCode: string,
  items: readonly CashClosureVerification[],
  statuses: Readonly<Record<string, CashClosureVerificationStatus | undefined>>,
  fallback: boolean,
) {
  const required = items.filter(item =>
    requiresIndividualCashClosureVerification(item) &&
    item.paymentMethodCode === paymentMethodCode);
  return required.length
    ? required.every(item => statuses[item.verificationKey] !== undefined)
    : paymentMethodCode === "Cash" || fallback || items.some(item =>
      item.paymentMethodCode === paymentMethodCode && !requiresIndividualCashClosureVerification(item));
}

export function cashClosurePaymentGroups<T extends CashClosureVerification>(
  items: readonly T[],
  includeCashMovements: boolean,
) {
  const groups = [
    { key: "Sale", label: "Facturas y comprobantes", items: items.filter(item => item.movementType === "Sale") },
    { key: "Refund", label: "Devoluciones", items: items.filter(item => item.movementType === "Refund") },
    { key: "ReceivablePayment", label: "Abonos a cartera", items: items.filter(item => item.movementType === "ReceivablePayment") },
    { key: "PayablePayment", label: "Pagos a proveedores", items: items.filter(item => item.movementType === "PayablePayment") },
  ];
  return includeCashMovements ? [
    ...groups,
    { key: "CashIn", label: "Entradas de dinero", items: items.filter(item => item.movementType === "CashIn") },
    { key: "CashOut", label: "Salidas de dinero", items: items.filter(item => item.movementType === "CashOut") },
  ] : groups;
}
