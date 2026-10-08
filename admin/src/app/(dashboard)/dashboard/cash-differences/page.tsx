"use client";

import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertCircle, Banknote, Check, CheckCircle2, ChevronDown, ChevronUp, Clock3, CreditCard, Landmark, Loader2, Pencil, ReceiptText, Scale, TimerReset, X } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { DatePicker } from "@/components/ui/date-picker";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import { useReferenceOptions } from "@/hooks/use-reference-options";
import { workSessionDifferencesApi, type ClosurePaymentCorrection, type ClosurePaymentTotal, type ClosurePaymentVerification, type WorkSessionClosure } from "@/services/api/work-session-differences";
import { tenantsApi } from "@/services/api/tenants";
import { InvoiceChargeSummary } from "@/app/(pos)/pos/pos-invoice-charge-summary";
import { PosCashClosureDialog } from "@/app/(pos)/pos/pos-cash-closure-dialog";
import { PosEdgeClient, readEdgeTokenFromLaunch, readEdgeUserSession, type PosAuthorizedClosurePreview, type PosWorkSessionPaymentCount } from "@/services/pos/pos-edge-client";
import { formatWorkSessionCountInput, normalizeWorkSessionCountInput, printWorkSessionClosure, workSessionPaymentMethodName } from "@/services/pos/pos-work-session-close";
import { cashClosurePaymentGroups, cashClosureVerificationDecisions, correctedCashClosureAmount, isCashClosureMethodConfirmed, requiresIndividualCashClosureVerification } from "@/services/pos/cash-closure-reconciliation";
import { useAuthStore } from "@/stores/auth-store";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });
const isoDate = (value: Date) => `${value.getFullYear()}-${String(value.getMonth() + 1).padStart(2, "0")}-${String(value.getDate()).padStart(2, "0")}`;
const result = (difference: number | null) => difference == null ? "Sin conteo" : difference > 0 ? `Sobrante ${money.format(difference)}` : difference < 0 ? `Faltante ${money.format(Math.abs(difference))}` : "Cuadra";
const accountingStatusLabels: Record<string, string> = { AccountingDisabled: "No requiere asiento", AccountingPendingConfiguration: "Falta configuración contable", NotRequired: "No requiere asiento", Pending: "Pendiente de contabilizar", Processing: "Contabilizando", Posted: "Contabilizado", Failed: "Error contable" };
const accountingStatusName = (value: string) => accountingStatusLabels[value] ?? "Pendiente de contabilizar";
export default function CashClosuresPage() {
  const canReadOwnSession = useAuthStore(state => state.user?.permissions.includes("work-sessions.read") ?? false);
  const canReconcile = useAuthStore(state => state.user?.permissions.includes("work-sessions.closures.reconcile") ?? false);
  const today = useMemo(() => new Date(), []);
  const monthStart = useMemo(() => new Date(today.getFullYear(), today.getMonth(), 1), [today]);
  const [from, setFrom] = useState(isoDate(monthStart));
  const [to, setTo] = useState(isoDate(today));
  const [status, setStatus] = useState("Pending");
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<WorkSessionClosure | null>(null);
  const closures = useQuery({ queryKey: ["work-session-closures", from, to, status, page], queryFn: () => workSessionDifferencesApi.listClosures(from, to, status === "all" ? undefined : status, page, 50), enabled: Boolean(from && to && from <= to) });
  const rows = closures.data?.items ?? [];
  const pending = rows.filter(row => row.reconciliationStatus === "Pending").length;

  return <div className="space-y-5">
    <header className="rounded-3xl bg-gradient-to-r from-slate-950 via-teal-950 to-cyan-700 p-6 text-white shadow-lg"><p className="text-xs font-bold uppercase tracking-[.2em] text-teal-200">Control y conciliación</p><h1 className="mt-2 text-3xl font-black">Cierres de sesión</h1><p className="mt-2 max-w-3xl text-sm text-teal-50/80">Revisa las ventas y devoluciones, confirma los demás movimientos y cuadra el efectivo antes de conciliar.</p></header>
    {canReadOwnSession && <ActiveWorkSessionPanel />}
    <div className="grid gap-4 sm:grid-cols-3"><Summary title="Cierres encontrados" value={closures.isError ? "—" : String(closures.data?.totalItems ?? 0)} icon={<Scale className="h-5 w-5" />} /><Summary title="Pendientes en esta página" value={closures.isError ? "—" : String(pending)} icon={<Clock3 className="h-5 w-5 text-amber-600" />} /><Summary title="Otros estados en esta página" value={closures.isError ? "—" : String(rows.length - pending)} icon={<CheckCircle2 className="h-5 w-5 text-emerald-600" />} /></div>
    <Card className="overflow-hidden rounded-3xl"><CardHeader className="gap-5 border-b"><div><CardTitle>Historial de cierres</CardTitle><p className="mt-1 text-sm text-muted-foreground">Cada cierre conserva el cuadre y su resultado contable.</p></div><div className="grid w-full max-w-4xl gap-3 md:grid-cols-3"><div className="min-w-0 space-y-1.5"><Label htmlFor="closure-from">Desde</Label><DatePicker id="closure-from" value={from} onChange={value => { setFrom(value); setPage(1); }} /></div><div className="min-w-0 space-y-1.5"><Label htmlFor="closure-to">Hasta</Label><DatePicker id="closure-to" value={to} onChange={value => { setTo(value); setPage(1); }} /></div><div className="min-w-0 space-y-1.5"><Label htmlFor="closure-status">Estado</Label><Select value={status} onValueChange={value => { setStatus(value); setPage(1); }}><SelectTrigger id="closure-status" aria-label="Estado del cierre" className="w-full"><SelectValue /></SelectTrigger><SelectContent><SelectItem value="Pending">Pendientes</SelectItem><SelectItem value="Partial">Parciales</SelectItem><SelectItem value="Reconciled">Conciliados</SelectItem><SelectItem value="ReconciledWithDifferences">Conciliados con diferencias</SelectItem><SelectItem value="all">Todos</SelectItem></SelectContent></Select></div></div></CardHeader><CardContent className="p-0">
      {closures.isLoading && <div className="flex items-center justify-center gap-2 p-10 text-muted-foreground"><Loader2 className="h-5 w-5 animate-spin" />Consultando cierres…</div>}{closures.isError && <div className="flex items-center justify-between gap-4 p-8 text-sm text-destructive"><span>No fue posible consultar los cierres.</span><Button variant="outline" onClick={() => void closures.refetch()}>Reintentar</Button></div>}{!closures.isLoading && !closures.isError && !rows.length && <p className="p-10 text-center text-sm text-muted-foreground">No hay cierres en el periodo.</p>}
      {!!rows.length && <div className="overflow-x-auto"><table className="w-full min-w-[1040px] text-sm"><thead className="bg-muted/50 text-xs uppercase tracking-wide text-muted-foreground"><tr><th className="px-5 py-4 text-left">Cierre / inicio</th><th className="px-4 py-4 text-left">Responsable / sede</th><th className="px-4 py-4 text-right">Ventas</th><th className="px-4 py-4 text-right">Devoluciones</th><th className="px-4 py-4 text-left">Resultado</th><th className="px-4 py-4 text-left">Estado</th><th className="px-5 py-4 text-center">Acción</th></tr></thead><tbody>{rows.map(row => <tr key={row.workSessionClosureId} className="border-t align-middle hover:bg-muted/20"><td className="whitespace-nowrap px-5 py-4 font-medium">{new Date(row.closedAt).toLocaleString("es-CO")}<small className="mt-1 block font-normal text-muted-foreground">Inició {new Date(row.openedAt).toLocaleString("es-CO")}</small></td><td className="px-4 py-4"><strong>{row.userName}</strong><small className="mt-1 block text-muted-foreground">{row.businessName} · {row.warehouseName}</small></td><td className="px-4 py-4 text-right font-semibold">{money.format(row.totalSales)}<small className="mt-1 block font-normal text-muted-foreground">{row.salesCount} ventas · {row.creditSalesCount} a crédito</small></td><td className="px-4 py-4 text-right font-semibold">{money.format(row.totalRefunds)}<small className="mt-1 block font-normal text-muted-foreground">{row.returnCount} devoluciones</small></td><td className="px-4 py-4">{row.paymentTotals.filter(item => item.requiresCount).map(item => <small key={item.paymentMethodCode} className={`block font-semibold ${item.difference && item.difference < 0 ? "text-red-700" : item.difference && item.difference > 0 ? "text-emerald-700" : "text-slate-600"}`}>{workSessionPaymentMethodName(item.paymentMethodCode)}: {result(item.difference)}</small>)}</td><td className="px-4 py-4"><Status value={row.reconciliationStatus} /><small className="mt-2 block text-muted-foreground">{accountingStatusName(row.accountingStatus)}</small></td><td className="px-5 py-4 text-center"><Button size="sm" variant={row.reconciliationStatus === "Pending" && canReconcile ? "default" : "outline"} onClick={() => setSelected(row)}>{row.reconciliationStatus === "Pending" && canReconcile ? "Conciliar" : "Ver cierre"}</Button></td></tr>)}</tbody></table></div>}
      {!!closures.data && <div className="flex items-center justify-between border-t p-4"><small className="text-muted-foreground">{closures.data.totalItems} cierres</small><div className="flex gap-2"><Button variant="outline" size="sm" disabled={page === 1} onClick={() => setPage(value => value - 1)}>Anterior</Button><Button variant="outline" size="sm" disabled={page * 50 >= closures.data.totalItems} onClick={() => setPage(value => value + 1)}>Siguiente</Button></div></div>}
    </CardContent></Card>{selected && <ReconciliationDialog closure={selected} canReconcile={canReconcile} onClose={() => setSelected(null)} />}
  </div>;
}

