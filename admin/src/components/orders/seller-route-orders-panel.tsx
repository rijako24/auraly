"use client";

import { useEffect, useState } from "react";
import { ClipboardList, Loader2, PackageCheck, Pencil, RefreshCw } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { SellerOrderCaptureDialog } from "@/components/orders/seller-order-capture-dialog";
import type { SalesRouteDetail, SalesRouteStop } from "@/services/api/routes";
import { useAuthStore } from "@/stores/auth-store";
import { loadCommerceOrder, loadCommerceOrders, type CommerceOrderDetail, type CommerceOrderListItem } from "@/services/orders/commerce-orders-client";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });
const time = new Intl.DateTimeFormat("es-CO", { hour: "numeric", minute: "2-digit" });

export function SellerRouteOrdersPanel({ route, warehouseId, operationalDate, revision }: { route: SalesRouteDetail; warehouseId: string; operationalDate: string; revision: number }) {
  const canUpdate = useAuthStore((state) => state.user?.permissions?.includes("orders.update") ?? false);
  const [items, setItems] = useState<CommerceOrderListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [detail, setDetail] = useState<CommerceOrderDetail | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [refreshKey, setRefreshKey] = useState(0);
  const [editing, setEditing] = useState<{ order: CommerceOrderDetail; stop: SalesRouteStop } | null>(null);
  useEffect(() => {
    let active = true;
    const from = new Date(`${operationalDate}T00:00:00`);
    const to = new Date(from); to.setDate(to.getDate() + 1);
    setLoading(true); setError(null);
    const common = { page: 1, pageSize: 100, source: 1, warehouseId, onlyMine: true, createdFrom: from.toISOString(), createdTo: to.toISOString() };
    void loadCommerceOrders(common)
      .then((page) => { if (active) setItems(page.items); })
      .catch((caught) => { if (active) setError(caught instanceof Error ? caught.message : "No fue posible consultar tus pedidos de hoy."); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [operationalDate, refreshKey, revision, warehouseId]);
  const total = items.reduce((sum, item) => sum + item.total, 0);
  const open = async (orderId: string) => { setDetailLoading(true); try { setDetail(await loadCommerceOrder(orderId)); } catch (caught) { setError(caught instanceof Error ? caught.message : "No fue posible abrir el pedido."); } finally { setDetailLoading(false); } };
  const editDetail = canUpdate && detail && (["InReview","Pending","Available","Confirmed"].includes(detail.status)) && detail.customerId ? route.stops.find((stop) => stop.customerId === detail.customerId) : undefined;
  return <div className="space-y-4"><section className="grid grid-cols-2 gap-3"><div className="rounded-3xl border bg-card p-5"><small className="font-semibold text-muted-foreground">Mis pedidos de hoy</small><strong className="mt-1 block text-3xl font-black">{items.length}</strong></div><div className="rounded-3xl border bg-card p-5"><small className="font-semibold text-muted-foreground">Valor tomado hoy</small><strong className="mt-1 block text-xl font-black sm:text-2xl">{money.format(total)}</strong></div></section><div className="flex items-center justify-between"><div><h2 className="font-black">Pedidos hechos hoy</h2><p className="text-xs text-muted-foreground">Incluye pedidos en ruta y fuera de ruta del vendedor autenticado.</p></div><Button size="icon" variant="outline" aria-label="Actualizar pedidos" onClick={() => setRefreshKey((value) => value + 1)}><RefreshCw className="h-4 w-4"/></Button></div>{loading && <div className="flex items-center justify-center gap-2 rounded-3xl border p-12 text-sm text-muted-foreground"><Loader2 className="h-5 w-5 animate-spin"/>Actualizando pedidos…</div>}{error && <div className="rounded-2xl border border-red-200 bg-red-50 p-4 text-sm text-red-800">{error}</div>}{!loading && !error && !items.length && <div className="rounded-3xl border border-dashed p-12 text-center"><ClipboardList className="mx-auto h-10 w-10 text-teal-600"/><h3 className="mt-3 font-bold">Todavía no hay pedidos</h3><p className="mt-1 text-sm text-muted-foreground">Tu primer pedido de hoy aparecerá aquí inmediatamente.</p></div>}<div className="grid gap-3 md:grid-cols-2">{items.map((item) => <button type="button" key={item.orderId} onClick={() => void open(item.orderId)} className="rounded-3xl border bg-card p-4 text-left transition hover:border-teal-300 hover:shadow-md"><div className="flex items-start justify-between gap-3"><span className="grid h-11 w-11 shrink-0 place-items-center rounded-2xl bg-teal-50 text-teal-700"><PackageCheck className="h-5 w-5"/></span><StatusBadge status={item.status}/></div><strong className="mt-3 block truncate">{item.customerName ?? "Cliente"}</strong><small className="block text-muted-foreground">{item.orderNumber} · {time.format(new Date(item.createdAt))}</small><div className="mt-4 flex items-end justify-between border-t pt-3"><span className="text-xs text-muted-foreground">{item.lineCount} producto{item.lineCount === 1 ? "" : "s"}</span><strong>{money.format(item.total)}</strong></div></button>)}</div>{detailLoading && <p className="text-center text-sm text-muted-foreground">Abriendo detalle…</p>}{detail && <OrderDetail value={detail} editable={Boolean(editDetail)} onEdit={()=>{if(editDetail){setEditing({order:detail,stop:editDetail});setDetail(null)}}} onClose={() => setDetail(null)}/>} {editing&&<SellerOrderCaptureDialog businessId={route.businessId} warehouseId={warehouseId} route={route} stop={editing.stop} editing={editing.order} onClose={()=>setEditing(null)} onCreated={async()=>{setEditing(null);setRefreshKey(value=>value+1)}}/>}</div>;
}

function OrderDetail({ value, editable, onEdit, onClose }: { value: CommerceOrderDetail; editable:boolean; onEdit:()=>void; onClose: () => void }) { return <Dialog open onOpenChange={(open) => !open && onClose()}><DialogContent className="max-h-[92dvh] w-[calc(100%-1.5rem)] overflow-y-auto rounded-3xl sm:max-w-xl"><DialogHeader><DialogTitle>{value.orderNumber}</DialogTitle><DialogDescription>{value.customerName} · {new Date(value.createdAt).toLocaleString("es-CO")}</DialogDescription></DialogHeader><div className="space-y-2">{value.lines.map((line) => <div key={line.orderItemId} className="flex items-start justify-between gap-3 rounded-2xl border p-3"><span className="min-w-0"><strong className="block truncate text-sm">{line.productName}</strong><small className="text-muted-foreground">{line.quantity} {line.unitCode} · {money.format(line.unitPrice)}</small></span><strong className="shrink-0 text-sm">{money.format(line.lineTotal)}</strong></div>)}</div><div className="flex items-center justify-between rounded-2xl bg-slate-950 p-4 text-white"><span>Total</span><strong className="text-xl">{money.format(value.total)}</strong></div>{editable&&<Button className="w-full" onClick={onEdit}><Pencil className="mr-2 h-4 w-4"/>Actualizar pedido en revisión</Button>}</DialogContent></Dialog>; }
function StatusBadge({ status }: { status: string }) { const label = status === "Available" ? "Listo para facturar" : status === "Invoiced" ? "Facturado" : status === "InReview" || status === "Pending" ? "Pendiente de inventario" : status; const pending=status === "InReview"||status === "Pending"; return <Badge variant="outline" className={pending ? "border-amber-200 bg-amber-50 text-amber-800" : "border-emerald-200 bg-emerald-50 text-emerald-800"}>{label}</Badge>; }
