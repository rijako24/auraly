"use client";

import { Loader2, PackageSearch, Search, X } from "lucide-react";
import { useCallback, useEffect, useRef, useState, type KeyboardEvent as ReactKeyboardEvent } from "react";

import type {
  PosCatalogProduct,
  PosCatalogSearchPage,
} from "@/services/pos/pos-edge-client";

const PAGE_SIZE = 50;

const money = new Intl.NumberFormat("es-CO", {
  style: "currency",
  currency: "COP",
  maximumFractionDigits: 0,
});

export function PosProductSearchDialog({
  busy,
  onSearch,
  onSelect,
  onCancel,
}: {
  busy: boolean;
  onSearch: (term: string, skip: number) => Promise<PosCatalogSearchPage>;
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
  const resultElements = useRef(new Map<number, HTMLButtonElement>());

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
          window.requestAnimationFrame(() => resultElements.current.get(0)?.focus());
          setHasMore(page.hasMore);
          setNextOffset(page.nextOffset);
        })
        .catch(() => {
          if (requestVersion.current !== version) return;
          setResults([]);
          setHasMore(false);
          setNextOffset(null);
          setError("No fue posible consultar el catálogo local.");
        })
        .finally(() => {
          if (requestVersion.current === version) setLoading(false);
        });
    }, normalized ? 180 : 0);

    return () => window.clearTimeout(timer);
  }, [onSearch, term]);

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
    const target = Math.max(
      0,
      Math.min(results.length - 1, selected + direction),
    );
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
              ref={input}
              autoFocus
              value={term}
              onChange={(event) => setTerm(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === "ArrowDown") {
                  event.preventDefault();
                  moveSelection(1);
                  return;
                }
                if (event.key === "ArrowUp") {
                  event.preventDefault();
                  moveSelection(-1);
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
              <Loader2 className="absolute right-4 top-1/2 h-5 w-5 -translate-y-1/2 animate-spin text-teal-700" />
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
              onMouseEnter={() => setSelected(index)}
              onClick={() => void choose(product)}
              onKeyDown={handleListNavigation}
              disabled={busy}
              className={`grid w-full grid-cols-[minmax(0,1fr)_130px] items-center gap-4 border-b border-slate-100 px-3 py-3 text-left outline-none transition sm:grid-cols-[minmax(0,1fr)_150px_130px] ${
                selected === index ? "bg-teal-50 ring-2 ring-inset ring-teal-600/25" : "hover:bg-slate-50"
              }`}
            >
              <span className="min-w-0">
                <span className="block truncate font-semibold text-slate-900">{product.name}</span>
                <span className="mt-0.5 block truncate text-xs text-slate-500">
                  {product.productCode}{product.reference ? ` - ${product.reference}` : ""}
                </span>
              </span>
              <span className="hidden text-sm text-slate-600 sm:block">
                {product.baseUnitCode} - IVA {product.taxRate}%
              </span>
              <span className="text-right font-bold tabular-nums text-teal-800">
                {money.format(product.unitPrice)}
                <small className="mt-0.5 block font-medium text-slate-500">{product.priceSource === "PriceList" ? "Lista" : product.priceSource === "PriceChannel" ? "Canal" : "Público"}</small>
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
      </section>
    </div>
  );
}