function ActiveWorkSessionPanel() {
  const queryClient = useQueryClient();
  const canClose = useAuthStore(state => state.user?.permissions.includes("work-sessions.close") ?? false);
  const current = useQuery({
    queryKey: ["work-session-current"],
    queryFn: workSessionDifferencesApi.current,
    staleTime: 0,
  });
  const [preview, setPreview] = useState<PosAuthorizedClosurePreview | null>(null);
  const [busy, setBusy] = useState(false);
  const [submitted, setSubmitted] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const operationId = useRef<string | null>(null);
  const active = current.data;

  const openClosure = async () => {
    if (!active) return;
    setBusy(true);
    setError(null);
    try {
      setPreview({
        authorizationToken: crypto.randomUUID(),
        preview: await workSessionDifferencesApi.previewCurrent(active.workSessionId),
      });
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "No fue posible preparar el cierre.");
    } finally {
      setBusy(false);
    }
  };

  const close = async (paymentCounts: PosWorkSessionPaymentCount[], note: string | null) => {
    if (!active) return;
    setBusy(true);
    setSubmitted(true);
    setError(null);
    operationId.current ??= crypto.randomUUID();
    try {
      const closure = await workSessionDifferencesApi.closeCurrent(
        active.workSessionId, paymentCounts, note, operationId.current);
      const branding = await tenantsApi.getBranding().catch(() => null);
      const printable = {
        ...closure,
        companyName: branding?.displayName ?? branding?.legalName ?? closure.businessName,
        logoUrl: branding?.logoUrl ?? null,
      };
      const edgeToken = readEdgeTokenFromLaunch();
      if (edgeToken) {
        await new PosEdgeClient(edgeToken, readEdgeUserSession())
          .printWorkSessionClosure(printable)
          .catch(async () => {
            const receipt = await workSessionDifferencesApi.closureReceipt(
              closure.workSessionId, printable.companyName, printable.logoUrl);
            await printWorkSessionClosure(receipt.html);
          });
      } else {
        const receipt = await workSessionDifferencesApi.closureReceipt(
          closure.workSessionId, printable.companyName, printable.logoUrl);
        await printWorkSessionClosure(receipt.html);
      }
      setPreview(null);
      setSubmitted(false);
      operationId.current = null;
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["work-session-current"] }),
        queryClient.invalidateQueries({ queryKey: ["work-session-closures"] }),
      ]);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "No fue posible cerrar e imprimir la sesión.");
    } finally {
      setBusy(false);
    }
  };

  if (current.isLoading)
    return <Card className="rounded-3xl"><CardContent className="flex items-center gap-3 p-6 text-muted-foreground"><Loader2 className="h-5 w-5 animate-spin" />Consultando la sesión operativa…</CardContent></Card>;
  if (current.isError)
    return <Card className="rounded-3xl border-red-200"><CardContent className="flex items-center justify-between gap-4 p-6"><span className="text-sm text-red-800">No fue posible consultar la sesión activa.</span><Button variant="outline" onClick={() => void current.refetch()}>Reintentar</Button></CardContent></Card>;
  if (!active)
    return <Card className="rounded-3xl border-dashed"><CardContent className="flex items-center gap-4 p-6"><span className="grid h-11 w-11 place-items-center rounded-2xl bg-slate-100 text-slate-600"><TimerReset className="h-5 w-5" /></span><div><Badge variant="secondary" className="mb-2">Caja cerrada</Badge><strong className="block">No tienes una sesión operativa abierta</strong><p className="mt-1 text-sm text-muted-foreground">Se abrirá automáticamente cuando inicies una operación que maneje dinero.</p></div></CardContent></Card>;

  return <>
    <Card className="overflow-hidden rounded-3xl border-teal-200 bg-gradient-to-r from-white via-teal-50/70 to-cyan-50 shadow-sm">
      <CardContent className="grid gap-5 p-6 md:grid-cols-[auto_1fr_auto] md:items-center">
        <span className="grid h-14 w-14 place-items-center rounded-2xl bg-teal-700 text-white shadow-md"><TimerReset className="h-6 w-6" /></span>
        <div><Badge className="mb-2 bg-emerald-100 text-emerald-900 hover:bg-emerald-100">Sesión activa</Badge><h2 className="text-xl font-black text-slate-950">{active.businessName}</h2><div className="mt-2 flex flex-wrap gap-x-5 gap-y-1 text-sm text-slate-600"><span>Abierta <strong className="text-slate-900">{new Date(active.openedAt).toLocaleString("es-CO")}</strong></span><span>Duración <strong className="text-slate-900">{elapsed(active.openedAt)}</strong></span><span>Responsable <strong className="text-slate-900">{active.userName}</strong></span></div><p className="mt-2 text-sm text-slate-600">Ventas, devoluciones, recaudos y movimientos de dinero seguirán acumulándose aquí hasta el cierre.</p>{error && <p className="mt-3 text-sm font-medium text-red-700">{error}</p>}</div>
        {canClose ? <Button className="h-12 px-6" disabled={busy} onClick={() => void openClosure()}>{busy ? <><Loader2 className="mr-2 h-4 w-4 animate-spin" />Preparando…</> : "Cerrar sesión ahora"}</Button> : <p className="max-w-48 text-sm text-muted-foreground">No tienes permiso para cerrar esta sesión.</p>}
      </CardContent>
    </Card>
    {preview && <PosCashClosureDialog value={preview} busy={busy} submitted={submitted} onClose={() => { if (!busy) { setPreview(null); setSubmitted(false); operationId.current = null; } }} onConfirm={close} />}
  </>;
}

