"use client";

import { Fragment, useEffect, useMemo, useRef, useState } from "react";
import { Banknote, Check, CheckCircle2, CreditCard,
  FileText, Plus, ReceiptText, RotateCcw, Trash2, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { useBodyScrollLock } from "@/hooks/use-body-scroll-lock";
import { finalRestaurantPayments, settleRestaurantPayment } from "@/components/restaurant/restaurant-payment-calculation";
import { roundToNearestHundred } from "@/app/(pos)/pos/pos-payment-settlement";
import { Select, SelectContent, SelectItem, SelectTrigger,
  SelectValue } from "@/components/ui/select";
import type { RestaurantAccountDraft, RestaurantCollectionAllocation,
  RestaurantCollectionSummary } from "@/services/api/restaurant-account";
import type { OnlinePosClient } from "@/services/pos/online-pos-client";
import type { PosPaymentInput, PosSaleDocumentType,
  PosPrintTemplateFormat } from "@/services/pos/pos-edge-client";
import { PAYMENT_PRINT_SHORTCUTS } from "@/app/(pos)/pos/pos-payment-keyboard";

const money = new Intl.NumberFormat("es-CO", {
  style: "currency", currency: "COP", maximumFractionDigits: 0,
});
const round = (value: number) => Math.round(value * 10000) / 10000;
const methods = ["Cash", "DebitCard", "CreditCard", "Transfer"] as const;
type Method = typeof methods[number];
type PaymentRow = { id: string; methodCode: Method; amount: string;
  reference: string; franchise: string; approval: string;
  bankAccountId: string };
const newRow = (amount: number): PaymentRow => ({ id: crypto.randomUUID(),
  methodCode: "Cash", amount: String(amount), reference: "", franchise: "",
  approval: "", bankAccountId: "" });

export function RestaurantPaymentDialog({ draft, summary, client,
  workSessionId, documentType, documentTypeBlockReason, busy,
  onChangeDocumentType, onClose, onCapture, onReverse, onFinalize }: {
  draft: RestaurantAccountDraft;
  summary: RestaurantCollectionSummary;
  client: OnlinePosClient;
  workSessionId: string;
  documentType: PosSaleDocumentType;
  documentTypeBlockReason: string | null;
  busy: boolean;
  onChangeDocumentType: (type: PosSaleDocumentType) => void;
  onClose: () => void;
  onCapture: (allocations: RestaurantCollectionAllocation[],
    payments: PosPaymentInput[], tipPayment: PosPaymentInput | null) => Promise<RestaurantCollectionSummary>;
  onReverse: (collectionId: string) => Promise<void>;
  onFinalize: (payments: PosPaymentInput[], version: number,
    printChoice: PosPrintTemplateFormat | "none") => Promise<void>;
}) {
  useBodyScrollLock(true);
  const [selectionMode, setSelectionMode] = useState<"all" | "choose">("all");
  const [quantities, setQuantities] = useState<Record<string, number>>({});
  const [paymentRows, setPaymentRows] = useState<PaymentRow[]>([]);
  const [tipAmount, setTipAmount] = useState("");
  const [separateTipCashReceived, setSeparateTipCashReceived] = useState("");
  const [tipRow, setTipRow] = useState<PaymentRow>(() => newRow(0));
  const [catalog, setCatalog] = useState<{ code: string; label: string }[]>([]);
  const [franchises, setFranchises] = useState<{ code: string; label: string }[]>([]);
  const [banks, setBanks] = useState<{ bankAccountId: string;
    displayName: string }[]>([]);
  const [accountingEnabled, setAccountingEnabled] = useState(false);
  const [referencesReady, setReferencesReady] = useState(false);
  const [printChoice, setPrintChoice] = useState<PosPrintTemplateFormat | "none">("none");
  const [documentTypeOpen, setDocumentTypeOpen] = useState(false);
  const [confirmReverse, setConfirmReverse] = useState<string | null>(null);
  const [working, setWorking] = useState(false);
  const [error, setError] = useState("");
  const previousSuggestedCash = useRef(0);
  const previousIncludedCashTip = useRef(0);
  const disabled = busy || working;
  const covered = useMemo(() => new Map(summary.coveredQuantities.map(
    (line) => [line.additionId, line.quantity])), [summary.coveredQuantities]);
  const { available, paidLines } = useMemo(() => {
    const lines = draft.additions.filter((line) =>
      line.status === "Sent" || line.status === "Direct")
      .map((line) => ({ ...line,
      coveredQuantity: covered.get(line.additionId) ?? 0,
      remainingQuantity: Math.max(0, round(line.quantity - (covered.get(line.additionId) ?? 0))) }));
    return { available: lines.filter((line) => line.remainingQuantity > 0),
      paidLines: lines.filter((line) => line.coveredQuantity > 0) };
  }, [draft.additions, covered]);
  const selection = useMemo(() => available.map((line) => ({
    additionId: line.additionId,
    quantity: selectionMode === "all" ? line.remainingQuantity :
      Math.min(line.remainingQuantity, quantities[line.additionId] ?? 0),
    unitPrice: line.publicUnitPrice - line.promotionDiscountPerUnit,
  })).filter((line) => line.quantity > 0), [available, quantities, selectionMode]);
  const selectedTotal = round(selection.reduce((sum, line) =>
    sum + line.quantity * line.unitPrice, 0));
  const roundingAdjustment = round(roundToNearestHundred(summary.total) - summary.total);
  const finalSelection = summary.remaining > 0 &&
    round(selectedTotal - summary.remaining) === 0;
  const currentAdjustment = finalSelection ? roundingAdjustment : 0;
  const capturedAdjustment = round(summary.collections.reduce((sum, entry) =>
    sum + entry.payments.reduce((paymentSum, payment) =>
      paymentSum + (payment.roundingAdjustment ?? 0), 0), 0));
  const missingRecordedAdjustment = summary.remaining === 0 &&
    capturedAdjustment !== roundingAdjustment;
  const actualTip = tipAmount.trim() === "" ? 0 : Number(tipAmount);
  const tipBase = selectedTotal > 0 ? selectedTotal : summary.total;
  const invalidTip = !Number.isFinite(actualTip) || actualTip < 0 ||
    !Number.isInteger(actualTip) || actualTip > 1000000000;
  const cashTipIncluded = summary.remaining > 0 && !invalidTip && actualTip > 0 &&
    tipRow.methodCode === "Cash" && paymentRows.some((row) =>
      row.methodCode === "Cash") ? actualTip : 0;
  const separateCashTip = actualTip > 0 && !invalidTip &&
    tipRow.methodCode === "Cash" && cashTipIncluded === 0;
  const separateTipTendered = separateTipCashReceived.trim() === ""
    ? NaN : Number(separateTipCashReceived);
  const invalidSeparateTipTendered = separateCashTip &&
    (!Number.isFinite(separateTipTendered) ||
      separateTipTendered < actualTip);
  const separateTipChange = separateCashTip && !invalidSeparateTipTendered
    ? round(separateTipTendered - actualTip) : 0;
  const { cashTendered, cashSaleTendered, cashApplied, due, change } =
    settleRestaurantPayment(selectedTotal, paymentRows, cashTipIncluded,
      currentAdjustment);
  const cashTipExceedsTendered = cashTipIncluded > cashTendered;
  const otherSession = summary.collections.some((item) =>
    item.workSessionId !== workSessionId);
  const incompletePaymentDetails = paymentRows.some((row) =>
    (row.methodCode === "DebitCard" || row.methodCode === "CreditCard") &&
      (!row.franchise || !row.approval.trim()) ||
    row.methodCode === "Transfer" &&
      (!row.reference.trim() || accountingEnabled && !row.bankAccountId));
  const incompleteTipDetails = actualTip > 0 && !invalidTip &&
    ((tipRow.methodCode === "DebitCard" || tipRow.methodCode === "CreditCard") &&
      (!tipRow.franchise || !tipRow.approval.trim()) ||
    tipRow.methodCode === "Transfer" &&
      (!tipRow.reference.trim() || accountingEnabled && !tipRow.bankAccountId));
  const captureBlockReason = summary.remaining <= 0
    ? otherSession ? "Esta mesa tiene cobros de otra sesión de caja. Revísalos antes de continuar."
      : invalidTip ? "Ingresa un valor de propina válido o déjalo en cero."
        : invalidSeparateTipTendered ? "El efectivo entregado no cubre la propina indicada."
        : incompleteTipDetails ? "Completa los datos del medio usado para la propina."
        : actualTip > 0 && !referencesReady ? "Cargando medios de pago…"
          : documentTypeBlockReason
    : otherSession
    ? "Esta mesa tiene cobros de otra sesión de caja. Revísalos antes de continuar."
    : !referencesReady ? error || "Cargando medios de pago…"
      : invalidTip ? "Ingresa un valor de propina válido o déjalo en cero."
      : invalidSeparateTipTendered ? "Escribe el efectivo entregado para cubrir la propina."
      : cashTipExceedsTendered ? "El efectivo entregado no cubre la propina indicada."
      : incompleteTipDetails ? "Completa los datos del medio usado para la propina."
      : selectedTotal <= 0 ? "Selecciona los productos que vas a cobrar."
        : !finalSelection && round(summary.remaining - selectedTotal +
            roundingAdjustment) <= 0
          ? "Selecciona más productos: el último pago debe superar el ajuste al peso."
        : due > 0 ? `Faltan ${money.format(due)} por asignar a los medios de pago.`
          : due < 0 ? `Los medios de pago exceden el cobro por ${money.format(-due)}.`
            : incompletePaymentDetails ? "Completa los datos de tarjeta o transferencia."
              : documentTypeBlockReason;
  const paymentLabels = useMemo(() => new Map(catalog.map((item) =>
    [item.code, item.label])), [catalog]);
  const canAddPaymentMethod = catalog.some((item) => item.code !== "Cash");

  useEffect(() => {
    let active = true;
    void Promise.all([
      client.referenceOptions("payment-method"),
      client.referenceOptions("card-franchise"),
      client.settlementConfiguration(),
    ]).then(([paymentOptions, franchiseOptions, settlement]) => {
      if (!active) return;
      setCatalog(paymentOptions.filter((option) =>
        methods.includes(option.code as Method)));
      setFranchises(franchiseOptions);
      setBanks(settlement.bankAccounts);
      setAccountingEnabled(settlement.isAccountingEnabled);
      setReferencesReady(true);
    }).catch(() => { if (active) setError("No pudimos cargar los medios de pago."); });
    return () => { active = false; };
  }, [client]);

  useEffect(() => {
    if (paymentRows.length || selectedTotal <= 0) return;
    setPaymentRows([newRow(selectedTotal + currentAdjustment)]);
  }, [paymentRows.length, selectedTotal, currentAdjustment]);

  useEffect(() => {
    const previous = previousSuggestedCash.current;
    const previousTip = previousIncludedCashTip.current;
    const suggested = selectedTotal + currentAdjustment + cashTipIncluded;
    previousSuggestedCash.current = suggested;
    previousIncludedCashTip.current = cashTipIncluded;
    setPaymentRows((rows) => {
      if (rows.length === 1 && rows[0].methodCode === "Cash" &&
          Number(rows[0].amount) === previous)
        return [{ ...rows[0], amount: String(suggested) }];
      const difference = cashTipIncluded - previousTip;
      const cashIndex = rows.findIndex((row) => row.methodCode === "Cash");
      if (!difference || cashIndex < 0) return rows;
      return rows.map((row, index) => index === cashIndex
        ? { ...row, amount: String(round(Number(row.amount) + difference)) } : row);
    });
  }, [selectedTotal, currentAdjustment, cashTipIncluded]);

  function updateRow(id: string, patch: Partial<PaymentRow>) {
    setPaymentRows((rows) => rows.map((row) => row.id === id ?
      { ...row, ...patch } : row));
    setError("");
  }

  function changePaymentMethod(row: PaymentRow, methodCode: Method) {
    const tipLeavingCash = row.methodCode === "Cash" &&
      methodCode !== "Cash" && cashTipIncluded > 0;
    const tipEnteringCash = row.methodCode !== "Cash" &&
      methodCode === "Cash" && tipRow.methodCode === "Cash" &&
      actualTip > 0 && !invalidTip &&
      !paymentRows.some((item) => item.id !== row.id && item.methodCode === "Cash");
    updateRow(row.id, { methodCode, reference: "", franchise: "", approval: "",
      bankAccountId: "", amount: String(round(Number(row.amount) +
        (tipEnteringCash ? actualTip : 0) - (tipLeavingCash ? actualTip : 0))) });
    previousIncludedCashTip.current = tipEnteringCash ? actualTip :
      tipLeavingCash ? 0 : cashTipIncluded;
  }

  function addPaymentMethod() {
    const method = (catalog.find((option) => option.code !== "Cash" &&
      !paymentRows.some((row) => row.methodCode === option.code))?.code
      ?? catalog.find((option) => option.code !== "Cash")?.code) as Method | undefined;
    if (!method) return;
    setPaymentRows((rows) => {
      if (due > 0) return [...rows, { ...newRow(due), methodCode: method }];
      const sourceIndex = rows.findIndex((row) => Number(row.amount) > 0);
      if (sourceIndex < 0) return rows;
      const share = round(Number(rows[sourceIndex].amount) / 2);
      if (share <= 0) return rows;
      return [...rows.map((row, index) => index === sourceIndex
        ? { ...row, amount: String(round(Number(row.amount) - share)) } : row),
        { ...newRow(share), methodCode: method }];
    });
    setError("");
  }

  function changeSelection(additionId: string, quantity: number, max: number) {
    setQuantities((current) => ({ ...current,
      [additionId]: Math.max(0, Math.min(max, round(quantity))) }));
    setError("");
  }

  function paymentInputs(): PosPaymentInput[] | null {
    if (!referencesReady || selectedTotal <= 0 || due !== 0 ||
        cashTipExceedsTendered ||
        paymentRows.some((row) => !Number.isFinite(Number(row.amount)) ||
          Number(row.amount) <= 0) ||
        paymentRows.filter((row) => row.methodCode === "Cash").length > 1) {
      setError("El pago debe cubrir exactamente los productos seleccionados.");
      return null;
    }
    const rows = paymentRows.filter((row) =>
      row.methodCode !== "Cash" || cashApplied > 0).map((row): PosPaymentInput => ({
      methodCode: row.methodCode,
      amount: row.methodCode === "Cash" ? cashApplied : round(Number(row.amount)),
      reference: row.methodCode === "Transfer" ? row.reference.trim() || null : null,
      cardFranchiseCode: row.methodCode === "DebitCard" ||
        row.methodCode === "CreditCard" ? row.franchise || null : null,
      approvalNumber: row.methodCode === "DebitCard" ||
        row.methodCode === "CreditCard" ? row.approval.trim() || null : null,
      bankAccountId: row.methodCode === "Transfer" && accountingEnabled ?
        row.bankAccountId || null : null,
      tenderedAmount: row.methodCode === "Cash" ? cashSaleTendered : null,
    }));
    if (incompletePaymentDetails || rows.some((row) =>
        row.methodCode === "Cash" && row.tenderedAmount != null &&
          row.tenderedAmount < row.amount)) {
      setError("Completa los datos de tarjeta, transferencia o efectivo.");
      return null;
    }
    if (currentAdjustment !== 0) {
      const index = rows.findLastIndex((row) =>
        row.amount > currentAdjustment);
      if (index < 0) {
        setError("Distribuye el pago para cubrir el ajuste al peso en un medio.");
        return null;
      }
      rows[index] = { ...rows[index],
        amount: round(rows[index].amount - currentAdjustment),
        roundingAdjustment: currentAdjustment };
    }
    return rows;
  }

  function tipPaymentInput(): PosPaymentInput | null | undefined {
    if (invalidTip) { setError("Ingresa un valor de propina válido."); return undefined; }
    if (actualTip === 0) return null;
    if (invalidSeparateTipTendered) {
      setError("Escribe el efectivo entregado para cubrir la propina.");
      return undefined;
    }
    if (incompleteTipDetails) {
      setError("Completa los datos del medio usado para la propina.");
      return undefined;
    }
    return { methodCode: tipRow.methodCode, amount: actualTip,
      reference: tipRow.methodCode === "Transfer" ? tipRow.reference.trim() : null,
      cardFranchiseCode: tipRow.methodCode === "DebitCard" || tipRow.methodCode === "CreditCard" ? tipRow.franchise : null,
      approvalNumber: tipRow.methodCode === "DebitCard" || tipRow.methodCode === "CreditCard" ? tipRow.approval.trim() : null,
      bankAccountId: tipRow.methodCode === "Transfer" && accountingEnabled ? tipRow.bankAccountId : null,
      tenderedAmount: tipRow.methodCode === "Cash"
        ? separateCashTip ? separateTipTendered : actualTip : null };
  }

  async function receivePayment() {
    if (disabled || otherSession) return;
    const payments = summary.remaining > 0 ? paymentInputs() : [];
    if (!payments) return;
    const tipPayment = tipPaymentInput();
    if (tipPayment === undefined || summary.remaining <= 0 && !tipPayment) return;
    setWorking(true);
    setError("");
    try {
      const updated = await onCapture(selection.map(({ additionId, quantity }) =>
        ({ additionId, quantity })), payments, tipPayment);
      setPaymentRows([]);
      setTipAmount("");
      setSeparateTipCashReceived("");
      setQuantities({});
      setSelectionMode("all");
      if (updated.remaining === 0)
        await onFinalize(finalRestaurantPayments(updated.collections), updated.accountVersion,
          printChoice);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message :
        "No se pudo guardar el cobro. Revisa la mesa antes de intentarlo de nuevo.");
    } finally { setWorking(false); }
  }

  async function reversePayment(collectionId: string) {
    setWorking(true);
    setError("");
    try { await onReverse(collectionId); setConfirmReverse(null); }
    catch (cause) { setError(cause instanceof Error ? cause.message :
      "No se pudo anular el cobro."); }
    finally { setWorking(false); }
  }

  async function retryDocument() {
    if (disabled || missingRecordedAdjustment) return;
    setWorking(true);
    setError("");
    try {
      await onFinalize(finalRestaurantPayments(summary.collections), summary.accountVersion, printChoice);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message :
        "El cobro está guardado, pero no se pudo emitir el documento. Reintenta sin cobrar de nuevo.");
    } finally { setWorking(false); }
  }

  return <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/70 p-0 backdrop-blur-sm sm:p-4"
    role="dialog" aria-modal="true" aria-label="Cobrar mesa">
    <div className="flex h-[100dvh] w-full max-w-6xl flex-col overflow-hidden bg-[#f8f7f3] text-slate-950 shadow-2xl dark:bg-slate-950 dark:text-white sm:h-[min(92vh,900px)] sm:rounded-[2rem]">
      <header className="flex shrink-0 items-center justify-between border-b border-slate-200 bg-white px-4 py-4 dark:border-slate-800 dark:bg-slate-900 sm:px-7">
        <div><p className="text-xs font-bold uppercase tracking-[.18em] text-teal-700 dark:text-teal-400">Caja · Restaurante y bar</p>
          <h2 className="mt-1 text-2xl font-extrabold sm:text-3xl">Cobrar mesa</h2></div>
        <Button variant="ghost" size="icon" className="h-12 w-12 rounded-2xl" aria-label="Cerrar cobro" disabled={disabled} onClick={onClose}><X className="h-6 w-6"/></Button>
      </header>
      <div className="flex min-h-0 flex-1 flex-col overflow-y-auto overscroll-contain lg:grid lg:overflow-hidden lg:grid-cols-[minmax(0,1fr)_500px]">
        <div className="shrink-0 p-4 sm:p-7 lg:min-h-0 lg:overflow-y-auto">
          <div className="grid grid-cols-2 gap-2 sm:grid-cols-3 sm:gap-3">
            <div className="col-span-2 sm:col-span-1"><Metric label="Total de la mesa" value={summary.total}/></div>
            <Metric label="Pagado" value={summary.collected + capturedAdjustment} accent="teal"/>
            <Metric label="Por cobrar" value={summary.remaining +
              (summary.remaining > 0 ? roundingAdjustment : 0)} accent="amber"/>
          </div>
          {roundingAdjustment !== 0 && <p className="mt-3 text-sm font-semibold text-slate-600 dark:text-slate-300">Ajuste al peso {roundingAdjustment > 0 ? "+" : ""}{money.format(roundingAdjustment)} · Total a pagar {money.format(summary.total + roundingAdjustment)}</p>}
          {missingRecordedAdjustment && <p role="alert" className="mt-3 rounded-xl border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900">Este cobro anterior se guardó sin el ajuste al peso. Anúlalo y vuelve a recibir el total redondeado antes de emitir el documento.</p>}
          {summary.tipsCollected > 0 && <p className="mt-3 text-sm font-semibold text-teal-800">Propinas recibidas aparte: {money.format(summary.tipsCollected)}</p>}
          {summary.remaining > 0 && <><div className="mt-7 flex flex-wrap items-center justify-between gap-3"><div><h3 className="text-xl font-bold">¿Qué productos paga ahora?</h3><p className="mt-1 text-sm text-slate-500 dark:text-slate-400">Puedes cobrar todo o seleccionar cantidades por persona.</p></div></div>
            <div className="mt-4 grid grid-cols-2 gap-2 rounded-2xl bg-slate-200/70 p-1.5 dark:bg-slate-800" role="group" aria-label="Modo de cobro">
              <ModeButton active={selectionMode === "all"} onClick={() => setSelectionMode("all")} label="Todo lo pendiente"/>
              <ModeButton active={selectionMode === "choose"} onClick={() => { setSelectionMode("choose"); setQuantities({}); }} label="Elegir productos"/>
            </div>
            <div className="mt-4 space-y-2">{[
              ...available.map((line) => ({ line, paid: false })),
              ...paidLines.map((line) => ({ line, paid: true })),
            ].map(({ line, paid }, index) => {
              const chosen = paid ? 0 : selectionMode === "all" ? line.remainingQuantity :
                quantities[line.additionId] ?? 0;
              const unit = line.publicUnitPrice - line.promotionDiscountPerUnit;
              return <Fragment key={`${line.additionId}:${paid}`}>
                {index === 0 && <h4 className="text-sm font-bold text-slate-600 dark:text-slate-300">Pendientes de pago</h4>}
                {paid && index === available.length && <h4 className="pt-3 text-sm font-bold text-teal-700 dark:text-teal-300">Ya pagados</h4>}
                <div className={`flex min-h-20 items-center gap-3 rounded-2xl border px-4 py-3 transition ${paid ? "border-teal-200 bg-teal-50/60 dark:border-teal-900 dark:bg-teal-950/20" : chosen ? "border-teal-500 bg-teal-50 dark:bg-teal-950/40" : "border-slate-200 bg-white dark:border-slate-800 dark:bg-slate-900"}`}>
                <button type="button" disabled={disabled || selectionMode === "all" || paid} onClick={() => changeSelection(line.additionId, chosen ? 0 : line.remainingQuantity, line.remainingQuantity)} className={`grid h-11 w-11 shrink-0 place-items-center rounded-xl border-2 ${paid || chosen ? "border-teal-600 bg-teal-600 text-white" : "border-slate-300"}`} aria-label={paid ? `${line.description} pagado` : `${chosen ? "Desmarcar" : "Marcar"} ${line.description}`}>{(paid || chosen > 0) && <Check className="h-5 w-5"/>}</button>
                <div className="min-w-0 flex-1"><p className="truncate font-semibold">{line.description}</p><p className="text-xs text-slate-500 dark:text-slate-400">{paid ? `${line.coveredQuantity} pagado${line.coveredQuantity !== 1 ? "s" : ""}` : `${line.remainingQuantity} pendiente${line.remainingQuantity !== 1 ? "s" : ""}`} · {money.format(unit)} c/u</p></div>
                {!paid && selectionMode === "choose" && line.remainingQuantity > 1 && !line.allowsFractionalSale ? <div className="flex items-center gap-1"><Button variant="outline" size="icon" className="h-11 w-11 rounded-xl" onClick={() => changeSelection(line.additionId, chosen - 1, line.remainingQuantity)} disabled={disabled} aria-label={`Quitar ${line.description}`}>−</Button><span className="min-w-7 text-center font-bold">{chosen}</span><Button variant="outline" size="icon" className="h-11 w-11 rounded-xl" onClick={() => changeSelection(line.additionId, chosen + 1, line.remainingQuantity)} disabled={disabled} aria-label={`Agregar ${line.description}`}>+</Button></div> : <strong className="whitespace-nowrap">{paid ? money.format(line.coveredQuantity * unit) : money.format(chosen * unit)}</strong>}
                </div>
              </Fragment>;
            })}</div></>}
          {summary.collections.length > 0 && <section className="mt-8"><h3 className="text-lg font-bold">Cobros recibidos</h3><div className="mt-3 space-y-2">{summary.collections.map((entry) => <div key={entry.collectionId} className="flex items-center gap-3 rounded-2xl border border-teal-200 bg-white px-4 py-3 dark:border-teal-900 dark:bg-slate-900"><CheckCircle2 className="h-6 w-6 shrink-0 text-teal-600"/><div className="min-w-0 flex-1"><p className="font-semibold">{money.format(entry.amount + entry.payments.reduce((sum, payment) => sum + (payment.roundingAdjustment ?? 0), 0))} · {entry.payments.map((payment) => paymentLabels.get(payment.methodCode) ?? payment.methodCode).join(" + ")}</p>{entry.tipPayment && <p className="text-sm font-medium text-teal-700">Propina: {money.format(entry.tipPayment.amount)} · {paymentLabels.get(entry.tipPayment.methodCode) ?? entry.tipPayment.methodCode}</p>}<p className="text-xs text-slate-500">{new Date(entry.capturedAt).toLocaleString("es-CO")} · {entry.allocations.length} producto(s)</p></div><Button variant="ghost" size="sm" className="min-h-11 text-red-700" disabled={disabled} onClick={() => setConfirmReverse(entry.collectionId)}><RotateCcw className="mr-1 h-4 w-4"/>Anular</Button></div>)}</div></section>}
        </div>
        <aside className="flex shrink-0 flex-col border-t border-slate-200 bg-white dark:border-slate-800 dark:bg-slate-900 lg:min-h-0 lg:border-l lg:border-t-0">
          <section className="shrink-0 border-b border-slate-200 bg-slate-50 px-4 py-3 dark:border-slate-800 dark:bg-slate-900 sm:px-6" aria-label="Documento de venta">
            <div className="flex items-center justify-between gap-3"><div className="flex min-w-0 items-center gap-3"><span className="grid h-11 w-11 shrink-0 place-items-center rounded-xl bg-teal-100 text-teal-800 dark:bg-teal-900 dark:text-teal-100">{documentType === "SalesInvoice" ? <FileText className="h-5 w-5"/> : <ReceiptText className="h-5 w-5"/>}</span><div className="min-w-0"><p className="text-xs font-bold uppercase tracking-wide text-slate-500 dark:text-slate-400">Documento de venta</p><p className="font-bold leading-tight">{documentType === "SalesInvoice" ? "Factura electrónica" : "Comprobante de venta"}</p></div></div><Button variant="outline" className="min-h-11 shrink-0 rounded-xl" disabled={disabled} onClick={() => setDocumentTypeOpen(true)}>Cambiar documento</Button></div>
          </section>
          <div className="p-3 sm:p-4 lg:min-h-0 lg:flex-1 lg:overflow-y-auto"><p className="text-xs font-bold uppercase tracking-widest text-teal-700 dark:text-teal-400">Pago de esta persona · consumo</p><p className="mt-1 text-3xl font-extrabold">{money.format(selectedTotal + currentAdjustment)}</p>{currentAdjustment !== 0 && <p className="mt-1 text-sm text-slate-600 dark:text-slate-300">Consumo {money.format(selectedTotal)} · ajuste al peso {currentAdjustment > 0 ? "+" : ""}{money.format(currentAdjustment)}</p>}
            <div className="mt-3 rounded-xl border border-teal-200 bg-teal-50/70 p-3 dark:border-teal-900 dark:bg-teal-950/30">
              <label htmlFor="restaurant-actual-tip" className="block text-sm font-bold">Propina voluntaria recibida</label>
              <p className="mt-1 text-xs text-slate-600 dark:text-slate-300">Opcional. Se cobra aparte y no aumenta la factura de consumo.</p>
              {summary.suggestedTipPercent > 0 && (summary.remaining > 0 || summary.tipsCollected === 0) && <Button type="button" size="sm" variant="outline" className="mt-3 min-h-10" disabled={disabled || tipBase <= 0}
                onClick={() => setTipAmount(String(Math.round(tipBase * summary.suggestedTipPercent / 100)))}>
                Sugerir {summary.suggestedTipPercent} % · {money.format(Math.round(tipBase * summary.suggestedTipPercent / 100))}</Button>}
              <Input id="restaurant-actual-tip" inputMode="decimal" className="mt-2 h-10 bg-white text-lg font-bold dark:bg-slate-900" placeholder="0" value={tipAmount}
                onChange={(event) => { setTipAmount(event.target.value); setError(""); }} disabled={disabled}/>
              {actualTip > 0 && !invalidTip && <div className="mt-2 grid gap-2 sm:grid-cols-2"><div className="sm:col-span-2"><p className="mb-1 text-xs font-semibold text-slate-600 dark:text-slate-300">Medio de la propina</p><Select value={tipRow.methodCode} onValueChange={(value) => { setTipRow((row) => ({ ...row, methodCode: value as Method, reference: "", franchise: "", approval: "", bankAccountId: "" })); setSeparateTipCashReceived(""); }}>
                <SelectTrigger aria-label="Medio de la propina" className="h-10 bg-white dark:bg-slate-900"><SelectValue/></SelectTrigger><SelectContent>{catalog.map((method) => <SelectItem key={method.code} value={method.code}>{method.label}</SelectItem>)}</SelectContent></Select></div>
                {(tipRow.methodCode === "DebitCard" || tipRow.methodCode === "CreditCard") && <><Select value={tipRow.franchise} onValueChange={(value) => setTipRow((row) => ({ ...row, franchise: value }))}><SelectTrigger aria-label="Franquicia de la propina" className="h-12"><SelectValue placeholder="Franquicia"/></SelectTrigger><SelectContent>{franchises.map((item) => <SelectItem key={item.code} value={item.code}>{item.label}</SelectItem>)}</SelectContent></Select><Input aria-label="Aprobación de la propina" placeholder="Número de aprobación" value={tipRow.approval} onChange={(event) => setTipRow((row) => ({ ...row, approval: event.target.value }))}/></>}
                {tipRow.methodCode === "Transfer" && <>{accountingEnabled && <Select value={tipRow.bankAccountId} onValueChange={(value) => setTipRow((row) => ({ ...row, bankAccountId: value }))}><SelectTrigger aria-label="Cuenta de la propina" className="h-12"><SelectValue placeholder="Cuenta bancaria"/></SelectTrigger><SelectContent>{banks.map((bank) => <SelectItem key={bank.bankAccountId} value={bank.bankAccountId}>{bank.displayName}</SelectItem>)}</SelectContent></Select>}<Input aria-label="Referencia de la propina" placeholder="Referencia de transferencia" value={tipRow.reference} onChange={(event) => setTipRow((row) => ({ ...row, reference: event.target.value }))}/></>}
                {separateCashTip && <label className="block text-sm font-semibold">Efectivo entregado para la propina<Input inputMode="decimal" className="mt-1 h-12 bg-white text-lg font-bold dark:bg-slate-900" placeholder={String(actualTip)} value={separateTipCashReceived} onChange={(event) => { setSeparateTipCashReceived(event.target.value); setError(""); }}/></label>}
                {separateTipChange > 0 && <p className="font-semibold text-teal-800 dark:text-teal-200">Cambio de la propina: {money.format(separateTipChange)}</p>}
              </div>}
              {actualTip > 0 && <p className="mt-2 text-sm font-semibold">Consumo + propina: {money.format(selectedTotal + currentAdjustment + (invalidTip ? 0 : actualTip))}</p>}
            </div>
            {summary.remaining > 0 && <div className="mt-3 space-y-2">{paymentRows.map((row) => <div key={row.id} className="rounded-xl border bg-slate-50 p-3 dark:bg-slate-950"><div className="flex items-center gap-2"><Select value={row.methodCode} onValueChange={(value) => changePaymentMethod(row, value as Method)}><SelectTrigger aria-label="Medio de pago" className="h-11 flex-1 rounded-xl bg-white text-base dark:bg-slate-900"><SelectValue/></SelectTrigger><SelectContent>{catalog.map((method) => <SelectItem key={method.code} value={method.code} className="min-h-12 text-base">{method.label}</SelectItem>)}</SelectContent></Select><Button variant="ghost" size="icon" className="h-11 w-11 shrink-0 rounded-xl text-slate-500" disabled={paymentRows.length === 1} onClick={() => setPaymentRows((rows) => rows.filter((item) => item.id !== row.id))} aria-label="Quitar medio"><Trash2 className="h-5 w-5"/></Button></div>
                <label className="mt-2 block text-sm font-semibold">{row.methodCode === "Cash" ? cashTipIncluded > 0 ? "Efectivo entregado (incluye propina)" : "Efectivo entregado" : "Valor cobrado por consumo"}<Input inputMode="decimal" className="mt-1 h-11 rounded-xl text-lg font-bold" value={row.amount} onChange={(event) => updateRow(row.id, { amount: event.target.value })}/></label>
                {(row.methodCode === "DebitCard" || row.methodCode === "CreditCard") && <div className="mt-2 grid grid-cols-2 gap-2"><Select value={row.franchise} onValueChange={(value) => updateRow(row.id, { franchise: value })}><SelectTrigger aria-label="Franquicia" className="h-12 rounded-xl"><SelectValue placeholder="Franquicia de tarjeta"/></SelectTrigger><SelectContent>{franchises.map((item) => <SelectItem key={item.code} value={item.code} className="min-h-11">{item.label}</SelectItem>)}</SelectContent></Select><Input aria-label="Número de aprobación" className="h-12 rounded-xl" placeholder="Número de aprobación" value={row.approval} onChange={(event) => updateRow(row.id, { approval: event.target.value })}/></div>}
                {row.methodCode === "Transfer" && <div className="mt-2 grid grid-cols-2 gap-2">{accountingEnabled && <Select value={row.bankAccountId} onValueChange={(value) => updateRow(row.id, { bankAccountId: value })}><SelectTrigger aria-label="Cuenta bancaria" className="h-12 rounded-xl"><SelectValue placeholder="Cuenta bancaria"/></SelectTrigger><SelectContent>{banks.map((bank) => <SelectItem key={bank.bankAccountId} value={bank.bankAccountId} className="min-h-11">{bank.displayName}</SelectItem>)}</SelectContent></Select>}<Input aria-label="Referencia de transferencia" className="h-12 rounded-xl" placeholder="Referencia de transferencia" value={row.reference} onChange={(event) => updateRow(row.id, { reference: event.target.value })}/></div>}
              </div>)}<Button variant="outline" className="min-h-12 w-full rounded-xl" disabled={disabled || !referencesReady || !canAddPaymentMethod || paymentRows.length >= 10 || selectedTotal <= 0} onClick={addPaymentMethod}><Plus className="mr-2 h-5 w-5"/>Agregar otro medio</Button><div className="space-y-1 rounded-xl bg-slate-100 px-4 py-3 text-sm font-semibold dark:bg-slate-800"><div className="flex justify-between"><span>{due > 0 ? "Falta" : due < 0 ? "Excede" : "Pago completo"}</span><span>{money.format(Math.abs(due))}</span></div>{change > 0 && <div className="flex justify-between text-teal-700 dark:text-teal-300"><span>Cambio a entregar</span><span>{money.format(change)}</span></div>}</div></div>}
          </div>
          <footer className="shrink-0 border-t bg-white p-3 dark:bg-slate-900 sm:p-4">{error && <p role="alert" className="mb-3 rounded-xl bg-red-50 p-3 text-sm text-red-800 dark:bg-red-950 dark:text-red-200">{error}</p>}{captureBlockReason && captureBlockReason !== error && <p role="status" className="mb-3 rounded-xl bg-amber-50 p-3 text-sm text-amber-900 dark:bg-amber-950 dark:text-amber-200">{captureBlockReason}</p>}
            {summary.remaining > 0 || actualTip > 0 ? <Button className="min-h-16 w-full rounded-2xl bg-teal-700 text-lg font-bold text-white hover:bg-teal-800" disabled={disabled || !!captureBlockReason || missingRecordedAdjustment} onClick={() => void receivePayment()}><Banknote className="mr-2 h-6 w-6"/>{summary.remaining === 0 ? "Guardar propina y emitir documento" : selectedTotal === summary.remaining ? `Cobrar y emitir ${documentType === "SalesInvoice" ? "factura" : "documento"}` : "Guardar este pago"}</Button> : <Button className="min-h-16 w-full rounded-2xl bg-teal-700 text-lg font-bold text-white hover:bg-teal-800" disabled={disabled || !!documentTypeBlockReason || otherSession || missingRecordedAdjustment} onClick={() => void retryDocument()}><CreditCard className="mr-2 h-6 w-6"/>Reintentar {documentType === "SalesInvoice" ? "factura" : "documento"} único</Button>}
            <div className="mt-3" role="group" aria-label="Imprimir al finalizar"><p className="mb-2 text-xs font-semibold text-slate-500 dark:text-slate-400">Imprimir al finalizar</p><div className="flex flex-wrap gap-1.5">{PAYMENT_PRINT_SHORTCUTS.map(({ key, label, choice }) => <button key={key} type="button" aria-label={label} aria-pressed={printChoice === choice} disabled={disabled} onClick={() => setPrintChoice(choice)} className={`min-h-10 rounded-lg border px-2.5 py-2 text-xs font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-teal-600/30 disabled:opacity-50 ${printChoice === choice ? "border-teal-600 bg-teal-50 text-teal-900 dark:bg-teal-950 dark:text-teal-200" : "border-slate-200 bg-slate-50 text-slate-600 hover:border-teal-300 hover:bg-teal-50 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-300"}`}><span className="font-bold text-teal-700 underline decoration-teal-500/60 decoration-2 underline-offset-4 dark:text-teal-300">{key}</span>{label.slice(1)}</button>)}</div></div>
          </footer>
        </aside>
      </div>
    </div>
    <Dialog open={documentTypeOpen} onOpenChange={(open) => { if (!disabled) setDocumentTypeOpen(open); }}>
      <DialogContent className="max-w-2xl overflow-hidden rounded-3xl p-0" aria-label="Tipo de documento">
        <div className="border-b px-6 py-5"><p className="text-xs font-bold uppercase tracking-[.18em] text-teal-700 dark:text-teal-400">Caja · Restaurante y bar</p><DialogTitle className="mt-1 text-2xl">Tipo de documento</DialogTitle><DialogDescription className="mt-2">Elige el documento que se emitirá al cobrar toda la mesa.</DialogDescription></div>
        <div className="grid gap-3 p-5 sm:grid-cols-2 sm:p-6">{([ ["SalesInvoice", "Factura electrónica", "Documento electrónico enviado a la DIAN.", FileText], ["SalesReceipt", "Comprobante de venta", "Comprobante de la venta de esta mesa.", ReceiptText] ] as const).map(([type, label, description, Icon]) => <button key={type} type="button" autoFocus={documentType === type} aria-pressed={documentType === type} disabled={disabled} onClick={() => { onChangeDocumentType(type); setDocumentTypeOpen(false); }} className={`relative min-h-40 rounded-2xl border-2 p-5 text-left transition-all focus-visible:outline-4 focus-visible:outline-teal-500/30 disabled:opacity-60 ${documentType === type ? "border-teal-500 bg-teal-50 shadow-sm dark:bg-teal-950/40" : "bg-card hover:border-teal-300 hover:shadow-md"}`}><span className={`grid h-11 w-11 place-items-center rounded-xl ${documentType === type ? "bg-teal-600 text-white" : "bg-slate-100 text-slate-700 dark:bg-slate-800 dark:text-slate-100"}`}><Icon className="h-5 w-5"/></span><span className="mt-4 block text-lg font-bold">{label}</span><span className="mt-1 block text-sm text-muted-foreground">{description}</span>{documentType === type && <span className="absolute right-4 top-4 grid h-7 w-7 place-items-center rounded-full bg-teal-600 text-white"><Check className="h-4 w-4"/></span>}</button>)}</div>
      </DialogContent>
    </Dialog>
    {confirmReverse && <div className="fixed inset-0 z-[60] grid place-items-center bg-slate-950/70 p-4" role="alertdialog" aria-modal="true" aria-label="Anular cobro"><div className="w-full max-w-md rounded-3xl bg-white p-6 text-slate-950 shadow-2xl"><h3 className="text-xl font-bold">¿Anular este cobro?</h3><p className="mt-2 text-sm text-slate-600">Confirma que devolviste el dinero. Las cantidades quedarán disponibles para cobrar otra vez y la acción quedará auditada.</p><div className="mt-6 flex justify-end gap-2"><Button variant="outline" className="min-h-12" disabled={disabled} onClick={() => setConfirmReverse(null)}>Volver</Button><Button className="min-h-12 bg-red-700 hover:bg-red-800" disabled={disabled} onClick={() => void reversePayment(confirmReverse)}>Anular cobro</Button></div></div></div>}
  </div>;
}

function Metric({ label, value, accent }: { label: string; value: number;
  accent?: "teal" | "amber" }) {
  return <div className={`min-w-0 rounded-2xl border p-2.5 sm:p-4 ${accent === "teal" ?
    "border-teal-200 bg-teal-50 dark:border-teal-900 dark:bg-teal-950/40" :
    accent === "amber" ? "border-amber-200 bg-amber-50 dark:border-amber-900 dark:bg-amber-950/40" :
      "border-slate-200 bg-white dark:border-slate-800 dark:bg-slate-900"}`}>
    <p className="text-xs font-semibold text-slate-500 dark:text-slate-400">{label}</p>
    <p className="mt-1 break-words text-xl font-extrabold">{money.format(value)}</p>
  </div>;
}

function ModeButton({ active, onClick, label }: { active: boolean;
  onClick: () => void; label: string }) {
  return <button type="button" onClick={onClick} className={`min-h-13 rounded-xl px-3 text-sm font-bold transition ${active ?
    "bg-white text-teal-800 shadow-sm dark:bg-slate-700 dark:text-teal-300" :
    "text-slate-600 hover:bg-white/60 dark:text-slate-300"}`}>{label}</button>;
}
