"use client";

import { useEffect, useMemo, useRef, useState, type KeyboardEvent, type UIEvent } from "react";
import { useInfiniteQuery } from "@tanstack/react-query";
import { Barcode, Check, Loader2, Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
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
  inputId = "product-picker-search",
  showAddButton = true,
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
  inputId?: string;
  showAddButton?: boolean;
}) {
  const [search, setSearch] = useState("");
  const [open, setOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState<number | null>(null);
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

  useEffect(() => { setActiveIndex(null); listRef.current?.scrollTo({ top: 0 }); }, [search, warehouseId, conversionOnly, conversionFamilyRootProductId]);
  useEffect(() => {
    if (!open) return;
    const closeOnOutsidePointer = (event: PointerEvent) => {
      if (!pickerRef.current?.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener("pointerdown", closeOnOutsidePointer, true);
    return () => document.removeEventListener("pointerdown", closeOnOutsidePointer, true);
  }, [open]);
  useEffect(() => { if (activeIndex !== null && activeIndex >= products.length) setActiveIndex(null); }, [activeIndex, products.length]);

  function choose(product: InventoryProductItem) {
    onSelect(product); setSearch(""); setOpen(false);
  }
  function chooseActive() {
    if (activeIndex !== null) choose(products[activeIndex]);
  }
  async function moveDown() {
    if (activeIndex === null) { if (products.length) setActiveIndex(0); return; }
    if (activeIndex < products.length - 1) { setActiveIndex((current) => current === null ? 0 : current + 1); return; }
    if (!query.hasNextPage || query.isFetchingNextPage) return;
    const previousLength = products.length;
    const next = await query.fetchNextPage();
    const nextLength = next.data?.pages.flatMap((page) => page.items).filter((item) => !excludedProductIds.has(item.productId)).length ?? previousLength;
    if (nextLength > previousLength) setActiveIndex(previousLength);
  }
  function keyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === "ArrowDown" && search.trim()) { event.preventDefault(); setOpen(true); void moveDown(); }
    else if (event.key === "ArrowUp" && search.trim()) { event.preventDefault(); setActiveIndex((current) => current === null ? Math.max(0, products.length - 1) : Math.max(0, current - 1)); }
    else if (event.key === "Enter" && search.trim()) { event.preventDefault(); if (activeIndex === null && products.length) choose(products[0]); else chooseActive(); }
    else if (event.key === "Escape") { event.preventDefault(); setOpen(false); }
  }
  function scroll(event: UIEvent<HTMLDivElement>) {
    const target = event.currentTarget;
    if (target.scrollHeight - target.scrollTop - target.clientHeight < 80 && query.hasNextPage && !query.isFetchingNextPage) void query.fetchNextPage();
  }

  function resultRows() {
    const messageClass = "p-4 text-sm text-muted-foreground";
    if (query.isLoading) return <p className={`${messageClass} flex items-center gap-2`}><Loader2 className="h-4 w-4 animate-spin" />Buscando productos…</p>;
    if (query.isError) return <div className="p-4 text-sm text-red-700"><p>No fue posible cargar los productos.</p><Button className="mt-3" size="sm" variant="outline" onClick={() => void query.refetch()}>Reintentar</Button></div>;
    if (products.length === 0) return <p className={messageClass}>No hay productos activos que coincidan con la búsqueda.</p>;
    return <>
      <div className="px-3 py-2 text-xs text-muted-foreground">{products.length.toLocaleString("es-CO")} de {totalCount.toLocaleString("es-CO")} productos</div>
      {products.map((product, index) => <button key={product.productId} type="button" role="option" aria-selected={activeIndex === index} onMouseDown={(event) => event.preventDefault()} onMouseEnter={() => setActiveIndex(index)} onClick={() => choose(product)} className={`flex w-full items-center justify-between gap-4 border-t px-3 py-2.5 text-left text-sm ${activeIndex === index ? "bg-emerald-50 text-emerald-950" : "hover:bg-muted"}`}>
        <span className="min-w-0"><strong className="block truncate">{product.productName}</strong>{conversionOnly && product.conversionFactor && <small className="block truncate text-muted-foreground">Factor {product.conversionFactor}</small>}</span>
        <small className="min-w-0 truncate text-muted-foreground">{product.productCode || "Sin código"}{product.reference ? ` · ${product.reference}` : ""}</small>
        <span className="flex items-center justify-end gap-3 text-xs text-muted-foreground">{product.quantityOnHand}{selectedProductIds.has(product.productId) && <Check className="h-4 w-4 text-emerald-700" aria-label="Agregado" />}</span>
      </button>)}
      {query.hasNextPage && <Button type="button" variant="ghost" className="mt-1 w-full" disabled={query.isFetchingNextPage} onMouseDown={(event) => event.preventDefault()} onClick={() => void query.fetchNextPage()}>{query.isFetchingNextPage && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}Cargar 50 más</Button>}
    </>;
  }

  return <div ref={pickerRef} onBlur={(event) => { if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setOpen(false); }} className="relative [&_strong]:font-normal">
    <Label htmlFor={inputId}>{label}</Label>
    <div className="mt-2 flex gap-2">
      <div className="relative min-w-0 flex-1"><Barcode className="pointer-events-none absolute left-3 top-3 h-4 w-4 text-primary" /><Input id={inputId} data-testid="product-picker-search" className="pl-9" disabled={disabled} value={search} onChange={(event) => { const value = event.target.value; setSearch(value); setOpen(Boolean(value.trim())); }} onKeyDown={keyDown} autoComplete="off" aria-autocomplete="list" aria-expanded={open} aria-controls={`${inputId}-results`} placeholder="Código interno, código de barras, referencia o nombre" /></div>
      {showAddButton && <Button type="button" disabled={disabled || query.isFetching || activeIndex === null} onMouseDown={(event) => event.preventDefault()} onClick={chooseActive}>{query.isFetching && !query.isFetchingNextPage ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Plus className="mr-2 h-4 w-4" />} Agregar</Button>}
    </div>
    {open && <div id={`${inputId}-results`} ref={listRef} role="listbox" onScroll={scroll} className="absolute z-30 mt-1 max-h-72 w-full overflow-auto rounded-xl border bg-popover p-1 shadow-xl">{resultRows()}</div>}
  </div>;
}
