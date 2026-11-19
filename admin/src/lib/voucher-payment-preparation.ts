import type { AccountingOpenPosition, VoucherDraftLineView, VoucherDraftPreparation } from "@/services/api/accounting";

export type VoucherDiscountDecision = "pending" | "earned" | "lost";

export function createVoucherPositionLine(position: AccountingOpenPosition, amount: number,
  financialGroupId: string | null, financialDiscountEarned: boolean | null = null): VoucherDraftLineView {
  return { value: {
    lineId: crypto.randomUUID(), accountId: position.accountId, partyId: position.partyId,
    partySiteId: position.partySiteId, costCenterId: null,
    description: `Aplicación ${position.documentNumber}`.slice(0, 500), reference: position.documentNumber.slice(0, 100),
    debit: (position.openingCredit ?? 0) > 0 ? amount : 0, credit: (position.openingDebit ?? 0) > 0 ? amount : 0,
    existingPosition: { subledgerKind: position.subledgerKind, subledgerId: position.subledgerId },
    financialGroupId, financialDiscountEarned,
  }, accountCode: position.accountCode, accountName: position.accountName,
    partyName: position.partyName, partyIdentification: position.partyIdentification, costCenterName: null };
}

export function voucherDiscountAdjustment(position: AccountingOpenPosition, decision: VoucherDiscountDecision) {
  if (decision === "pending") return 0;
  return (decision === "earned" ? position.financialDiscountEligibleAmount ?? 0 : 0) -
    (position.financialDiscountAnticipated ? position.financialDiscountEligibleAmount ?? 0 : 0);
}

export function createVoucherPositionLines(position: AccountingOpenPosition, amount: number, decision: VoucherDiscountDecision) {
  const adjustment = voucherDiscountAdjustment(position, decision);
  const groupId = decision === "pending" ? null : crypto.randomUUID();
  const selected = createVoucherPositionLine(position, amount, groupId, decision === "pending" ? null : decision === "earned");
  if (adjustment === 0) return [selected];
  const accountId = adjustment > 0 ? position.financialDiscountEarnedAccountId : position.financialDiscountLostAccountId;
  if (!accountId) throw new Error("Falta la cuenta del descuento financiero. Revisa su configuración.");
  return [selected, { value: {
    lineId: crypto.randomUUID(), accountId, partyId: position.partyId, costCenterId: null,
    description: `Descuento financiero ${position.documentNumber}`.slice(0, 500),
    reference: position.documentNumber.slice(0, 100), debit: adjustment < 0 ? -adjustment : 0,
    credit: adjustment > 0 ? adjustment : 0, financialGroupId: groupId,
  }, accountCode: adjustment > 0 ? position.financialDiscountEarnedAccountCode ?? null : position.financialDiscountLostAccountCode ?? null,
    accountName: adjustment > 0 ? position.financialDiscountEarnedAccountName ?? null : position.financialDiscountLostAccountName ?? null,
    partyName: position.partyName, partyIdentification: position.partyIdentification, costCenterName: null }];
}

// Maps the selected cash amounts to the same editable lines as the pending-
// document picker. Availability and accounts are reread together before opening.
export function prepareVoucherPayment(kind: "Payable" | "Receivable", roleId: string,
  allocations: ReadonlyArray<{ id: string; amount: number; financialDiscountEarned?: boolean | null }>,
  positions: ReadonlyArray<AccountingOpenPosition>): VoucherDraftPreparation {
  if (positions.length !== allocations.length || positions.length === 0)
    throw new Error("Una factura seleccionada ya no está disponible para este tercero. Revisa sus saldos.");
  const first = positions[0], byId = new Map(positions.map(position => [position.subledgerId, position]));
  const lines = allocations.flatMap(allocation => {
    const position = byId.get(allocation.id);
    if (!position || position.subledgerKind !== kind || position.counterpartyRoleId !== roleId || position.partyId !== first.partyId)
      throw new Error("Las facturas deben pertenecer al tercero seleccionado.");
    if (position.openingLinkIssue || !position.accountId || !position.accountIsActive)
      throw new Error(position.openingLinkIssue || "La obligación requiere una cuenta de apertura válida antes de pagar.");
    if (position.currencyCode !== "COP") throw new Error("Este formulario sólo admite obligaciones en COP.");
    const decision = allocation.financialDiscountEarned == null ? "pending" : allocation.financialDiscountEarned ? "earned" : "lost";
    if (decision !== "pending" && (kind !== "Payable" || !position.financialDiscountEligibleAmount ||
      position.financialDiscountAnticipated == null || position.financialDiscountDecided || position.financialDiscountPendingReturn))
      throw new Error("El descuento financiero cambió o no está disponible. Revisa la factura antes de continuar.");
    // The wizard captures money; a discount counterpart completes the original
    // obligation amount. This is the same presentation used by the line picker.
    const amount = Number((allocation.amount + voucherDiscountAdjustment(position, decision)).toFixed(4));
    if (!Number.isFinite(allocation.amount) || allocation.amount <= 0 || amount <= 0 || amount > position.availableAmount)
      throw new Error("El abono supera el saldo disponible de una factura. Revisa los pagos pendientes.");
    return createVoucherPositionLines(position, amount, decision);
  });
  return { description: `${kind === "Receivable" ? "Recaudo" : "Pago"} ${positions.length === 1 ? first.documentNumber : `de ${positions.length} facturas`}`,
    reference: positions.length === 1 ? first.documentNumber : null, mainPartyId: first.partyId,
    mainPartyName: first.partyName, mainPartyIdentification: first.partyIdentification, lines };
}
