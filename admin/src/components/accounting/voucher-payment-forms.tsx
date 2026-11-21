"use client";

import { useState } from "react";
import { useReferenceOptions } from "@/hooks/use-reference-options";
import { PaymentMethodSelect } from "@/components/payments/payment-method-select";
import { Button } from "@/components/ui/button";
import { FormattedNumberInput } from "@/components/ui/formatted-number-input";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import type { VoucherAppliedPaymentMethod, VoucherDraftLine, VoucherDraftLineView, VoucherPaymentEvidence } from "@/services/api/accounting";
import type { PaymentMethod } from "@/services/api/payment-methods";

export interface VoucherPaymentCapture {
  amount: number; reference: string | null; approvalNumber: string | null; cardFranchiseCode: string | null;
}

export function VoucherPaymentForms({ tenantId, businessId, lines, applied, direction = "incoming",
  disabled, readOnly, canAdd, addBlockReason, onSelect, onChange, onEvidence, onRemove }: {
  tenantId: string | null; businessId: string; direction?: "incoming" | "outgoing";
  lines: ReadonlyArray<{ line: VoucherDraftLineView; index: number }>;
  applied: ReadonlyMap<string, VoucherAppliedPaymentMethod>;
  disabled: boolean; readOnly: boolean; canAdd: boolean;
  addBlockReason?: string;
  onSelect: (method: PaymentMethod, index: number, capture?: VoucherPaymentCapture) => boolean;
  onChange: (index: number, patch: Partial<VoucherDraftLine>) => void;
  onEvidence: (index: number, patch: Partial<VoucherPaymentEvidence>) => void;
  onRemove: (index: number) => void;
}) {
  const outgoing=direction === "outgoing";
  const usage=outgoing?"SupplierPayments":"Receipts";
  // A candidate is not a document line until Add is explicitly pressed.
  const [candidate, setCandidate] = useState<PaymentMethod | null>(null);
  const [capture, setCapture] = useState<VoucherPaymentCapture>({ amount: 0, reference: null, approvalNumber: null, cardFranchiseCode: null });
  const candidateCard = candidate?.methodCode === "DebitCard" || candidate?.methodCode === "CreditCard";
  const hasCard = candidateCard || lines.some(({ line }) => line.value.paymentEvidence?.paymentMethodTypeCode === "DebitCard" || line.value.paymentEvidence?.paymentMethodTypeCode === "CreditCard");
  const franchises = useReferenceOptions("card-franchise", !readOnly && hasCard);
  const duplicate = !!candidate && lines.some(({ line }) => line.value.paymentEvidence?.paymentMethodId === candidate.paymentMethodId);
  const evidenceMissing = candidateCard ? !capture.approvalNumber?.trim() || !capture.cardFranchiseCode
    : candidate?.methodCode === "Transfer" && !capture.reference?.trim();
  return <section aria-label="Formas de pago del comprobante" className="space-y-4 rounded-2xl border bg-card p-5">
    <div><h2 className="font-semibold">Formas de pago</h2><p className="text-sm text-muted-foreground">
      {outgoing?"Selecciona de dónde sale el dinero y su valor.":"Selecciona dónde ingresa el dinero y su valor."} Cada forma se refleja en las partidas.</p></div>
    {lines.map(({ line, index }, ordinal) => {
      const evidence = line.value.paymentEvidence!;
      const used = applied.get(line.value.lineId!);
      const card = evidence.paymentMethodTypeCode === "DebitCard" || evidence.paymentMethodTypeCode === "CreditCard";
      const transfer = evidence.paymentMethodTypeCode === "Transfer";
      return <fieldset key={line.value.lineId} disabled={disabled || readOnly}
        aria-label={`Forma de pago ${ordinal + 1}`} className="grid min-w-0 grid-cols-1 gap-3 rounded-xl border p-4 sm:grid-cols-2">
        <div className="space-y-1"><Label>Forma de pago</Label>
          <PaymentMethodSelect tenantId={tenantId} businessId={businessId} usage={usage}
            value={evidence.paymentMethodId ?? ""} name={used?.name ?? evidence.paymentMethodName}
            accountLabel={`${used?.accountCode ?? line.accountCode} · ${used?.accountName ?? line.accountName}`}
            ariaLabel={`Forma de pago ${ordinal + 1}`} disabled={disabled || readOnly}
            onChange={method => onSelect(method, index)} />
          <p className="text-xs text-muted-foreground">Cuenta: {used?.accountCode ?? line.accountCode} · {used?.accountName ?? line.accountName}</p>
        </div>
        <div className="space-y-1"><Label>{outgoing?"Valor pagado":"Valor recibido"}</Label>
          <FormattedNumberInput kind="currency" ariaLabel={`${outgoing?"Valor pagado":"Valor recibido"} por la forma ${ordinal + 1}`}
            value={outgoing?line.value.credit:line.value.debit}
            onValueChange={amount => onChange(index, outgoing?{debit:0,credit:amount??0}:{debit:amount??0,credit:0})} />
        </div>
        {transfer && <div className="space-y-1"><Label htmlFor={`transfer-${line.value.lineId}`}>Referencia de transferencia</Label>
          <Input id={`transfer-${line.value.lineId}`} maxLength={100} value={line.value.reference ?? ""}
            onChange={event => onChange(index, { reference: event.target.value || null })} /></div>}
        {card && <><div className="space-y-1"><Label htmlFor={`approval-${line.value.lineId}`}>Número de aprobación</Label>
          <Input id={`approval-${line.value.lineId}`} maxLength={100} value={evidence.approvalNumber ?? ""}
            onChange={event => onEvidence(index, { approvalNumber: event.target.value || null })} /></div>
          <div className="space-y-1"><Label>Franquicia de tarjeta</Label>{readOnly ? <p>{evidence.cardFranchiseCode ?? "—"}</p> : <Select value={evidence.cardFranchiseCode ?? "none"}
            disabled={disabled || readOnly} onValueChange={value => onEvidence(index, { cardFranchiseCode: value === "none" ? null : value })}>
            <SelectTrigger aria-label={`Franquicia de la forma ${ordinal + 1}`}><SelectValue /></SelectTrigger>
            <SelectContent><SelectItem value="none">Selecciona una franquicia</SelectItem>
              {franchises.data?.map(item => <SelectItem key={item.code} value={item.code}>{item.label}</SelectItem>)}</SelectContent>
          </Select>}</div></>}
        {!evidence.paymentMethodTypeCode && <div className="space-y-1 text-sm sm:col-span-2">
          {line.value.reference && <p>Referencia capturada: {line.value.reference}</p>}
          {evidence.approvalNumber && <p>Aprobación capturada: {evidence.approvalNumber}</p>}
          {evidence.cardFranchiseCode && <p>Franquicia capturada: {evidence.cardFranchiseCode}</p>}
        </div>}
        <details className="sm:col-span-2"><summary className="cursor-pointer text-sm">Observación del pago (opcional)</summary>
          <Input aria-label={`Observación de la forma ${ordinal + 1}`} maxLength={500} value={evidence.notes ?? ""}
            onChange={event => onEvidence(index, { notes: event.target.value || null })} />
        </details>
        {!readOnly && <div className="sm:col-span-2"><Button type="button" size="sm" variant="ghost"
          onClick={() => onRemove(index)}>Quitar forma de pago {ordinal + 1}</Button></div>}
      </fieldset>;
    })}
    {!readOnly && addBlockReason && <p role="status" className="text-sm text-muted-foreground">{addBlockReason}</p>}
    {!readOnly && <fieldset disabled={disabled || !canAdd || !!addBlockReason} aria-label="Agregar forma de pago" className="grid min-w-0 grid-cols-1 gap-3 rounded-xl border p-4 sm:grid-cols-2">
      <div className="space-y-1"><Label>{lines.length ? "Otra forma de pago (opcional)" : "Forma de pago"}</Label>
      <PaymentMethodSelect tenantId={tenantId} businessId={businessId} usage={usage}
        value={candidate?.paymentMethodId ?? ""} name={candidate?.name}
        accountLabel={candidate ? `${candidate.accountCode} · ${candidate.accountName}` : null}
        ariaLabel="Seleccionar forma de pago" disabled={disabled || !canAdd || !!addBlockReason}
        onChange={method => {
          setCandidate(method);
          setCapture(previous => ({ amount: previous.amount, reference: null, approvalNumber: null, cardFranchiseCode: null }));
        }} />
      {candidate && <p className="text-xs text-muted-foreground">Cuenta: {candidate.accountCode} · {candidate.accountName}</p>}</div>
      <div className="space-y-1"><Label>{outgoing?"Valor pagado":"Valor recibido"}</Label>
        <FormattedNumberInput kind="currency" ariaLabel="Valor de la nueva forma de pago" value={capture.amount}
          onValueChange={amount => setCapture(previous => ({ ...previous, amount: amount ?? 0 }))} /></div>
      {candidate?.methodCode === "Transfer" && <div className="space-y-1"><Label htmlFor="new-payment-reference">Referencia de transferencia</Label>
        <Input id="new-payment-reference" maxLength={100} value={capture.reference ?? ""}
          onChange={event => setCapture(previous => ({ ...previous, reference: event.target.value || null }))} /></div>}
      {candidateCard && <><div className="space-y-1"><Label htmlFor="new-payment-approval">Número de aprobación</Label>
        <Input id="new-payment-approval" maxLength={100} value={capture.approvalNumber ?? ""}
          onChange={event => setCapture(previous => ({ ...previous, approvalNumber: event.target.value || null }))} /></div>
        <div className="space-y-1"><Label>Franquicia de tarjeta</Label>
          <Select value={capture.cardFranchiseCode ?? "none"} disabled={disabled || !canAdd || !!addBlockReason}
            onValueChange={value => setCapture(previous => ({ ...previous, cardFranchiseCode: value === "none" ? null : value }))}>
            <SelectTrigger aria-label="Franquicia de la nueva forma"><SelectValue /></SelectTrigger>
            <SelectContent><SelectItem value="none">Selecciona una franquicia</SelectItem>
              {franchises.data?.map(item => <SelectItem key={item.code} value={item.code}>{item.label}</SelectItem>)}
            </SelectContent>
          </Select></div></>}
      {duplicate && <p role="alert" className="text-sm text-destructive sm:col-span-2">La forma ya está en las partidas. Cambia su importe o selecciona otra forma.</p>}
      <div className="sm:col-span-2"><Button type="button" disabled={disabled || !canAdd || !!addBlockReason || !candidate || capture.amount <= 0 || duplicate || !!evidenceMissing}
        onClick={() => {
          if (candidate && onSelect(candidate, -1, capture)) {
            setCandidate(null); setCapture({ amount: 0, reference: null, approvalNumber: null, cardFranchiseCode: null });
          }
        }}>Agregar forma de pago</Button></div>
    </fieldset>}
    {!readOnly && hasCard && franchises.isError && <p role="alert" className="text-sm text-destructive">No se pudieron cargar las franquicias.
      <Button type="button" variant="link" onClick={() => void franchises.refetch()}>Reintentar franquicias</Button></p>}
  </section>;
}
