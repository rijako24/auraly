"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import { useReferenceOptions } from "@/hooks/use-reference-options";
import { useTenantContextStore } from "@/stores/tenant-context-store";
import { PaymentMethodSelect } from "./payment-method-select";
import { usePaymentEvidenceFocus } from "./use-payment-evidence-focus";
import type { PaymentMethod, PaymentMethodQuery } from "@/services/api/payment-methods";
import type { ClosurePaymentCorrection, ClosurePaymentVerification } from "@/services/api/work-session-differences";
import { workSessionPaymentMethodName } from "@/services/pos/pos-work-session-close";
import { formatCurrency } from "@/lib/utils";

export function PaymentCorrectionEditor({ item, businessId, methods, current, onClose, onRemove, onApply, movementLabel, applicationNotice = "Se aplicará al confirmar la conciliación.", submitLabel = "Aplicar", usageOverride, referenceMaxLength = 160, approvalMaxLength = 100, originalPaymentName }: {
  item: ClosurePaymentVerification; businessId: string; methods: string[]; current?: ClosurePaymentCorrection;
  movementLabel: string; applicationNotice?: string; submitLabel?: string;
  originalPaymentName?: string | null;
  usageOverride?: PaymentMethodQuery["usage"]; referenceMaxLength?: number; approvalMaxLength?: number;
  onClose: () => void; onRemove: () => void; onApply: (value: ClosurePaymentCorrection) => Promise<void>;
}) {
  const tenant = useTenantContextStore(state => state.selectedTenantId);
  const paymentTypes = useReferenceOptions("payment-method");
  const originalTender = item.tenderMethodCode ?? item.paymentMethodCode;
  const [tender, setTender] = useState(current?.tenderMethodCode ?? (originalTender === "Card" || originalTender === "Credit" ? "" : originalTender));
  const [form, setForm] = useState<{ id: string; version: number; name: string; account?: string } | null>(
    current?.paymentMethodId && current.paymentMethodVersion ? {
      id: current.paymentMethodId, version: current.paymentMethodVersion, name: current.paymentMethodName ?? "Forma seleccionada",
    } : null);
  const [franchise, setFranchise] = useState(current?.cardFranchiseCode ?? item.cardFranchiseCode ?? "");
  const [approval, setApproval] = useState(current?.approvalNumber ?? item.approvalNumber ?? "");
  const [reference, setReference] = useState(current?.reference ?? item.reference ?? "");
  const [reason, setReason] = useState(current?.reason ?? "");
  const [validating, setValidating] = useState(false);
  const [validationError, setValidationError] = useState<string | null>(null);
  const amount = item.movementType === "CreditSale" ? item.collectibleAmount ?? 0 : item.amount;
  const method = tender === "DebitCard" || tender === "CreditCard" ? "Card" : tender;
  const isCard = tender === "DebitCard" || tender === "CreditCard";
  const evidenceInput = usePaymentEvidenceFocus(Boolean(form) && (isCard || tender === "Transfer"), form?.id);
  const cardFranchises = useReferenceOptions("card-franchise", isCard);
  const manualVoucher = item.sourceDocumentType === "ManualAccountingVoucher";
  const electronic = item.sourceDocumentType === "SalesInvoice" || item.sourceDocumentType === "ServiceInvoice";
  const usage: NonNullable<PaymentMethodQuery["usage"]> = usageOverride ?? (manualVoucher
    ? item.movementType === "CashOut" ? "VoucherPayments" : "VoucherReceipts"
    : item.movementType === "PayablePayment" ? "SupplierPayments"
      : item.movementType === "ReceivablePayment" ? "Receipts" : "Sales");
  const available = (item.movementType === "CashIn" || item.movementType === "CashOut") && !manualVoucher
    ? (paymentTypes.data ?? []).filter(option => option.code === "Cash")
    : (paymentTypes.data ?? []).filter(option =>
        ["Cash", "DebitCard", "CreditCard", "Transfer"].includes(option.code) &&
        methods.includes(option.code === "DebitCard" || option.code === "CreditCard" ? "Card" : option.code));
  const valid = Number.isFinite(amount) && amount !== 0 && reason.trim().length > 0 && form !== null &&
    available.some(option => option.code === tender) &&
    (!isCard || (franchise.length > 0 && approval.trim().length > 0)) &&
    (tender !== "Transfer" || reference.trim().length > 0) &&
    (item.movementType !== "Refund" || Math.abs(amount) <= Math.abs(item.amount));
  const changeType = (value: string) => {
    setTender(value); setForm(null); setFranchise(""); setApproval(""); setReference(""); setValidationError(null);
  };
  const chooseForm = (value: PaymentMethod) => {
    if (form?.id !== value.paymentMethodId) {
      setFranchise(""); setApproval(""); setReference("");
    }
    setForm({ id: value.paymentMethodId, version: value.version, name: value.name,
      account: `${value.accountCode} · ${value.accountName}` });
  };
  const apply = async () => {
    if (!valid || !form) return;
    setValidating(true); setValidationError(null);
    try {
      await onApply({ verificationKey: item.verificationKey, paymentMethodCode: method,
        tenderMethodCode: tender, paymentMethodId: form.id, paymentMethodVersion: form.version, paymentMethodName: form.name,
        cardFranchiseCode: isCard ? franchise : null, approvalNumber: isCard ? approval.trim() : null,
        reference: tender === "Transfer" ? reference.trim() : null, amount, reason: reason.trim() });
    } catch (error) {
      setValidationError(error instanceof Error ? error.message : "No fue posible validar esta corrección.");
    } finally { setValidating(false); }
  };
  return <Dialog open onOpenChange={open => !open && !validating && onClose()}>
    <DialogContent className="flex max-h-[90dvh] max-w-md flex-col overflow-hidden"><DialogHeader className="shrink-0"><DialogTitle>Corregir medio de recaudo</DialogTitle>
      <DialogDescription>{item.documentNumber} · {movementLabel}. {applicationNotice}
        {electronic && " La factura electrónica emitida se conserva; esta es una corrección interna del recaudo."}
      </DialogDescription></DialogHeader>
      <div className="min-h-0 space-y-4 overflow-y-auto py-2 pr-1">
        {(paymentTypes.isError || (isCard && cardFranchises.isError)) && <div role="alert" className="rounded-xl border border-red-200 bg-red-50 p-3 text-sm text-red-800">
          No fue posible consultar los tipos de pago o las franquicias.
          <Button variant="link" disabled={validating} onClick={() => {
            if (paymentTypes.isError) void paymentTypes.refetch();
            if (isCard && cardFranchises.isError) void cardFranchises.refetch();
          }}>Reintentar</Button>
        </div>}
        <p className="rounded-xl bg-slate-50 p-3 text-sm">Registrado: <strong>{originalPaymentName ?? workSessionPaymentMethodName(originalTender)} · {formatCurrency(Math.abs(item.amount), "COP", 4)}</strong></p>
        <div className="space-y-1.5"><Label>Tipo de pago real</Label>
          <Select value={tender} onValueChange={changeType} disabled={validating}><SelectTrigger aria-label="Tipo de pago real"><SelectValue placeholder="Selecciona el tipo" /></SelectTrigger>
            <SelectContent>{available.map(option => <SelectItem key={option.code} value={option.code}>{option.label}</SelectItem>)}</SelectContent>
          </Select>
        </div>
        {tender && <div className="space-y-1.5"><Label>Forma de pago</Label>
          <PaymentMethodSelect key={tender} tenantId={tenant} businessId={businessId} usage={usage} methodCode={tender}
            value={form?.id ?? ""} name={form?.name} accountLabel={form?.account} disabled={validating}
            ariaLabel="Forma de pago del recaudo" autoSelectSingle wrapLabels onChange={chooseForm} />
        </div>}
        {isCard && <><div className="space-y-1.5"><Label>Franquicia</Label>
          <Select value={franchise} onValueChange={setFranchise} disabled={validating}><SelectTrigger aria-label="Franquicia"><SelectValue placeholder="Selecciona la franquicia" /></SelectTrigger>
            <SelectContent>{(cardFranchises.data ?? []).map(option => <SelectItem key={option.code} value={option.code}>{option.label}</SelectItem>)}</SelectContent>
          </Select></div><div className="space-y-1.5"><Label htmlFor="closure-correction-approval">Número de aprobación</Label>
          <Input ref={evidenceInput} data-payment-evidence id="closure-correction-approval" disabled={validating} value={approval} onChange={event => setApproval(event.target.value)} maxLength={approvalMaxLength} />
        </div></>}
        {tender === "Transfer" && <div className="space-y-1.5"><Label htmlFor="closure-correction-reference">Referencia de transferencia</Label>
          <Input ref={evidenceInput} data-payment-evidence id="closure-correction-reference" disabled={validating} value={reference} onChange={event => setReference(event.target.value)} maxLength={referenceMaxLength} />
        </div>}
        <div className="space-y-1.5"><Label htmlFor="closure-correction-amount">Valor del recaudo</Label>
          <Input id="closure-correction-amount" readOnly disabled value={formatCurrency(Math.abs(amount), "COP", 4)} />
          <p className="text-xs text-muted-foreground">{item.movementType === "CreditSale" ? "Se recauda únicamente el saldo disponible. Los abonos anteriores se conservan." : "La corrección del medio conserva el importe del recaudo."}</p>
        </div>
        <div className="space-y-1.5"><Label htmlFor="closure-correction-reason">Motivo de la corrección</Label>
          <Textarea id="closure-correction-reason" disabled={validating} value={reason} onChange={event => setReason(event.target.value)} maxLength={500} />
        </div>
      </div>
      {validationError && <p role="alert" className="shrink-0 rounded-xl border border-red-200 bg-red-50 p-3 text-sm text-red-800">{validationError}</p>}
      <div className="flex shrink-0 justify-end gap-2">{current && <Button variant="ghost" className="mr-auto" disabled={validating} onClick={onRemove}>Quitar corrección</Button>}
        <Button variant="outline" disabled={validating} onClick={onClose}>Cerrar</Button>
        <Button disabled={!valid || validating} onClick={() => void apply()}>{validating ? "Validando…" : submitLabel}</Button>
      </div>
    </DialogContent>
  </Dialog>;
}
