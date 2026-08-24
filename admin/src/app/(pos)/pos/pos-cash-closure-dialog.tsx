"use client";

import { useMemo, useState } from "react";
import { LockKeyhole } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import type { PosAuthorizedClosurePreview, PosWorkSessionPaymentCount } from "@/services/pos/pos-edge-client";
import { workSessionPaymentMethodName, workSessionPaymentMethodRequiresCount } from "@/services/pos/pos-work-session-close";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });

export function PosCashClosureDialog({ value, busy, submitted, onClose, onConfirm }: {
  value: PosAuthorizedClosurePreview;
  busy: boolean;
  submitted: boolean;
  onClose: () => void;
  onConfirm: (paymentCounts: PosWorkSessionPaymentCount[], note: string | null) => Promise<void>;
}) {
  const countablePayments = useMemo(
    () => value.preview.paymentTotals.filter((payment) => workSessionPaymentMethodRequiresCount(payment.paymentMethodCode)),
    [value.preview.paymentTotals],
  );
  const [counted, setCounted] = useState<Record<string, string>>(() =>
    Object.fromEntries(countablePayments.map((payment) => [payment.paymentMethodCode, ""])),
  );
  const [note, setNote] = useState("");
  const paymentCounts = useMemo(() => countablePayments.map((payment) => ({
    paymentMethodCode: payment.paymentMethodCode,
    countedAmount: Number(counted[payment.paymentMethodCode]),
  })), [countablePayments, counted]);
  const valid = paymentCounts.every((payment) =>
    counted[payment.paymentMethodCode]?.trim() !== "" && Number.isFinite(payment.countedAmount) && payment.countedAmount >= 0,
  );
  const totalDigitized = paymentCounts.reduce(
    (sum, payment) => sum + (Number.isFinite(payment.countedAmount) ? payment.countedAmount : 0), 0,
  );

  return (
    <Dialog open onOpenChange={(open) => !open && !busy && !submitted && onClose()}>
      <DialogContent showClose={!busy && !submitted} className="max-h-[92vh] max-w-3xl overflow-y-auto p-0">
        <DialogHeader className="border-b bg-slate-50 px-6 py-5 text-left">
          <div className="flex items-start gap-3">
            <span className="grid h-11 w-11 place-items-center rounded-xl bg-teal-100 text-teal-800"><LockKeyhole className="h-5 w-5" /></span>
            <div>
              <DialogTitle>Cerrar sesión de venta</DialogTitle>
              <DialogDescription className="mt-1">
                Conteo ciego: registra efectivo y tarjetas. Los valores del sistema se revelan únicamente en el comprobante final.
              </DialogDescription>
            </div>
          </div>
        </DialogHeader>

        <div className="space-y-5 px-6 py-5">
          <section className="rounded-xl border bg-slate-50 px-4 py-3 text-sm text-slate-700">
            <div className="grid gap-2 sm:grid-cols-3">
              <p><span className="block text-xs font-medium text-slate-500">Sede</span><strong>{value.preview.businessName}</strong></p>
              <p><span className="block text-xs font-medium text-slate-500">Bodega</span><strong>{value.preview.warehouseName}</strong></p>
              <p><span className="block text-xs font-medium text-slate-500">Responsable</span><strong>{value.preview.userName}</strong></p>
            </div>
          </section>

          <section className="overflow-hidden rounded-xl border">
            <div className="border-b bg-slate-50 px-4 py-3">
              <h3 className="font-semibold">Valores contados</h3>
              <p className="text-xs text-slate-500">Las transferencias no se cuentan: se concilian automáticamente con el valor registrado.</p>
            </div>
            <div className="grid gap-4 p-4 sm:grid-cols-2">
              {countablePayments.map((payment, index) => (
                <div className="space-y-2" key={payment.paymentMethodCode}>
                  <Label htmlFor={`count-${payment.paymentMethodCode}`}>{workSessionPaymentMethodName(payment.paymentMethodCode)}</Label>
                  <Input
                    id={`count-${payment.paymentMethodCode}`}
                    autoFocus={index === 0}
                    type="number"
                    min="0"
                    step="1"
                    value={counted[payment.paymentMethodCode] ?? ""}
                    disabled={busy || submitted}
                    onChange={(event) => setCounted((current) => ({ ...current, [payment.paymentMethodCode]: event.target.value }))}
                    placeholder="0"
                    className="h-12 text-lg font-bold"
                  />
                </div>
              ))}
              {!countablePayments.length && <p className="text-sm text-slate-500">La sesión no tiene efectivo ni tarjetas para contar.</p>}
            </div>
          </section>

          <div className="flex items-center justify-between rounded-xl border border-teal-100 bg-teal-50 p-4">
            <span><strong className="block">Total digitado</strong><small className="text-teal-800">No incluye transferencias ni revela el total esperado.</small></span>
            <strong className="text-2xl tabular-nums text-teal-950">{money.format(totalDigitized)}</strong>
          </div>

          <div className="space-y-2">
            <Label htmlFor="closure-note">Observación (opcional)</Label>
            <Textarea id="closure-note" value={note} disabled={busy || submitted} onChange={(event) => setNote(event.target.value)} maxLength={500} placeholder="Novedad identificada durante el conteo." />
          </div>

          {submitted && <p className="rounded-xl border border-amber-200 bg-amber-50 p-3 text-sm font-medium text-amber-900">El cierre ya fue enviado. Si falló únicamente la impresión, vuelve a intentarlo sin cambiar el conteo.</p>}
        </div>

        <footer className="flex flex-wrap justify-end gap-2 border-t bg-slate-50 px-6 py-4">
          <Button type="button" variant="outline" disabled={busy || submitted} onClick={onClose}>Cancelar</Button>
          <Button type="button" disabled={busy || !valid} onClick={() => void onConfirm(paymentCounts, note.trim() || null)}>
            {busy ? "Cerrando e imprimiendo…" : "Cerrar sesión de venta"}
          </Button>
        </footer>
      </DialogContent>
    </Dialog>
  );
}
