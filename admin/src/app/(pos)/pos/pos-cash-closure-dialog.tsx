"use client";

import { useMemo, useState } from "react";
import { Banknote, Clock3, LockKeyhole, ReceiptText, RotateCcw, WalletCards } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import type { PosAuthorizedClosurePreview } from "@/services/pos/pos-edge-client";

const money = new Intl.NumberFormat("es-CO", {
  style: "currency",
  currency: "COP",
  maximumFractionDigits: 0,
});

function signedMoney(value: number) {
  if (value === 0) return money.format(0);
  return `${value > 0 ? "+" : "−"}${money.format(Math.abs(value))}`;
}

export function PosCashClosureDialog({
  value,
  busy,
  submitted,
  onClose,
  onConfirm,
}: {
  value: PosAuthorizedClosurePreview;
  busy: boolean;
  submitted: boolean;
  onClose: () => void;
  onConfirm: (countedCash: number, note: string | null) => Promise<void>;
}) {
  const [counted, setCounted] = useState("");
  const [note, setNote] = useState("");
  const [renderedAt] = useState(() => Date.now());
  const countedCash = Number(counted);
  const valid = counted.trim() !== "" && Number.isFinite(countedCash) && countedCash >= 0;
  const difference = useMemo(
    () => (valid ? countedCash - value.preview.expectedCash : null),
    [countedCash, valid, value.preview.expectedCash],
  );
  const opened = new Date(value.preview.openedAt);
  const elapsedMs = Math.max(0, renderedAt - opened.getTime());
  const days = Math.floor(elapsedMs / 86_400_000);
  const hours = Math.floor((elapsedMs % 86_400_000) / 3_600_000);

  return (
    <Dialog open onOpenChange={(open) => !open && !busy && !submitted && onClose()}>
      <DialogContent showClose={!busy && !submitted} className="max-h-[92vh] max-w-4xl overflow-y-auto p-0">
        <DialogHeader className="border-b bg-slate-50 px-6 py-5 text-left">
          <div className="flex items-start gap-3">
            <span className="grid h-11 w-11 place-items-center rounded-xl bg-teal-100 text-teal-800"><LockKeyhole className="h-5 w-5" /></span>
            <div>
              <DialogTitle>Cierre y arqueo de la sesión</DialogTitle>
              <DialogDescription className="mt-1">
                Revisa el consolidado, cuenta el efectivo y confirma. La sesión solo saldrá de venta cuando el cierre completo se haya impreso.
              </DialogDescription>
            </div>
          </div>
        </DialogHeader>

        <div className="space-y-5 px-6 py-5">
          <section className="rounded-xl border bg-slate-50 px-4 py-3 text-sm text-slate-700">
            <div className="grid gap-2 sm:grid-cols-3">
              <p><span className="block text-xs font-medium text-slate-500">Sede</span><strong>{value.preview.businessName}</strong></p>
              <p><span className="block text-xs font-medium text-slate-500">Bodega</span><strong>{value.preview.warehouseName}</strong></p>
              <p><span className="block text-xs font-medium text-slate-500">Cajero</span><strong>{value.preview.userName}</strong></p>
            </div>
          </section>

          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            <Summary icon={Clock3} label="Duración de la sesión" value={`${days} d · ${hours} h`} />
            <Summary icon={ReceiptText} label="Ventas" value={money.format(value.preview.totalSales)} />
            <Summary icon={RotateCcw} label="Devoluciones" value={money.format(value.preview.totalRefunds)} />
            <Summary icon={WalletCards} label="Otros movimientos" value={money.format(value.preview.totalOther)} />
            <Summary icon={ReceiptText} label="Neto consolidado" value={money.format(value.preview.netAmount)} />
            <Summary icon={Banknote} label="Efectivo esperado" value={money.format(value.preview.expectedCash)} />
          </div>

          <section className="overflow-hidden rounded-xl border">
            <div className="border-b bg-slate-50 px-4 py-3">
              <h3 className="font-semibold">Acumulado por medio de pago</h3>
              <p className="text-xs text-slate-500">Desde {opened.toLocaleString("es-CO")} hasta ahora.</p>
            </div>
            <div className="divide-y">
              {value.preview.paymentTotals.map((payment) => (
                <div key={payment.paymentMethodCode} className="grid grid-cols-[1fr_auto] gap-4 px-4 py-3 text-sm">
                  <div>
                    <strong>{payment.paymentMethodCode}</strong>
                    <p className="mt-0.5 text-xs text-slate-500">
                      Ventas {money.format(payment.salesAmount)} · Devoluciones {money.format(payment.refundAmount)} · Otros {money.format(payment.otherAmount)}
                    </p>
                  </div>
                  <strong className="tabular-nums">{money.format(payment.netAmount)}</strong>
                </div>
              ))}
              {!value.preview.paymentTotals.length && <p className="px-4 py-5 text-sm text-slate-500">No hay movimientos en esta sesión.</p>}
            </div>
          </section>

          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="counted-cash">Efectivo físico contado</Label>
              <Input id="counted-cash" autoFocus type="number" min="0" step="1" value={counted} disabled={busy || submitted} onChange={(event) => setCounted(event.target.value)} placeholder="0" className="h-12 text-lg font-bold" />
            </div>
            <div className={`rounded-xl border p-4 ${difference === null ? "bg-slate-50" : difference === 0 ? "border-emerald-200 bg-emerald-50" : difference > 0 ? "border-sky-200 bg-sky-50" : "border-red-200 bg-red-50"}`}>
              <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Diferencia</p>
              <p className="mt-1 text-2xl font-black tabular-nums">{difference === null ? "—" : signedMoney(difference)}</p>
              {difference !== null && <p className="mt-1 text-xs font-medium">{difference > 0 ? "Diferencia positiva · sobrante" : difference < 0 ? "Diferencia negativa · faltante" : "Conteo cuadrado · sin diferencia"}</p>}
            </div>
          </div>

          <div className="space-y-2">
            <Label htmlFor="closure-note">Observación (opcional)</Label>
            <Textarea id="closure-note" value={note} disabled={busy || submitted} onChange={(event) => setNote(event.target.value)} maxLength={500} placeholder="Explica una diferencia o deja una nota para auditoría." />
          </div>

          {submitted && (
            <p className="rounded-xl border border-amber-200 bg-amber-50 p-3 text-sm font-medium text-amber-900">
              El cierre ya fue enviado y el conteo quedó bloqueado. Corrige la impresora si es necesario y reintenta hasta obtener el arqueo impreso.
            </p>
          )}
        </div>

        <footer className="flex justify-end gap-2 border-t bg-slate-50 px-6 py-4">
          <Button type="button" variant="outline" disabled={busy || submitted} onClick={onClose}>Cancelar</Button>
          <Button type="button" disabled={busy || !valid} onClick={() => void onConfirm(countedCash, note.trim() || null)}>
            {busy ? "Cerrando e imprimiendo…" : submitted ? "Reintentar impresión del arqueo" : "Confirmar cierre e imprimir arqueo"}
          </Button>
        </footer>
      </DialogContent>
    </Dialog>
  );
}

function Summary({ icon: Icon, label, value }: { icon: typeof Clock3; label: string; value: string }) {
  return <div className="rounded-xl border bg-white p-4"><Icon className="h-5 w-5 text-teal-700" /><p className="mt-3 text-xs font-medium text-slate-500">{label}</p><strong className="mt-1 block text-lg tabular-nums">{value}</strong></div>;
}
