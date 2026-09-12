"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { ArrowLeft, Minus, PackagePlus, Plus, Search, ShoppingBag, Trash2, X } from "lucide-react";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { loadSellerCatalog, loadSellerDraft, queueSellerOrder, removeSellerDraft, saveSellerCatalog, saveSellerDraft, saveSellerOrderSnapshot } from "@/lib/seller-order-offline-store";
import type { SalesRouteDetail, SalesRouteStop } from "@/services/api/routes";
import { sellerOrdersApi, type SellerCatalogItem, type SellerOrderRequest, type SellerOrderResult } from "@/services/api/seller-orders";
import type { CommerceOrderDetail } from "@/services/orders/commerce-orders-client";
import { SELLER_ORDER_SYNC_REQUEST_EVENT, sellerOrderErrorMessage } from "@/services/orders/seller-order-reliability";
import { useAuthStore } from "@/stores/auth-store";
import { editableOrderInitialState, editableOrderAvailableQuantity, sellerOrderMaximumQuantity, sellerOrderSubmitDisabled } from "@/components/orders/order-review";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });
export function SellerOrderCaptureDialog({ businessId, warehouseId, route, stop, editing, localFirst = false, allowsNegativeStockSales = false, onClose, onCreated }: { businessId: string; warehouseId: string; route: SalesRouteDetail | null; stop: SalesRouteStop; editing?: CommerceOrderDetail|null; localFirst?: boolean; allowsNegativeStockSales?: boolean; onClose: () => void; onCreated: (orderId: string) => Promise<void> }) {
  const userId = useAuthStore((state) => state.user?.userId ?? "");
  const key = `${businessId}:${warehouseId}:${route?.routeId ?? "outside-route"}:${stop.routeStopId}:${editing?.orderId??"new"}`;
  const initialEditingState = useMemo(
    () => editableOrderInitialState(editing?.lines ?? []),
    [editing],
  );
  const originalLinesByProduct = useMemo(
    () => new Map((editing?.lines ?? []).flatMap((line) => line.productId ? [[line.productId, line] as const] : [])),
    [editing],
  );
  const searchRef = useRef<HTMLInputElement>(null);
  const searchTimer = useRef<number | null>(null);
  const [query, setQuery] = useState("");
  const [searched, setSearched] = useState(false);
  const [results, setResults] = useState<SellerCatalogItem[]>([]);
  const [knownItems, setKnownItems] = useState<Record<string, SellerCatalogItem>>(
    () => initialEditingState.knownItems,
  );
  const [quantities, setQuantities] = useState<Record<string, number>>(
    () => initialEditingState.quantities,
  );
  const [notes, setNotes] = useState(() => editing?.notes ?? "");
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [quantityItem, setQuantityItem] = useState<SellerCatalogItem | null>(null);

  useEffect(() => {
    if (editing) return;
    let active = true;
    void Promise.all([loadSellerCatalog(businessId, warehouseId, stop.customerId), loadSellerDraft(key)]).then(async ([catalog, draft]) => {
      if (!active) return;
      let available = catalog;
      const requiredProducts=draft?.request.lines.map(line=>line.productId)??[];
      if (!localFirst && requiredProducts.some(productId=>!available.some(item=>item.productId===productId)) && navigatorOnline()) {
        try {
          const page = await sellerOrdersApi.catalog({ businessId, warehouseId, customerId: stop.customerId, search: undefined, skip: 0, take: 500 });
          available = [...new Map([...catalog, ...page.items].map((item) => [item.productId, item])).values()];
          await saveSellerCatalog(businessId, warehouseId, stop.customerId, page.items);
        } catch { /* The cached catalog still allows any known draft lines to render. */ }
      }
      if (!active) return;
      const known=Object.fromEntries(available.map((item) => [item.productId, item]));
      if(draft) for(const line of draft.request.lines){const catalogItem=known[line.productId];if(!catalogItem)continue;known[line.productId]={...catalogItem,unitPrice:typeof line.unitPrice==="number"&&line.unitPrice>0?line.unitPrice:catalogItem.unitPrice,priceSource:line.priceSource||catalogItem.priceSource};}
      setKnownItems((current) => ({ ...current, ...known }));
      if (draft) { setQuantities(draft.quantities); setNotes(draft.request.notes ?? ""); }
    });
    return () => { active = false; };
  }, [businessId, editing, key, localFirst, stop.customerId, warehouseId]);

  useEffect(() => {
    if (editing) return;
    if (!Object.values(quantities).some((quantity) => quantity > 0)) return;
    const lines=Object.entries(quantities).filter(([, quantity]) => quantity > 0).flatMap(([productId, quantity]) => { const item=knownItems[productId]; return item?[{ productId, quantity, unitPrice:item.unitPrice, discountAmount:0, priceSource:item.priceSource }]:[]; });
    if(!lines.length)return;
    const request: SellerOrderRequest = { businessId, warehouseId, customerId: stop.customerId, partySiteId: stop.partySiteId, routeId: route?.routeId ?? null, routeStopId: route ? stop.routeStopId : null, capturedOffline: localFirst || !navigatorOnline(), notes: notes || null, idempotencyKey: `draft-${key}`, lines };
    void saveSellerDraft(key, request, quantities);
  }, [businessId, editing, key, knownItems, localFirst, notes, quantities, route, stop.customerId, stop.partySiteId, stop.routeStopId, warehouseId]);

  const selected = useMemo(() => Object.entries(quantities).filter(([, quantity]) => quantity > 0).flatMap(([productId, quantity]) => knownItems[productId] ? [{ item: knownItems[productId], quantity }] : []), [knownItems, quantities]);
  const units = selected.reduce((sum, value) => sum + value.quantity, 0);
  const total = selected.reduce(
    (sum, value) => sum + value.item.unitPrice * value.quantity -
      (originalLinesByProduct.get(value.item.productId)?.discountAmount ?? 0),
    0,
  );
  const online = navigatorOnline() && !localFirst;
  const shortages = online && !allowsNegativeStockSales
    ? selected.filter((value) => value.item.manageStock && value.quantity > value.item.quantityOnHand)
    : [];
  const invalidQuantity = selected.some(({ item, quantity }) => quantity > sellerOrderMaximumQuantity(
    item.manageStock,
    item.quantityOnHand,
    originalLinesByProduct.get(item.productId)?.quantity ?? null,
    allowsNegativeStockSales,
  ));

  const search = async () => {
    if (searchTimer.current !== null) {
      window.clearTimeout(searchTimer.current);
      searchTimer.current = null;
    }
    const term = query.trim();
    if (term.length < 2) { toast.info("Escribe al menos dos letras o el código del producto."); return; }
    setLoading(true); setSearched(true);
    try {
      if (localFirst) {
        const cached = await loadSellerCatalog(businessId, warehouseId, stop.customerId);
        const normalized = term.toLocaleLowerCase("es");
        const filtered = cached.filter((item) => `${item.productCode} ${item.name}`.toLocaleLowerCase("es").includes(normalized)).slice(0, 100);
        setResults(filtered);
        setKnownItems((current) => ({ ...current, ...Object.fromEntries(cached.map((item) => [item.productId, item])) }));
        if (!filtered.length) toast.info("No encontramos coincidencias en el catálogo guardado.");
        return;
      }
      const page = await sellerOrdersApi.catalog({ businessId, warehouseId, customerId: stop.customerId, search: term, skip: 0, take: 100 });
      const visibleItems=page.items.map((item)=>{const original=originalLinesByProduct.get(item.productId);return original?{...item,unitPrice:original.unitPrice,priceSource:original.priceSource,quantityOnHand:editableOrderAvailableQuantity(item.quantityOnHand,original.reservedQuantity)}:item;});
      setResults(visibleItems);
      setKnownItems((current) => ({ ...current, ...Object.fromEntries(visibleItems.map((item) => [item.productId, item])) }));
      if (!editing) await saveSellerCatalog(businessId, warehouseId, stop.customerId, page.items);
    } catch (error) {
      if (editing) {
        setResults([]);
        toast.error(sellerOrderErrorMessage(error, "No fue posible buscar productos en línea."));
        return;
      }
      const cached = await loadSellerCatalog(businessId, warehouseId, stop.customerId);
      const normalized = term.toLocaleLowerCase("es");
      const filtered = cached.filter((item) => `${item.productCode} ${item.name}`.toLocaleLowerCase("es").includes(normalized)).slice(0, 100);
      setResults(filtered);
      setKnownItems((current) => ({ ...current, ...Object.fromEntries(cached.map((item) => [item.productId, item])) }));
      if (!filtered.length) toast.info("No encontramos coincidencias en el catálogo guardado.");
    } finally { setLoading(false); }
  };
  useEffect(() => {
    if (query.trim().length < 2) return;
    searchTimer.current = window.setTimeout(() => {
      searchTimer.current = null;
      void search();
    }, 250);
    return () => {
      if (searchTimer.current !== null) window.clearTimeout(searchTimer.current);
      searchTimer.current = null;
    };
    // The query is the only trigger; workspace and customer changes remount the dialog.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [query]);
  const change = (item: SellerCatalogItem, delta: number) => {
    const maximum = sellerOrderMaximumQuantity(
      item.manageStock,
      item.quantityOnHand,
      originalLinesByProduct.get(item.productId)?.quantity ?? null,
      allowsNegativeStockSales,
    );
    setKnownItems((current) => ({ ...current, [item.productId]: item }));
    setQuantities((current) => ({
      ...current,
      [item.productId]: Math.min(maximum, Math.max(0, (current[item.productId] ?? 0) + delta)),
    }));
  };
  const addFromSearch = (item: SellerCatalogItem, delta: number) => {
    change(item, delta);
    if (delta > 0) {
      setQuery("");
      setResults([]);
      setSearched(false);
      requestAnimationFrame(() => searchRef.current?.focus());
    }
  };
  const setQuantity = (item: SellerCatalogItem, value: number) => { setKnownItems((current) => ({ ...current, [item.productId]: item })); setQuantities((current) => ({ ...current, [item.productId]: Math.max(0, value) })); };
  const submit = async () => {
    if (!userId) { toast.error("No encontramos el usuario de esta sesión. Vuelve a iniciar sesión."); return; }
    setSaving(true);
    try {
      const request: SellerOrderRequest = { businessId, warehouseId, customerId: stop.customerId, partySiteId: stop.partySiteId, routeId: route?.routeId ?? null, routeStopId: route ? stop.routeStopId : null, capturedOffline: !online, notes: notes || null, idempotencyKey: crypto.randomUUID(), lines: selected.map(({ item, quantity }) => {const original=originalLinesByProduct.get(item.productId);return { productId: item.productId, quantity, unitPrice:item.unitPrice, discountAmount:original?.discountAmount??0, priceSource:item.priceSource };}) };
      let synchronized = online;
      let result: SellerOrderResult;
      if (editing) {
        result = await sellerOrdersApi.update(editing.orderId,{customerId:stop.customerId,notes:request.notes,idempotencyKey:crypto.randomUUID(),lines:request.lines});
      } else if (localFirst) {
        result = await queueSellerOrder(userId, request, route, localDateKey());
        synchronized = false;
      } else {
        if (!online) throw new Error("Prepara el teléfono con conexión antes de tomar pedidos sin Internet.");
        result = await sellerOrdersApi.create(request);
      }
      if (!editing) await saveSellerOrderSnapshot({idempotencyKey:request.idempotencyKey,userId,orderId:result.orderId,orderNumber:result.orderNumber,businessId,warehouseId,routeId:request.routeId,customerName:stop.customerName,total:synchronized?result.total:total,lineCount:selected.length,operationalDate:localDateKey(),createdAt:new Date().toISOString(),status:result.status,synchronized});
      await removeSellerDraft(key);
      if (!synchronized) { toast.info("Pedido guardado en este dispositivo", { description: online ? "Ya aparece en Pedidos y se está sincronizando en segundo plano." : "Aparece en Pedidos y se enviará automáticamente cuando regrese la conexión." }); window.dispatchEvent(new Event(SELLER_ORDER_SYNC_REQUEST_EVENT)); }
      else toast.success(result.requiresReview ? `${result.orderNumber} quedó en revisión` : editing?`${result.orderNumber} actualizado`:`${result.orderNumber} guardado`);
      await onCreated(result.orderId);
    } catch (error) { toast.error(sellerOrderErrorMessage(error)); }
    finally { setSaving(false); }
  };

  return <><Dialog open onOpenChange={(open) => !open && onClose()}><DialogContent className="flex h-[100dvh] max-h-[100dvh] w-screen max-w-none flex-col gap-0 overflow-hidden rounded-none p-0 sm:h-[min(86dvh,720px)] sm:w-[calc(100vw-3rem)] sm:max-w-3xl sm:rounded-[2rem] sm:border-slate-200">
    <DialogHeader className="shrink-0 border-b bg-background px-4 pb-4 pt-5 text-left sm:px-6"><div className="flex items-start gap-3"><Button type="button" size="icon" variant="ghost" className="-ml-2 shrink-0" onClick={onClose}><ArrowLeft className="h-5 w-5"/></Button><div className="min-w-0 flex-1"><DialogTitle className="truncate">{editing?`Editar ${editing.orderNumber}`:stop.customerName}</DialogTitle><DialogDescription className="line-clamp-1">{stop.siteName} · {stop.addressLine}</DialogDescription></div><Badge className="rounded-full bg-teal-600">{selected.length}</Badge></div><div className="relative mt-4"><Search className="pointer-events-none absolute left-3 top-3.5 h-5 w-5 text-teal-700"/><Input ref={searchRef} enterKeyHint="search" autoComplete="off" className="h-12 rounded-2xl border-teal-300 bg-white pl-10 pr-12 text-base" value={query} onChange={(event)=>{setQuery(event.target.value);setSearched(false)}} onKeyDown={(event)=>{if(event.key==="Enter"){event.preventDefault();void search()}}} placeholder="Buscar por nombre o código"/>{query&&<Button type="button" size="icon" variant="ghost" className="absolute right-1.5 top-1.5 h-9 w-9 rounded-xl" onClick={()=>{setQuery("");setResults([]);setSearched(false);searchRef.current?.focus()}}><X className="h-4 w-4"/></Button>}</div></DialogHeader>
    <main className="min-h-0 flex-1 overflow-y-auto bg-slate-50/70 px-3 py-4 sm:px-6">
      {shortages.length > 0 && <div className="mb-4 rounded-2xl border border-amber-300 bg-amber-50 p-4 text-sm text-amber-950"><strong>{shortages.length} {shortages.length === 1 ? "producto requiere" : "productos requieren"} ajuste.</strong><p className="mt-1 text-xs">Reduce la cantidad al saldo disponible o elimina los productos sin existencia para confirmar la reserva en Pedidos.</p></div>}
      {query.trim().length>=2||searched||loading?<SearchResults searching={loading} searched={searched} results={results} quantities={quantities} online={online} allowsNegativeStockSales={allowsNegativeStockSales} originalLinesByProduct={originalLinesByProduct} onChange={addFromSearch} onEdit={setQuantityItem}/>:<Cart items={selected} online={online} allowsNegativeStockSales={allowsNegativeStockSales} originalLinesByProduct={originalLinesByProduct} onChange={change} onEdit={setQuantityItem} onSearch={()=>searchRef.current?.focus()}/>}
    </main>
    <footer className="shrink-0 border-t bg-background/95 px-3 pb-[max(.75rem,env(safe-area-inset-bottom))] pt-3 shadow-[0_-12px_35px_-24px_rgba(15,118,110,.7)] backdrop-blur sm:px-6"><div className="mb-3 flex items-center justify-between gap-3"><button type="button" onClick={()=>{setQuery("");setResults([]);setSearched(false)}} className="text-left"><small className="block text-muted-foreground">{selected.length} productos · {units.toLocaleString("es-CO", { maximumFractionDigits: 3 })} unidades</small><strong className="text-xl">{money.format(total)}</strong></button>{(query||searched)&&<Button type="button" variant="outline" onClick={()=>{setQuery("");setResults([]);setSearched(false)}}><ShoppingBag className="mr-2 h-4 w-4"/>Ver pedido</Button>}</div><Input value={notes} onChange={(event) => setNotes(event.target.value)} className="mb-3" placeholder="Nota para este pedido (opcional)"/><Button className="h-12 w-full bg-teal-600 text-base font-bold hover:bg-teal-700" disabled={sellerOrderSubmitDisabled(saving, selected.length, Boolean(editing), online, invalidQuantity)} onClick={submit}><PackagePlus className="mr-2 h-5 w-5"/>{saving ? "Guardando…" : editing?`Actualizar pedido · ${money.format(total)}`:online ? `Crear pedido · ${money.format(total)}` : "Guardar para sincronizar"}</Button></footer>
  </DialogContent></Dialog>{quantityItem && <QuantityDialog item={quantityItem} current={quantities[quantityItem.productId] ?? 0} maximumQuantity={sellerOrderMaximumQuantity(quantityItem.manageStock, quantityItem.quantityOnHand, originalLinesByProduct.get(quantityItem.productId)?.quantity ?? null, allowsNegativeStockSales)} onClose={() => setQuantityItem(null)} onConfirm={(quantity) => { setQuantity(quantityItem, quantity); setQuantityItem(null); }}/>}</>;
}

function Cart({ items, online, allowsNegativeStockSales, originalLinesByProduct, onChange, onEdit, onSearch }: { items: Array<{ item: SellerCatalogItem; quantity: number }>; online: boolean; allowsNegativeStockSales: boolean; originalLinesByProduct: ReadonlyMap<string, CommerceOrderDetail["lines"][number]>; onChange: (item: SellerCatalogItem, delta: number) => void; onEdit: (item: SellerCatalogItem) => void; onSearch: () => void }) {
  if (!items.length) return <div className="grid min-h-[22rem] place-items-center text-center"><div><span className="mx-auto grid h-16 w-16 place-items-center rounded-3xl bg-teal-100 text-teal-700"><ShoppingBag className="h-8 w-8"/></span><h2 className="mt-5 text-xl font-black">Empieza este pedido</h2><p className="mx-auto mt-2 max-w-sm text-sm text-muted-foreground">Aquí aparecerán primero todos los productos agregados, con sus cantidades y total.</p><Button className="mt-6 h-12 bg-teal-600 hover:bg-teal-700" onClick={onSearch}><Search className="mr-2 h-5 w-5"/>Buscar productos</Button></div></div>;
  return <div className="space-y-3"><div className="flex items-center justify-between"><div><h2 className="font-black">Productos agregados</h2><p className="text-xs text-muted-foreground">Revisa cantidades antes de guardar.</p></div><Button size="sm" variant="outline" onClick={onSearch}><Plus className="mr-1 h-4 w-4"/>Agregar</Button></div>{items.map(({ item, quantity }) => <ProductRow key={item.productId} item={item} quantity={quantity} online={online} maximumQuantity={sellerOrderMaximumQuantity(item.manageStock, item.quantityOnHand, originalLinesByProduct.get(item.productId)?.quantity ?? null, allowsNegativeStockSales)} selected onChange={onChange} onEdit={onEdit}/>)}</div>;
}

function SearchResults({ searching, searched, results, quantities, online, allowsNegativeStockSales, originalLinesByProduct, onChange, onEdit }: { searching: boolean; searched: boolean; results: SellerCatalogItem[]; quantities: Record<string, number>; online: boolean; allowsNegativeStockSales: boolean; originalLinesByProduct: ReadonlyMap<string, CommerceOrderDetail["lines"][number]>; onChange: (item: SellerCatalogItem, delta: number) => void; onEdit: (item: SellerCatalogItem) => void }) {
  return <div className="space-y-3">{searching&&<p className="rounded-3xl border bg-white p-8 text-center text-sm text-muted-foreground">Buscando productos…</p>}{searched&&!searching&&!results.length&&<p className="rounded-3xl border border-dashed bg-white p-10 text-center text-sm text-muted-foreground">No encontramos productos con esa búsqueda.</p>}{results.map((item)=><ProductRow key={item.productId} item={item} quantity={quantities[item.productId]??0} online={online} maximumQuantity={sellerOrderMaximumQuantity(item.manageStock, item.quantityOnHand, originalLinesByProduct.get(item.productId)?.quantity ?? null, allowsNegativeStockSales)} selected={(quantities[item.productId]??0)>0} onChange={onChange} onEdit={onEdit}/>)}</div>;
}

function ProductRow({ item, quantity, online, maximumQuantity, selected, onChange, onEdit }: { item: SellerCatalogItem; quantity: number; online: boolean; maximumQuantity: number; selected: boolean; onChange: (item: SellerCatalogItem, delta: number) => void; onEdit: (item: SellerCatalogItem) => void }) {
  const short = Number.isFinite(maximumQuantity) && item.manageStock && quantity > item.quantityOnHand;
  return <article className={`rounded-3xl border bg-white p-4 transition ${selected ? "border-teal-400 shadow-sm ring-1 ring-teal-100" : ""}`}><div className="flex items-start justify-between gap-3"><button type="button" className="min-w-0 flex-1 text-left" onClick={() => onEdit(item)}><strong className="block leading-tight">{item.name}</strong><small className="mt-1 block text-muted-foreground">{item.productCode} · {item.unitCode} · Disponible {item.quantityOnHand}</small></button><strong className="shrink-0">{money.format(item.unitPrice)}</strong></div><div className="mt-4 flex items-center justify-between gap-2"><Badge variant="outline">{item.priceSource === "PriceChannel" ? "Canal" : "Público"}</Badge><span className="flex items-center rounded-2xl border bg-background p-1"><Button type="button" size="icon" variant="ghost" className="h-9 w-9 rounded-xl" disabled={quantity <= 0} onClick={() => onChange(item, -1)}>{quantity === 1 ? <Trash2 className="h-4 w-4"/> : <Minus className="h-4 w-4"/>}</Button><button type="button" onClick={() => onEdit(item)} className="h-9 min-w-12 px-2 text-center text-base font-black">{quantity || 0}</button><Button type="button" size="icon" className="h-9 w-9 rounded-xl bg-teal-600" disabled={quantity >= maximumQuantity} onClick={() => onChange(item, 1)}><Plus className="h-4 w-4"/></Button></span></div>{short && <p className="mt-2 text-xs font-semibold text-amber-800">{online ? "Supera la existencia disponible; reduce la cantidad para dejarlo disponible." : "Se revisará al sincronizar."}</p>}</article>;
}

function QuantityDialog({ item, current, maximumQuantity, onClose, onConfirm }: { item: SellerCatalogItem; current: number; maximumQuantity: number; onClose: () => void; onConfirm: (quantity: number) => void }) { const [value, setValue] = useState(String(current || 1)); const quantity = Math.max(0, Number(value) || 0); const overStock = quantity > maximumQuantity; return <Dialog open onOpenChange={(open) => !open && onClose()}><DialogContent className="w-[calc(100%-1.5rem)] rounded-3xl p-5 sm:max-w-sm"><DialogHeader><DialogTitle>Cantidad</DialogTitle><DialogDescription>{item.name} · disponible {item.quantityOnHand}</DialogDescription></DialogHeader><Input autoFocus className="h-16 rounded-2xl text-center text-3xl font-black" inputMode="decimal" value={value} onFocus={(event) => event.currentTarget.select()} onChange={(event) => setValue(event.target.value)} onKeyDown={(event) => { if (event.key === "Enter" && quantity > 0 && !overStock) onConfirm(quantity); }}/>{overStock && <p className="text-sm font-medium text-red-700">La bodega no permite negativos y esa cantidad supera el máximo disponible.</p>}<DialogFooter className="grid grid-cols-2 gap-2 sm:grid-cols-2"><Button variant="outline" onClick={onClose}>Cancelar</Button><Button className="bg-teal-600" disabled={quantity <= 0 || overStock} onClick={() => onConfirm(quantity)}>Aplicar</Button></DialogFooter></DialogContent></Dialog>; }
function localDateKey(value = new Date()) { return `${value.getFullYear()}-${String(value.getMonth() + 1).padStart(2, "0")}-${String(value.getDate()).padStart(2, "0")}`; }
function navigatorOnline() { return typeof navigator === "undefined" || navigator.onLine; }
