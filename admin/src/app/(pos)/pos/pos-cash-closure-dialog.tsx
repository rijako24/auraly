"use client";

import { useMemo, useState } from "react";
import { Calculator, Coins, LockKeyhole } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import type { PosAuthorizedClosurePreview, PosClient, PosWorkSessionPaymentCount } from "@/services/pos/pos-edge-client";
import { formatWorkSessionCountInput, normalizeWorkSessionCountInput, workSessionPaymentMethodName } from "@/services/pos/pos-work-session-close";
import { usePosReferenceOptions } from "./use-pos-reference-options";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });

export function PosCashClosureDialog({ client, value, busy, submitted, onClose, onConfirm }: {
  client: PosClient;
  value: PosAuthorizedClosurePreview;
  busy: boolean;
  submitted: boolean;
  onClose: () => void;
  onConfirm: (paymentCounts: PosWorkSessionPaymentCount[], note: string | null) => Promise<void>;
}) {
  const countablePayments = useMemo(
    () => value.preview.paymentTotals.filter((payment) => payment.requiresCount),
    [value.preview.paymentTotals],
  );
  const [counted, setCounted] = useState<Record<string, string>>(() =>
    Object.fromEntries(countablePayments.map((payment) => [payment.paymentMethodCode, ""])),
  );
  const [note, setNote] = useState("");
  const [calculatorOpen, setCalculatorOpen] = useState(false);
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
              <p className="text-xs text-slate-500">Registra el valor realmente recibido en efectivo, tarjeta y transferencia.</p>
            </div>
            <div className="grid gap-4 p-4 sm:grid-cols-2">
              {countablePayments.map((payment, index) => (
                <div className="space-y-2" key={payment.paymentMethodCode}>
                  <Label htmlFor={`count-${payment.paymentMethodCode}`}>{workSessionPaymentMethodName(payment.paymentMethodCode)}</Label>
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
                  {payment.paymentMethodCode === "Cash" && (
                    <Button type="button" variant="outline" className="w-full gap-2" disabled={busy || submitted} onClick={() => setCalculatorOpen(true)}>
                      <Calculator className="h-4 w-4" /> Contar por denominaciones
                    </Button>
                  )}
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
            {busy ? "Cerrando e imprimiendo…" : submitted ? "Reintentar cierre" : "Cerrar sesión de venta"}
          </Button>
        </footer>
      </DialogContent>
      {calculatorOpen && (
        <CashCountCalculator
          client={client}
          onCancel={() => setCalculatorOpen(false)}
          onLoad={(amount) => {
            setCounted((current) => ({ ...current, Cash: String(amount) }));
            setCalculatorOpen(false);
          }}
        />
      )}
    </Dialog>
  );
}

function CashCountCalculator({ client, onCancel, onLoad }: { client: PosClient; onCancel: () => void; onLoad: (amount: number) => void }) {
  const denominations = usePosReferenceOptions(client, "cash-denomination");
  const [quantities, setQuantities] = useState<Record<string, string>>({});
  const rows = denominations.data ?? [];
  const total = rows.reduce((sum, denomination) => {
    const value = Number(denomination.code);
    const quantity = Number(quantities[denomination.code] ?? 0);
    return sum + (Number.isFinite(value * quantity) ? value * quantity : 0);
  }, 0);

  return (
    <Dialog open onOpenChange={(open) => !open && onCancel()}>
      <DialogContent className="max-h-[90vh] max-w-2xl overflow-y-auto p-0">
        <DialogHeader className="border-b bg-gradient-to-r from-teal-50 to-cyan-50 px-6 py-5 text-left">
          <div className="flex items-center gap-3">
            <span className="grid h-11 w-11 place-items-center rounded-2xl bg-teal-600 text-white shadow-sm"><Coins className="h-5 w-5" /></span>
            <div><DialogTitle>Te ayudamos a contar</DialogTitle><DialogDescription>Indica cuántas monedas y billetes tienes. Auraly hará la suma por ti.</DialogDescription></div>
          </div>
        </DialogHeader>
        <div className="space-y-5 p-6">
          {denominations.isLoading && <p className="text-sm text-slate-500">Cargando denominaciones…</p>}
          {denominations.isError && <p className="rounded-xl border border-red-200 bg-red-50 p-3 text-sm text-red-800">No fue posible cargar las denominaciones configuradas.</p>}
          {(["Billete", "Moneda"] as const).map((kind) => {
            const group = rows.filter((item) => item.description === kind);
            if (!group.length) return null;
            return <section key={kind} className="space-y-3"><h3 className="text-sm font-bold uppercase tracking-wide text-slate-500">{kind}s</h3><div className="grid gap-3 sm:grid-cols-2">{group.map((denomination) => {
              const quantity = quantities[denomination.code] ?? "";
              const subtotal = Number(denomination.code) * Number(quantity || 0);
              return <label key={denomination.id} className="flex items-center gap-3 rounded-2xl border bg-white p-3 shadow-sm transition focus-within:border-teal-500 focus-within:ring-2 focus-within:ring-teal-100"><span className="min-w-20 font-bold text-slate-800">{denomination.label}</span><span className="text-slate-400">×</span><Input aria-label={`Cantidad de ${denomination.label}`} inputMode="numeric" value={quantity} onChange={(event) => setQuantities((current) => ({ ...current, [denomination.code]: normalizeWorkSessionCountInput(event.target.value) }))} className="h-10 w-20 text-center font-bold" placeholder="0"/><strong className="ml-auto text-sm tabular-nums text-teal-800">{money.format(subtotal)}</strong></label>;
            })}</div></section>;
          })}
          <div className="sticky bottom-0 flex items-center justify-between rounded-2xl bg-slate-950 p-5 text-white shadow-xl"><span><small className="block text-slate-300">Efectivo contado</small><strong className="text-sm">Total calculado</strong></span><strong className="text-3xl tabular-nums text-teal-300">{money.format(total)}</strong></div>
        </div>
        <footer className="flex justify-end gap-2 border-t bg-slate-50 px-6 py-4"><Button type="button" variant="outline" onClick={onCancel}>Cancelar</Button><Button type="button" disabled={!rows.length} onClick={() => onLoad(total)}>Cargar efectivo</Button></footer>
      </DialogContent>
    </Dialog>
  );
}
