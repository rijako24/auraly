"use client";

import { useState, type ReactNode } from "react";
import { Pencil, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { PaymentMethodSelect } from "@/components/payments/payment-method-select";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import { useReferenceOptions } from "@/hooks/use-reference-options";
import { dispatchesApi, type DeliveryPayment, type DispatchExecution, type DispatchPaymentReviewResult } from "@/services/api/dispatches";
import type { PaymentMethod } from "@/services/api/payment-methods";
import { useTenantContextStore } from "@/stores/tenant-context-store";
import { useBusinessContextStore } from "@/stores/business-context-store";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });
const groupOf = (code: string | null) => code === "Cash" ? "Cash" : code === "DebitCard" || code === "CreditCard" ? "Card" : "Transfer";
const methodName = (code: string | null) => code === "Cash" ? "Efectivo" : code === "DebitCard" ? "Tarjeta débito" : code === "CreditCard" ? "Tarjeta crédito" : "Transferencia";

export function DispatchPaymentGroups({ data, editable, cashContent, onChanged }: {
  data: DispatchExecution; editable: boolean; cashContent?: ReactNode;
  onChanged: (result: DispatchPaymentReviewResult) => void;
}) {
  const [busy, setBusy] = useState<string | null>(null);
  const [correcting, setCorrecting] = useState<DeliveryPayment | null>(null);
  const [image, setImage] = useState<string | null>(null);
  const payments = data.documents.flatMap(document => document.payments
    .filter(payment => payment.applicationType !== "CreditDocument")
    .map(payment => ({ document, payment })));
  const verify = async (payment: DeliveryPayment, value: boolean) => {
    setBusy(payment.paymentId);
    try { onChanged(await dispatchesApi.verifyPayment(data.dispatchId, payment.paymentId, value, payment.correctedAt ?? null)); }
    catch (error) { toast.error(error instanceof Error ? error.message : "No fue posible chequear el pago."); }
    finally { setBusy(null); }
  };
  return <section className="space-y-3" aria-label="Recaudos por forma de pago">
    {(["Cash", "Card", "Transfer"] as const).map(group => {
      const rows = payments.filter(row => groupOf(row.payment.paymentMethod) === group);
      const total = rows.reduce((sum, row) => sum + row.payment.amount, 0);
      const verified = rows.filter(row => row.payment.verifiedAt).length;
      const expected = group === "Cash" ? data.settlement?.expectedCash ?? total : total;
      const counted = group === "Cash" ? data.settlement?.declaredCash : group === "Card" ? data.settlement?.declaredCard : data.settlement?.declaredTransfer;
      return <details key={group} open={group === "Cash"} className="rounded-2xl border bg-card">
        <summary className="cursor-pointer list-none p-4">
          <div className="flex flex-wrap items-center justify-between gap-2"><strong>{group === "Cash" ? "Efectivo" : group === "Card" ? "Tarjetas" : "Transferencias"} · {rows.length} {rows.length === 1 ? "factura" : "facturas"}</strong><strong>{money.format(total)}</strong></div>
          <dl className="mt-3 grid grid-cols-3 gap-2 text-xs sm:text-sm">
            <div><dt className="text-muted-foreground">Esperado</dt><dd className="mt-1 font-semibold tabular-nums">{money.format(expected)}</dd></div>
            <div><dt className="text-muted-foreground">Declarado</dt><dd className="mt-1 font-semibold tabular-nums">{counted == null ? "No declarado" : money.format(counted)}</dd></div>
            <div><dt className="text-muted-foreground">Diferencia declarada</dt><dd className={`mt-1 font-semibold tabular-nums ${counted != null && counted !== expected ? "text-amber-800" : "text-muted-foreground"}`}>{counted == null ? "Sin declaración" : money.format(counted - expected)}</dd></div>
          </dl>
          {group !== "Cash" && <small className="mt-2 block text-muted-foreground">{verified} de {rows.length} pagos chequeados</small>}
        </summary>
        <div className="space-y-3 border-t p-3 sm:p-4">
          {group === "Cash" && cashContent}
          {!rows.length && <p className="text-sm text-muted-foreground">No hay recaudos en este grupo.</p>}
          {rows.map(({ document, payment }) => <article key={payment.paymentId} className="space-y-3 rounded-xl border p-3">
            <div className="flex items-start gap-3">
              <div className="min-w-0 flex-1 text-sm"><strong className="block">{document.documentNumber} · {document.customerName}</strong>
                <span className="block text-muted-foreground">{payment.formName ?? methodName(payment.paymentMethod)}</span>
                {payment.reference && <small className="block">Referencia: {payment.reference}</small>}
                {payment.cardFranchiseCode && <small className="block">{payment.cardFranchiseCode} · Aprobación {payment.approvalNumber}</small>}
              </div>
              <strong className="text-sm tabular-nums">{money.format(payment.amount)}</strong>
              {busy === payment.paymentId && <Loader2 className="h-4 w-4 animate-spin"/>}
            </div>
            {group !== "Cash" && <div className="flex flex-wrap items-center gap-2">
              <Button size="sm" variant={payment.verifiedAt ? "default" : "outline"} disabled={!editable || busy !== null}
                aria-label={`Verificado ${document.documentNumber} ${payment.formName ?? methodName(payment.paymentMethod)}`}
                onClick={() => void verify(payment, true)}>Verificado</Button>
              <Button size="sm" variant="outline" disabled={!editable || busy !== null}
                aria-label={`No encontrado ${document.documentNumber} ${payment.formName ?? methodName(payment.paymentMethod)}`}
                onClick={() => void verify(payment, false)}>No encontrado</Button>
            </div>}
            {editable && <Button size="sm" variant="outline" aria-label={`Corregir forma de pago de ${document.documentNumber}`} disabled={busy !== null} onClick={() => setCorrecting(payment)}><Pencil className="mr-2 h-4 w-4"/>Corregir</Button>}
            {payment.correctionReason && <p className="rounded-lg bg-amber-50 p-2 text-xs text-amber-950">Original: {payment.originalFormName ?? methodName(payment.originalPaymentMethod ?? null)}. Corrección: {payment.correctionReason}</p>}
            {payment.evidenceUrl && <button type="button" className="flex items-center gap-3 rounded-xl border p-2 text-left text-sm" onClick={() => setImage(payment.evidenceUrl)}>
              {/* The saved URL is reused directly; no per-row signing or blob lookup. */}
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img src={payment.evidenceUrl} alt={`Soporte de ${document.documentNumber}`} loading="lazy" decoding="async" className="h-16 w-20 rounded-lg object-cover"/>
              <span>Ampliar soporte</span>
            </button>}
          </article>)}
        </div>
      </details>;
    })}
    {correcting && <CorrectionDialog dispatchId={data.dispatchId} payment={correcting}
      onClose={() => setCorrecting(null)} onSaved={result => { onChanged(result); setCorrecting(null); }}/>}
    {image && <Dialog open onOpenChange={open => { if (!open) setImage(null); }}><DialogContent className="max-h-[94dvh] max-w-4xl overflow-y-auto"><DialogHeader><DialogTitle>Soporte del pago</DialogTitle><DialogDescription>Imagen adjunta al recaudo del despacho.</DialogDescription></DialogHeader>
      {/* eslint-disable-next-line @next/next/no-img-element */}
      <img src={image} alt="Soporte ampliado del pago" className="max-h-[75dvh] w-full object-contain"/>
    </DialogContent></Dialog>}
  </section>;
}

