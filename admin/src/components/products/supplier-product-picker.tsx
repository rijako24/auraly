"use client";

import { forwardRef, useEffect, useMemo, useRef, useState, type KeyboardEvent, type UIEvent } from "react";
import { Barcode, Plus, Search } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { useGoodsReceiptProducts } from "@/hooks/use-goods-receipts";
import { formatCurrency } from "@/lib/utils";
import type { GoodsReceiptProduct } from "@/services/api/goods-receipts";

interface SupplierProductPickerProps {
  supplierId?: string;
  disabled?: boolean;
  includeUnassociated: boolean;
  onIncludeUnassociatedChange: (value: boolean) => void;
  onSelect: (product: GoodsReceiptProduct) => void;
  inputId?: string;
}

export const SupplierProductPicker = forwardRef<HTMLInputElement, SupplierProductPickerProps>(function SupplierProductPicker({
  supplierId,
  disabled = false,
  includeUnassociated,
  onIncludeUnassociatedChange,
  onSelect,
  inputId = "supplier-product-picker-search",
}, inputRef) {
  const [search, setSearch] = useState("");
  const [open, setOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState(0);
  const pickerRef = useRef<HTMLDivElement>(null);
  const productsQuery = useGoodsReceiptProducts(supplierId, search, includeUnassociated);
  const products = useMemo(
    () => productsQuery.data?.pages.flatMap((page) => page.items) ?? [],
    [productsQuery.data],
  );

  useEffect(() => setActiveIndex(0), [search, includeUnassociated, supplierId]);
  useEffect(() => { setSearch(""); setOpen(false); }, [supplierId]);
  useEffect(() => {
    if (!open) return;
    const closeOnOutsidePointer = (event: PointerEvent) => {
      if (!pickerRef.current?.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener("pointerdown", closeOnOutsidePointer, true);
    return () => document.removeEventListener("pointerdown", closeOnOutsidePointer, true);
  }, [open]);

  function choose(product: GoodsReceiptProduct) {
    setSearch("");
    setOpen(false);
    onSelect(product);
  }

  function onKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === "Escape") {
      event.preventDefault();
      event.stopPropagation();
      setOpen(false);
      return;
    }
    if (event.key === "ArrowDown" && products.length > 0) {
      event.preventDefault();
      setOpen(true);
      setActiveIndex((current) => Math.min(current + 1, products.length - 1));
      return;
    }
    if (event.key === "ArrowUp" && products.length > 0) {
      event.preventDefault();
      setOpen(true);
      setActiveIndex((current) => Math.max(current - 1, 0));
      return;
    }
    if (event.key !== "Enter") return;
    event.preventDefault();
    const product = products[activeIndex] ?? products[0];
    if (product) choose(product);
  }

  function onScroll(event: UIEvent<HTMLDivElement>) {
    const target = event.currentTarget;
    if (target.scrollHeight - target.scrollTop - target.clientHeight < 80 && productsQuery.hasNextPage && !productsQuery.isFetchingNextPage) {
      void productsQuery.fetchNextPage();
    }
  }

  return <div ref={pickerRef} className="relative" onBlur={(event) => {
    if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setOpen(false);
  }}>
    <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_auto_auto]">
      <div className="relative">
        <Barcode className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-primary" />
        <Input id={inputId} ref={inputRef} data-testid="supplier-product-picker-search" className="pl-9"
          value={search} disabled={disabled || !supplierId}
          onFocus={() => setOpen(true)} onClick={() => setOpen(true)}
          onChange={(event) => { setSearch(event.target.value); setOpen(true); }}
          onKeyDown={onKeyDown} autoComplete="off" aria-autocomplete="list" aria-expanded={open}
          placeholder={supplierId
            ? "Escanea o busca por código, referencia, nombre o código del proveedor"
            : "Selecciona primero el proveedor"} />
      </div>
      <Button type="button" variant={includeUnassociated ? "secondary" : "outline"}
        disabled={disabled || !supplierId}
        onClick={() => { onIncludeUnassociatedChange(!includeUnassociated); setOpen(true); }}>
        <Search className="mr-2 h-4 w-4" />
        {includeUnassociated ? "Ver productos del proveedor" : "Buscar en todo el catálogo"}
      </Button>
      <Button type="button" variant="outline" disabled={disabled || !products[activeIndex]}
        onClick={() => products[activeIndex] && choose(products[activeIndex])}>
        <Plus className="mr-2 h-4 w-4" /> Agregar
      </Button>
    </div>
    {open && products.length > 0 && <div role="listbox" onScroll={onScroll}
      className="absolute z-40 mt-1 max-h-64 w-full overflow-y-auto rounded-xl border bg-background p-2 shadow-xl [&_strong]:font-normal">
      {products.map((product, index) => <button key={product.productId} type="button" role="option"
        aria-selected={index === activeIndex}
        className={`flex w-full items-center justify-between rounded-lg px-3 py-2 text-left ${index === activeIndex ? "bg-emerald-50" : "hover:bg-muted"}`}
        onMouseEnter={() => setActiveIndex(index)} onMouseDown={(event) => event.preventDefault()}
        onClick={() => choose(product)}>
        <span className="min-w-0">
          <span className="flex items-center gap-2"><strong className="truncate">{product.name}</strong>
            {!product.isAssociated && <Badge variant="outline">Nuevo para este proveedor</Badge>}
          </span>
          <span className="block truncate text-xs text-muted-foreground">{product.productCode}{product.supplierProductCode ? ` · Prov. ${product.supplierProductCode}` : ""}</span>
        </span>
        <span className="shrink-0 text-right text-xs"><span className="block font-medium">Último {product.latestUnitCost == null ? "—" : formatCurrency(product.latestUnitCost)}</span>
          <span className="block text-muted-foreground">Promedio {product.averageUnitCost == null ? "—" : formatCurrency(product.averageUnitCost)}</span></span>
      </button>)}
      {productsQuery.hasNextPage && <Button type="button" variant="ghost" className="mt-2 w-full"
        disabled={productsQuery.isFetchingNextPage} onMouseDown={(event) => event.preventDefault()}
        onClick={() => void productsQuery.fetchNextPage()}>{productsQuery.isFetchingNextPage ? "Cargando…" : "Cargar 50 más"}</Button>}
    </div>}
    {open && supplierId && search.trim().length === 0 &&
      <div role="listbox" className="absolute z-40 mt-1 w-full rounded-xl border bg-background px-4 py-3 text-sm text-muted-foreground shadow-xl">Escribe al menos una letra, código o referencia para buscar.</div>}
    {open && supplierId && search.trim().length > 0 && !productsQuery.isLoading && products.length === 0 &&
      <div role="listbox" className="absolute z-40 mt-1 w-full rounded-xl border bg-background px-4 py-3 text-sm text-muted-foreground shadow-xl">Sin resultados para esta búsqueda.</div>}
  </div>;
});
