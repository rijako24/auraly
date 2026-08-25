"use client";

import { useEffect, useMemo, useRef, useState, type KeyboardEvent, type UIEvent } from "react";
import { useInfiniteQuery } from "@tanstack/react-query";
import { AlertTriangle, Barcode, Check, Loader2, PackageX, Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { inventoryApi, type InventoryProductItem } from "@/services/api/inventory";
import { productsApi } from "@/services/api/products";

const PRODUCT_PAGE_SIZE = 50;

export function ProductPicker({
  businessId,
  warehouseId,
  selectedProductIds,
  excludedProductIds = new Set<string>(),
  disabled,
  onSelect,
  label = "Agregar productos",
  conversionOnly = false,
  conversionFamilyRootProductId,
  resultsMode = "popover",
  inputId = "product-picker-search",
  requireZeroInventory = false,
}: {
  businessId: string;
  warehouseId?: string;
  selectedProductIds: ReadonlySet<string>;
  excludedProductIds?: ReadonlySet<string>;
  disabled: boolean;
  onSelect: (product: InventoryProductItem) => void;
  label?: string;
  conversionOnly?: boolean;
  conversionFamilyRootProductId?: string;
  resultsMode?: "popover" | "inline";
  inputId?: string;
  requireZeroInventory?: boolean;
}) {
  const [search, setSearch] = useState("");
  const [open, setOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState(0);
  const [inventoryBlocked, setInventoryBlocked] = useState<InventoryProductItem | null>(null);
  const listRef = useRef<HTMLDivElement>(null);
  const pickerRef = useRef<HTMLDivElement>(null);

  const query = useInfiniteQuery({
    queryKey: ["product-picker", businessId, warehouseId ?? "catalog", conversionOnly, conversionFamilyRootProductId ?? "all-families", search.trim()],
    queryFn: async ({ pageParam }) => {
      if (warehouseId && conversionOnly) return inventoryApi.conversionProducts({ warehouseId, familyRootProductId: conversionFamilyRootProductId, search: search.trim() || undefined, page: pageParam, pageSize: PRODUCT_PAGE_SIZE });
      if (warehouseId) return inventoryApi.products({ warehouseId, search: search.trim() || undefined, page: pageParam, pageSize: PRODUCT_PAGE_SIZE });
      const page = await productsApi.list(businessId, { page: pageParam, pageSize: PRODUCT_PAGE_SIZE, search: search.trim() || undefined, includeInactive: false });
      return { ...page, items: page.items.map((product) => ({ productId: product.productId, productCode: product.productCode ?? product.sku ?? "", reference: product.reference ?? null, productName: product.name, unitCode: "EA", quantityOnHand: product.stockQuantity ?? 0, averageUnitCost: null, saleUnitPrice: product.unitPrice })) };
    },
    initialPageParam: 1,
    getNextPageParam: (lastPage) => lastPage.page < lastPage.totalPages ? lastPage.page + 1 : undefined,
    enabled: Boolean(businessId) && open && !disabled,
  });
  const allProducts = useMemo(() => query.data?.pages.flatMap((page) => page.items) ?? [], [query.data]);
  const products = useMemo(() => allProducts.filter((product) => !excludedProductIds.has(product.productId)), [allProducts, excludedProductIds]);
  const totalCount = Math.max(0, (query.data?.pages[0]?.totalCount ?? 0) - allProducts.filter((product) => excludedProductIds.has(product.productId)).length);

  useEffect(() => { setActiveIndex(0); listRef.current?.scrollTo({ top: 0 }); }, [search, warehouseId, conversionOnly, conversionFamilyRootProductId]);
  useEffect(() => {
    if (!open) return;
    const closeOnOutsidePointer = (event: PointerEvent) => {
      if (!pickerRef.current?.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener("pointerdown", closeOnOutsidePointer, true);
    return () => document.removeEventListener("pointerdown", closeOnOutsidePointer, true);
  }, [open]);
  useEffect(() => { if (activeIndex >= products.length) setActiveIndex(Math.max(0, products.length - 1)); }, [activeIndex, products.length]);

  function choose(product: InventoryProductItem) {
    if (requireZeroInventory && product.quantityOnHand !== 0) {
      setInventoryBlocked(product);
      setOpen(false);
      return;
    }
    onSelect(product); setSearch(""); setOpen(false);
  }
  async function chooseActive() {
    let product: InventoryProductItem | undefined = products[activeIndex];
    if (!product && !query.isFetching) {
      const refreshed = await query.refetch();
      product = refreshed.data?.pages.flatMap((page) => page.items).find((item) => !excludedProductIds.has(item.productId));
    }
    if (product) choose(product);
  }
  async function moveDown() {
    if (activeIndex < products.length - 1) { setActiveIndex((current) => current + 1); return; }
    if (!query.hasNextPage || query.isFetchingNextPage) return;
    const previousLength = products.length;
    const next = await query.fetchNextPage();
    const nextLength = next.data?.pages.flatMap((page) => page.items).filter((item) => !excludedProductIds.has(item.productId)).length ?? previousLength;
    if (nextLength > previousLength) setActiveIndex(previousLength);
  }
  function keyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === "ArrowDown") { event.preventDefault(); setOpen(true); void moveDown(); }
    else if (event.key === "ArrowUp") { event.preventDefault(); setActiveIndex((current) => Math.max(0, current - 1)); }
    else if (event.key === "Enter") { event.preventDefault(); void chooseActive(); }
    else if (event.key === "Escape") { event.preventDefault(); setOpen(false); }
  }
  function scroll(event: UIEvent<HTMLDivElement>) {
    const target = event.currentTarget;
    if (target.scrollHeight - target.scrollTop - target.clientHeight < 80 && query.hasNextPage && !query.isFetchingNextPage) void query.fetchNextPage();
  }

  function resultRows(inline: boolean) {
    const messageClass = inline ? "col-span-3 p-4 text-sm text-muted-foreground" : "p-4 text-sm text-muted-foreground";
    if (!open) return <p className={messageClass}>Busca un producto para ver resultados.</p>;
    if (query.isLoading) return <p className={`${messageClass} flex items-center gap-2`}><Loader2 className="h-4 w-4 animate-spin" />Buscando productos…</p>;
    if (query.isError) return <div className={inline ? "col-span-3 p-4 text-sm text-red-700" : "p-4 text-sm text-red-700"}><p>No fue posible cargar los productos.</p><Button className="mt-3" size="sm" variant="outline" onClick={() => void query.refetch()}>Reintentar</Button></div>;
    if (products.length === 0) return <p className={messageClass}>No hay productos activos que coincidan con la búsqueda.</p>;
    return <>
      <div className={inline ? "col-span-3 px-3 py-2 text-xs text-muted-foreground" : "px-3 py-2 text-xs text-muted-foreground"}>{products.length.toLocaleString("es-CO")} de {totalCount.toLocaleString("es-CO")} productos</div>
      {products.map((product, index) => <button key={product.productId} type="button" role="option" aria-selected={activeIndex === index} onMouseDown={(event) => event.preventDefault()} onMouseEnter={() => setActiveIndex(index)} onClick={() => choose(product)} className={`${inline ? "col-span-3 grid grid-cols-[minmax(0,1fr)_minmax(120px,0.55fr)_100px] items-center" : "flex items-center justify-between"} w-full gap-4 border-t px-3 py-2.5 text-left text-sm ${activeIndex === index ? "bg-emerald-50 text-emerald-950" : "hover:bg-muted"}`}>
        <span className="min-w-0"><strong className="block truncate">{product.productName}</strong>{conversionOnly && product.conversionFactor && <small className="block truncate text-muted-foreground">Factor {product.conversionFactor}</small>}</span>
        <small className="min-w-0 truncate text-muted-foreground">{product.productCode || "Sin código"}{product.reference ? ` · ${product.reference}` : ""}</small>
        <span className="flex items-center justify-end gap-3 text-xs text-muted-foreground">{product.quantityOnHand}{selectedProductIds.has(product.productId) && <Check className="h-4 w-4 text-emerald-700" aria-label="Agregado" />}</span>
      </button>)}
      {query.hasNextPage && <Button type="button" variant="ghost" className={inline ? "col-span-3 m-1" : "mt-1 w-full"} disabled={query.isFetchingNextPage} onMouseDown={(event) => event.preventDefault()} onClick={() => void query.fetchNextPage()}>{query.isFetchingNextPage && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}Cargar 50 más</Button>}
    </>;
  }

  return <div ref={pickerRef} onBlur={(event) => { if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setOpen(false); }} className="relative [&_strong]:font-normal">
    <Label htmlFor={inputId}>{label}</Label>
    <div className="mt-2 flex gap-2">
      <div className="relative min-w-0 flex-1"><Barcode className="pointer-events-none absolute left-3 top-3 h-4 w-4 text-primary" /><Input id={inputId} data-testid="product-picker-search" className="pl-9" disabled={disabled} value={search} onFocus={() => setOpen(true)} onClick={() => setOpen(true)} onChange={(event) => { setSearch(event.target.value); setOpen(true); }} onKeyDown={keyDown} autoComplete="off" aria-autocomplete="list" aria-expanded={open} aria-controls={`${inputId}-results`} placeholder="Código interno, código de barras, referencia o nombre" /></div>
      <Button type="button" disabled={disabled || query.isFetching || products.length === 0} onMouseDown={(event) => event.preventDefault()} onClick={() => void chooseActive()}>{query.isFetching && !query.isFetchingNextPage ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Plus className="mr-2 h-4 w-4" />} Agregar</Button>
    </div>
    {resultsMode === "inline" ? <div id={`${inputId}-results`} ref={listRef} role="listbox" onScroll={scroll} className="relative mt-2 grid min-h-40 max-h-72 w-full grid-cols-[minmax(0,1fr)_minmax(120px,0.55fr)_100px] content-start overflow-auto rounded-xl border bg-background">
      <div className="sticky top-0 z-10 bg-muted/80 px-3 py-2 text-xs font-medium uppercase tracking-wide text-muted-foreground">Producto</div>
      <div className="sticky top-0 z-10 bg-muted/80 px-3 py-2 text-xs font-medium uppercase tracking-wide text-muted-foreground">Código / referencia</div>
      <div className="sticky top-0 z-10 bg-muted/80 px-3 py-2 text-right text-xs font-medium uppercase tracking-wide text-muted-foreground">Saldo</div>
      {resultRows(true)}
    </div> : open && <div id={`${inputId}-results`} ref={listRef} role="listbox" onScroll={scroll} className="absolute z-30 mt-1 max-h-72 w-full overflow-auto rounded-xl border bg-popover p-1 shadow-xl">{resultRows(false)}</div>}
    <Dialog open={Boolean(inventoryBlocked)} onOpenChange={(value) => !value && setInventoryBlocked(null)}>
      <DialogContent className="max-w-md overflow-hidden p-0">
        <div className="bg-gradient-to-br from-slate-950 via-slate-900 to-teal-950 px-6 py-5 text-white">
          <span className="mb-3 grid h-11 w-11 place-items-center rounded-2xl bg-amber-400/15 text-amber-300"><PackageX className="h-6 w-6" /></span>
          <DialogHeader><DialogTitle className="text-white">Primero deja el inventario en cero</DialogTitle><DialogDescription className="text-slate-300">Este producto todavía tiene existencias y no puede compartir inventario con otro.</DialogDescription></DialogHeader>
        </div>
        <div className="space-y-4 p-6">
          <div className="rounded-2xl border border-amber-200 bg-amber-50 p-4 text-amber-950"><p className="font-semibold">{inventoryBlocked?.productName}</p><p className="mt-1 text-sm">Saldo actual: <strong>{inventoryBlocked?.quantityOnHand.toLocaleString("es-CO")}</strong></p></div>
          <p className="flex gap-2 text-sm text-muted-foreground"><AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-amber-600" />Traslada, ajusta o vende las existencias actuales. Cuando el saldo sea cero podrás vincularlo y Auraly dejará de manejarle inventario propio.</p>
        </div>
        <DialogFooter className="border-t bg-muted/20 px-6 py-4"><Button type="button" onClick={() => setInventoryBlocked(null)}>Entendido</Button></DialogFooter>
      </DialogContent>
    </Dialog>
  </div>;
}
