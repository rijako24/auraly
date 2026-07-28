"use client";

import { CreditCard, Loader2, Plus, Trash2 } from "lucide-react";
import { FormEvent, useMemo, useState } from "react";

import { PosPaymentInput } from "@/services/pos/pos-edge-client";

const money = new Intl.NumberFormat("es-CO", {
  style: "currency",
  currency: "COP",
  maximumFractionDigits: 0,
});

const methods = [
  { code: "Cash", label: "Efectivo" },
  { code: "DebitCard", label: "Tarjeta débito" },
  { code: "CreditCard", label: "Tarjeta crédito" },
  { code: "Transfer", label: "Transferencia" },
  { code: "Credit", label: "Crédito" },
];

type PaymentRow = PosPaymentInput & { id: string };

export function PosPaymentDialog({
  total,
  busy,
  onCancel,
  onConfirm,
}: {
  total: number;
  busy: boolean;
  onCancel: () => void;
  onConfirm: (payments: PosPaymentInput[]) => Promise<void>;
}) {
  const [payments, setPayments] = useState<PaymentRow[]>([
    { id: crypto.randomUUID(), methodCode: "Cash", amount: total, reference: null },
  ]);
  const paid = useMemo(
    () => payments.reduce((sum, payment) => sum + (Number(payment.amount) || 0), 0),
    [payments],
  );
  const difference = Math.round((total - paid) * 100) / 100;
  const balanced = Math.abs(difference) < 0.005 && payments.every((payment) => payment.amount > 0);

  function update(id: string, value: Partial<PaymentRow>) {
    setPayments((current) =>
      current.map((payment) => (payment.id === id ? { ...payment, ...value } : payment)),
    );
  }

  function addPayment() {
    const remaining = Math.max(0, Math.round(difference * 100) / 100);
    setPayments((current) => [
      ...current,
      {
        id: crypto.randomUUID(),
        methodCode: "DebitCard",
        amount: remaining,
        reference: null,
      },
    ]);
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!balanced || busy) return;
    await onConfirm(
      payments.map(({ methodCode, amount, reference }) => ({
        methodCode,
        amount,
        reference: reference?.trim() || null,
      })),
    );
  }

  return (
    <div className="fixed inset-0 z-50 grid place-items-center bg-slate-950/60 p-4">
      <form
        onSubmit={submit}
        className="w-full max-w-xl rounded-2xl bg-white p-5 shadow-2xl"
        aria-labelledby="pos-payment-title"
      >
        <div className="flex items-start justify-between gap-4">
          <div>
            <h2 id="pos-payment-title" className="flex items-center gap-2 text-xl font-semibold">
              <CreditCard className="h-5 w-5 text-teal-700" />
              Finalizar venta
            </h2>
            <p className="mt-1 text-sm text-slate-500">
              La factura se emitirá e imprimirá en la tirilla configurada.
            </p>
          </div>
          <p className="text-right">
            <span className="block text-xs uppercase tracking-wide text-slate-500">Total</span>
            <span className="text-2xl font-bold text-teal-800">{money.format(total)}</span>
          </p>
        </div>

        <div className="mt-5 space-y-3">
          {payments.map((payment, index) => (
            <div
              key={payment.id}
              className="grid gap-2 rounded-xl border border-slate-200 p-3 sm:grid-cols-[1fr_150px_1fr_42px]"
            >
              <label className="text-xs font-medium text-slate-600">
                Medio
                <select
                  autoFocus={index === 0}
                  value={payment.methodCode}
                  onChange={(event) => update(payment.id, { methodCode: event.target.value })}
                  className="mt-1 h-11 w-full rounded-lg border border-slate-300 bg-white px-3 text-sm outline-none focus:border-teal-600"
                >
                  {methods.map((method) => (
                    <option key={method.code} value={method.code}>
                      {method.label}
                    </option>
                  ))}
                </select>
              </label>
              <label className="text-xs font-medium text-slate-600">
                Valor
                <input
                  type="number"
                  min="0.01"
                  step="0.01"
                  value={payment.amount}
                  onChange={(event) =>
                    update(payment.id, { amount: event.currentTarget.valueAsNumber || 0 })
                  }
                  className="mt-1 h-11 w-full rounded-lg border border-slate-300 px-3 text-right font-semibold outline-none focus:border-teal-600"
                />
              </label>
              <label className="text-xs font-medium text-slate-600">
                Referencia
                <input
                  value={payment.reference ?? ""}
                  onChange={(event) => update(payment.id, { reference: event.target.value })}
                  className="mt-1 h-11 w-full rounded-lg border border-slate-300 px-3 text-sm outline-none focus:border-teal-600"
                  placeholder="Opcional"
                />
              </label>
              <button
                type="button"
                onClick={() =>
                  setPayments((current) => current.filter((item) => item.id !== payment.id))
                }
                disabled={payments.length === 1 || busy}
                className="mt-5 grid h-11 place-items-center rounded-lg text-slate-500 hover:bg-red-50 hover:text-red-700 disabled:opacity-30"
                aria-label="Eliminar medio de pago"
              >
                <Trash2 className="h-4 w-4" />
              </button>
            </div>
          ))}
        </div>

        <div className="mt-4 flex flex-wrap items-center justify-between gap-3">
          <button
            type="button"
            onClick={addPayment}
            disabled={busy}
            className="flex h-10 items-center gap-2 rounded-lg border border-slate-300 px-3 text-sm font-semibold"
          >
            <Plus className="h-4 w-4" />
            Agregar medio
          </button>
          <p className={`text-sm font-semibold ${balanced ? "text-emerald-700" : "text-amber-700"}`}>
            {balanced
              ? "Pago completo"
              : difference > 0
                ? `Faltan ${money.format(difference)}`
                : `Excede ${money.format(Math.abs(difference))}`}
          </p>
        </div>

        <div className="mt-5 flex justify-end gap-2 border-t border-slate-200 pt-4">
          <button
            type="button"
            onClick={onCancel}
            disabled={busy}
            className="h-11 rounded-lg border border-slate-300 px-5 font-medium"
          >
            Cancelar
          </button>
          <button
            type="submit"
            disabled={!balanced || busy}
            className="flex h-11 min-w-40 items-center justify-center gap-2 rounded-lg bg-teal-700 px-5 font-semibold text-white disabled:opacity-45"
          >
            {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <CreditCard className="h-4 w-4" />}
            Emitir e imprimir
          </button>
        </div>
      </form>
    </div>
  );
}
