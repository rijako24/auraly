"use client";

import { CreditCard, FileText, Loader2, Receipt, Trash2, X } from "lucide-react";
import { FormEvent, KeyboardEvent, useCallback, useEffect, useMemo, useRef, useState } from "react";

import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { PosPaymentInput, type PosBankAccount, type PosClient, type PosCustomer, type PosSaleDocumentType } from "@/services/pos/pos-edge-client";
import {
  calculatePaymentSettlement,
  chooseAdditionalPaymentMethod,
  handlePosPaymentAmountEnter,
  PosPaymentSettlement,
} from "./pos-payment-settlement";
import {
  formatMoneyDraft,
  formatMoneyValue,
  parseMoneyDraft,
} from "./pos-money-input";
import { usePosReferenceOptions } from "./use-pos-reference-options";
import { initialTransferBankAccountId } from "./pos-transfer-settlement";

const money = new Intl.NumberFormat("es-CO", {
  style: "currency",
  currency: "COP",
  maximumFractionDigits: 0,
});

type PaymentRow = PosPaymentInput & { id: string };

export function PosPaymentDialog({
  client,
  total,
  grossTotal,
  withholdingTotal,
  busy,
  documentType,
  documentTypeLocked,
  documentTypeReady,
  customer,
  onChangeDocumentType,
  onCancel,
  onConfirm,
}: {
  client: PosClient;
  total: number;
  grossTotal: number;
  withholdingTotal: number;
  busy: boolean;
  documentType: PosSaleDocumentType;
  documentTypeLocked: boolean;
  documentTypeReady: boolean;
  customer: PosCustomer | null;
  onChangeDocumentType: () => void;
  onCancel: () => void;
  onConfirm: (
    payments: PosPaymentInput[],
    settlement: PosPaymentSettlement,
  ) => Promise<void>;
}) {
  const paymentMethods = usePosReferenceOptions(client, "payment-method");
  const cardFranchises = usePosReferenceOptions(client, "card-franchise");
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
  const [creditError, setCreditError] = useState<string | null>(null);
  const [cardCapture, setCardCapture] = useState<{ paymentId: string; franchiseCode: string; approvalNumber: string } | null>(null);
  const [transferCapture, setTransferCapture] = useState<{ paymentId: string; bankAccountId: string; reference: string; notes: string } | null>(null);
  const [bankAccounts, setBankAccounts] = useState<PosBankAccount[]>([]);
  const [accountingEnabled, setAccountingEnabled] = useState(false);
  const [settlementConfigurationLoaded, setSettlementConfigurationLoaded] = useState(false);
  const cardApprovalRef = useRef<HTMLInputElement>(null);
  const transferReferenceRef = useRef<HTMLInputElement>(null);
  const amountRefs = useRef(new Map<string, HTMLInputElement>());
  const settlement = useMemo(
    () => calculatePaymentSettlement(total, payments),
    [payments, total],
  );

  useEffect(() => {
    const defaultMethod = methods.find((method) => method.code === "Cash");
    if (!defaultMethod || payments.length > 0) return;
    setPayments([{ id: crypto.randomUUID(), methodCode: defaultMethod.code, amount: total, reference: null }]);
  }, [methods, payments.length, total]);

  useEffect(() => {
    let active = true;
    void client.settlementConfiguration().then(configuration => {
      if (!active) return;
      setAccountingEnabled(configuration.isAccountingEnabled);
      setBankAccounts(configuration.bankAccounts);
      setSettlementConfigurationLoaded(true);
    }).catch(() => { if (active) setSettlementConfigurationLoaded(false); });
    return () => { active = false; };
  }, [client]);

  const openTransferCapture = useCallback((paymentId: string, payment?: PaymentRow) => {
    if (!settlementConfigurationLoaded) {
      setCreditError("No fue posible consultar la configuración de transferencias. Intenta nuevamente.");
      return false;
    }
    if (accountingEnabled && bankAccounts.length === 0) {
      setCreditError("Configura una cuenta bancaria activa en Contabilidad antes de usar transferencia.");
      return false;
    }
    setCreditError(null);
    setTransferCapture({
      paymentId,
      bankAccountId: initialTransferBankAccountId(bankAccounts, payment?.bankAccountId),
      reference: payment?.reference ?? "",
      notes: payment?.notes ?? "",
    });
    return true;
  }, [accountingEnabled, bankAccounts, settlementConfigurationLoaded]);

  const activeTransferPaymentId = transferCapture?.paymentId;
  useEffect(() => {
    if (!activeTransferPaymentId) return;
    window.requestAnimationFrame(() => transferReferenceRef.current?.focus());
  }, [activeTransferPaymentId]);

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

  const requiresCardCapture = useCallback((methodCode: string) =>
    methodCode === "Card" || methodCode === "DebitCard" || methodCode === "CreditCard", []);

  const openCardCapture = useCallback((paymentId: string) => {
    const firstFranchise = cardFranchises.data?.[0]?.code ?? "";
    setCardCapture({ paymentId, franchiseCode: firstFranchise, approvalNumber: "" });
  }, [cardFranchises.data]);

  const addPayment = useCallback((requestedMethod?: string) => {
    if (busy) return;
    if (requestedMethod === "Credit" && !customer?.isCreditEnabled) {
      setCreditError(customer
        ? "Este cliente no está habilitado para ventas a crédito. Actívalo en su ficha antes de facturar."
        : "Selecciona un cliente habilitado para crédito antes de usar este medio.");
      return;
    }
    setCreditError(null);
    const active = activePaymentId
      ? payments.find((payment) => payment.id === activePaymentId)
      : null;
    if (requestedMethod && settlement.missing <= 0) {
      const current = active ?? payments[0];
      if (!current) return;
      update(current.id, { methodCode: requestedMethod, reference: null, notes: null,
        bankAccountId: null, cardFranchiseCode: null, approvalNumber: null });
      if (requiresCardCapture(requestedMethod)) openCardCapture(current.id);
      else if (requestedMethod === "Transfer") openTransferCapture(current.id);
      else focusAmount(current.id);
      return;
    }
    if (settlement.missing <= 0) return;
    const used = new Set(payments.map((payment) => payment.methodCode));
    const nextMethodCode = requestedMethod ?? "Cash";
    const existingCash = nextMethodCode === "Cash"
      ? payments.find((payment) => payment.methodCode === "Cash")
      : null;
    if (existingCash) {
      update(existingCash.id, { amount: existingCash.amount + settlement.missing });
      setActivePaymentId(existingCash.id);
      setPendingFocusId(existingCash.id);
      return;
    }
    const nextMethod = methods.find((method) => method.code === nextMethodCode) ??
      methods.find((method) => method.code === chooseAdditionalPaymentMethod(methods.map((method) => method.code), used));
    if (!nextMethod) return;
    const id = crypto.randomUUID();
    setPayments((current) => [
      ...current,
      {
        id,
        methodCode: nextMethod.code,
        amount: settlement.missing,
        reference: null,
        notes: null,
        bankAccountId: null,
      },
    ]);
    setActivePaymentId(id);
    if (requiresCardCapture(nextMethod.code)) openCardCapture(id);
    else if (nextMethod.code === "Transfer") openTransferCapture(id);
    else setPendingFocusId(id);
  }, [activePaymentId, busy, customer, focusAmount, methods, openCardCapture, openTransferCapture, payments, requiresCardCapture, settlement.missing]);

  useEffect(() => {
    if (!pendingFocusId) return;
    focusAmount(pendingFocusId);
    setPendingFocusId(null);
  }, [focusAmount, pendingFocusId, payments]);

  useEffect(() => {
    const shortcut = (event: globalThis.KeyboardEvent) => {
      if (cardCapture || transferCapture) {
        if (event.key === "Escape") {
          event.preventDefault();
          setCardCapture(null);
          setTransferCapture(null);
        }
        return;
      }
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
  }, [activePaymentId, addPayment, busy, cardCapture, transferCapture, methods, onCancel, payments.length, removePayment]);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!settlement.isValid || busy || !documentTypeReady ||
        payments.some(payment => requiresCardCapture(payment.methodCode) && (!payment.cardFranchiseCode || !payment.approvalNumber?.trim())) ||
        payments.some(payment => payment.methodCode === "Transfer" &&
          (!payment.reference?.trim() || (accountingEnabled && !payment.bankAccountId))) ||
        paymentMethods.isLoading || paymentMethods.isError) return;
    await onConfirm(
      settlement.appliedPayments.map(({ methodCode, amount, reference, cardFranchiseCode, approvalNumber, bankAccountId, notes }) => ({
        methodCode,
        amount,
        reference: reference?.trim() || null,
        cardFranchiseCode: cardFranchiseCode?.trim() || null,
        approvalNumber: approvalNumber?.trim() || null,
        bankAccountId: methodCode === "Transfer" && accountingEnabled ? bankAccountId ?? null : null,
        notes: methodCode === "Transfer" ? notes?.trim() || null : null,
      })),
      settlement,
    );
  }

  function handleAmountEnter(event: KeyboardEvent<HTMLInputElement>) {
    // Installed WebView versions do not consistently perform the same
    // implicit form action for Enter. Own the keyboard contract here: an
    // incomplete payment fills the remainder and a complete payment submits
    // this same form through its canonical submit handler.
    handlePosPaymentAmountEnter(event, settlement.missing, () => addPayment("Cash"));
  }

  function saveCardCapture() {
    if (!cardCapture?.franchiseCode || !cardCapture.approvalNumber.trim()) return;
    update(cardCapture.paymentId, { reference: null, cardFranchiseCode: cardCapture.franchiseCode, approvalNumber: cardCapture.approvalNumber.trim() });
    const paymentId = cardCapture.paymentId;
    setCardCapture(null);
    setPendingFocusId(paymentId);
  }

  function saveTransferCapture() {
    if (!transferCapture?.reference.trim() || (accountingEnabled && !transferCapture.bankAccountId)) return;
    update(transferCapture.paymentId, {
      bankAccountId: accountingEnabled ? transferCapture.bankAccountId : null,
      reference: transferCapture.reference.trim(),
      notes: transferCapture.notes.trim() || null,
    });
    const paymentId = transferCapture.paymentId;
    setTransferCapture(null);
    setPendingFocusId(paymentId);
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
            <span className="block text-xs uppercase tracking-wide text-slate-500">Total a pagar</span>
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
        {creditError && <p role="alert" className="mt-3 rounded-lg bg-amber-50 p-3 text-sm font-medium text-amber-900">{creditError}</p>}
        {customer?.isCreditEnabled && <p className="mt-3 text-xs text-slate-500">Crédito habilitado · plazo {customer.defaultCreditDueDays ?? 0} días · cupo disponible {customer.availableCredit == null ? "sin límite" : money.format(customer.availableCredit)}</p>}

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
                  onValueChange={(methodCode) => {
                    update(payment.id, { methodCode, reference: null, notes: null,
                      cardFranchiseCode: null, approvalNumber: null, bankAccountId: null });
                    if (requiresCardCapture(methodCode)) openCardCapture(payment.id);
                    else if (methodCode === "Transfer") openTransferCapture(payment.id);
                  }}
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
              {requiresCardCapture(payment.methodCode) ? (
                <div className="text-xs font-medium text-slate-600"><span>Tarjeta</span><button type="button" onClick={() => openCardCapture(payment.id)} className={`mt-1 flex h-11 w-full items-center gap-2 rounded-lg border px-3 text-left text-sm font-semibold ${payment.cardFranchiseCode && payment.approvalNumber ? "border-emerald-300 bg-emerald-50 text-emerald-900" : "border-amber-300 bg-amber-50 text-amber-900"}`}><CreditCard className="h-4 w-4 shrink-0"/><span className="truncate">{payment.cardFranchiseCode && payment.approvalNumber ? `${cardFranchises.data?.find(item => item.code === payment.cardFranchiseCode)?.label ?? payment.cardFranchiseCode} · ${payment.approvalNumber}` : "Capturar franquicia y aprobación"}</span></button></div>
              ) : payment.methodCode === "Cash" ? (
                <div className="text-xs font-medium text-slate-600"><span>Efectivo</span><div className="mt-1 flex h-11 items-center gap-2 rounded-lg border border-emerald-200 bg-emerald-50 px-3 text-sm font-semibold text-emerald-900"><span className="grid h-7 w-7 place-items-center rounded-full bg-emerald-700 text-white">$</span><span>Cambio automático</span></div></div>
              ) : payment.methodCode === "Credit" ? (
                <div className="text-xs font-medium text-slate-600"><span>Crédito del cliente</span><div className="mt-1 flex h-11 items-center rounded-lg border border-violet-200 bg-violet-50 px-3 text-sm font-semibold text-violet-900">{customer ? `Plazo ${customer.defaultCreditDueDays ?? 0} días` : "Selecciona un cliente"}</div></div>
              ) : payment.methodCode === "Transfer" ? (
                <div className="text-xs font-medium text-slate-600"><span>Transferencia</span><button type="button" onClick={() => openTransferCapture(payment.id, payment)} className={`mt-1 flex h-11 w-full items-center gap-2 rounded-lg border px-3 text-left text-sm font-semibold ${payment.reference && (!accountingEnabled || payment.bankAccountId) ? "border-emerald-300 bg-emerald-50 text-emerald-900" : "border-amber-300 bg-amber-50 text-amber-900"}`}><Receipt className="h-4 w-4 shrink-0"/><span className="truncate">{payment.reference && (!accountingEnabled || payment.bankAccountId) ? `${accountingEnabled ? `${bankAccounts.find(account => account.bankAccountId === payment.bankAccountId)?.displayName ?? "Cuenta"} · ` : ""}${payment.reference}` : "Registrar transferencia"}</span></button></div>
              ) : (
                <label className="text-xs font-medium text-slate-600">Referencia<input value={payment.reference ?? ""} data-payment-reference="true" onChange={(event) => update(payment.id, { reference: event.target.value })} className="mt-1 h-11 w-full rounded-lg border border-slate-300 px-3 text-sm outline-none focus:border-teal-600 focus:ring-2 focus:ring-teal-600/15" placeholder="Comprobante o referencia"/></label>
              )}
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

        <div className={`mt-4 grid grid-cols-1 gap-3 ${withholdingTotal > 0 ? "sm:grid-cols-4" : "sm:grid-cols-3"}`}>
          <PaymentMetric label={withholdingTotal > 0 ? "Total venta" : "Total a pagar"} value={grossTotal} />
          {withholdingTotal > 0 && <PaymentMetric label="Retenciones" value={-withholdingTotal} />}
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

        {cardCapture && (
          <div className="fixed inset-0 z-[70] grid place-items-center bg-slate-950/65 p-4" data-pos-focus-surface="modal">
            <div onKeyDown={event => { if (event.key === "Enter") { event.preventDefault(); saveCardCapture(); } }} className="w-full max-w-md rounded-2xl bg-white p-5 shadow-2xl" role="dialog" aria-modal="true" aria-labelledby="card-capture-title">
              <div className="flex items-start justify-between gap-3"><div><h3 id="card-capture-title" className="text-lg font-bold text-slate-950">Datos de la tarjeta</h3><p className="mt-1 text-sm text-slate-500">Selecciona la franquicia y registra la aprobación del datáfono.</p></div><button type="button" onClick={() => setCardCapture(null)} aria-label="Cerrar datos de tarjeta" className="grid h-9 w-9 place-items-center rounded-lg text-slate-500 hover:bg-slate-100"><X className="h-5 w-5"/></button></div>
              <label className="mt-5 block text-sm font-medium text-slate-700">Franquicia<Select value={cardCapture.franchiseCode} onValueChange={franchiseCode => setCardCapture(current => current ? { ...current, franchiseCode } : null)}><SelectTrigger className="mt-1 h-11"><SelectValue placeholder="Selecciona"/></SelectTrigger><SelectContent>{(cardFranchises.data ?? []).map(item => <SelectItem key={item.id} value={item.code}>{item.label}</SelectItem>)}</SelectContent></Select></label>
              <label className="mt-4 block text-sm font-medium text-slate-700">Número de aprobación<input ref={cardApprovalRef} autoFocus required value={cardCapture.approvalNumber} onChange={event => setCardCapture(current => current ? { ...current, approvalNumber: event.target.value } : null)} className="mt-1 h-11 w-full rounded-lg border border-slate-300 px-3 text-lg font-semibold tracking-wide outline-none focus:border-teal-600 focus:ring-2 focus:ring-teal-600/15" placeholder="Aprobación del datáfono"/></label>
              <p className="mt-4 rounded-lg bg-slate-100 px-3 py-2 text-xs text-slate-600">Enter guarda y vuelve al valor recibido.</p><button type="button" onClick={saveCardCapture} disabled={!cardCapture.franchiseCode || !cardCapture.approvalNumber.trim()} className="mt-4 h-11 w-full rounded-xl bg-teal-700 font-bold text-white disabled:opacity-45">Guardar datos · Enter</button>
            </div>
          </div>
        )}
        {transferCapture && (
          <div className="fixed inset-0 z-[70] grid place-items-center bg-slate-950/65 p-4" data-pos-focus-surface="modal">
            <div className="w-full max-w-md rounded-2xl bg-white p-5 shadow-2xl" role="dialog" aria-modal="true" aria-labelledby="transfer-capture-title">
              <div className="flex items-start justify-between gap-3"><div><h3 id="transfer-capture-title" className="text-lg font-bold text-slate-950">Datos de la transferencia</h3><p className="mt-1 text-sm text-slate-500">Registra el soporte del movimiento. La cuenta principal es solo la selección inicial.</p></div><button type="button" onClick={() => setTransferCapture(null)} aria-label="Cerrar datos de transferencia" className="grid h-9 w-9 place-items-center rounded-lg text-slate-500 hover:bg-slate-100"><X className="h-5 w-5"/></button></div>
              {accountingEnabled && <label className="mt-5 block text-sm font-medium text-slate-700">Cuenta bancaria<Select value={transferCapture.bankAccountId} onValueChange={bankAccountId => { setTransferCapture(current => current ? { ...current, bankAccountId } : null); window.requestAnimationFrame(() => transferReferenceRef.current?.focus()); }}><SelectTrigger className="mt-1 h-11"><SelectValue placeholder="Selecciona la cuenta"/></SelectTrigger><SelectContent>{bankAccounts.map(account => <SelectItem key={account.bankAccountId} value={account.bankAccountId}>{account.displayName} · {account.accountNumber}</SelectItem>)}</SelectContent></Select></label>}
              <label className={`${accountingEnabled ? "mt-4" : "mt-5"} block text-sm font-medium text-slate-700`}>Referencia<input ref={transferReferenceRef} required maxLength={160} value={transferCapture.reference} onChange={event => setTransferCapture(current => current ? { ...current, reference: event.target.value } : null)} className="mt-1 h-11 w-full rounded-lg border border-slate-300 px-3 text-base font-semibold outline-none focus:border-teal-600 focus:ring-2 focus:ring-teal-600/15" placeholder="Número o referencia del comprobante"/></label>
              <label className="mt-4 block text-sm font-medium text-slate-700">Nota <span className="font-normal text-slate-400">(opcional)</span><textarea maxLength={500} rows={3} value={transferCapture.notes} onChange={event => setTransferCapture(current => current ? { ...current, notes: event.target.value } : null)} className="mt-1 w-full resize-none rounded-lg border border-slate-300 px-3 py-2 text-sm outline-none focus:border-teal-600 focus:ring-2 focus:ring-teal-600/15" placeholder="Detalle útil para identificar la transferencia"/></label>
              <button type="button" onClick={saveTransferCapture} disabled={!transferCapture.reference.trim() || (accountingEnabled && !transferCapture.bankAccountId)} className="mt-4 h-11 w-full rounded-xl bg-teal-700 font-bold text-white disabled:opacity-45">Guardar transferencia</button>
            </div>
          </div>
        )}
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
              payments.some(payment => requiresCardCapture(payment.methodCode) && (!payment.cardFranchiseCode || !payment.approvalNumber?.trim())) ||
              payments.some(payment => payment.methodCode === "Transfer" &&
                (!payment.reference?.trim() || (accountingEnabled && !payment.bankAccountId))) ||
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