function elapsed(openedAt: string): string {
  const totalMinutes = Math.max(0, Math.floor((Date.now() - new Date(openedAt).getTime()) / 60_000));
  const days = Math.floor(totalMinutes / 1440);
  const hours = Math.floor((totalMinutes % 1440) / 60);
  const minutes = totalMinutes % 60;
  return days ? `${days} d ${hours} h` : hours ? `${hours} h ${minutes} min` : `${minutes} min`;
}

type VerificationStatus = "Verified" | "Missing";

function ReconciliationDialog({ closure, canReconcile, onClose }: { closure: WorkSessionClosure; canReconcile: boolean; onClose: () => void }) {
  const queryClient = useQueryClient();
  const countable = closure.paymentTotals.filter(item => item.requiresCount);
  const editable = canReconcile && closure.reconciliationStatus === "Pending";
  const verificationQuery = useQuery({ queryKey: ["work-session-payment-verification-groups", closure.workSessionClosureId], queryFn: () => workSessionDifferencesApi.listPaymentVerifications(closure.workSessionClosureId, 1, 1), staleTime: Infinity, refetchOnWindowFocus: false });
  const snapshotQuery = useQuery({ queryKey: ["work-session-closure-snapshot", closure.workSessionId],
    queryFn: () => workSessionDifferencesApi.closureSnapshot(closure.workSessionId), staleTime: Infinity,
    refetchOnWindowFocus: false, retry: false });
  const invoiceCharges = snapshotQuery.data?.invoiceCharges ?? [];
  const [loadedItems, setLoadedItems] = useState<Record<string, ClosurePaymentVerification>>({});
  const verificationItems = useMemo(() => Object.values(loadedItems), [loadedItems]);
  const onItemsLoaded = useCallback((items: ClosurePaymentVerification[]) => setLoadedItems(current => {
    const pending = items.filter(item => current[item.verificationKey] === undefined);
    if (!pending.length) return current;
    return { ...current, ...Object.fromEntries(pending.map(item => [item.verificationKey, item])) };
  }), []);
  const [verified, setVerified] = useState<Record<string, string>>(() => Object.fromEntries(countable.map(item => [item.paymentMethodCode, String(item.countedAmount ?? item.netAmount)])));
  const [confirmed, setConfirmed] = useState<Record<string, boolean>>({});
  const [verificationStatus, setVerificationStatus] = useState<Record<string, VerificationStatus>>({});
  const [paymentCorrections, setPaymentCorrections] = useState<Record<string, ClosurePaymentCorrection>>({});
  const displayCorrections = useMemo(() => ({
    ...Object.fromEntries(verificationItems.filter(item => item.correctedPaymentMethodCode && item.correctedAmount != null)
      .map(item => [item.verificationKey, {
        verificationKey: item.verificationKey, paymentMethodCode: item.correctedPaymentMethodCode!,
        amount: item.correctedAmount!, reason: item.correctionReason ?? "",
        tenderMethodCode: item.correctedTenderMethodCode,
        cardFranchiseCode: item.correctedCardFranchiseCode,
        approvalNumber: item.correctedApprovalNumber, reference: item.correctedReference,
      }])),
    ...paymentCorrections,
  }), [verificationItems, paymentCorrections]);
  const byMethod = useMemo(() => verificationItems.reduce<Record<string, ClosurePaymentVerification[]>>((groups, item) => {
    const method = displayCorrections[item.verificationKey]?.paymentMethodCode ?? item.paymentMethodCode;
    (groups[method] ??= []).push(item);
    return groups;
  }, {}), [verificationItems, displayCorrections]);
  const effectiveVerificationItems = useMemo(() => verificationItems.map(item => {
    const method = displayCorrections[item.verificationKey]?.paymentMethodCode;
    return method ? { ...item, paymentMethodCode: method } : item;
  }), [verificationItems, displayCorrections]);
  const [editingCorrection, setEditingCorrection] = useState<ClosurePaymentVerification | null>(null);
  const [expandedMethods, setExpandedMethods] = useState<Record<string, boolean>>({});
  const [note, setNote] = useState("");
  const reasons = useReferenceOptions("cash-reconciliation-reason", editable);
  const [reasonCode, setReasonCode] = useState("COUNT_DIFFERENCE");
  const changeVerificationStatus = (key: string, status: VerificationStatus) => {
    setVerificationStatus(current => ({ ...current, [key]: status }));
    if (status === "Missing") setPaymentCorrections(current => {
      if (!current[key]) return current;
      const next = { ...current }; delete next[key]; return next;
    });
  };
  useEffect(() => {
    const persisted = Object.fromEntries(
      verificationItems
        .filter((item): item is ClosurePaymentVerification & { status: VerificationStatus } =>
          item.status === "Verified" || item.status === "Missing")
        .map(item => [item.verificationKey, item.status]),
    );
    if (Object.keys(persisted).length) setVerificationStatus(current => ({ ...persisted, ...current }));
  }, [verificationItems]);
  const methodItems = (code: string) => byMethod[code] ?? [];
  const requiredVerificationItems = cashClosureVerificationDecisions(verificationItems);
  const groupCount = (code: string, movementType?: string) => (verificationQuery.data?.groups ?? [])
    .filter(group => group.paymentMethodCode === code && (!movementType || group.movementType === movementType))
    .reduce((total, group) => total + group.count, 0);
  const groupTotal = (code: string, movementType?: string) => (verificationQuery.data?.groups ?? [])
    .filter(group => group.paymentMethodCode === code && (!movementType || group.movementType === movementType))
    .reduce((total, group) => total + group.totalAmount, 0);
  const displayedGroup = (code: string, movementType: string) => {
    if (!editable) {
      const group = verificationQuery.data?.groups.find(value =>
        value.paymentMethodCode === code && value.movementType === movementType);
      return { count: group?.effectiveCount ?? group?.count ?? 0,
        total: group?.effectiveTotalAmount ?? group?.totalAmount ?? 0 };
    }
    let count = groupCount(code, movementType);
    let total = groupTotal(code, movementType);
    for (const correction of Object.values(paymentCorrections)) {
      const original = loadedItems[correction.verificationKey];
      if (!original || original.movementType !== movementType) continue;
      if (original.paymentMethodCode === code) { count--; total -= original.amount; }
      if (correction.paymentMethodCode === code) { count++; total += correction.amount; }
    }
    return { count, total };
  };
  const groupRequiresVerification = (code: string) => (verificationQuery.data?.groups ?? [])
    .some(group => group.paymentMethodCode === code && requiresIndividualCashClosureVerification({
      verificationKey: "", paymentMethodCode: code, movementType: group.movementType,
      amount: group.totalAmount,
    })) || effectiveVerificationItems.some(item => item.paymentMethodCode === code &&
      requiresIndividualCashClosureVerification(item));
  const creditItems = methodItems("Credit").filter(item => item.movementType === "CreditSale");
  const methodVerifiedAmount = (code: string) => correctedCashClosureAmount(
    code, verificationItems, verificationStatus, displayCorrections,
    Number(verified.Cash ?? 0), Object.fromEntries((verificationQuery.data?.groups ?? [])
      .filter(group => group.movementType === "Sale" || group.movementType === "Refund")
      .map(group => [group.paymentMethodCode, groupTotal(group.paymentMethodCode, "Sale") + groupTotal(group.paymentMethodCode, "Refund")])));
  const methodIsConfirmed = (code: string) => isCashClosureMethodConfirmed(
    code, effectiveVerificationItems, verificationStatus,
    (confirmed[code] ?? false) || groupCount(code) > 0 && !groupRequiresVerification(code));
  const expectedAmount = (item: ClosurePaymentTotal) => item.paymentMethodCode === "Cash"
    ? closure.expectedCash ?? item.netAmount : item.netAmount;
  const differences = Object.fromEntries(countable.map(item => [item.paymentMethodCode,
    methodVerifiedAmount(item.paymentMethodCode) - expectedAmount(item)]));
  const adjustedDifferences = { ...differences };
  for (const item of Object.values(displayCorrections)) { const original = loadedItems[item.verificationKey]; if (original) { adjustedDifferences[original.paymentMethodCode] = (adjustedDifferences[original.paymentMethodCode] ?? 0) + original.amount; adjustedDifferences[item.paymentMethodCode] = (adjustedDifferences[item.paymentMethodCode] ?? 0) - item.amount; } }
  const mutation = useMutation({ mutationFn: () => workSessionDifferencesApi.reconcile(closure.workSessionClosureId, { lines: countable.map(item => ({ paymentMethodCode: item.paymentMethodCode, verifiedAmount: methodVerifiedAmount(item.paymentMethodCode), isConfirmed: methodIsConfirmed(item.paymentMethodCode), reasonCode: adjustedDifferences[item.paymentMethodCode] === 0 ? null : reasonCode })), paymentVerifications: requiredVerificationItems.map(item => ({ verificationKey: item.verificationKey, status: verificationStatus[item.verificationKey]! })), paymentCorrections: Object.values(paymentCorrections), reclassifications: [], note: note.trim() || null }), onSuccess: async () => { void queryClient.invalidateQueries({ queryKey: ["work-session-payment-verification-groups", closure.workSessionClosureId], refetchType: "none" }); void queryClient.invalidateQueries({ queryKey: ["work-session-payment-verifications", closure.workSessionClosureId], refetchType: "none" }); await queryClient.invalidateQueries({ queryKey: ["work-session-closures"] }); onClose(); } });
  const requiredTotal = (verificationQuery.data?.groups ?? [])
    .filter(group => requiresIndividualCashClosureVerification({
      verificationKey: "", paymentMethodCode: group.paymentMethodCode,
      movementType: group.movementType, amount: group.totalAmount,
    }))
    .reduce((total, group) => total + group.count, 0);
  const allVerificationDecisionsMade = requiredVerificationItems.length === requiredTotal &&
    requiredVerificationItems.every(item => verificationStatus[item.verificationKey] !== undefined);
  const valid = !verificationQuery.isLoading && !verificationQuery.isError && allVerificationDecisionsMade && countable.every(item => { const amount = methodVerifiedAmount(item.paymentMethodCode); return Number.isFinite(amount) && (item.paymentMethodCode !== "Cash" || amount >= 0) && methodIsConfirmed(item.paymentMethodCode); });

  return <Dialog open onOpenChange={open => !open && onClose()}><DialogContent className="flex max-h-[94vh] max-w-4xl flex-col overflow-hidden p-0"><DialogHeader className="shrink-0 border-b bg-gradient-to-r from-slate-950 to-teal-900 p-6 text-left text-white"><DialogTitle className="text-xl text-white">{editable ? "Conciliar cierre" : "Detalle del cierre"}</DialogTitle><DialogDescription className="text-slate-200">{closure.userName} · {closure.businessName} · {new Date(closure.closedAt).toLocaleString("es-CO")}</DialogDescription></DialogHeader><div className="min-h-0 flex-1 space-y-5 overflow-y-auto px-6 pb-2">
    {verificationQuery.isLoading && <div className="flex items-center justify-center gap-2 rounded-2xl bg-muted p-5 text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin" />Cargando comprobantes…</div>}{verificationQuery.isError && <div className="flex items-center gap-2 rounded-2xl border border-red-200 bg-red-50 p-4 text-sm text-red-800"><AlertCircle className="h-5 w-5" />No fue posible cargar los comprobantes. No se habilitará la conciliación.</div>}
    {countable.map(item => {
      const items = methodItems(item.paymentMethodCode);
      const cash = item.paymentMethodCode === "Cash";
      const groups = cashClosurePaymentGroups(items, cash);
      const difference = adjustedDifferences[item.paymentMethodCode];
      const reportedDifference = cash ? difference :
        methodVerifiedAmount(item.paymentMethodCode) - (item.countedAmount ?? expectedAmount(item)) +
        difference - differences[item.paymentMethodCode];
      const headlineDifference = !cash && difference === 0 && reportedDifference !== 0
        ? reportedDifference : difference;
      return <section key={item.paymentMethodCode} className="overflow-hidden rounded-2xl border bg-white shadow-sm">
        <div className="flex flex-col gap-3 border-b bg-slate-50 p-4 sm:flex-row sm:items-center sm:justify-between"><div className="flex items-center gap-3"><div className="rounded-xl bg-teal-100 p-2 text-teal-800"><PaymentIcon code={item.paymentMethodCode} /></div><div><strong className="text-base">{workSessionPaymentMethodName(item.paymentMethodCode)}</strong><p className="text-sm text-muted-foreground">Esperado {money.format(expectedAmount(item))} · reportado al cerrar {money.format(item.countedAmount ?? 0)}</p></div></div><div className={`text-left sm:text-right ${headlineDifference < 0 ? "text-red-700" : headlineDifference > 0 ? "text-emerald-700" : "text-slate-700"}`}><span className="block text-xs font-semibold uppercase tracking-wide">{!cash && difference === 0 && reportedDifference !== 0 ? "Frente a lo reportado" : "Frente a lo esperado"}</span><strong>{result(headlineDifference)}</strong>{!cash && difference !== 0 && reportedDifference !== 0 && <small className="block text-muted-foreground">Frente a lo reportado: {result(reportedDifference)}</small>}</div></div>
        {snapshotQuery.isSuccess && <InvoiceChargeSummary charges={invoiceCharges} paymentMethod={item.paymentMethodCode} />}
        <div className="space-y-4 p-5">
          <p className="text-sm text-muted-foreground">{cash ? "Revisa las facturas, devoluciones, abonos, pagos a proveedores, entradas y salidas. El efectivo confirmado es el valor que realmente contaste." : "Revisa facturas, devoluciones, abonos a cartera y pagos a proveedores. Las ventas y devoluciones son informativas."}</p>
          {groups.map(group => {
            const expansionKey = `${item.paymentMethodCode}:${group.key}`;
            const requiresVerification = group.key !== "Sale" && group.key !== "Refund";
            const displayed = displayedGroup(item.paymentMethodCode, group.key);
            const total = Math.abs(displayed.total);
            const count = displayed.count;
            const savedSummary = verificationQuery.data?.groups.find(value =>
              value.paymentMethodCode === item.paymentMethodCode && value.movementType === group.key);
            const reviewed = editable ? group.items.filter(value => verificationStatus[value.verificationKey]).length
              : savedSummary?.effectiveReviewedCount ?? savedSummary?.reviewedCount ?? 0;
            const reviewedTotal = editable ? group.items.filter(value => verificationStatus[value.verificationKey] === "Verified")
              .reduce((sum, value) => {
                const correction = displayCorrections[value.verificationKey];
                return sum + Math.abs(correction?.amount ?? value.amount);
              }, 0) : savedSummary?.effectiveReviewedAmount ?? savedSummary?.reviewedAmount ?? 0;
            const fullyReviewed = count > 0 && reviewed === count;
            const reviewedDifference = reviewedTotal - total;
            return <div key={group.key} className="overflow-hidden rounded-xl border">
              <button type="button" className="flex w-full flex-wrap items-center justify-between gap-3 bg-white p-3 text-left transition hover:bg-slate-50" onClick={() => setExpandedMethods(current => ({ ...current, [expansionKey]: !current[expansionKey] }))}><span><strong>{group.label}</strong><small className="mt-1 block text-muted-foreground">{count} registro{count === 1 ? "" : "s"} · Total {money.format(total)}{requiresVerification ? ` · Revisado ${money.format(reviewedTotal)} (${reviewed} de ${count})` : ""}</small></span><span className="inline-flex items-center gap-2 text-sm font-semibold text-teal-800">{requiresVerification && <Badge variant={fullyReviewed && reviewedDifference === 0 ? "default" : "secondary"} className={fullyReviewed && reviewedDifference === 0 ? "bg-emerald-700" : fullyReviewed ? "bg-amber-100 text-amber-900" : ""}>{count === 0 ? "Sin movimientos" : fullyReviewed ? `Revisión completa · ${result(reviewedDifference)}` : `Pendientes ${count - reviewed}`}</Badge>}{expandedMethods[expansionKey] ? "Ocultar" : "Ver detalle"}{expandedMethods[expansionKey] ? <ChevronUp className="h-4 w-4" /> : <ChevronDown className="h-4 w-4" />}</span></button>
              {expandedMethods[expansionKey] && <PagedVerificationRows closureId={closure.workSessionClosureId} paymentMethodCode={item.paymentMethodCode} movementType={group.key} informational={!requiresVerification} disabled={!editable} queryEnabled={!editable || groupCount(item.paymentMethodCode, group.key) > 0} statuses={verificationStatus} onStatus={changeVerificationStatus} onItemsLoaded={onItemsLoaded} corrections={displayCorrections} movedIn={group.items.filter(value => value.paymentMethodCode !== item.paymentMethodCode)} onCorrect={setEditingCorrection} />}
            </div>;
          })}
          {!cash && groups.every(group => displayedGroup(item.paymentMethodCode, group.key).count === 0) && <label className="flex items-center gap-2 rounded-xl border bg-slate-50 px-3 py-2 text-sm"><Checkbox checked={confirmed[item.paymentMethodCode] ?? false} disabled={!editable} onCheckedChange={value => setConfirmed(current => ({ ...current, [item.paymentMethodCode]: value === true }))} />Confirmo que no hay comprobantes de este medio en el cierre.</label>}
          <div className="flex flex-col gap-3 border-t pt-3 sm:flex-row sm:items-end sm:justify-between">{cash && <div className="space-y-1.5"><Label htmlFor="closure-cash-verified">Efectivo contado y confirmado</Label><Input id="closure-cash-verified" inputMode="numeric" disabled={!editable} value={formatWorkSessionCountInput(verified.Cash ?? "")} onChange={event => setVerified(current => ({ ...current, Cash: normalizeWorkSessionCountInput(event.target.value) }))} /></div>}<span className="w-full rounded-xl bg-teal-50 px-3 py-2 text-center text-sm text-teal-900 sm:ml-auto sm:w-auto sm:text-right">Total confirmado: <strong>{money.format(methodVerifiedAmount(item.paymentMethodCode))}</strong></span></div>
        </div>
      </section>;
    })}
    <section className="overflow-hidden rounded-2xl border bg-white shadow-sm">
      <div className="flex flex-col gap-3 border-b bg-slate-50 p-4 sm:flex-row sm:items-center sm:justify-between"><div className="flex items-center gap-3"><div className="rounded-xl bg-teal-100 p-2 text-teal-800"><CreditCard className="h-5 w-5" /></div><div><strong className="text-base">Ventas a crédito</strong><p className="text-sm text-muted-foreground">{groupCount("Credit", "CreditSale")} facturas · Total {money.format(groupTotal("Credit", "CreditSale"))}</p></div></div><div className="text-left text-slate-700 sm:text-right"><span className="block text-xs font-semibold uppercase tracking-wide">Revisión</span><strong>{creditItems.filter(item => verificationStatus[item.verificationKey]).length} de {groupCount("Credit", "CreditSale")}</strong></div></div>
      {snapshotQuery.isSuccess && <InvoiceChargeSummary charges={invoiceCharges} paymentMethod="Credit" />}
      <div className="p-4">{groupCount("Credit", "CreditSale") > 0 ? <><button type="button" className="flex w-full items-center justify-between gap-3 rounded-xl border bg-white p-3 text-left transition hover:bg-slate-50" onClick={() => setExpandedMethods(current => ({ ...current, Credit: !current.Credit }))}><span><strong>Facturas a crédito</strong><small className="mt-1 block text-muted-foreground">{money.format(methodVerifiedAmount("Credit"))} confirmados</small></span><span className="inline-flex items-center gap-2 text-sm font-semibold text-teal-800">{expandedMethods.Credit ? "Ocultar detalle" : "Ver y conciliar detalle"}{expandedMethods.Credit ? <ChevronUp className="h-4 w-4" /> : <ChevronDown className="h-4 w-4" />}</span></button>{expandedMethods.Credit && <div className="mt-3"><p className="mb-3 text-sm text-muted-foreground">Verifica cada venta a crédito contra la factura y el cliente.</p><PagedVerificationRows closureId={closure.workSessionClosureId} paymentMethodCode="Credit" movementType="CreditSale" disabled={!editable} statuses={verificationStatus} onStatus={(key, status) => setVerificationStatus(current => ({ ...current, [key]: status }))} onItemsLoaded={onItemsLoaded} /><div className="mt-3 flex justify-end text-sm"><span className="rounded-xl bg-teal-50 px-3 py-2 text-teal-900">Total confirmado: <strong>{money.format(methodVerifiedAmount("Credit"))}</strong></span></div></div>}</> : <p className="rounded-xl border border-dashed p-4 text-sm text-muted-foreground">No hay ventas a crédito en este cierre.</p>}</div>
    </section>
    {snapshotQuery.isLoading && <p className="p-4 text-sm text-muted-foreground">Cargando cargos del cierre…</p>}
    {snapshotQuery.isError && <div className="flex items-center justify-between gap-3 p-4 text-sm text-destructive">No fue posible cargar los cargos.<Button variant="outline" onClick={() => void snapshotQuery.refetch()}>Reintentar</Button></div>}
    {snapshotQuery.isSuccess && <InvoiceChargeSummary charges={invoiceCharges} />}
    {editable && Object.values(adjustedDifferences).some(value => value !== 0) && <div className="space-y-2"><Label>Motivo de la diferencia</Label><Select value={reasonCode} onValueChange={setReasonCode}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent>{(reasons.data ?? []).map(item => <SelectItem key={item.code} value={item.code}>{item.label}</SelectItem>)}</SelectContent></Select></div>}
    <div className="space-y-2"><Label>Observación</Label><Textarea disabled={!editable} value={note} onChange={event => setNote(event.target.value)} maxLength={500} placeholder="Agrega una nota si hace falta explicar el cierre." /></div>{mutation.isError && <p className="rounded-xl bg-red-50 p-3 text-sm text-destructive">No fue posible conciliar. Revisa los comprobantes, diferencias y motivos.</p>}
  </div><footer className="flex shrink-0 items-center justify-end gap-3 border-t bg-slate-50 p-5"><Button variant="outline" onClick={onClose}>Cerrar</Button>{editable && <Button disabled={!valid || mutation.isPending} onClick={() => mutation.mutate()}>{mutation.isPending ? <><Loader2 className="mr-2 h-4 w-4 animate-spin" />Conciliando…</> : <><CheckCircle2 className="mr-2 h-4 w-4" />Confirmar conciliación</>}</Button>}</footer></DialogContent>{editingCorrection && <CorrectionEditor item={editingCorrection} methods={countable.map(item => item.paymentMethodCode)} current={paymentCorrections[editingCorrection.verificationKey]} onClose={() => setEditingCorrection(null)} onRemove={() => { setPaymentCorrections(current => { const next = { ...current }; delete next[editingCorrection.verificationKey]; return next; }); setEditingCorrection(null); }} onApply={async correction => { await workSessionDifferencesApi.validatePaymentCorrections(closure.workSessionClosureId, [...Object.values(paymentCorrections).filter(value => value.verificationKey !== correction.verificationKey), correction]); setPaymentCorrections(current => ({ ...current, [correction.verificationKey]: correction })); if (requiresIndividualCashClosureVerification(editingCorrection)) changeVerificationStatus(editingCorrection.verificationKey, "Verified"); setExpandedMethods(current => ({ ...current, [`${correction.paymentMethodCode}:${editingCorrection.movementType}`]: true })); setEditingCorrection(null); }} />}</Dialog>;
}

