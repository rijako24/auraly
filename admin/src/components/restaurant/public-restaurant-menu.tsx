"use client";

import { useEffect, useState } from "react";
import { ArrowUp, ChefHat, ChevronDown, Search, Sparkles,
  UtensilsCrossed } from "lucide-react";
import { restaurantDigitalMenuApi, type RestaurantDigitalMenuProductPage, type RestaurantDigitalMenuView } from
  "@/services/api/restaurant-digital-menu";

const formatPrice = (value: number, currency: string) =>
  new Intl.NumberFormat("es-CO", { style: "currency", currency,
    maximumFractionDigits: 0 }).format(value);

function safeImageUrl(value: string | null): string | null {
  if (!value) return null;
  if (value.startsWith("/")) return value;
  try {
    const parsed = new URL(value);
    return parsed.protocol === "https:" || parsed.protocol === "http:"
      ? value : null;
  } catch { return null; }
}

export function PublicRestaurantMenu({ code, preview = false, previewTitle,
  previewIntroduction, previewPhone = false }: { code?: string;
  preview?: boolean; previewTitle?: string; previewIntroduction?: string;
  previewPhone?: boolean }) {
  const [menu, setMenu] = useState<RestaurantDigitalMenuView | null>(null);
  const [activeCategoryId, setActiveCategoryId] = useState<string | null>(null);
  const [products, setProducts] = useState<RestaurantDigitalMenuProductPage["items"]>([]);
  const [page, setPage] = useState(1);
  const [hasMore, setHasMore] = useState(false);
  const [loadingMenu, setLoadingMenu] = useState(true);
  const [loadingProducts, setLoadingProducts] = useState(false);
  const [error, setError] = useState("");
  const [search, setSearch] = useState("");

  useEffect(() => {
    const controller = new AbortController();
    setLoadingMenu(true);
    const request = preview ? restaurantDigitalMenuApi.preview() :
      fetch(`/api/public/v1/restaurant-menus/${encodeURIComponent(code ?? "")}`,
        { signal: controller.signal, cache: "no-store" })
        .then(async (response) => {
          if (!response.ok) throw new Error("Este menú no está disponible.");
          return response.json() as Promise<RestaurantDigitalMenuView>;
        });
    void request.then((value) => {
        if (controller.signal.aborted) return;
        setMenu(value);
        setActiveCategoryId(value.categories[0]?.categoryId ?? null);
        setError("");
      }).catch((cause) => {
        if (!controller.signal.aborted)
          setError(preview && (cause as { statusCode?: number })?.statusCode === 404
            ? "El menú no está disponible para la sede seleccionada. Elige una sede Restaurante y Bar."
            : cause instanceof Error ? cause.message : "No pudimos abrir el menú.");
      }).finally(() => {
        if (!controller.signal.aborted) setLoadingMenu(false);
      });
    return () => controller.abort();
  }, [code, preview]);

  useEffect(() => {
    if (!activeCategoryId) return;
    const controller = new AbortController();
    setLoadingProducts(true);
    const request = preview ? restaurantDigitalMenuApi.previewProducts(activeCategoryId, page) :
      fetch(`/api/public/v1/restaurant-menus/${encodeURIComponent(code ?? "")}/products?categoryId=${encodeURIComponent(activeCategoryId)}&page=${page}`,
        { signal: controller.signal, cache: "no-store" })
        .then(async (response) => {
          if (!response.ok) throw new Error("No pudimos cargar los productos.");
          return response.json() as Promise<RestaurantDigitalMenuProductPage>;
        });
    void request.then((value) => {
        if (controller.signal.aborted) return;
        setProducts((current) => page === 1 ? value.items :
          [...current, ...value.items]);
        setHasMore(value.hasMore);
        setError("");
      }).catch((cause) => {
        if (!controller.signal.aborted)
          setError(cause instanceof Error ? cause.message :
            "No pudimos cargar los productos.");
      }).finally(() => {
        if (!controller.signal.aborted) setLoadingProducts(false);
      });
    return () => controller.abort();
  }, [code, preview, activeCategoryId, page]);

  const visible = search.trim()
    ? products.filter((product) => `${product.name} ${product.description ?? ""}`
        .toLocaleLowerCase("es")
        .includes(search.trim().toLocaleLowerCase("es")))
    : products;
  const featured = products.find((product) => safeImageUrl(product.imageUrl)) ?? products[0];
  const featuredImage = safeImageUrl(featured?.imageUrl ?? null);
  return <main className="min-h-screen bg-[#f7f5ef] text-[#182e2b] dark:bg-[#101918] dark:text-slate-50">
    <header className={`relative overflow-hidden bg-[#173d36] text-white ${previewPhone ? "px-5 pb-9 pt-9" : "px-5 pb-12 pt-12 sm:px-8 sm:pb-16 sm:pt-16"}`}>
      <div className="pointer-events-none absolute -left-32 -top-32 h-96 w-96 rounded-full border border-white/10"/><div className="pointer-events-none absolute -left-16 -top-16 h-72 w-72 rounded-full border border-white/10"/>
      <div className={`relative mx-auto grid max-w-6xl items-center gap-8 ${previewPhone ? "" : "lg:grid-cols-[minmax(0,1fr)_minmax(300px,.8fr)]"}`}>
        <div><div className="flex items-center gap-2 text-[10px] font-bold uppercase tracking-[.26em] text-[#dfd5ad]"><UtensilsCrossed className="h-4 w-4"/>La carta · hecha para disfrutar</div>
          {loadingMenu ? <div className="mt-10 h-12 w-64 animate-pulse rounded-xl bg-white/20"/>
            : <><h1 className={`mt-7 max-w-3xl font-serif font-semibold leading-[1.05] tracking-tight ${previewPhone ? "text-4xl" : "text-4xl sm:text-6xl"}`}>{(preview && previewTitle?.trim()) || menu?.title || "Menú no disponible"}</h1>
              {(preview ? previewIntroduction : menu?.introduction) && <p className="mt-5 max-w-2xl text-sm leading-relaxed text-emerald-50/80 sm:text-lg">{preview ? previewIntroduction : menu?.introduction}</p>}</>}
          <div className="mt-7 flex items-center gap-2 border-t border-white/20 pt-5 text-xs font-medium text-[#e4dfc8]"><Sparkles className="h-4 w-4"/>Explora cada categoría y elige tu favorito</div>
        </div>
        <div className={`relative overflow-hidden rounded-[2rem] border border-white/15 bg-[#244f44] shadow-2xl ${previewPhone ? "h-48" : "h-60 sm:h-80"}`}>
          {featuredImage ?
            // Product image URLs may come from tenant media storage.
            // eslint-disable-next-line @next/next/no-img-element
            <img src={featuredImage} alt="" className="h-full w-full object-cover"/> : <div className="absolute inset-0 grid place-items-center bg-[radial-gradient(circle_at_65%_40%,#427262,#173d36_68%)]"><div className="grid h-40 w-40 place-items-center rounded-full border-[12px] border-[#faf5e4]/70 bg-[#ecdfbd]/15 shadow-[0_18px_50px_#061d1980] sm:h-56 sm:w-56"><UtensilsCrossed className="h-12 w-12 text-[#ece1c2]"/></div></div>}
          {featured && <div className="absolute inset-x-0 bottom-0 bg-gradient-to-t from-[#09241f]/95 via-[#09241f]/55 to-transparent p-5 pt-12"><span className="text-[10px] font-bold uppercase tracking-[.2em] text-[#e2d6ad]">En la carta</span><p className="mt-1 font-serif text-xl font-semibold">{featured.name}</p></div>}
        </div>
      </div>
    </header>
    <div className={`relative mx-auto max-w-6xl pb-20 ${previewPhone ? "px-4" : "px-4 sm:px-8"}`}>
      {error && <div role="alert" className="mt-6 rounded-2xl border border-rose-300 bg-rose-50 p-4 text-rose-900 dark:bg-rose-950 dark:text-rose-100">{error}</div>}
      {menu && !menu.categories.length && <div className="mt-8 rounded-3xl border border-[#dfd9c9] bg-white p-8 text-center shadow-sm dark:border-slate-700 dark:bg-slate-900"><ChefHat className="mx-auto h-10 w-10 text-emerald-700 dark:text-emerald-300"/><h2 className="mt-4 font-serif text-2xl font-semibold">{preview ? "Elige los productos de tu carta" : "Pronto encontrarás nuestra carta aquí"}</h2><p className="mt-2 text-sm text-muted-foreground">{preview ? "Guarda una selección de productos para verla en esta vista previa." : "Estamos preparando el menú para ti."}</p></div>}
      {menu && menu.categories.length > 0 && <><div className={`sticky top-0 z-10 -mx-4 border-b border-[#dfd9c9] bg-[#f7f5ef]/95 px-4 py-4 backdrop-blur dark:border-slate-800 dark:bg-[#101918]/95 ${previewPhone ? "" : "sm:-mx-8 sm:px-8"}`}>
          <div className="flex gap-2 overflow-x-auto pb-1">{menu.categories.map((category) =>
            <button key={category.categoryId} type="button" onClick={() => {
              setActiveCategoryId(category.categoryId); setPage(1);
              setProducts([]); setSearch("");
            }} className={`min-h-12 shrink-0 rounded-full border px-5 text-sm font-semibold transition active:scale-95 ${activeCategoryId === category.categoryId ? "border-emerald-800 bg-emerald-800 text-white shadow-md" : "border-slate-200 bg-white hover:border-emerald-500 dark:border-slate-700 dark:bg-slate-900"}`}>{category.name} <span className="ml-1 opacity-70">{category.productCount}</span></button>)}</div>
        </div>
        {activeCategoryId && <><div className={`mt-9 flex justify-between gap-4 ${previewPhone ? "flex-col" : "flex-col sm:flex-row sm:items-end"}`}><div><p className="text-xs font-bold uppercase tracking-[.18em] text-emerald-700 dark:text-emerald-400">Nuestra selección</p><h2 className="mt-1 font-serif text-4xl font-semibold">{menu.categories.find((category) => category.categoryId === activeCategoryId)?.name}</h2><p className="mt-1 text-sm text-muted-foreground">Explora los sabores de esta sección.</p></div>
          <label className={`relative block w-full ${previewPhone ? "" : "sm:w-72"}`}><Search className="absolute left-4 top-3.5 h-5 w-5 text-slate-500"/><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Buscar en esta categoría" className="h-12 w-full rounded-2xl border bg-white pl-11 pr-4 outline-none focus:border-emerald-600 dark:bg-slate-900"/></label></div>
          {loadingProducts && page === 1 && <div className={`mt-6 grid gap-4 ${previewPhone ? "" : "sm:grid-cols-2 lg:grid-cols-3"}`}>{Array.from({ length: 6 }, (_, index) => <div key={index} className="h-80 animate-pulse rounded-[1.75rem] bg-slate-200 dark:bg-slate-800"/>)}</div>}
          <div className={`mt-6 grid gap-4 ${previewPhone ? "" : "sm:grid-cols-2 lg:grid-cols-3"}`}>{visible.map((product) => {
            const image = safeImageUrl(product.imageUrl);
            return <article key={product.productId} className="group overflow-hidden rounded-[1.75rem] border border-slate-200 bg-white shadow-sm transition duration-300 hover:-translate-y-1 hover:shadow-xl dark:border-slate-800 dark:bg-slate-900">
              <div className="relative grid aspect-[16/10] place-items-center overflow-hidden bg-gradient-to-br from-emerald-100 to-stone-100 dark:from-emerald-950 dark:to-slate-900">{image
                // Product image URLs may come from tenant media storage.
                // eslint-disable-next-line @next/next/no-img-element
                ? <img src={image} alt={product.name} loading="lazy" className="h-full w-full object-cover transition duration-500 group-hover:scale-105"/>
                : <ChefHat className="h-16 w-16 text-emerald-700/65 dark:text-emerald-400/65"/>}</div>
              <div className="p-5"><div className="flex items-start justify-between gap-3"><h3 className="text-lg font-bold leading-tight">{product.name}</h3><span className="shrink-0 rounded-full bg-emerald-50 px-3 py-1 text-sm font-bold text-emerald-800 dark:bg-emerald-950 dark:text-emerald-300">{formatPrice(product.price, product.currencyCode)}</span></div>
                {product.description && <p className="mt-3 line-clamp-3 text-sm leading-relaxed text-slate-600 dark:text-slate-300">{product.description}</p>}</div></article>;
          })}</div>
          {!loadingProducts && !visible.length && <div className="mt-8 rounded-[1.75rem] border border-dashed p-12 text-center text-slate-500">No encontramos productos en esta categoría.</div>}
          {hasMore && !search && <button className="mx-auto mt-8 flex min-h-12 items-center gap-2 rounded-full border bg-white px-6 font-semibold transition hover:border-emerald-600 dark:bg-slate-900" disabled={loadingProducts} onClick={() => setPage((current) => current + 1)}>{loadingProducts ? "Cargando…" : "Ver más productos"}<ChevronDown className="h-4 w-4"/></button>}
        </>}
      </>}
      {!preview && <button type="button" onClick={() => window.scrollTo({ top: 0, behavior: "smooth" })} aria-label="Volver arriba" className="fixed bottom-5 right-5 grid h-12 w-12 place-items-center rounded-full bg-emerald-800 text-white shadow-lg transition hover:-translate-y-1"><ArrowUp className="h-5 w-5"/></button>}
    </div>
  </main>;
}
