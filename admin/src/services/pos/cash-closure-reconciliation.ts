export type CashClosureVerification = {
  verificationKey: string;
  paymentMethodCode: string;
  movementType: string;
  amount: number;
};

export type CashClosureVerificationStatus = "Verified" | "Missing";

export function requiresIndividualCashClosureVerification(
  item: CashClosureVerification,
) {
  return item.paymentMethodCode !== "Cash" ||
    item.movementType === "CashIn" ||
    item.movementType === "CashOut" ||
    item.movementType === "CreditSale";
}

export function cashClosureVerificationDecisions<T extends CashClosureVerification>(
  items: readonly T[],
) {
  return items.filter(requiresIndividualCashClosureVerification);
}

export function verifiedCashClosureAmount(
  paymentMethodCode: string,
  items: readonly CashClosureVerification[],
  statuses: Readonly<Record<string, CashClosureVerificationStatus | undefined>>,
  fallback: number,
) {
  const methodItems = items.filter(item => item.paymentMethodCode === paymentMethodCode);
  if (!methodItems.length) return fallback;
  return methodItems.reduce((sum, item) => {
    if (!requiresIndividualCashClosureVerification(item)) return sum + item.amount;
    return statuses[item.verificationKey] === "Verified" ? sum + item.amount : sum;
  }, 0);
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
    : paymentMethodCode === "Cash" || fallback;
}

export function cashClosureCashGroups<T extends CashClosureVerification>(
  items: readonly T[],
) {
  return [
    { key: "CashIn", label: "Entradas de dinero", items: items.filter(item => item.movementType === "CashIn") },
    { key: "CashOut", label: "Salidas de dinero", items: items.filter(item => item.movementType === "CashOut") },
  ].filter(group => group.items.length > 0);
}