function CorrectionEditor({ item, methods, current, onClose, onRemove, onApply }: {
  item: ClosurePaymentVerification; methods: string[]; current?: ClosurePaymentCorrection;
  onClose: () => void; onRemove: () => void; onApply: (value: ClosurePaymentCorrection) => Promise<void>;
}) {
  const paymentMethods = useReferenceOptions("payment-method");
  const cardFranchises = useReferenceOptions("card-franchise");
  const originalTender = item.tenderMethodCode ?? item.paymentMethodCode;
  const [tender, setTender] = useState(current?.tenderMethodCode ?? (originalTender === "Card" ? "" : originalTender));
  const [franchise, setFranchise] = useState(current?.cardFranchiseCode ?? item.cardFranchiseCode ?? "");
  const [approval, setApproval] = useState(current?.approvalNumber ?? item.approvalNumber ?? "");
  const [reference, setReference] = useState(current?.reference ?? item.reference ?? "");
  const [amountInput, setAmountInput] = useState(String(Math.abs(current?.amount ?? item.amount)));
  const [reason, setReason] = useState(current?.reason ?? "");
  const [validating, setValidating] = useState(false);
  const [validationError, setValidationError] = useState<string | null>(null);
  const amount = Number(amountInput) * Math.sign(item.amount);
  const method = tender === "DebitCard" || tender === "CreditCard" ? "Card" : tender;
  const isCard = tender === "DebitCard" || tender === "CreditCard";
  const available = item.movementType === "CashIn" || item.movementType === "CashOut"
    ? (paymentMethods.data ?? []).filter(option => option.code === "Cash")
    : (paymentMethods.data ?? []).filter(option =>
        ["Cash", "DebitCard", "CreditCard", "Transfer"].includes(option.code) &&
        methods.includes(option.code === "DebitCard" || option.code === "CreditCard" ? "Card" : option.code));
  const valid = Number.isFinite(amount) && amount !== 0 && reason.trim().length > 0 &&
    available.some(option => option.code === tender) &&
    (!isCard || (franchise.length > 0 && approval.trim().length > 0)) &&
    (tender !== "Transfer" || reference.trim().length > 0) &&
    (method !== item.paymentMethodCode || amount !== item.amount || tender !== originalTender ||
      (isCard && (franchise !== (item.cardFranchiseCode ?? "") || approval.trim() !== (item.approvalNumber ?? ""))) ||
      (tender === "Transfer" && reference.trim() !== (item.reference ?? ""))) &&
    (item.movementType !== "Refund" || Math.abs(amount) <= Math.abs(item.amount));
  const apply = async () => {
    setValidating(true);
    setValidationError(null);
    try {
      await onApply({ verificationKey: item.verificationKey, paymentMethodCode: method,
        tenderMethodCode: tender, cardFranchiseCode: isCard ? franchise : null,
        approvalNumber: isCard ? approval.trim() : null,
        reference: tender === "Transfer" ? reference.trim() : null, amount, reason: reason.trim() });
    } catch (error) {
      setValidationError(error instanceof Error ? error.message : "No fue posible validar esta corrección.");
    } finally { setValidating(false); }
  };
  return <Dialog open onOpenChange={open => !open && !validating && onClose()}><DialogContent className="max-w-md"><DialogHeader><DialogTitle>Corregir comprobante</DialogTitle><DialogDescription>{item.documentNumber} · {verificationMovementName(item)}. El cambio se aplicará al confirmar la conciliación.</DialogDescription></DialogHeader><div className="space-y-4 py-2"><p className="rounded-xl bg-slate-50 p-3 text-sm">Registrado: <strong>{workSessionPaymentMethodName(originalTender)} · {money.format(Math.abs(item.amount))}</strong></p><div className="space-y-1.5"><Label>Medio real</Label><Select value={tender} onValueChange={setTender}><SelectTrigger><SelectValue placeholder="Selecciona el medio real" /></SelectTrigger><SelectContent>{available.map(option => <SelectItem key={option.code} value={option.code}>{option.label}</SelectItem>)}</SelectContent></Select></div>{isCard && <><div className="space-y-1.5"><Label>Tipo de tarjeta</Label><Select value={franchise} onValueChange={setFranchise}><SelectTrigger><SelectValue placeholder="Selecciona la franquicia" /></SelectTrigger><SelectContent>{(cardFranchises.data ?? []).map(option => <SelectItem key={option.code} value={option.code}>{option.label}</SelectItem>)}</SelectContent></Select></div><div className="space-y-1.5"><Label htmlFor="closure-correction-approval">Número de aprobación o referencia</Label><Input id="closure-correction-approval" value={approval} onChange={event => setApproval(event.target.value)} maxLength={100} /></div></>}{tender === "Transfer" && <div className="space-y-1.5"><Label htmlFor="closure-correction-reference">Referencia de transferencia</Label><Input id="closure-correction-reference" value={reference} onChange={event => setReference(event.target.value)} maxLength={160} /></div>}<div className="space-y-1.5"><Label htmlFor="closure-correction-amount">Valor real</Label><Input id="closure-correction-amount" inputMode="decimal" value={formatWorkSessionCountInput(amountInput)} onChange={event => setAmountInput(normalizeWorkSessionCountInput(event.target.value))} /></div><div className="space-y-1.5"><Label htmlFor="closure-correction-reason">Motivo de la corrección</Label><Textarea id="closure-correction-reason" value={reason} onChange={event => setReason(event.target.value)} maxLength={500} /></div></div>{validationError && <p role="alert" className="rounded-xl border border-red-200 bg-red-50 p-3 text-sm text-red-800">{validationError}</p>}<div className="flex justify-end gap-2">{current && <Button variant="ghost" className="mr-auto" disabled={validating} onClick={onRemove}>Quitar corrección</Button>}<Button variant="outline" disabled={validating} onClick={onClose}>Cerrar</Button><Button disabled={!valid || validating} onClick={() => void apply()}>{validating ? "Validando…" : "Aplicar"}</Button></div></DialogContent></Dialog>;
}

