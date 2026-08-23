"use client";

import { CreditCard, FileText, Loader2, Receipt, Trash2 } from "lucide-react";
import { FormEvent, KeyboardEvent, useCallback, useEffect, useMemo, useRef, useState } from "react";

import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { PosPaymentInput, type PosSaleDocumentType } from "@/services/pos/pos-edge-client";
import {
  calculatePaymentSettlement,
  chooseAdditionalPaymentMethod,
  PosPaymentSettlement,
} from "./pos-payment-settlement";
import {
  formatMoneyDraft,
  formatMoneyValue,
  parseMoneyDraft,
} from "./pos-money-input";
import { useReferenceOptions } from "@/hooks/use-reference-options";

const money = new Intl.NumberFormat("es-CO", {
  style: "currency",
  currency: "COP",
  maximumFractionDigits: 0,
});

type PaymentRow = PosPaymentInput & { id: string };

export function PosPaymentDialog({
  total,
  busy,
  documentType,
  documentTypeLocked,
  documentTypeReady,
  onChangeDocumentType,
  onCancel,
  onConfirm,
}: {
  total: number;
  busy: boolean;
  documentType: PosSaleDocumentType;
  documentTypeLocked: boolean;
  documentTypeReady: boolean;
  onChangeDocumentType: () => void;
  onCancel: () => void;
  onConfirm: (
    payments: PosPaymentInput[],
    settlement: PosPaymentSettlement,
  ) => Promise<void>;
}) {
  const paymentMethods = useReferenceOptions("payment-method");
  const methods = useMemo(
    () => (paymentMethods.data ?? []).slice(0, 5).map((option, index) => ({
      code: option.code,
      label: option.label,
      shortcut: `F${index + 1}`,
    })),
    [paymentMethods.data],
  );
  const [payments, setPayments] = useState<PaymentRow[]>([]);
  const [amountDrafts, setAmountDrafts] = useState<Record<string, string>>({});
  const [pendingFocusId, setPendingFocusId] = useState<string | null>(null);
  const [activePaymentId, setActivePaymentId] = useState<string | null>(null);
  const amountRefs = useRef(new Map<string, HTMLInputElement>());
  const settlement = useMemo(
    () => calculatePaymentSettlement(total, payments),
    [payments, total],
  );

  useEffect(() => {
    const defaultMethod = methods[0];
    if (!defaultMethod || payments.length > 0) return;
    setPayments([{ id: crypto.randomUUID(), methodCode: defaultMethod.code, amount: total, reference: null }]);
  }, [methods, payments.length, total]);

  function update(id: string, value: Partial<PaymentRow>) {
    setPayments((current) =>
      current.map((payment) => (payment.id === id ? { ...payment, ...value } : payment)),
    );
  }

  const removePayment = useCallback((id: string) => {
    if (busy || payments.length === 1) return;
    const index = payments.findIndex((payment) => payment.id === id);
    const nextFocusId = payments[index - 1]?.id ?? payments[index + 1]?.id ?? null;
    setPayments((current) => current.filter((payment) => payment.id !== id));
    setAmountDrafts((current) => {
      const next = { ...current };
      delete next[id];
      return next;
    });
    setActivePaymentId(nextFocusId);
    if (nextFocusId) setPendingFocusId(nextFocusId);
  }, [busy, payments]);

  const focusAmount = useCallback((id: string) => {
    window.requestAnimationFrame(() => {
      const amount = amountRefs.current.get(id);
      amount?.focus();
      amount?.select();
    });
  }, []);

  const addPayment = useCallback((requestedMethod?: string) => {
    if (busy) return;
    const active = activePaymentId
      ? payments.find((payment) => payment.id === activePaymentId)
      : null;
    const requestedIsAvailable = requestedMethod && !payments.some(
      (payment) => payment.id !== active?.id && payment.methodCode === requestedMethod,
    );
    if (active && requestedMethod && requestedIsAvailable) {
      update(active.id, { methodCode: requestedMethod });
      focusAmount(active.id);
      return;
    }
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
    const nextMethodCode = requestedMethod ?? chooseAdditionalPaymentMethod(
      methods.map((method) => method.code),
      used,
    );
    const nextMethod = methods.find(
      (method) => method.code === nextMethodCode && !used.has(method.code),
    );
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
    setActivePaymentId(id);
    setPendingFocusId(id);
  }, [activePaymentId, busy, focusAmount, methods, payments, settlement.change, settlement.isValid, settlement.missing]);

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
      } else if (event.key.toLowerCase() === "e" && activePaymentId && payments.length > 1 &&
          !(event.target instanceof HTMLElement && event.target.dataset.paymentReference === "true")) {
        event.preventDefault();
        removePayment(activePaymentId);
      } else if (event.key === "Escape" && !busy) {
        event.preventDefault();
        onCancel();
      }
    };
    window.addEventListener("keydown", shortcut);
    return () => window.removeEventListener("keydown", shortcut);
  }, [activePaymentId, addPayment, busy, methods, onCancel, payments.length, removePayment]);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!settlement.isValid || busy || !documentTypeReady ||
        paymentMethods.isLoading || paymentMethods.isError) return;
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
    <div className="fixed inset-0 z-50 flex h-[100dvh] items-end justify-center overflow-hidden bg-slate-950/60 sm:items-center sm:p-4">
      <form
        onSubmit={submit}
        className="flex max-h-[100dvh] w-full max-w-4xl flex-col overflow-hidden rounded-t-2xl bg-white shadow-2xl sm:max-h-[calc(100dvh-2rem)] sm:rounded-2xl"
        aria-labelledby="pos-payment-title"
        aria-modal="true"
        role="dialog"
      >
        <div className="shrink-0 border-b border-slate-100 px-4 py-4 sm:px-5">
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
        </div>

        <div className="min-h-0 flex-1 overflow-y-auto overscroll-contain px-4 pb-4 sm:px-5">

        <section className="mt-5 rounded-xl border border-slate-200 bg-slate-50 p-4">
          <div className="flex flex-col justify-between gap-3 sm:flex-row sm:items-center">
            <div><p className="text-xs font-bold uppercase tracking-wide text-slate-500">Documento de venta</p><p className="mt-1 flex items-center gap-2 font-semibold text-slate-950">{documentType==="SalesInvoice"?<FileText className="h-4 w-4 text-teal-700"/>:<Receipt className="h-4 w-4 text-teal-700"/>}{documentType==="SalesInvoice"?"Factura electrónica":"Comprobante de venta"}</p><p className="mt-1 text-xs text-slate-500">{documentTypeLocked?"Este cliente requiere factura electrónica; la selección está protegida.":"Puedes elegir el documento antes de confirmar el pago."}</p></div>
            <button type="button" onClick={onChangeDocumentType} disabled={busy||(documentTypeLocked&&documentTypeReady)} className="h-10 rounded-lg border border-slate-300 bg-white px-4 text-sm font-semibold text-slate-700 hover:border-teal-500 disabled:cursor-not-allowed disabled:opacity-50">{documentTypeLocked?(documentTypeReady?"Factura obligatoria":"Configurar factura"):"Cambiar documento"}</button>
          </div>
          {!documentTypeReady&&<p className="mt-3 rounded-lg bg-amber-100 px-3 py-2 text-sm text-amber-900">Completa la configuración de factura electrónica para poder emitir esta venta.</p>}
        </section>

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
        {paymentMethods.isLoading && (
          <p className="mt-3 text-sm text-slate-500">Cargando medios de pago...</p>
        )}
        {paymentMethods.isError && (
          <p role="alert" className="mt-3 rounded-lg bg-red-50 p-3 text-sm text-red-700">
            No fue posible cargar los medios de pago. Cierra e intenta nuevamente.
          </p>
        )}

        <div className="mt-4 space-y-3">
          {payments.map((payment, index) => (
            <div
              key={payment.id}
              onFocusCapture={() => setActivePaymentId(payment.id)}
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
                  data-payment-reference="true"
                  onChange={(event) => update(payment.id, { reference: event.target.value })}
                  className="mt-1 h-11 w-full rounded-lg border border-slate-300 px-3 text-sm outline-none focus:border-teal-600 focus:ring-2 focus:ring-teal-600/15"
                  placeholder="Opcional"
                />
              </label>
              <button
                type="button"
                onClick={() => removePayment(payment.id)}
                disabled={payments.length === 1 || busy}
                className="mt-5 grid h-11 place-items-center rounded-lg text-slate-500 hover:bg-red-50 hover:text-red-700 focus:outline-none focus:ring-2 focus:ring-red-300 disabled:opacity-30"
                aria-label="Eliminar medio de pago"
              >
                <Trash2 className="h-4 w-4" />
                <span className="sr-only">Atajo E</span>
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
            F1-F5 cambian el medio enfocado; Enter agrega el saldo faltante y E elimina la fila activa.
          </p>
          <PaymentStatus settlement={settlement} />
        </div>
        </div>

        <div className="grid shrink-0 grid-cols-[minmax(0,1fr)_minmax(0,1.6fr)] gap-2 border-t border-slate-200 bg-white px-4 pt-3 [padding-bottom:max(0.75rem,env(safe-area-inset-bottom))] sm:flex sm:justify-end sm:px-5 sm:pb-4 sm:pt-4">
          <button
            type="button"
            onClick={onCancel}
            disabled={busy}
            className="h-11 min-w-0 rounded-lg border border-slate-300 px-3 font-medium focus:outline-none focus:ring-2 focus:ring-slate-400 sm:px-5"
          >
            Cerrar <span className="ml-1 hidden text-xs text-slate-500 sm:inline">Esc</span>
          </button>
          <button
            type="submit"
            disabled={!settlement.isValid || busy || !documentTypeReady ||
              paymentMethods.isLoading || paymentMethods.isError}
            className="flex h-11 min-w-0 items-center justify-center gap-2 rounded-lg bg-teal-700 px-3 font-semibold text-white focus:outline-none focus:ring-4 focus:ring-teal-600/20 disabled:opacity-45 sm:min-w-48 sm:px-5"
          >
            {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <CreditCard className="h-4 w-4" />}
            <span className="sm:hidden">Emitir</span>
            <span className="hidden sm:inline">Emitir e imprimir</span>
            <span className="hidden rounded bg-white/15 px-1.5 py-0.5 text-xs sm:inline">Enter</span>
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
