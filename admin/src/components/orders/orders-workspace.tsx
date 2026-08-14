"use client";

import {
  CalendarDays,
  Check,
  ChevronLeft,
  ChevronRight,
  ClipboardList,
  Expand,
  FileText,
  Loader2,
  PackageSearch,
  Receipt,
  RotateCcw,
  Search,
  UserRound,
  X,
} from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  type CommerceOrderDetail,
  type CommerceOrderListItem,
  type CommerceOrderPage,
} from "@/services/orders/commerce-orders-client";

const money = new Intl.NumberFormat("es-CO", {
  style: "currency",
  currency: "COP",
  maximumFractionDigits: 0,
});

const date = new Intl.DateTimeFormat("es-CO", {
  dateStyle: "medium",
  timeStyle: "short",
});

type OrdersWorkspaceProps = {
  compact?: boolean;
  connected?: boolean;
  showHeader?: boolean;
  loadPage: (filters: {
    page: number;
    pageSize: number;
    orderNumber?: string;
    customer?: string;
    product?: string;
    status?: string;
    createdFrom?: string;
    createdTo?: string;
  }) => Promise<CommerceOrderPage>;
  loadDetail: (orderId: string) => Promise<CommerceOrderDetail>;
  onRecover?: (order: CommerceOrderListItem) => Promise<void>;
  onInvoiceSelected?: (
    orders: CommerceOrderListItem[],
    paymentMethodCode: string,
  ) => Promise<{ completedCount: number; failedCount: number }>;
  onExpand?: () => void;
};