function PagedVerificationRows({ closureId, paymentMethodCode, movementType, informational = false, disabled,
  queryEnabled = true, statuses, onStatus, onItemsLoaded, corrections = {}, movedIn = [], onCorrect }: {
    closureId: string; paymentMethodCode: string; movementType?: string; informational?: boolean;
    disabled: boolean; queryEnabled?: boolean; statuses: Record<string, VerificationStatus>;
    onStatus: (key: string, status: VerificationStatus) => void;
    onItemsLoaded: (items: ClosurePaymentVerification[]) => void;
    corrections?: Record<string, ClosurePaymentCorrection>;
    movedIn?: ClosurePaymentVerification[];
    onCorrect?: (item: ClosurePaymentVerification) => void;
  }) {
  const sentinel = useRef<HTMLDivElement>(null);
  const pages = useInfiniteQuery({
    queryKey: ["work-session-payment-verifications", closureId, paymentMethodCode, movementType],
    queryFn: ({ pageParam }) => workSessionDifferencesApi.listPaymentVerifications(
      closureId, pageParam, 100, paymentMethodCode, movementType),
    initialPageParam: 1,
    enabled: queryEnabled,
    getNextPageParam: last => last.page * last.pageSize < last.totalItems ? last.page + 1 : undefined,
    staleTime: Infinity,
    refetchOnWindowFocus: false,
  });
  const items = useMemo(() => pages.data?.pages.flatMap(page => page.items) ?? [], [pages.data]);
  const movedKeys = new Set(movedIn.map(item => item.verificationKey));
  const displayedItems = [
    ...movedIn,
    ...items.filter(item => !movedKeys.has(item.verificationKey) &&
      (corrections[item.verificationKey]?.paymentMethodCode ?? item.correctedPaymentMethodCode ?? item.paymentMethodCode) === paymentMethodCode),
  ];
  useEffect(() => { if (items.length) onItemsLoaded(items); }, [items, onItemsLoaded]);
  const { hasNextPage, isFetchingNextPage, fetchNextPage } = pages;
  useEffect(() => {
    if (!sentinel.current || !hasNextPage) return;
    const observer = new IntersectionObserver(entries => {
      if (entries[0]?.isIntersecting && !isFetchingNextPage) void fetchNextPage();
    }, { rootMargin: "120px" });
    observer.observe(sentinel.current);
    return () => observer.disconnect();
  }, [hasNextPage, isFetchingNextPage, fetchNextPage]);
  if (queryEnabled && pages.isPending) return <p className="border-t p-3 text-sm text-muted-foreground">Cargando movimientos…</p>;
  if (pages.isError && !pages.data) return <div className="flex items-center justify-between gap-3 border-t p-3 text-sm text-destructive">No fue posible cargar los movimientos.<Button variant="outline" onClick={() => void pages.refetch()}>Reintentar</Button></div>;
  return <div className="space-y-2 border-t bg-slate-50/50 p-3">
    {displayedItems.length ? displayedItems.map(item => <VerificationRow key={item.verificationKey} item={item} status={statuses[item.verificationKey]} disabled={disabled} informational={informational} correction={corrections[item.verificationKey]} onCorrect={onCorrect ? () => onCorrect(item) : undefined} onChange={status => onStatus(item.verificationKey, status)} />) : <p className="text-sm text-muted-foreground">Sin movimientos en este grupo.</p>}
    <div ref={sentinel} className="h-px" aria-hidden="true" />
    {pages.isFetchingNextPage && <p className="text-center text-sm text-muted-foreground">Cargando más movimientos…</p>}
    {pages.isFetchNextPageError && <Button variant="outline" onClick={() => void pages.fetchNextPage()}>Reintentar carga</Button>}
  </div>;
}

