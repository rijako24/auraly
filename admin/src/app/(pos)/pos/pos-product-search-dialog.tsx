"use client";

import { Boxes, Loader2, PackageSearch, Search, ShieldAlert, Warehouse, WifiOff, X, Scale } from "lucide-react";
import { useCallback, useEffect, useRef, useState, type KeyboardEvent as ReactKeyboardEvent } from "react";

import type {
  PosCatalogProduct,
  PosCatalogSearchPage,
  PosProductWarehouseAvailability,
} from "@/services/pos/pos-edge-client";

const money = new Intl.NumberFormat("es-CO", {
  style: "currency",
  currency: "COP",
  maximumFractionDigits: 0,
});

export function PosProductSearchDialog({
  busy,
  focusRequest,
  onSearch,
  connected,
  canReadAvailability,
  onLoadAvailability,
  onSelect,
  onCancel,
}: {
  busy: boolean;
  focusRequest: number;
  onSearch: (term: string, skip: number) => Promise<PosCatalogSearchPage>;
  connected: boolean;
  canReadAvailability: boolean;
  onLoadAvailability: (productId: string) => Promise<PosProductWarehouseAvailability[]>;
  onSelect: (product: PosCatalogProduct) => Promise<boolean>;
  onCancel: () => void;
}) {
  const [term, setTerm] = useState("");
  const [results, setResults] = useState<PosCatalogProduct[]>([]);
  const [selected, setSelected] = useState(0);
  const [hasMore, setHasMore] = useState(false);
  const [nextOffset, setNextOffset] = useState<number | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const input = useRef<HTMLInputElement>(null);
  const requestVersion = useRef(0);
  const availabilityVersion = useRef(0);
  const resultElements = useRef(new Map<number, HTMLButtonElement>());
  const [availability, setAvailability] = useState<PosProductWarehouseAvailability[]>([]);
  const [availabilityLoading, setAvailabilityLoading] = useState(false);
  const [availabilityError, setAvailabilityError] = useState<string | null>(null);

  useEffect(() => {
    const frame = window.requestAnimationFrame(() =>
      input.current?.focus({ preventScroll: true }),
    );
    return () => window.cancelAnimationFrame(frame);
  }, [focusRequest]);

  useEffect(() => {
    const version = ++requestVersion.current;
    const normalized = term.trim();
    const timer = window.setTimeout(() => {
      setLoading(true);
      setError(null);
      void onSearch(normalized, 0)
        .then((page) => {
          if (requestVersion.current !== version) return;
          setResults(page.items);
          setSelected(0);
          setHasMore(page.hasMore);
          setNextOffset(page.nextOffset);
        })
        .catch((caught) => {
          if (requestVersion.current !== version) return;
          setResults([]);
          setHasMore(false);
          setNextOffset(null);
          setError(caught instanceof Error
            ? caught.message
            : "No fue posible consultar el catálogo de Auraly.");
        })
        .finally(() => {
          if (requestVersion.current === version) setLoading(false);
        });
    }, normalized ? 180 : 0);

    return () => window.clearTimeout(timer);
  }, [onSearch, term]);

  useEffect(() => {
    const product = results[selected];
    const version = ++availabilityVersion.current;
    setAvailability([]);
    setAvailabilityLoading(false);
    if (!product) {
      setAvailabilityError(null);
      return;
    }
    if (!canReadAvailability) {
      setAvailabilityError("Tu perfil no tiene permiso para consultar existencias por bodega.");
      return;
    }
    if (!connected) {
      setAvailabilityError("Sin conexión al servidor. El producto local sigue disponible, pero no podemos consultar otras bodegas.");
      return;
    }
    setAvailabilityError(null);
    setAvailabilityLoading(true);
    void onLoadAvailability(product.productId)
      .then((items) => {
        if (availabilityVersion.current === version) setAvailability(items);
      })
      .catch(() => {
        if (availabilityVersion.current === version)
          setAvailabilityError("No fue posible consultar las existencias del servidor.");
      })
      .finally(() => {
        if (availabilityVersion.current === version) setAvailabilityLoading(false);
      });
  }, [canReadAvailability, connected, onLoadAvailability, results, selected]);

  useEffect(() => {
    const close = (event: globalThis.KeyboardEvent) => {
      if (event.key !== "Escape" || busy) return;
      event.preventDefault();
      onCancel();
    };
    window.addEventListener("keydown", close);
    return () => window.removeEventListener("keydown", close);
  }, [busy, onCancel]);

  const loadMore = useCallback(async () => {
    if (busy || loading || loadingMore || !hasMore || nextOffset === null) return;
    setLoadingMore(true);
    setError(null);
    try {
      const page = await onSearch(term.trim(), nextOffset);
      setResults((current) => {
        const known = new Set(current.map((product) => product.productId));
        return [
          ...current,
          ...page.items.filter((product) => !known.has(product.productId)),
        ];
      });
      setHasMore(page.hasMore);
      setNextOffset(page.nextOffset);
    } catch {
      setError("No fue posible cargar la página siguiente.");
    } finally {
      setLoadingMore(false);
    }
  }, [busy, hasMore, loading, loadingMore, nextOffset, onSearch, term]);

  function moveSelection(direction: -1 | 1) {
    if (!results.length) return;
    const target = selected + direction;
    if (target < 0 || target >= results.length) {
      input.current?.focus();
      return;
    }
    setSelected(target);
    window.requestAnimationFrame(() => {
      const element = resultElements.current.get(target);
      element?.focus();
      element?.scrollIntoView({ block: "nearest" });
    });
    if (direction > 0 && target >= results.length - 2)
      void loadMore();
  }

  async function choose(product: PosCatalogProduct) {
    if (busy) return;
    const added = await onSelect(product);
    if (!added) input.current?.focus();
  }

  function handleListNavigation(event: ReactKeyboardEvent) {
    if (event.key.length === 1 && !event.ctrlKey && !event.altKey && !event.metaKey) {
      event.preventDefault();
      setTerm((current) => current + event.key);
      input.current?.focus();
      return;
    }
    if (event.key === "Backspace") {
      event.preventDefault();
      setTerm((current) => current.slice(0, -1));
      input.current?.focus();
      return;
    }
    if (event.key === "ArrowDown") {
      event.preventDefault();
      moveSelection(1);
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      moveSelection(-1);
    } else if (event.key === "Enter" && results[selected]) {
      event.preventDefault();
      void choose(results[selected]);
    }
  }

  return (
    <div className="fixed inset-0 z-50 grid place-items-center bg-slate-950/60 p-4">
      <section
        role="dialog"
        aria-modal="true"
        data-pos-focus-surface="modal"
        className="flex max-h-[85vh] w-full max-w-4xl flex-col overflow-hidden rounded-2xl bg-white shadow-2xl"
        aria-labelledby="pos-product-search-title"
      >
        <header className="flex items-start justify-between gap-4 border-b border-slate-200 p-5">
          <div>
            <h2 id="pos-product-search-title" className="flex items-center gap-2 text-xl font-semibold">
              <PackageSearch className="h-5 w-5 text-teal-700" />
              Buscar producto
            </h2>
            <p className="mt-1 text-sm text-slate-500">
              Nombre, código interno, referencia, código de barras o alterno.
            </p>
          </div>
          <button
            type="button"
            onClick={onCancel}
            disabled={busy}
            className="grid h-10 w-10 place-items-center rounded-lg text-slate-500 hover:bg-slate-100 focus:outline-none focus:ring-2 focus:ring-teal-600/20"
            aria-label="Cerrar búsqueda"
          >
            <X className="h-5 w-5" />
          </button>
        </header>

        <div className="p-5 pb-3">
          <label className="relative block">
            <Search className="pointer-events-none absolute left-4 top-1/2 h-5 w-5 -translate-y-1/2 text-slate-400" />
          <input
            data-pos-product-search-input
              ref={input}
              autoFocus
              value={term}
              onChange={(event) => {
                setTerm(event.target.value);
                setSelected(0);
              }}
              onFocus={() => setSelected(0)}
              onKeyDown={(event) => {
                if (event.key === "ArrowDown") {
                  event.preventDefault();
                  setSelected(0);
                  resultElements.current.get(0)?.focus();
                  return;
                }
                if (event.key === "ArrowUp") {
                  event.preventDefault();
                  const last = results.length - 1;
                  setSelected(last);
                  resultElements.current.get(last)?.focus();
                  return;
                }
                if (event.key === "Enter" && results[selected]) {
                  event.preventDefault();
                  void choose(results[selected]);
                }
              }}
              className="h-14 w-full rounded-xl border-2 border-teal-700/25 bg-slate-50 pl-12 pr-12 text-lg font-medium outline-none focus:border-teal-600 focus:bg-white focus:ring-4 focus:ring-teal-600/10"
              placeholder="Escribe para filtrar; usa flechas para recorrer"
              aria-label="Buscar producto"
              role="combobox"
              aria-controls="pos-product-results"
              aria-expanded={true}
              aria-activedescendant={results[selected] ? `pos-product-${results[selected].productId}` : undefined}
            />
            {loading && (
              <span className="pointer-events-none absolute right-4 top-1/2 grid h-5 w-5 -translate-y-1/2 place-items-center" role="status" aria-label="Cargando productos">
                <Loader2 className="h-5 w-5 animate-spin text-teal-700" />
              </span>
            )}
          </label>
          <p className="mt-2 text-xs text-slate-500">
            Flechas recorren; Tab entra al listado; Enter agrega; Esc vuelve al lector.
          </p>
        </div>

        <div
          id="pos-product-results"
          role="listbox"
          className="min-h-64 flex-1 overflow-auto px-5 pb-5"
          onScroll={(event) => {
            const list = event.currentTarget;
            if (list.scrollHeight - list.scrollTop - list.clientHeight < 120) {
              void loadMore();
            }
          }}
        >
          {results.map((product, index) => (
            <button
              key={product.productId}
              type="button"
              id={`pos-product-${product.productId}`}
              ref={(element) => {
                if (element) resultElements.current.set(index, element);
                else resultElements.current.delete(index);
              }}
              aria-selected={selected === index}
              role="option"
              onFocus={() => {
                setSelected(index);
                if (index === results.length - 1) void loadMore();
              }}
              onClick={() => void choose(product)}
              onKeyDown={handleListNavigation}
              disabled={busy}
              className={`grid w-full grid-cols-[minmax(0,1fr)_130px] items-center gap-4 border-b border-slate-100 px-3 py-3 text-left outline-none transition sm:grid-cols-[minmax(0,1fr)_150px_130px] ${
                selected === index ? "bg-teal-50 ring-2 ring-inset ring-teal-600/25" : ""
              }`}
            >
              <span className="min-w-0">
                <span className="block truncate font-semibold text-slate-900">{product.name}</span>
                {product.isWeighable&&<span className="mt-1 inline-flex items-center gap-1 rounded-full bg-teal-100 px-2 py-0.5 text-[10px] font-bold text-teal-800"><Scale className="h-3 w-3"/>Venta por peso</span>}
                <span className="mt-0.5 block truncate text-xs text-slate-500">
                  {product.productCode}{product.reference ? ` - ${product.reference}` : ""}
                </span>
              </span>
              <span className="hidden text-sm text-slate-600 sm:block">
                {product.baseUnitCode} - IVA {product.taxRate}%
              </span>
              <span className="text-right font-bold tabular-nums text-teal-800">
                {money.format(product.unitPrice)}
                <small className="mt-0.5 block font-medium text-slate-500">{
                  product.priceSource === "Promotion+PriceChannel" ? "Promoción + canal"
                    : product.priceSource === "Promotion" ? "Promoción"
                      : product.priceSource === "PriceChannel" ? "Canal" : "Público"
                }</small>
              </span>
            </button>
          ))}

          {!loading && !results.length && !error && (
            <p className="grid min-h-48 place-items-center text-center text-sm text-slate-500">
              No encontramos productos vendibles con ese criterio.
            </p>
          )}
          {loadingMore && (
            <p className="flex items-center justify-center gap-2 py-4 text-sm text-teal-800" role="status">
              <Loader2 className="h-4 w-4 animate-spin" />
              Cargando 50 productos más
            </p>
          )}
          {!loading && results.length > 0 && !hasMore && (
            <p className="py-4 text-center text-xs text-slate-500">
              Fin del catálogo disponible
            </p>
          )}
          {error && (
            <p className="grid min-h-24 place-items-center text-center text-sm font-medium text-red-700" role="alert">
              {error}
            </p>
          )}
        </div>

        <section className="shrink-0 border-t border-slate-200 bg-slate-50/80 px-5 py-4" aria-label="Existencias por sede y bodega">
          <header className="mb-2 flex items-center justify-between gap-3">
            <div className="min-w-0">
              <h3 className="flex items-center gap-2 text-sm font-bold text-slate-900">
                <Boxes className="h-4 w-4 text-teal-700" />
                Existencias por sede y bodega
              </h3>
              <p className="truncate text-xs text-slate-500">
                {results[selected]?.name ?? "Selecciona un producto para consultar su disponibilidad."}
              </p>
            </div>
            {availabilityLoading && <span className="flex shrink-0 items-center gap-2 text-xs font-medium text-teal-800" role="status"><Loader2 className="h-4 w-4 animate-spin" />Consultando servidor</span>}
          </header>

          {availabilityError ? (
            <div className="flex min-h-16 items-center gap-3 rounded-xl border border-amber-200 bg-amber-50 px-4 text-sm text-amber-950">
              {connected ? <ShieldAlert className="h-5 w-5 shrink-0 text-amber-700" /> : <WifiOff className="h-5 w-5 shrink-0 text-amber-700" />}
              {availabilityError}
            </div>
          ) : (
            <div className="max-h-36 overflow-auto rounded-xl border border-slate-200 bg-white">
              <div className="sticky top-0 grid grid-cols-[minmax(0,1fr)_minmax(0,1fr)_110px] gap-3 border-b bg-slate-100 px-3 py-2 text-[11px] font-bold uppercase tracking-wide text-slate-500">
                <span>Sede</span><span>Bodega</span><span className="text-right">Existencias</span>
              </div>
              {availability.map((item) => (
                <div key={`${item.businessId}-${item.warehouseId}`} className="grid grid-cols-[minmax(0,1fr)_minmax(0,1fr)_110px] items-center gap-3 border-b border-slate-100 px-3 py-2 text-sm last:border-b-0">
                  <span className="truncate font-medium text-slate-800">{item.businessName}{item.isCurrentBusiness && <small className="ml-2 rounded-full bg-teal-100 px-2 py-0.5 text-[10px] font-bold text-teal-800">Actual</small>}</span>
                  <span className="flex min-w-0 items-center gap-2 truncate text-slate-600"><Warehouse className="h-3.5 w-3.5 shrink-0 text-teal-700" />{item.warehouseName} · {item.warehouseCode}</span>
                  <strong className={`text-right tabular-nums ${item.quantityOnHand < 0 ? "text-red-700" : "text-slate-900"}`}>{item.quantityOnHand.toLocaleString("es-CO", { maximumFractionDigits: 3 })}</strong>
                </div>
              ))}
              {!availabilityLoading && availability.length === 0 && results[selected] && (
                <p className="p-4 text-center text-sm text-slate-500">No hay bodegas operativas para este producto.</p>
              )}
            </div>
          )}
        </section>
      </section>
    </div>
  );
}
