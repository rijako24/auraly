"use client";

import { useMemo, useState } from "react";
import { Calculator, LockKeyhole } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import type { PosAuthorizedClosurePreview, PosWorkSessionPaymentCount } from "@/services/pos/pos-edge-client";
import { formatWorkSessionCountInput, normalizeWorkSessionCountInput, workSessionPaymentMethodName } from "@/services/pos/pos-work-session-close";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });

export function PosCashClosureDialog({ value, busy, submitted, onClose, onConfirm, onOpenDenominations }: {
  value: PosAuthorizedClosurePreview;
  busy: boolean;
  submitted: boolean;
  onClose: () => void;
  onConfirm: (paymentCounts: PosWorkSessionPaymentCount[], note: string | null) => Promise<void>;
  onOpenDenominations?: () => void;
}) {
  const countablePayments = useMemo(
    () => value.preview.paymentTotals.filter((payment) => payment.requiresCount),
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
    <Dialog open onOpenChange={(open) => !open && !busy && onClose()}>
      <DialogContent showClose={!busy} className="max-h-[92vh] max-w-3xl overflow-y-auto p-0">
        <DialogHeader className="border-b bg-slate-50 px-6 py-5 text-left">
          <div className="flex items-start gap-3">
            <span className="grid h-11 w-11 place-items-center rounded-xl bg-teal-100 text-teal-800"><LockKeyhole className="h-5 w-5" /></span>
            <div>
              <DialogTitle>Cerrar sesión operativa</DialogTitle>
              <DialogDescription className="mt-1">
                Conteo ciego: registra efectivo, tarjetas y transferencias. Los valores del sistema se revelan únicamente en el comprobante final.
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

          {onOpenDenominations && (
            <section className="flex flex-col gap-4 rounded-xl border border-teal-200 bg-gradient-to-r from-teal-50 to-cyan-50 p-4 sm:flex-row sm:items-center sm:justify-between">
              <div className="flex items-center gap-3">
                <span className="grid h-11 w-11 shrink-0 place-items-center rounded-xl bg-teal-600 text-white shadow-sm">
                  <Calculator className="h-5 w-5" />
                </span>
                <div>
                  <h3 className="font-semibold text-slate-900">Te ayudamos a contar el efectivo</h3>
                  <p className="text-sm text-slate-600">Suma monedas y billetes antes de registrar el valor contado.</p>
                </div>
              </div>
              <Button
                type="button"
                variant="outline"
                disabled={busy || submitted}
                onClick={onOpenDenominations}
                aria-keyshortcuts="Control+D"
                className="shrink-0 border-teal-300 bg-white text-teal-800 hover:bg-teal-100 hover:text-teal-950"
              >
                <Calculator className="mr-2 h-4 w-4" />
                Contar por denominaciones
                <kbd className="ml-2 hidden text-[10px] opacity-70 sm:inline">Ctrl+D</kbd>
              </Button>
            </section>
          )}

          <section className="overflow-hidden rounded-xl border">
            <div className="border-b bg-slate-50 px-4 py-3">
              <h3 className="font-semibold">Valores contados</h3>
              <p className="text-xs text-slate-500">Registra el valor realmente recibido en efectivo, tarjeta y transferencia.</p>
            </div>
            <div className="space-y-3 p-4">
              {countablePayments.map((payment, index) => (
                <div className="grid gap-2 rounded-xl border bg-white p-3 sm:grid-cols-[minmax(0,1fr)_minmax(220px,0.7fr)] sm:items-center sm:gap-4" key={payment.paymentMethodCode}>
                  <Label className="font-semibold text-slate-800" htmlFor={`count-${payment.paymentMethodCode}`}>{workSessionPaymentMethodName(payment.paymentMethodCode)}</Label>
                  <Input
                    id={`count-${payment.paymentMethodCode}`}
                    autoFocus={index === 0}
                    type="text"
                    inputMode="numeric"
                    value={formatWorkSessionCountInput(counted[payment.paymentMethodCode] ?? "")}
                    disabled={busy || submitted}
                    onChange={(event) => setCounted((current) => ({ ...current, [payment.paymentMethodCode]: normalizeWorkSessionCountInput(event.target.value) }))}
                    placeholder="0"
                    className="h-12 text-lg font-bold"
                  />
                </div>
              ))}
              {!countablePayments.length && <p className="text-sm text-slate-500">La sesión no tiene efectivo ni tarjetas para contar.</p>}
            </div>
          </section>

          <div className="flex items-center justify-between rounded-xl border border-teal-100 bg-teal-50 p-4">
            <span><strong className="block">Total digitado</strong><small className="text-teal-800">Conteo ciego: no revela el valor esperado.</small></span>
            <strong className="text-2xl tabular-nums text-teal-950">{money.format(totalDigitized)}</strong>
          </div>

          <div className="space-y-2">
            <Label htmlFor="closure-note">Observación (opcional)</Label>
            <Textarea id="closure-note" value={note} disabled={busy || submitted} onChange={(event) => setNote(event.target.value)} maxLength={500} placeholder="Novedad identificada durante el conteo." />
          </div>

          {submitted && <p className="rounded-xl border border-amber-200 bg-amber-50 p-3 text-sm font-medium text-amber-900">El conteo quedó protegido para que puedas reintentar sin modificarlo.</p>}
        </div>

        <footer className="flex flex-wrap justify-end gap-2 border-t bg-slate-50 px-6 py-4">
          <Button type="button" variant="outline" disabled={busy} onClick={onClose}>Cancelar</Button>
          <Button type="button" disabled={busy || !valid} onClick={() => void onConfirm(paymentCounts, note.trim() || null)}>
            {busy ? "Cerrando e imprimiendo…" : submitted ? "Reintentar cierre" : "Cerrar sesión operativa"}
          </Button>
        </footer>
      </DialogContent>
    </Dialog>
  );
}