function VerificationRow({ item, status, disabled, informational = false, correction, onCorrect, onChange }: { item: ClosurePaymentVerification; status?: VerificationStatus; disabled: boolean; informational?: boolean; correction?: ClosurePaymentCorrection; onCorrect?: () => void; onChange: (status: VerificationStatus) => void }) {
  const isInformational = informational || item.movementType === "Sale" || item.movementType === "Refund";
  const isCreditSale = item.movementType === "CreditSale";
  const isCashMovement = item.movementType === "CashIn" || item.movementType === "CashOut";
  const isThirdPartyPayment = item.movementType === "ReceivablePayment" || item.movementType === "PayablePayment";
  const displayedCorrection = correction ?? (item.correctedPaymentMethodCode && item.correctedAmount != null ? {
    verificationKey: item.verificationKey, paymentMethodCode: item.correctedPaymentMethodCode,
    amount: item.correctedAmount, reason: item.correctionReason ?? "",
    tenderMethodCode: item.correctedTenderMethodCode,
    cardFranchiseCode: item.correctedCardFranchiseCode,
    approvalNumber: item.correctedApprovalNumber,
    reference: item.correctedReference,
  } : undefined);
  const title = isCreditSale ? item.customerName ?? "Cliente"
    : isCashMovement ? item.reasonName?.trim() || "Motivo no registrado"
    : verificationMovementName(item);
  return <div className={`grid gap-3 rounded-xl border p-3 ${isInformational ? "sm:grid-cols-[1fr_auto]" : "sm:grid-cols-[1fr_auto_auto]"} sm:items-center`}>
    <div className="min-w-0">
      <div className="flex flex-wrap items-center gap-2">
        <ReceiptText className="h-4 w-4 shrink-0 text-slate-500" />
        <strong className="break-words">{title}</strong>
        {isThirdPartyPayment
          ? <span className="break-words text-sm text-slate-700">{item.counterpartyName?.trim() || "Tercero no disponible"}</span>
          : !isCashMovement && <span>{item.documentNumber}</span>}
        {item.cardFranchiseCode && <Badge variant="secondary">{item.cardFranchiseCode}</Badge>}
      </div>
      {isCashMovement && item.notes?.trim() && <p className="mt-1 whitespace-pre-wrap break-words text-xs text-muted-foreground">{item.notes}</p>}
      {!isCashMovement && !isCreditSale && <p className="mt-1 text-xs text-muted-foreground">{isThirdPartyPayment && item.documentNumber && <>Comprobante: {item.documentNumber} · </>}Referencia: {item.approvalNumber || item.reference || "Sin referencia"} · {new Date(item.occurredAt).toLocaleString("es-CO")}</p>}
      {displayedCorrection && <p className="mt-1 text-xs font-semibold text-teal-800">Corregido: {workSessionPaymentMethodName(displayedCorrection.tenderMethodCode ?? displayedCorrection.paymentMethodCode)} · {money.format(Math.abs(displayedCorrection.amount))}{displayedCorrection.cardFranchiseCode && ` · ${displayedCorrection.cardFranchiseCode}`}{(displayedCorrection.approvalNumber || displayedCorrection.reference) && ` · ${displayedCorrection.approvalNumber || displayedCorrection.reference}`} · {displayedCorrection.reason}</p>}
    </div>
    <span className="text-left sm:text-right"><strong className={(displayedCorrection?.amount ?? item.amount) < 0 ? "text-red-700" : "text-slate-950"}>{money.format(Math.abs(displayedCorrection?.amount ?? item.amount))}</strong>{displayedCorrection && <small className="block text-xs text-muted-foreground">Antes {money.format(Math.abs(item.amount))}</small>}</span>
    <div className="flex flex-wrap gap-2">{!isInformational && <><Button type="button" size="sm" variant={status === "Verified" ? "default" : "outline"} disabled={disabled} onClick={() => onChange("Verified")}><Check className="mr-1 h-4 w-4" />Verificado</Button><Button type="button" size="sm" variant={status === "Missing" ? "destructive" : "outline"} disabled={disabled} onClick={() => onChange("Missing")}><X className="mr-1 h-4 w-4" />No encontrado</Button></>}{!disabled && !isCreditSale && onCorrect && <Button type="button" size="sm" variant="outline" onClick={onCorrect}><Pencil className="mr-1 h-4 w-4" />Corregir</Button>}</div>
  </div>;
}

