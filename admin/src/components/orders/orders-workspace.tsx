"use client";

import {
  Check,
  ChevronLeft,
  ChevronRight,
  ClipboardList,
  Expand,
  FileText,
  Landmark,
  Loader2,
  PackageSearch,
  Printer,
  Receipt,
  RotateCcw,
  Search,
  UserRound,
  X,
} from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { DatePicker } from "@/components/ui/date-picker";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  type CommerceOrderDetail,
  type CommerceOrderFilters,
  type CommerceOrderListItem,
  type CommerceOrderPage,
} from "@/services/orders/commerce-orders-client";
import { loadAllMatchingOrders } from "@/services/orders/order-batch-selection";
import type { PosSettlementConfiguration } from "@/services/pos/pos-edge-client";
import { Textarea } from "@/components/ui/textarea";
import { getOrderAvailability } from "./order-availability";

const ORDER_STATUS_REFRESH_INTERVAL_MS = 10_000;

const money = new Intl.NumberFormat("es-CO", {
  style: "currency",
  currency: "COP",
  maximumFractionDigits: 0,
});

const date = new Intl.DateTimeFormat("es-CO", {
  dateStyle: "medium",
  timeStyle: "short",
});

function localToday() {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-${String(now.getDate()).padStart(2, "0")}`;
}

type OrdersWorkspaceProps = {
  compact?: boolean;
  connected?: boolean;
  showHeader?: boolean;
  loadPage: (filters: CommerceOrderFilters & {
    page: number;
    pageSize: number;
  }) => Promise<CommerceOrderPage>;
  loadDetail: (orderId: string) => Promise<CommerceOrderDetail>;
  loadSettlementConfiguration?: () => Promise<PosSettlementConfiguration>;
  onRecover?: (order: CommerceOrderListItem) => Promise<void>;
  onRetryEmission?: (orderId: string) => Promise<void>;
  onInvoiceSelected?: (
    orders: CommerceOrderListItem[],
    paymentMethodCode: string,
    documentType: "SalesInvoice" | "SalesReceipt",
    transfer?: { bankAccountId: string | null; reference: string; notes: string | null },
  ) => Promise<{
    completedCount: number;
    failedCount: number;
    printError?: string | null;
  }>;
  onExpand?: () => void;
  onConfigurePrinting?: () => void;
  onCountChange?: (count: number) => void;
  routeOptions?: Array<{ routeId: string; name: string }>;
  onlyMine?: boolean;
  source?: number;
  activeOrderId?: string | null;
};

type InvoiceProgress = {
  total: number;
  processed: number;
  completed: number;
  failed: number;
  current: string;
  events: Array<{ id: string; text: string; tone: "active" | "success" | "error" }>;
};

function isAvailableForThisSession(
  order: CommerceOrderListItem,
  activeOrderId?: string | null,
) {
  return getOrderAvailability(order, activeOrderId).canUseInCurrentSession;
}

export function OrdersWorkspace({
  compact = false,
  connected = true,
  showHeader = true,
  loadPage,
  loadDetail,
  loadSettlementConfiguration,
  onRecover,
  onRetryEmission,
  onInvoiceSelected,
  onExpand,
  onConfigurePrinting,
  onCountChange,
  routeOptions = [],
  onlyMine = false,
  source,
  activeOrderId,
}: OrdersWorkspaceProps) {
  const [page, setPage] = useState(1);
  const [data, setData] = useState<CommerceOrderPage | null>(null);
  const [query, setQuery] = useState("");
  const [customer, setCustomer] = useState("");
  const [product, setProduct] = useState("");
  const [status, setStatus] = useState("Available");
  const [createdFrom, setCreatedFrom] = useState(localToday);
  const [createdTo, setCreatedTo] = useState(localToday);
  const [routeId, setRouteId] = useState("All");
  const [selected, setSelected] = useState<Map<string, CommerceOrderListItem>>(new Map());
  const [allMatchingSelected, setAllMatchingSelected] = useState(false);
  const [selectingAll, setSelectingAll] = useState(false);
  const [detail, setDetail] = useState<CommerceOrderDetail | null>(null);
  const [loading, setLoading] = useState(false);
  const [working, setWorking] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [paymentMethod, setPaymentMethod] = useState("Cash");
  const [transferOpen, setTransferOpen] = useState(false);
  const [transferLoading, setTransferLoading] = useState(false);
  const [settlementConfiguration, setSettlementConfiguration] = useState<PosSettlementConfiguration | null>(null);
  const [transferBankAccountId, setTransferBankAccountId] = useState("");
  const [transferReference, setTransferReference] = useState("");
  const [transferNotes, setTransferNotes] = useState("");
  const [documentType, setDocumentType] = useState<"SalesInvoice" | "SalesReceipt">(
    "SalesInvoice",
  );
  const [invoiceProgress, setInvoiceProgress] = useState<InvoiceProgress | null>(null);
  const pageSize = compact ? 8 : 20;

  const orderFilters = useMemo<Omit<CommerceOrderFilters, "page" | "pageSize">>(() => ({
    orderNumber: query || undefined,
    customer: customer || undefined,
    product: product || undefined,
    status: status === "All" ? undefined : status,
    createdFrom: createdFrom
      ? new Date(`${createdFrom}T00:00:00`).toISOString()
      : undefined,
    createdTo: createdTo
      ? new Date(`${createdTo}T23:59:59.999`).toISOString()
      : undefined,
    routeId: routeId === "All" ? undefined : routeId,
    onlyMine: onlyMine || undefined,
    source,
  }), [createdFrom, createdTo, customer, onlyMine, product, query, routeId, source, status]);

  const openTransfer = useCallback(async () => {
    if (!loadSettlementConfiguration) {
      setError("No fue posible cargar la configuración de transferencias.");
      return;
    }
    setTransferLoading(true);
    setError(null);
    try {
      const configuration = await loadSettlementConfiguration();
      if (configuration.isAccountingEnabled && configuration.bankAccounts.length === 0) {
        setError("Contabilidad está activa, pero no hay una cuenta bancaria disponible.");
        return;
      }
      setSettlementConfiguration(configuration);
      setTransferBankAccountId((current) => current || configuration.bankAccounts.find((account) => account.isPrimary)?.bankAccountId || "");
      setTransferOpen(true);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "No fue posible cargar la configuración de transferencias.");
    } finally {
      setTransferLoading(false);
    }
  }, [loadSettlementConfiguration]);

  const refresh = useCallback(async (silent = false) => {
    if (!connected) {
      setData(null);
      onCountChange?.(0);
      return;
    }
    if (!silent) setLoading(true);
    setError(null);
    try {
      const next = await loadPage({ ...orderFilters, page, pageSize });
      setData(next);
      onCountChange?.(next.totalCount);
      setSelected((current) => {
        const updated = new Map(current);
        for (const item of next.items) {
          if (!updated.has(item.orderId)) continue;
          if (isAvailableForThisSession(item, activeOrderId))
            updated.set(item.orderId, item);
          else
            updated.delete(item.orderId);
        }
        return updated;
      });
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "No fue posible consultar los pedidos.");
    } finally {
      if (!silent) setLoading(false);
    }
  }, [
    connected,
    activeOrderId,
    loadPage,
    orderFilters,
    page,
    pageSize,
    onCountChange,
  ]);

  useEffect(() => {
    const timer = window.setTimeout(() => void refresh(), query ? 250 : 0);
    return () => window.clearTimeout(timer);
  }, [refresh, query]);

  useEffect(() => {
    if (!connected) return;
    const refreshSilently = () => void refresh(true);
    const onVisibilityChange = () => {
      if (document.visibilityState === "visible") refreshSilently();
    };
    const interval = window.setInterval(
      refreshSilently,
      ORDER_STATUS_REFRESH_INTERVAL_MS,
    );
    window.addEventListener("focus", refreshSilently);
    document.addEventListener("visibilitychange", onVisibilityChange);
    return () => {
      window.clearInterval(interval);
      window.removeEventListener("focus", refreshSilently);
      document.removeEventListener("visibilitychange", onVisibilityChange);
    };
  }, [connected, refresh]);

  const selectedOrders = useMemo(() => [...selected.values()], [selected]);

  useEffect(() => {
    setSelected(new Map());
    setAllMatchingSelected(false);
    setPage(1);
  }, [orderFilters]);

  async function toggleSelectAllMatching() {
    if (allMatchingSelected) {
      setSelected(new Map());
      setAllMatchingSelected(false);
      return;
    }
    setSelectingAll(true);
    setError(null);
    try {
      const matches = await loadAllMatchingOrders(loadPage, orderFilters);
      const available = matches.filter((order) =>
        isAvailableForThisSession(order, activeOrderId));
      setSelected(new Map(available.map((order) => [order.orderId, order])));
      setAllMatchingSelected(true);
      setNotice(`${available.length} pedidos disponibles seleccionados en todas las páginas.`);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "No fue posible seleccionar todos los pedidos.");
    } finally {
      setSelectingAll(false);
    }
  }

  async function showDetail(orderId: string) {
    setWorking(true);
    setError(null);
    try {
      setDetail(await loadDetail(orderId));
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "No fue posible abrir el pedido.");
    } finally {
      setWorking(false);
    }
  }

  async function recover(order: CommerceOrderListItem) {
    if (!onRecover) return;
    setWorking(true);
    setError(null);
    setNotice(null);
    try {
      await onRecover(order);
      setNotice(`Pedido ${order.orderNumber} llevado a la venta.`);
      await refresh();
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "No fue posible recuperar el pedido.");
    } finally {
      setWorking(false);
    }
  }

  async function invoiceSelected() {
    if (!onInvoiceSelected || selectedOrders.length === 0) return;
    const available = selectedOrders.filter((order) =>
      isAvailableForThisSession(order, activeOrderId));
    if (available.length !== selectedOrders.length) {
      setSelected(new Map(available.map((order) => [order.orderId, order])));
      setAllMatchingSelected(false);
      setError("La selección cambió: uno o más pedidos ya no están disponibles para esta sesión.");
      await refresh();
      return;
    }
    let confirmedOrderIds = new Set<string>();
    if (paymentMethod !== "Transfer") {
      try {
        const details = await Promise.all(available.map((order) => loadDetail(order.orderId)));
        confirmedOrderIds = new Set(details
          .filter((order) => order.paymentStatus === "Confirmed")
          .map((order) => order.orderId));
      } catch (caught) {
        setError(caught instanceof Error ? caught.message : "No fue posible validar el pago de los pedidos.");
        return;
      }
    }
    const requiresTransfer = paymentMethod === "Transfer" || confirmedOrderIds.size > 0;
    if (requiresTransfer) {
      const accountRequired = settlementConfiguration?.isAccountingEnabled === true;
      if (!settlementConfiguration || !transferReference.trim() ||
          (accountRequired && !transferBankAccountId)) {
        await openTransfer();
        return;
      }
    }
    setWorking(true);
    setError(null);
    setNotice(null);
    let completed = 0;
    let failed = 0;
    let printError: string | null = null;
    setInvoiceProgress({ total: available.length, processed: 0, completed: 0, failed: 0, current: "Preparando lote", events: [] });
    try {
      for (const [index, order] of available.entries()) {
        const activeEvent = { id: `${order.orderId}-active`, text: `Validando y emitiendo ${order.orderNumber}`, tone: "active" as const };
        setInvoiceProgress((current) => current && ({ ...current, current: order.orderNumber, events: [...current.events.slice(-3), activeEvent] }));
        const effectivePaymentMethod = confirmedOrderIds.has(order.orderId)
          ? "Transfer"
          : paymentMethod;
        const result = await onInvoiceSelected(
          [order],
          effectivePaymentMethod,
          documentType,
          effectivePaymentMethod === "Transfer"
            ? {
                bankAccountId: settlementConfiguration!.isAccountingEnabled
                  ? transferBankAccountId
                  : null,
                reference: transferReference.trim(),
                notes: transferNotes.trim() || null,
              }
            : undefined,
        );
        completed += result.completedCount;
        failed += result.failedCount;
        printError ||= result.printError ?? null;
        const tone = result.failedCount ? "error" as const : "success" as const;
        const text = result.failedCount ? `${order.orderNumber} requiere revisión` : `${order.orderNumber} emitido`;
        setInvoiceProgress((current) => current && ({
          ...current,
          processed: index + 1,
          completed,
          failed,
          current: text,
          events: [...current.events.filter((event) => event.id !== activeEvent.id).slice(-3), { id: `${order.orderId}-${tone}`, text, tone }],
        }));
        if (result.failedCount) break;
      }
      setNotice(
        printError
          ? `${completed} emitidos. ${printError}`
          : failed === 0
          ? `${completed} ${completed === 1 ? "pedido emitido" : "pedidos emitidos"} correctamente.`
          : `${completed} emitidos y ${failed} pendientes de revisar.`,
      );
      setSelected(new Map());
      setAllMatchingSelected(false);
      await refresh();
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "No fue posible facturar los pedidos.");
    } finally {
      setWorking(false);
      window.setTimeout(() => setInvoiceProgress(null), 2200);
    }
  }

  if (!connected) {
    return (
      <div className="grid min-h-44 place-items-center rounded-xl border border-dashed border-slate-300 p-5 text-center">
        <div>
          <span className="mx-auto grid h-10 w-10 place-items-center rounded-xl bg-amber-50 text-amber-700">
            <ClipboardList className="h-5 w-5" />
          </span>
          <p className="mt-3 font-semibold text-slate-900">Pedidos disponibles en línea</p>
          <p className="mt-1 max-w-sm text-sm text-slate-500">
            La venta local sigue disponible. Los pedidos se actualizarán al recuperar la conexión con Auraly.
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className={`flex min-h-0 flex-col ${compact ? "gap-2" : "gap-4"}`}>
      {showHeader && <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <div className="flex items-center gap-2">
            <span className="grid h-9 w-9 place-items-center rounded-xl bg-teal-50 text-teal-700">
              <ClipboardList className="h-5 w-5" />
            </span>
            <div>
              <h2 className={`${compact ? "text-base" : "text-xl"} font-bold text-slate-950`}>
                Pedidos
              </h2>
              <p className="text-xs text-slate-500">
                {data?.totalCount ?? 0} encontrados · sin información tributaria
              </p>
            </div>
          </div>
        </div>
        {onExpand && (
          <Button type="button" variant="outline" size="sm" onClick={onExpand}>
            <Expand className="mr-2 h-4 w-4" />
            Expandir
          </Button>
        )}
      </div>}

      <div className={`grid gap-2 ${compact ? "grid-cols-1" : "md:grid-cols-2 xl:grid-cols-6"}`}>
        <label className={`relative ${compact ? "" : "xl:col-span-2"}`}>
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
          <Input
            value={query}
            onChange={(event) => {
              setQuery(event.target.value);
              setPage(1);
            }}
            className="pl-9"
            placeholder="Número de pedido"
          />
        </label>
        {!compact && (
          <>
            <Input
              value={customer}
              onChange={(event) => {
                setCustomer(event.target.value);
                setPage(1);
              }}
              placeholder="Cliente, documento o teléfono"
            />
            <Input
              value={product}
              onChange={(event) => {
                setProduct(event.target.value);
                setPage(1);
              }}
              placeholder="Producto, código o referencia"
            />
          </>
        )}
        <Select
          value={status}
          onValueChange={(value) => {
            setStatus(value);
            setPage(1);
          }}
        >
          <SelectTrigger>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="Available">Disponibles</SelectItem>
            <SelectItem value="Invoiced">Facturados</SelectItem>
            <SelectItem value="ProcessingEmission">Procesando emisión</SelectItem>
            <SelectItem value="EmissionFailed">Con error de emisión</SelectItem>
            <SelectItem value="Cancelled">Cancelados</SelectItem>
            <SelectItem value="All">Todos</SelectItem>
          </SelectContent>
        </Select>
        {!compact && routeOptions.length > 0 && (
          <Select value={routeId} onValueChange={(next) => { setRouteId(next); setPage(1); }}>
            <SelectTrigger><SelectValue placeholder="Todas las rutas" /></SelectTrigger>
            <SelectContent>
              <SelectItem value="All">Todas las rutas</SelectItem>
              {routeOptions.map((route) => <SelectItem key={route.routeId} value={route.routeId}>{route.name}</SelectItem>)}
            </SelectContent>
          </Select>
        )}
        {!compact && onlyMine && (
          <div className="flex items-center rounded-xl border bg-teal-50 px-3 text-sm font-semibold text-teal-800">
            <UserRound className="mr-2 h-4 w-4" />Vendedor: mis pedidos
          </div>
        )}
        {!compact && (
          <div className="flex gap-2 xl:col-span-2">
            <div className="flex-1">
              <DatePicker
                value={createdFrom}
                onChange={(value) => {
                  setCreatedFrom(value);
                  setPage(1);
                }}
                placeholder="Pedidos desde"
              />
            </div>
            <div className="flex-1">
              <DatePicker
                value={createdTo}
                onChange={(value) => {
                  setCreatedTo(value);
                  setPage(1);
                }}
                placeholder="Pedidos hasta"
              />
            </div>
          </div>
        )}
      </div>

      {(error || notice) && (
        <div
          role="status"
          className={`rounded-xl border px-3 py-2 text-sm ${
            error
              ? "border-red-200 bg-red-50 text-red-800"
              : "border-emerald-200 bg-emerald-50 text-emerald-800"
          }`}
        >
          {error ?? notice}
        </div>
      )}

      {invoiceProgress && (
        <div className="overflow-hidden rounded-2xl border border-teal-200 bg-gradient-to-r from-slate-950 via-teal-950 to-emerald-900 p-4 text-white shadow-xl" role="status" aria-live="polite">
          <div className="flex items-center gap-4">
            <div
              className="grid h-20 w-20 shrink-0 place-items-center rounded-full p-2 shadow-[0_0_30px_rgba(45,212,191,0.25)] transition-all duration-500"
              style={{ background: `conic-gradient(rgb(45 212 191) ${invoiceProgress.total ? (invoiceProgress.processed / invoiceProgress.total) * 360 : 0}deg, rgb(255 255 255 / 0.12) 0deg)` }}
            >
              <div className="grid h-full w-full place-items-center rounded-full bg-slate-950 text-center">
                <span className="text-lg font-black tabular-nums">{invoiceProgress.processed}/{invoiceProgress.total}</span>
              </div>
            </div>
            <div className="min-w-0 flex-1">
              <p className="text-xs font-bold uppercase tracking-[0.18em] text-teal-200">Facturación en progreso</p>
              <p className="mt-1 truncate text-lg font-black">{invoiceProgress.current}</p>
              <div className="mt-2 flex gap-2 text-xs font-semibold">
                <span className="rounded-full bg-emerald-400/15 px-2.5 py-1 text-emerald-200">{invoiceProgress.completed} emitidos</span>
                <span className="rounded-full bg-red-400/15 px-2.5 py-1 text-red-200">{invoiceProgress.failed} fallidos</span>
                <span className="rounded-full bg-white/10 px-2.5 py-1 text-slate-200">{invoiceProgress.total - invoiceProgress.processed} faltan</span>
              </div>
            </div>
            {working && <Loader2 className="h-7 w-7 shrink-0 animate-spin text-teal-300" />}
          </div>
          <div className="mt-3 space-y-1 border-t border-white/10 pt-2">
            {invoiceProgress.events.map((event, index) => (
              <p key={event.id} className={`text-xs transition-all duration-700 ${index < invoiceProgress.events.length - 2 ? "opacity-35" : "opacity-90"} ${event.tone === "error" ? "text-red-200" : event.tone === "success" ? "text-emerald-200" : "text-teal-100 animate-pulse"}`}>
                {event.tone === "success" ? "✓" : event.tone === "error" ? "!" : "•"} {event.text}
              </p>
            ))}
          </div>
        </div>
      )}

      <div className="flex min-h-0 flex-col overflow-hidden rounded-xl border border-slate-200 bg-white">
        {!compact && (
          <div className="grid gap-3 border-b border-slate-200 bg-slate-50/80 px-3 py-3 md:px-4 lg:grid-cols-[auto_minmax(0,1fr)] lg:items-center">
            <label className="flex w-full shrink-0 items-center gap-2 text-sm font-medium text-slate-700 md:w-auto">
              <Checkbox
                checked={allMatchingSelected ? true : selected.size > 0 ? "indeterminate" : false}
                disabled={selectingAll}
                onCheckedChange={() => void toggleSelectAllMatching()}
                className="h-5 w-5 rounded-md"
              />
              {selectingAll ? "Seleccionando…" : "Seleccionar disponibles"}
            </label>
            <div className="grid min-w-0 gap-3 2xl:grid-cols-[minmax(20rem,1fr)_auto] 2xl:items-center">
              <div className="min-w-0">
                <div
                  className="grid w-full min-w-0 grid-cols-1 rounded-xl border border-slate-200 bg-white p-1 sm:grid-cols-2"
                  aria-label="Tipo de documento para los pedidos seleccionados"
                >
                  <button
                    type="button"
                    aria-pressed={documentType === "SalesInvoice"}
                    onClick={() => setDocumentType("SalesInvoice")}
                    className={`min-h-10 rounded-lg px-3 text-sm font-semibold transition ${
                      documentType === "SalesInvoice"
                        ? "bg-teal-700 text-white shadow-sm"
                        : "text-slate-600 hover:bg-slate-50"
                    }`}
                  >
                    Factura electrónica
                  </button>
                  <button
                    type="button"
                    aria-pressed={documentType === "SalesReceipt"}
                    onClick={() => setDocumentType("SalesReceipt")}
                    className={`min-h-10 rounded-lg px-3 text-sm font-semibold transition ${
                      documentType === "SalesReceipt"
                        ? "bg-teal-700 text-white shadow-sm"
                        : "text-slate-600 hover:bg-slate-50"
                    }`}
                  >
                    Comprobante de venta
                  </button>
                </div>
              </div>
              <div className="grid w-full grid-cols-[auto_minmax(0,1fr)] gap-2 sm:flex sm:w-auto sm:items-center 2xl:justify-end">
                {onConfigurePrinting && (
                  <Button type="button" variant="outline" size="icon"
                    title="Configurar plantillas e impresoras"
                    aria-label="Configurar plantillas e impresoras"
                    onClick={onConfigurePrinting}>
                    <Printer className="h-4 w-4" />
                  </Button>
                )}
                <Select value={paymentMethod} onValueChange={(value) => {
                  setPaymentMethod(value);
                  if (value === "Transfer") void openTransfer();
                }}>
                  <SelectTrigger className="w-full sm:w-44">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="Cash">Efectivo</SelectItem>
                    <SelectItem value="DebitCard">Tarjeta débito</SelectItem>
                    <SelectItem value="CreditCard">Tarjeta crédito</SelectItem>
                    <SelectItem value="Transfer">Transferencia</SelectItem>
                  </SelectContent>
                </Select>
                <Button
                  type="button"
                  disabled={!selectedOrders.length || working || selectingAll || transferLoading || !onInvoiceSelected}
                  onClick={() => void invoiceSelected()}
                  className="col-span-2 w-full whitespace-nowrap bg-teal-700 text-white hover:bg-teal-800 sm:w-auto"
                >
                  {working ? (
                    <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                  ) : (
                    <Receipt className="mr-2 h-4 w-4" />
                  )}
                  {documentType === "SalesInvoice"
                    ? "Facturar seleccionados"
                    : "Emitir comprobantes"} ({selectedOrders.length})
                </Button>
              </div>
            </div>
          </div>
        )}

        <div className={`${compact ? "max-h-80" : "min-h-[360px]"} overflow-auto`}>
          {loading ? (
            <div className="grid min-h-40 place-items-center text-sm text-slate-500">
              <Loader2 className="mb-2 h-6 w-6 animate-spin text-teal-700" />
              Actualizando pedidos…
            </div>
          ) : data?.items.length ? (
            <div className="divide-y divide-slate-100">
              {data.items.map((order) => {
                const checked = selected.has(order.orderId);
                const availability = getOrderAvailability(order, activeOrderId);
                return (
                  <article
                    key={order.orderId}
                    className={`group grid gap-3 px-3 py-3 transition hover:bg-teal-50/40 ${
                      compact
                        ? "grid-cols-[1fr_auto]"
                        : "md:grid-cols-[auto_minmax(190px,1.2fr)_minmax(170px,1fr)_110px_130px_auto] md:items-center"
                    }`}
                  >
                    {!compact && (
                      <Checkbox
                        checked={checked}
                        disabled={!availability.canUseInCurrentSession}
                        onCheckedChange={() => {
                          setSelected((current) => {
                            const next = new Map(current);
                            if (next.has(order.orderId)) next.delete(order.orderId);
                            else next.set(order.orderId, order);
                            return next;
                          });
                          setAllMatchingSelected(false);
                        }}
                        className="h-5 w-5 rounded-md"
                        aria-label={`Seleccionar ${order.orderNumber}`}
                      />
                    )}
                    <button
                      type="button"
                      onClick={() => void showDetail(order.orderId)}
                      className="min-w-0 text-left"
                    >
                      <div className="flex items-center gap-2">
                        <span className="font-mono text-sm font-bold text-teal-800">
                          {order.orderNumber}
                        </span>
                        <OrderStatus availability={availability} />
                      </div>
                      <p className="mt-1 truncate text-xs text-slate-500">
                        {order.lineCount} {order.lineCount === 1 ? "producto" : "productos"} ·{" "}
                        {sourceLabel(order.source)} · {date.format(new Date(order.createdAt))}
                      </p>
                    </button>
                    {!compact && (
                      <div className="min-w-0">
                        <p className="truncate text-sm font-semibold text-slate-900">
                          {order.customerName || "Consumidor final"}
                        </p>
                        <p className="truncate text-xs text-slate-500">
                          {order.customerIdentification || order.customerPhone || "Sin identificación"}
                        </p>
                      </div>
                    )}
                    {!compact && (
                      <p className="text-sm font-bold tabular-nums text-slate-950">
                        {money.format(order.total)}
                      </p>
                    )}
                    {!compact && (
                      <button
                        type="button"
                        onClick={() => void showDetail(order.orderId)}
                        className="flex h-9 items-center justify-center gap-2 rounded-lg border border-slate-200 px-3 text-sm font-semibold text-slate-700 hover:bg-slate-50"
                      >
                        <FileText className="h-4 w-4" />
                        Detalle
                      </button>
                    )}
                    <button
                      type="button"
                      disabled={!availability.canUseInCurrentSession || !onRecover || working}
                      onClick={() => void recover(order)}
                      className="flex h-9 items-center justify-center gap-2 rounded-lg bg-teal-50 px-3 text-sm font-bold text-teal-800 transition hover:bg-teal-100 disabled:cursor-not-allowed disabled:opacity-40"
                    >
                      <RotateCcw className="h-4 w-4" />
                      {availability.actionLabel}
                    </button>
                    {compact && (
                      <p className="col-span-2 -mt-2 text-right text-sm font-bold tabular-nums">
                        {money.format(order.total)}
                      </p>
                    )}
                  </article>
                );
              })}
            </div>
          ) : (
            <div className="grid min-h-44 place-items-center p-5 text-center">
              <div>
                <PackageSearch className="mx-auto h-9 w-9 text-teal-700" />
                <p className="mt-3 font-semibold text-slate-900">No hay pedidos con estos filtros</p>
                <p className="mt-1 text-sm text-slate-500">
                  Los pedidos nuevos del bot aparecerán aquí automáticamente.
                </p>
              </div>
            </div>
          )}
        </div>

        <div className="flex items-center justify-between border-t border-slate-200 px-3 py-2 text-xs text-slate-500">
          <span>
            Página {data?.page ?? page} · {data?.totalCount ?? 0} pedidos
          </span>
          <div className="flex gap-1">
            <Button
              type="button"
              variant="outline"
              size="icon"
              className="h-8 w-8"
              disabled={page <= 1 || loading}
              onClick={() => setPage((value) => Math.max(1, value - 1))}
              aria-label="Página anterior"
            >
              <ChevronLeft className="h-4 w-4" />
            </Button>
            <Button
              type="button"
              variant="outline"
              size="icon"
              className="h-8 w-8"
              disabled={!data?.hasMore || loading}
              onClick={() => setPage((value) => value + 1)}
              aria-label="Página siguiente"
            >
              <ChevronRight className="h-4 w-4" />
            </Button>
          </div>
        </div>
      </div>

      {detail && (
        <div className="fixed inset-0 z-[80] grid place-items-center bg-slate-950/55 p-4 backdrop-blur-sm">
          <section
            role="dialog"
            aria-modal="true"
            aria-label={`Pedido ${detail.orderNumber}`}
            className="flex max-h-[88vh] w-full max-w-3xl flex-col overflow-hidden rounded-2xl bg-white shadow-2xl"
          >
            <header className="flex items-start justify-between border-b border-slate-200 p-5">
              <div>
                <p className="text-xs font-bold uppercase tracking-[0.16em] text-teal-700">
                  Pedido comercial
                </p>
                <h3 className="mt-1 font-mono text-xl font-black text-slate-950">
                  {detail.orderNumber}
                </h3>
                <p className="mt-1 text-sm text-slate-500">
                  El IVA se determinará únicamente al facturar.
                </p>
              </div>
              <Button type="button" variant="ghost" size="icon" onClick={() => setDetail(null)}>
                <X className="h-5 w-5" />
              </Button>
            </header>
            <div className="grid gap-4 overflow-auto p-5 md:grid-cols-[1fr_280px]">
              <div className="space-y-2">
                {detail.lines.map((line) => (
                  <article key={line.orderItemId} className="rounded-xl border border-slate-200 p-3">
                    <div className="flex items-start justify-between gap-4">
                      <div>
                        <p className="font-semibold text-slate-950">{line.productName}</p>
                        <p className="text-xs text-slate-500">
                          {line.productCode || line.sku || "Sin código"} · {line.unitCode}
                        </p>
                      </div>
                      <p className="font-bold tabular-nums">{money.format(line.lineTotal)}</p>
                    </div>
                    <p className="mt-2 text-sm text-slate-600">
                      {line.quantity} × {money.format(line.unitPrice)}
                      {line.discountAmount > 0 && ` · Descuento ${money.format(line.discountAmount)}`}
                    </p>
                  </article>
                ))}
              </div>
              <aside className="space-y-4">
                <div className="rounded-xl bg-slate-50 p-4">
                  <div className="flex items-center gap-2 text-sm font-semibold text-slate-950">
                    <UserRound className="h-4 w-4 text-teal-700" />
                    {detail.customerName || "Consumidor final"}
                  </div>
                  <p className="mt-2 text-xs text-slate-500">
                    {detail.customerIdentification || "Sin identificación"}
                  </p>
                  <p className="text-xs text-slate-500">{detail.customerPhone || "Sin teléfono"}</p>
                </div>
                <dl className="space-y-2 rounded-xl border border-slate-200 p-4 text-sm">
                  <div className="flex justify-between">
                    <dt className="text-slate-500">Subtotal comercial</dt>
                    <dd>{money.format(detail.subtotal)}</dd>
                  </div>
                  <div className="flex justify-between">
                    <dt className="text-slate-500">Descuentos</dt>
                    <dd>-{money.format(detail.discountTotal)}</dd>
                  </div>
                  <div className="flex justify-between border-t border-slate-200 pt-3 text-base font-black">
                    <dt>Total pedido</dt>
                    <dd>{money.format(detail.total)}</dd>
                  </div>
                </dl>
                <div className="rounded-xl border border-teal-200 bg-teal-50 p-3 text-xs leading-5 text-teal-900">
                  <Check className="mb-1 h-4 w-4" />
                  Al facturar se aplicarán los impuestos vigentes y se generará una factura independiente.
                </div>
                {(detail.status === "EmissionFailed" || detail.status === "ProcessingEmission") && onRetryEmission && (
                  <Button
                    type="button"
                    className="w-full"
                    disabled={working}
                    onClick={async () => {
                      setWorking(true);
                      setError(null);
                      try {
                        await onRetryEmission(detail.orderId);
                        setDetail((current) => current ? { ...current, status: "ProcessingEmission" } : current);
                        setNotice("La emisión existente se reanudó sin crear otro comprobante.");
                        await refresh(true);
                      } catch (retryError) {
                        setError(retryError instanceof Error ? retryError.message : "No fue posible reanudar la emisión.");
                      } finally {
                        setWorking(false);
                      }
                    }}
                  >
                    {working ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <RotateCcw className="mr-2 h-4 w-4" />}
                    {detail.status === "ProcessingEmission" ? "Verificar y reanudar emisión" : "Reanudar emisión"}
                  </Button>
                )}
              </aside>
            </div>
          </section>
        </div>
      )}

      <Dialog open={transferOpen} onOpenChange={setTransferOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Datos de la transferencia</DialogTitle>
            <DialogDescription>
              La referencia identifica el movimiento. La cuenta principal se propone, pero puedes cambiarla.
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-4">
            {settlementConfiguration?.isAccountingEnabled && (
              <div className="space-y-2">
                <Label>Cuenta bancaria</Label>
                <Select value={transferBankAccountId} onValueChange={setTransferBankAccountId}>
                  <SelectTrigger><SelectValue placeholder="Selecciona una cuenta" /></SelectTrigger>
                  <SelectContent>
                    {settlementConfiguration.bankAccounts.map((account) => (
                      <SelectItem key={account.bankAccountId} value={account.bankAccountId}>
                        {account.displayName} · {account.bankName} · {account.accountNumber}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            )}
            <div className="space-y-2">
              <Label htmlFor="order-transfer-reference">Referencia</Label>
              <Input id="order-transfer-reference" maxLength={160} value={transferReference}
                onChange={(event) => setTransferReference(event.target.value)}
                placeholder="Comprobante o referencia bancaria" autoFocus />
            </div>
            <div className="space-y-2">
              <Label htmlFor="order-transfer-notes">Nota (opcional)</Label>
              <Textarea id="order-transfer-notes" maxLength={500} value={transferNotes}
                onChange={(event) => setTransferNotes(event.target.value)}
                placeholder="Detalle útil para conciliación" />
            </div>
          </div>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => {
              setTransferOpen(false);
              if (!transferReference.trim()) setPaymentMethod("Cash");
            }}>Cancelar</Button>
            <Button type="button"
              disabled={!transferReference.trim() || (settlementConfiguration?.isAccountingEnabled === true && !transferBankAccountId)}
              onClick={() => setTransferOpen(false)}>
              <Landmark className="mr-2 h-4 w-4" />Guardar transferencia
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

function OrderStatus({ availability }: { availability: ReturnType<typeof getOrderAvailability> }) {
  const styles = availability.tone === "available"
    ? "border-emerald-200 bg-emerald-50 text-emerald-800"
    : availability.tone === "owned"
      ? "border-teal-200 bg-teal-50 text-teal-800"
      : availability.tone === "claimed"
        ? "border-amber-200 bg-amber-50 text-amber-800"
        : availability.tone === "invoiced"
          ? "border-sky-200 bg-sky-50 text-sky-800"
          : "border-slate-200 bg-slate-50 text-slate-700";
  return (
    <Badge variant="outline" className={styles}>
      {availability.label}
    </Badge>
  );
}

function sourceLabel(source: number) {
  if (source === 0) return "Bot";
  if (source === 1) return "Administración";
  if (source === 2) return "Integración";
  return "Otro origen";
}