export function OrdersWorkspace({
  compact = false,
  connected = true,
  showHeader = true,
  loadPage,
  loadDetail,
  onRecover,
  onInvoiceSelected,
  onExpand,
}: OrdersWorkspaceProps) {
  const [page, setPage] = useState(1);
  const [data, setData] = useState<CommerceOrderPage | null>(null);
  const [query, setQuery] = useState("");
  const [customer, setCustomer] = useState("");
  const [product, setProduct] = useState("");
  const [status, setStatus] = useState("Available");
  const [createdFrom, setCreatedFrom] = useState("");
  const [createdTo, setCreatedTo] = useState("");
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [detail, setDetail] = useState<CommerceOrderDetail | null>(null);
  const [loading, setLoading] = useState(false);
  const [working, setWorking] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [paymentMethod, setPaymentMethod] = useState("Cash");
  const pageSize = compact ? 8 : 20;

  const refresh = useCallback(async () => {
    if (!connected) {
      setData(null);
      return;
    }
    setLoading(true);
    setError(null);
    try {
      const next = await loadPage({
        page,
        pageSize,
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
      });
      setData(next);
      setSelected((current) => {
        const visible = new Set(next.items.map((item) => item.orderId));
        return new Set([...current].filter((id) => visible.has(id)));
      });
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "No fue posible consultar los pedidos.");
    } finally {
      setLoading(false);
    }
  }, [
    connected,
    createdFrom,
    createdTo,
    customer,
    loadPage,
    page,
    pageSize,
    product,
    query,
    status,
  ]);

  useEffect(() => {
    const timer = window.setTimeout(() => void refresh(), query ? 250 : 0);
    return () => window.clearTimeout(timer);
  }, [refresh, query]);

  const selectedOrders = useMemo(
    () => (data?.items ?? []).filter((item) => selected.has(item.orderId)),
    [data?.items, selected],
  );
  const selectable = (data?.items ?? []).filter((item) => item.canInvoice);
  const allSelected =
    selectable.length > 0 &&
    selectable.every((item) => selected.has(item.orderId));

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
    setWorking(true);
    setError(null);
    setNotice(null);
    try {
      const result = await onInvoiceSelected(selectedOrders, paymentMethod);
      setNotice(
        result.failedCount === 0
          ? `${result.completedCount} ${result.completedCount === 1 ? "pedido facturado" : "pedidos facturados"} correctamente.`
          : `${result.completedCount} facturados y ${result.failedCount} pendientes de revisar.`,
      );
      setSelected(new Set());
      await refresh();
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "No fue posible facturar los pedidos.");
    } finally {
      setWorking(false);
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
            <SelectItem value="Cancelled">Cancelados</SelectItem>
            <SelectItem value="All">Todos</SelectItem>
          </SelectContent>
        </Select>
        {!compact && (
          <div className="flex gap-2 xl:col-span-2">
            <label className="relative flex-1">
              <CalendarDays className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
              <Input
                type="date"
                value={createdFrom}
                onChange={(event) => {
                  setCreatedFrom(event.target.value);
                  setPage(1);
                }}
                className="pl-9"
                aria-label="Pedidos desde"
              />
            </label>
            <label className="relative flex-1">
              <CalendarDays className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
              <Input
                type="date"
                value={createdTo}
                onChange={(event) => {
                  setCreatedTo(event.target.value);
                  setPage(1);
                }}
                className="pl-9"
                aria-label="Pedidos hasta"
              />
            </label>
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

      <div className="flex min-h-0 flex-col overflow-hidden rounded-xl border border-slate-200 bg-white">
        {!compact && (
          <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-200 bg-slate-50/80 px-4 py-3">
            <label className="flex items-center gap-2 text-sm font-medium text-slate-700">
              <input
                type="checkbox"
                checked={allSelected}
                onChange={() =>
                  setSelected(
                    allSelected
                      ? new Set()
                      : new Set(selectable.map((item) => item.orderId)),
                  )
                }
                className="h-4 w-4 rounded border-slate-300 accent-teal-700"
              />
              Seleccionar disponibles
            </label>
            <div className="flex items-center gap-2">
              <Select value={paymentMethod} onValueChange={setPaymentMethod}>
                <SelectTrigger className="w-44">
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
                disabled={!selectedOrders.length || working || !onInvoiceSelected}
                onClick={() => void invoiceSelected()}
                className="bg-teal-700 text-white hover:bg-teal-800"
              >
                {working ? (
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                ) : (
                  <Receipt className="mr-2 h-4 w-4" />
                )}
                Facturar seleccionados ({selectedOrders.length})
              </Button>
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
                      <input
                        type="checkbox"
                        checked={checked}
                        disabled={!order.canInvoice}
                        onChange={() =>
                          setSelected((current) => {
                            const next = new Set(current);
                            if (next.has(order.orderId)) next.delete(order.orderId);
                            else next.add(order.orderId);
                            return next;
                          })
                        }
                        className="h-4 w-4 rounded border-slate-300 accent-teal-700"
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
                        <OrderStatus status={order.status} />
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
                      disabled={!order.canInvoice || !onRecover || working}
                      onClick={() => void recover(order)}
                      className="flex h-9 items-center justify-center gap-2 rounded-lg bg-teal-50 px-3 text-sm font-bold text-teal-800 transition hover:bg-teal-100 disabled:cursor-not-allowed disabled:opacity-40"
                    >
                      <RotateCcw className="h-4 w-4" />
                      Recuperar
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
              </aside>
            </div>
          </section>
        </div>
      )}
    </div>
  );
}

function OrderStatus({ status }: { status: string }) {
  const styles =
    status === "Available"
      ? "border-emerald-200 bg-emerald-50 text-emerald-800"
      : status === "Invoiced"
        ? "border-sky-200 bg-sky-50 text-sky-800"
        : "border-slate-200 bg-slate-50 text-slate-700";
  const label =
    status === "Available"
      ? "Disponible"
      : status === "Invoiced"
        ? "Facturado"
        : status === "Cancelled"
          ? "Cancelado"
          : status;
  return (
    <Badge variant="outline" className={styles}>
      {label}
    </Badge>
  );
}

function sourceLabel(source: number) {
  if (source === 0) return "Bot";
  if (source === 1) return "Administración";
  if (source === 2) return "Integración";
  return "Otro origen";
}