function verificationMovementName(item: ClosurePaymentVerification) { if (item.movementType === "Refund") return "Devolución"; if (item.movementType === "ReceivablePayment") return "Abono a cartera"; if (item.movementType === "PayablePayment") return "Pago a proveedor"; if (item.movementType === "CashIn") return "Entrada"; if (item.movementType === "CashOut") return "Salida"; if (item.movementType === "CreditSale") return "Venta a cartera"; if (item.sourceDocumentType === "SalesInvoice") return "Factura electrónica"; if (item.sourceDocumentType === "ServiceInvoice") return "Factura de servicio"; if (item.sourceDocumentType === "SalesReceipt") return "Comprobante"; return "Movimiento"; }
function PaymentIcon({ code }: { code: string }) { if (code === "Cash") return <Banknote className="h-5 w-5" />; if (code === "Transfer") return <Landmark className="h-5 w-5" />; return <CreditCard className="h-5 w-5" />; }
function Summary({ title, value, icon }: { title: string; value: string; icon: ReactNode }) { return <Card className="rounded-3xl"><CardContent className="flex items-center gap-3 p-5"><div className="rounded-2xl bg-muted p-3">{icon}</div><div><p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">{title}</p><strong className="text-xl">{value}</strong></div></CardContent></Card>; }
function Status({ value }: { value: WorkSessionClosure["reconciliationStatus"] }) { return <Badge variant={value === "Pending" ? "destructive" : "secondary"}>{value === "Pending" ? "Pendiente" : value === "Reconciled" ? "Conciliado" : value === "ReconciledWithDifferences" ? "Conciliado con diferencias" : "Parcial"}</Badge>; }
