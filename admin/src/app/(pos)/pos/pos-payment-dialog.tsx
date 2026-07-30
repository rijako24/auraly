"use client";

import { CreditCard, Loader2, Trash2 } from "lucide-react";
import { FormEvent, KeyboardEvent, useCallback, useEffect, useMemo, useRef, useState } from "react";

import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { PosPaymentInput } from "@/services/pos/pos-edge-client";
import {
  calculatePaymentSettlement,
  PosPaymentSettlement,
} from "./pos-payment-settlement";
import {
  formatMoneyDraft,
  formatMoneyValue,
  parseMoneyDraft,
} from "./pos-money-input";

const money = new Intl.NumberFormat("es-CO", {
  style: "currency",
  currency: "COP",
  maximumFractionDigits: 0,
});

const methods = [
  { code: "Cash", label: "Efectivo", shortcut: "F1" },
  { code: "DebitCard", label: "Tarjeta d\u00e9bito", shortcut: "F2" },
  { code: "CreditCard", label: "Tarjeta cr\u00e9dito", shortcut: "F3" },
  { code: "Transfer", label: "Transferencia", shortcut: "F4" },
  { code: "Credit", label: "Cr\u00e9dito cliente", shortcut: "F5" },
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
  onConfirm: (
    payments: PosPaymentInput[],
    settlement: PosPaymentSettlement,
  ) => Promise<void>;
}) {
  const [payments, setPayments] = useState<PaymentRow[]>([
    { id: crypto.randomUUID(), methodCode: "Cash", amount: total, reference: null },
  ]);
  const [amountDrafts, setAmountDrafts] = useState<Record<string, string>>({});
  const [pendingFocusId, setPendingFocusId] = useState<string | null>(null);
  const amountRefs = useRef(new Map<string, HTMLInputElement>());
  const settlement = useMemo(
    () => calculatePaymentSettlement(total, payments),
    [payments, total],
  );

  function update(id: string, value: Partial<PaymentRow>) {
    setPayments((current) =>
      current.map((payment) => (payment.id === id ? { ...payment, ...value } : payment)),
    );
  }

  const focusAmount = useCallback((id: string) => {
    window.requestAnimationFrame(() => {
      const amount = amountRefs.current.get(id);
      amount?.focus();
      amount?.select();
    });
  }, []);

  const addPayment = useCallback((requestedMethod?: string) => {
    if (busy) return;
    const existing = requestedMethod
      ? payments.find((payment) => payment.methodCode === requestedMethod)
      : null;
    if (existing) {
      focusAmount(existing.id);
      return;
    }
    if (requestedMethod && payments.length === 1 && settlement.isValid && settlement.change === 0) {
      const current = payments[0];
      setPayments([{ ...current, methodCode: requestedMethod }]);
      focusAmount(current.id);
      return;
    }
    if (settlement.missing <= 0) return;
    const used = new Set(payments.map((payment) => payment.methodCode));
    const nextMethod = requestedMethod
      ? methods.find((method) => method.code === requestedMethod && !used.has(method.code))
      : methods.find((method) => method.code !== "Cash" && !used.has(method.code));
    if (!nextMethod) return;
    const id = crypto.randomUUID();
    setPayments((current) => [
      ...current,
      {
        id,
        methodCode: nextMethod.code,
        amount: settlement.missing,
        reference: null,
      },
    ]);
    setPendingFocusId(id);
  }, [busy, focusAmount, payments, settlement.change, settlement.isValid, settlement.missing]);

  useEffect(() => {
    if (!pendingFocusId) return;
    focusAmount(pendingFocusId);
    setPendingFocusId(null);
  }, [focusAmount, pendingFocusId, payments]);

  useEffect(() => {
    const shortcut = (event: globalThis.KeyboardEvent) => {
      const method = methods.find((value) => value.shortcut === event.key);
      if (method) {
        event.preventDefault();
        addPayment(method.code);
      } else if (event.key === "Escape" && !busy) {
        event.preventDefault();
        onCancel();
      }
    };
    window.addEventListener("keydown", shortcut);
    return () => window.removeEventListener("keydown", shortcut);
  }, [addPayment, busy, onCancel]);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!settlement.isValid || busy) return;
    await onConfirm(
      settlement.appliedPayments.map(({ methodCode, amount, reference }) => ({
        methodCode,
        amount,
        reference: reference?.trim() || null,
      })),
      settlement,
    );
  }

  function handleAmountEnter(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key !== "Enter" || settlement.missing <= 0) return;
    event.preventDefault();
    addPayment();
  }

  return (
    <div className="fixed inset-0 z-50 grid place-items-center bg-slate-950/60 p-4">
      <form
        onSubmit={submit}
        className="w-full max-w-4xl rounded-2xl bg-white p-5 shadow-2xl"
        aria-labelledby="pos-payment-title"
      >
        <div className="flex items-start justify-between gap-4">
          <div>
            <h2 id="pos-payment-title" className="flex items-center gap-2 text-xl font-semibold">
              <CreditCard className="h-5 w-5 text-teal-700" />
              Finalizar venta
            </h2>
            <p className="mt-1 text-sm text-slate-500">
              Escribe el valor recibido. Enter confirma; F1-F5 seleccionan el medio.
            </p>
          </div>
          <p className="text-right">
            <span className="block text-xs uppercase tracking-wide text-slate-500">Total</span>
            <span className="text-2xl font-bold text-teal-800">{money.format(total)}</span>
          </p>
        </div>

        <div className="mt-5 grid grid-cols-2 gap-2 md:grid-cols-3 xl:grid-cols-5">
          {methods.map((method) => (
            <button
              key={method.code}
              type="button"
              onClick={() => addPayment(method.code)}
              disabled={busy}
              className="flex min-h-12 items-center justify-between gap-3 rounded-lg border border-slate-300 px-3 py-2 text-left text-sm font-semibold outline-none transition hover:border-teal-500 hover:bg-teal-50 focus:ring-2 focus:ring-teal-600/20 disabled:opacity-45"
            >
              <span className="min-w-0 whitespace-normal leading-tight">{method.label}</span>
              <span className="shrink-0 rounded bg-slate-100 px-1.5 py-0.5 text-xs text-slate-600">{method.shortcut}</span>
            </button>
          ))}
        </div>

        <div className="mt-4 space-y-3">
          {payments.map((payment, index) => (
            <div
              key={payment.id}
              className="grid gap-2 rounded-xl border border-slate-200 p-3 sm:grid-cols-[1fr_150px_1fr_42px]"
            >
              <div className="text-xs font-medium text-slate-600">
                <span>Medio</span>
                <Select
                  value={payment.methodCode}
                  onValueChange={(methodCode) => update(payment.id, { methodCode })}
                  disabled={busy}
                >
                  <SelectTrigger aria-label="Medio de pago" className="mt-1 h-11 rounded-lg border-slate-300 bg-white shadow-none focus:border-teal-600 focus:ring-2 focus:ring-teal-600/15">
                    <SelectValue aria-label="Medio de pago" />
                  </SelectTrigger>
                  <SelectContent className="rounded-xl border-slate-200 shadow-xl">
                    {methods.map((method) => (
                      <SelectItem key={method.code} value={method.code} className="py-2.5">
                        {method.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <label className="relative text-xs font-medium text-slate-600">
                Valor recibido
                <input
                  ref={(element) => {
                    if (element) amountRefs.current.set(payment.id, element);
                    else amountRefs.current.delete(payment.id);
                  }}
                  autoFocus={index === 0}
                  type="text"
                  inputMode="decimal"
                  aria-label={`Valor recibido en ${methods.find((method) => method.code === payment.methodCode)?.label ?? payment.methodCode}`}
                  value={amountDrafts[payment.id] ?? formatMoneyValue(payment.amount)}
                  onFocus={(event) => event.currentTarget.select()}
                  onKeyDown={handleAmountEnter}
                  onChange={(event) => {
                    const formatted = formatMoneyDraft(event.currentTarget.value);
                    setAmountDrafts((current) => ({
                      ...current,
                      [payment.id]: formatted,
                    }));
                    update(payment.id, { amount: parseMoneyDraft(formatted) });
                  }}
                  onBlur={() =>
                    setAmountDrafts((current) => ({
                      ...current,
                      [payment.id]: formatMoneyValue(payment.amount),
                    }))
                  }
                  className="mt-1 h-11 w-full rounded-lg border border-slate-300 pl-8 pr-3 text-right text-lg font-semibold tabular-nums outline-none focus:border-teal-600 focus:ring-2 focus:ring-teal-600/15"
                />
                <span className="pointer-events-none absolute bottom-3 left-3 text-sm font-semibold text-slate-400">$</span>
              </label>
              <label className="text-xs font-medium text-slate-600">
                Referencia
                <input
                  value={payment.reference ?? ""}
                  onChange={(event) => update(payment.id, { reference: event.target.value })}
                  className="mt-1 h-11 w-full rounded-lg border border-slate-300 px-3 text-sm outline-none focus:border-teal-600 focus:ring-2 focus:ring-teal-600/15"
                  placeholder="Opcional"
                />
              </label>
              <button
                type="button"
                onClick={() =>
                  setPayments((current) => current.filter((item) => item.id !== payment.id))
                }
                disabled={payments.length === 1 || busy}
                className="mt-5 grid h-11 place-items-center rounded-lg text-slate-500 hover:bg-red-50 hover:text-red-700 focus:outline-none focus:ring-2 focus:ring-red-300 disabled:opacity-30"
                aria-label="Eliminar medio de pago"
              >
                <Trash2 className="h-4 w-4" />
              </button>
            </div>
          ))}
        </div>

        <div className="mt-4 grid grid-cols-1 gap-3 sm:grid-cols-3">
          <PaymentMetric label="Total venta" value={total} />
          <PaymentMetric label="Valor recibido" value={settlement.received} />
          <PaymentMetric
            label="Cambio a entregar"
            value={settlement.change}
            highlight={settlement.change > 0}
          />
        </div>

        <div className="mt-4 flex flex-wrap items-center justify-between gap-3">
          <p className="text-xs text-slate-500">
            Selecciona el medio con F1-F5; al agregarlo el foco pasa al valor.
          </p>
          <PaymentStatus settlement={settlement} />
        </div>

        <div className="mt-5 flex justify-end gap-2 border-t border-slate-200 pt-4">
          <button
            type="button"
            onClick={onCancel}
            disabled={busy}
            className="h-11 rounded-lg border border-slate-300 px-5 font-medium focus:outline-none focus:ring-2 focus:ring-slate-400"
          >
            Cancelar <span className="ml-1 text-xs text-slate-500">Esc</span>
          </button>
          <button
            type="submit"
            disabled={!settlement.isValid || busy}
            className="flex h-11 min-w-48 items-center justify-center gap-2 rounded-lg bg-teal-700 px-5 font-semibold text-white focus:outline-none focus:ring-4 focus:ring-teal-600/20 disabled:opacity-45"
          >
            {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <CreditCard className="h-4 w-4" />}
            Emitir e imprimir
            <span className="rounded bg-white/15 px-1.5 py-0.5 text-xs">Enter</span>
          </button>
        </div>
      </form>
    </div>
  );
}

function PaymentMetric({
  label,
  value,
  highlight = false,
}: {
  label: string;
  value: number;
  highlight?: boolean;
}) {
  return (
    <div className={`rounded-xl border p-3 ${highlight ? "border-emerald-300 bg-emerald-50" : "border-slate-200 bg-slate-50"}`}>
      <p className="text-xs font-medium uppercase tracking-wide text-slate-500">{label}</p>
      <p className={`mt-1 text-xl font-bold tabular-nums ${highlight ? "text-emerald-800" : "text-slate-900"}`}>
        {money.format(value)}
      </p>
    </div>
  );
}

function PaymentStatus({ settlement }: { settlement: PosPaymentSettlement }) {
  if (settlement.hasDuplicateCash)
    return <p className="text-sm font-semibold text-red-700">Usa una sola fila de efectivo.</p>;
  if (settlement.hasNonCashExcess)
    return <p className="text-sm font-semibold text-red-700">El excedente solo puede recibirse en efectivo.</p>;
  if (settlement.missing > 0)
    return <p className="text-sm font-semibold text-amber-700">Faltan {money.format(settlement.missing)}</p>;
  if (settlement.change > 0)
    return <p className="text-sm font-semibold text-emerald-700">Cambio listo: {money.format(settlement.change)}</p>;
  return <p className="text-sm font-semibold text-emerald-700">Pago completo</p>;
}