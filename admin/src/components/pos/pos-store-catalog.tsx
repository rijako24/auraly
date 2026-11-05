"use client";

import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ArrowLeft, LayoutGrid, Search, ShoppingBag } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { PosCategoryPill, PosProductTile } from "./pos-catalog-presentation";
import type { PosClient, PosCatalogProduct } from "@/services/pos/pos-edge-client";

export function PosStoreCatalog({ client, scopeKey, customerId, disabled, ready, onSelect }: {
  client: PosClient; scopeKey: string; customerId: string | null; disabled: boolean; ready: boolean;
  onSelect: (product: PosCatalogProduct) => Promise<boolean>;
}) {
  const [search, setSearch] = useState("");
  const [term, setTerm] = useState("");
  const [categoryId, setCategoryId] = useState<string | null>(null);
  const [family, setFamily] = useState<PosCatalogProduct | null>(null);
  const [page, setPage] = useState(0);
  const [addingId, setAddingId] = useState<string | null>(null);
  useEffect(() => { const timer = setTimeout(() => { setTerm(search); setPage(0); }, 250); return () => clearTimeout(timer); }, [search]);
  const categories = useQuery({ queryKey: ["pos-store-categories", scopeKey], queryFn: () => client.catalogCategories(), enabled: ready, staleTime: 30_000, refetchOnWindowFocus: false });
  const products = useQuery({ queryKey: ["pos-store-products", scopeKey, customerId, term, categoryId, family?.productId, page],
    queryFn: ({ signal }) => client.browseProducts(term, page * 50, customerId, categoryId, family?.productId ?? null, signal), enabled: ready, staleTime: 30_000, refetchOnWindowFocus: false });
  async function add(product: PosCatalogProduct) {
    if (disabled || addingId) return;
    setAddingId(product.productId);
    try { await onSelect(product); } finally { setAddingId(null); }
  }
  return <section aria-label="Catálogo de tiendas" className="lg:col-start-2 lg:row-start-1 lg:row-span-2 min-w-0 rounded-[2rem] border bg-card p-4 shadow-sm sm:p-5 lg:min-h-0 lg:overflow-y-auto">
    <div className="flex flex-wrap items-center justify-between gap-3"><div><p className="text-xs font-bold uppercase tracking-widest text-primary">Tiendas</p><h2 className="mt-1 text-2xl font-bold">Elige productos</h2></div><div className="relative w-full sm:w-64"><Search className="absolute left-4 top-3.5 h-5 w-5 text-muted-foreground"/><Input id="store-product-search" className="h-12 rounded-2xl pl-11" value={search} onChange={event => { setSearch(event.target.value); }} placeholder="Buscar producto" aria-label="Buscar producto en tiendas"/></div></div>
    {family ? <div className="mt-4 flex items-center gap-3"><Button variant="outline" className="min-h-11 rounded-xl" onClick={() => { setFamily(null); setSearch(""); setTerm(""); setPage(0); }}><ArrowLeft className="mr-2 h-4 w-4"/>Volver al catálogo</Button><p className="min-w-0 truncate text-sm font-semibold">Familia · {family.name}</p></div> : <nav className="mt-5 flex gap-2 overflow-x-auto pb-2" aria-label="Categorías de productos"><PosCategoryPill active={!categoryId} label="Todos" icon={LayoutGrid} onClick={() => { setCategoryId(null); setPage(0); }}/>{(categories.data ?? []).map(category => <PosCategoryPill key={category.categoryId} active={categoryId === category.categoryId} label={category.name} icon={ShoppingBag} onClick={() => { setCategoryId(category.categoryId); setPage(0); }}/>)}</nav>}
    {categories.isError && <p role="alert" className="mt-3 text-sm text-destructive">No pudimos cargar las categorías. <Button variant="link" onClick={() => void categories.refetch()}>Reintentar</Button></p>}
    {products.isLoading && <div role="status" className="mt-5 grid grid-cols-2 gap-3 xl:grid-cols-3">{Array.from({ length: 6 }, (_, index) => <div key={index} className="h-52 animate-pulse rounded-3xl bg-muted"/>)}<span className="sr-only">Cargando productos</span></div>}
    {products.isError && <div role="alert" className="mt-5 rounded-2xl border p-5">No pudimos cargar los productos. <Button variant="link" onClick={() => void products.refetch()}>Reintentar</Button></div>}
    {!products.isLoading && !products.isError && products.data?.items.length === 0 && <p className="py-16 text-center text-muted-foreground">No hay productos para esta búsqueda.</p>}
    <div className="mt-5 grid grid-cols-1 gap-3 min-[430px]:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4">{products.data?.items.map(product => <div key={product.productId} className="flex min-w-0 flex-col gap-2"><PosProductTile fallbackIcon={ShoppingBag} product={product} disabled={disabled || !!addingId} loading={addingId === product.productId} onClick={() => void add(product)}/>
      {(product.presentation?.colorName || product.presentation?.sizeName) && <p className="px-2 text-xs text-muted-foreground">{[product.presentation.colorName, product.presentation.sizeName].filter(Boolean).join(" · ")}</p>}
      {!family && product.presentation?.familyProductId && <Button variant="outline" className="min-h-11 rounded-xl" onClick={() => { setFamily(product); setCategoryId(null); setSearch(""); setTerm(""); setPage(0); }}>Ver familia</Button>}
    </div>)}</div>
    {(page > 0 || products.data?.hasMore) && <div className="mt-5 flex justify-between gap-3"><Button variant="outline" disabled={page === 0 || products.isFetching} onClick={() => setPage(value => value - 1)}>Anterior</Button><span className="self-center text-sm">Página {page + 1}</span><Button variant="outline" disabled={!products.data?.hasMore || products.isFetching} onClick={() => setPage(value => value + 1)}>Siguiente</Button></div>}
  </section>;
}