function CorrectionDialog({ dispatchId, payment, onClose, onSaved }: {
  dispatchId: string; payment: DeliveryPayment; onClose: () => void; onSaved: (result: DispatchPaymentReviewResult) => void;
}) {
  const tenant = useTenantContextStore(state => state.selectedTenantId);
  const business = useBusinessContextStore(state => state.selectedBusinessId);
  const [form, setForm] = useState<PaymentMethod | null>(null);
  const [reference, setReference] = useState("");
  const [franchise, setFranchise] = useState("");
  const [approval, setApproval] = useState("");
  const [reason, setReason] = useState("");
  const [saving, setSaving] = useState(false);
  const card = form?.methodCode === "DebitCard" || form?.methodCode === "CreditCard";
  const franchises = useReferenceOptions("card-franchise", card);
  const valid = form && reason.trim() && (!card || franchise && approval.trim()) && (form.methodCode !== "Transfer" || reference.trim());
  const save = async () => {
    if (!form || !valid) return;
    setSaving(true);
    try { onSaved(await dispatchesApi.correctPayment(dispatchId, payment.paymentId, {
      paymentMethodId: form.paymentMethodId, paymentMethodVersion: form.version, methodCode: form.methodCode,
      reference: form.methodCode === "Transfer" ? reference.trim() : null,
      cardFranchiseCode: card ? franchise : null, approvalNumber: card ? approval.trim() : null,
      reason: reason.trim(), expectedCorrectedAt: payment.correctedAt ?? null,
    })); } catch (error) { toast.error(error instanceof Error ? error.message : "No fue posible corregir la forma de pago."); }
    finally { setSaving(false); }
  };
  return <Dialog open onOpenChange={open => { if (!open && !saving) onClose(); }}><DialogContent className="max-h-[94dvh] max-w-lg overflow-y-auto">
    <DialogHeader><DialogTitle>Corregir forma de pago</DialogTitle><DialogDescription>El importe se conserva: {money.format(payment.amount)}.</DialogDescription></DialogHeader>
    <div className="space-y-4">
      <PaymentMethodSelect tenantId={tenant} businessId={business ?? ""} usage="Receipts" excludeMethodCode="Credit"
        value={form?.paymentMethodId ?? ""} name={form?.name} ariaLabel="Nueva forma de pago del despacho" disabled={saving}
        onChange={value => { setForm(value); setReference(""); setFranchise(""); setApproval(""); }}/>
      {form && <p className="rounded-xl bg-muted/50 p-3 text-sm">Cuenta configurada: {form.accountCode} · {form.accountName}</p>}
      {form?.methodCode === "Transfer" && <div className="space-y-2"><Label htmlFor="dispatch-correction-reference">Referencia de transferencia</Label><Input id="dispatch-correction-reference" maxLength={120} value={reference} onChange={event => setReference(event.target.value)}/></div>}
      {card && <><div className="space-y-2"><Label>Franquicia</Label><Select value={franchise} onValueChange={setFranchise}><SelectTrigger aria-label="Franquicia de la corrección"><SelectValue placeholder="Selecciona la franquicia"/></SelectTrigger><SelectContent>{franchises.data?.map(option => <SelectItem key={option.code} value={option.code}>{option.label}</SelectItem>)}</SelectContent></Select></div><div className="space-y-2"><Label htmlFor="dispatch-correction-approval">Número de aprobación</Label><Input id="dispatch-correction-approval" maxLength={80} value={approval} onChange={event => setApproval(event.target.value)}/></div></>}
      <div className="space-y-2"><Label htmlFor="dispatch-correction-reason">Motivo de la corrección</Label><Textarea id="dispatch-correction-reason" maxLength={500} value={reason} onChange={event => setReason(event.target.value)}/></div>
    </div>
    <div className="flex justify-end gap-2"><Button variant="outline" disabled={saving} onClick={onClose}>Cancelar</Button><Button disabled={saving || !valid} onClick={() => void save()}>{saving ? "Guardando…" : "Guardar corrección"}</Button></div>
  </DialogContent></Dialog>;
}
