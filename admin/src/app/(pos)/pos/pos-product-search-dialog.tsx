"use client";

import { Boxes, Loader2, PackageSearch, Search, ShieldAlert, Warehouse, WifiOff, X, Scale } from "lucide-react";
import { useCallback, useEffect, useRef, useState, type KeyboardEvent as ReactKeyboardEvent } from "react";

import type {
  PosCatalogProduct,
  PosCatalogSearchPage,
  PosProductWarehouseAvailability,
} from "@/services/pos/pos-edge-client";
import { usePosModalBehavior } from "./use-pos-modal-behavior";

const money = new Intl.NumberFormat("es-CO", {
  style: "currency",
  currency: "COP",
  maximumFractionDigits: 0,
});

export function PosProductSearchDialog({
  busy,
  verifierMode,
  focusRequest,
  availabilityRequest,
  onSearch,
  connected,
  canReadAvailability,
  onLoadAvailability,
  onSelect,
  onCancel,
}: {
  busy: boolean;
  verifierMode: boolean;
  focusRequest: number;
  availabilityRequest: number;
  onSearch: (term: string, skip: number) => Promise<PosCatalogSearchPage>;
  connected: boolean;
  canReadAvailability: boolean;
  onLoadAvailability: (
    productId: string,
    signal?: AbortSignal,
  ) => Promise<PosProductWarehouseAvailability[]>;
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
  const modal = useRef<HTMLElement>(null);
  const requestVersion = useRef(0);
  const handledAvailabilityRequest = useRef(availabilityRequest);
  const resultElements = useRef(new Map<number, HTMLButtonElement>());
  const availabilityController = useRef<AbortController | null>(null);
  const [availabilityLookup, setAvailabilityLookup] = useState<{
    product: PosCatalogProduct;
    response: Promise<PosProductWarehouseAvailability[]> | null;
  } | null>(null);

  usePosModalBehavior({
    modalRef: modal,
    initialFocusRef: input,
    escapeDisabled: busy,
    focusRequest,
    onEscape: onCancel,
  });

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

  const selectedProduct = results[selected];

  const openAvailability = useCallback((product: PosCatalogProduct) => {
    availabilityController.current?.abort();
    const controller = canReadAvailability && connected
      ? new AbortController()
      : null;
    availabilityController.current = controller;
    setAvailabilityLookup({
      product,
      response: controller
        ? onLoadAvailability(product.productId, controller.signal)
        : null,
    });
  }, [canReadAvailability, connected, onLoadAvailability]);

  const closeAvailability = useCallback(() => {
    availabilityController.current?.abort();
    availabilityController.current = null;
    setAvailabilityLookup(null);
  }, []);

  useEffect(() => {
    if (availabilityRequest === handledAvailabilityRequest.current) return;
    handledAvailabilityRequest.current = availabilityRequest;
    if (selectedProduct) openAvailability(selectedProduct);
  }, [availabilityRequest, openAvailability, selectedProduct]);

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
    if (direction > 0 && target === results.length - 1)
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
        ref={modal}
        tabIndex={-1}
        role="dialog"
        aria-modal="true"
        data-pos-focus-surface="modal"
        className="flex h-[calc(100dvh-2rem)] max-h-[48rem] w-full max-w-5xl flex-col overflow-hidden rounded-2xl bg-white shadow-2xl"
        aria-labelledby="pos-product-search-title"
      >
        <header className="flex shrink-0 items-start justify-between gap-4 border-b border-slate-200 p-5">
          <div>
            <h2 id="pos-product-search-title" className="flex items-center gap-2 text-xl font-semibold">
              <PackageSearch className="h-5 w-5 text-teal-700" />
              {verifierMode ? "Verificador de precios" : "Buscar producto"}
            </h2>
            <p className="mt-1 text-sm text-slate-500">
              {verifierMode ? "Escanea o busca: Enter sólo consulta. Pulsa F1 para volver a agregar productos." : "Nombre, código interno, referencia, código de barras o alterno."}
            </p>
          </div>
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={() => selectedProduct && openAvailability(selectedProduct)}
              disabled={!selectedProduct}
              className="inline-flex h-10 items-center gap-2 rounded-lg border border-teal-700/20 px-3 text-sm font-semibold text-teal-800 hover:bg-teal-50 disabled:cursor-not-allowed disabled:opacity-40 focus:outline-none focus:ring-2 focus:ring-teal-600/20"
            >
              <Boxes className="h-4 w-4" />
              Existencias <kbd className="rounded bg-teal-100 px-1.5 py-0.5 text-[11px]">F2</kbd>
            </button>
            <button
              type="button"
              onClick={onCancel}
              disabled={busy}
              className="grid h-10 w-10 place-items-center rounded-lg text-slate-500 hover:bg-slate-100 focus:outline-none focus:ring-2 focus:ring-teal-600/20"
              aria-label="Cerrar búsqueda"
            >
              <X className="h-5 w-5" />
            </button>
          </div>
        </header>

        <div className="shrink-0 p-5 pb-3">
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
            {verifierMode
              ? "Flechas recorren; Tab entra al listado; Enter consulta; Esc vuelve al lector."
              : "Flechas recorren; Enter agrega; F2 consulta existencias; F1 verifica precios; Esc vuelve al lector."}
          </p>
        </div>

        <div
          id="pos-product-results"
          role="listbox"
          className="min-h-0 flex-1 overflow-auto px-5 pb-3"
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
              className={`grid min-h-20 w-full grid-cols-[minmax(0,1fr)_110px] items-center gap-x-4 gap-y-1 border-b border-slate-100 px-3 py-3 text-left outline-none transition sm:grid-cols-[minmax(0,1fr)_200px_130px] ${
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
              <span className="col-start-1 row-start-2 sm:col-start-2 sm:row-start-1 sm:text-center">
                {(product.promotionDiscount ?? 0) > 0 && <span className="inline-block rounded-full bg-emerald-100 px-3 py-1 text-xs font-bold text-emerald-800">Promoción · ahorra {money.format(product.promotionDiscount ?? 0)}</span>}
              </span>
              <span className="col-start-2 row-start-1 text-right font-bold tabular-nums text-teal-800 sm:col-start-3">
                {money.format(product.unitPrice)}
                {product.isWeighable && <small className="ml-1 font-medium text-slate-500">/ {product.baseUnitCode}</small>}
                {(product.priceSource === "Promotion+PriceChannel" || product.priceSource === "PriceChannel") && <small className="mt-0.5 block font-medium text-slate-500">Precio de canal</small>}
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
      {availabilityLookup && (
        <PosProductAvailabilityDialog
          product={availabilityLookup.product}
          connected={connected}
          canReadAvailability={canReadAvailability}
          response={availabilityLookup.response}
          onClose={closeAvailability}
        />
      )}
    </div>
  );
}

function PosProductAvailabilityDialog({
  product,
  connected,
  canReadAvailability,
  response,
  onClose,
}: {
  product: PosCatalogProduct;
  connected: boolean;
  canReadAvailability: boolean;
  response: Promise<PosProductWarehouseAvailability[]> | null;
  onClose: () => void;
}) {
  const modal = useRef<HTMLElement>(null);
  const closeButton = useRef<HTMLButtonElement>(null);
  const [availability, setAvailability] = useState<PosProductWarehouseAvailability[]>([]);
  const [loading, setLoading] = useState(canReadAvailability && connected);
  const [error, setError] = useState<string | null>(
    !canReadAvailability
      ? "Tu perfil no tiene permiso para consultar existencias por bodega."
      : !connected
        ? "Sin conexión al servidor. No es posible consultar existencias en este momento."
        : null,
  );

  usePosModalBehavior({ modalRef: modal, initialFocusRef: closeButton, onEscape: onClose });

  useEffect(() => {
    if (!response) return;
    let active = true;
    void response
      .then((value) => {
        if (active) setAvailability(value);
      })
      .catch((caught) => {
        if (!active) return;
        if (caught instanceof DOMException && caught.name === "AbortError") return;
        setError("No fue posible consultar las existencias del servidor.");
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [response]);

  return (
    <div className="fixed inset-0 z-[60] grid place-items-center bg-slate-950/65 p-4">
      <section
        ref={modal}
        tabIndex={-1}
        role="dialog"
        aria-modal="true"
        aria-labelledby="pos-product-availability-title"
        data-pos-focus-surface="modal"
        className="flex max-h-[80dvh] w-full max-w-3xl flex-col overflow-hidden rounded-2xl bg-white shadow-2xl"
      >
        <header className="flex shrink-0 items-start justify-between gap-4 border-b border-slate-200 p-5">
          <div className="min-w-0">
            <h2 id="pos-product-availability-title" className="flex items-center gap-2 text-xl font-semibold text-slate-950">
              <Boxes className="h-5 w-5 text-teal-700" />
              Existencias por sede y bodega
            </h2>
            <p className="mt-1 truncate text-sm text-slate-500">{product.name} · {product.productCode}</p>
          </div>
          <button ref={closeButton} type="button" onClick={onClose} className="grid h-10 w-10 shrink-0 place-items-center rounded-lg text-slate-500 hover:bg-slate-100 focus:outline-none focus:ring-2 focus:ring-teal-600/20" aria-label="Cerrar existencias">
            <X className="h-5 w-5" />
          </button>
        </header>

        <div className="min-h-56 overflow-y-auto p-5">
          {loading ? (
            <div className="grid min-h-48 place-items-center text-sm font-medium text-teal-800" role="status">
              <span className="flex items-center gap-2"><Loader2 className="h-5 w-5 animate-spin" />Consultando existencias</span>
            </div>
          ) : error ? (
            <div className="flex min-h-40 items-center gap-3 rounded-xl border border-amber-200 bg-amber-50 px-4 text-sm text-amber-950" role="status">
              {connected ? <ShieldAlert className="h-5 w-5 shrink-0 text-amber-700" /> : <WifiOff className="h-5 w-5 shrink-0 text-amber-700" />}
              {error}
            </div>
          ) : (
            <div className="overflow-hidden rounded-xl border border-slate-200">
              <div className="grid grid-cols-[minmax(0,1fr)_minmax(0,1fr)_110px] gap-3 border-b bg-slate-100 px-3 py-2 text-[11px] font-bold uppercase tracking-wide text-slate-500">
                <span>Sede</span><span>Bodega</span><span className="text-right">Existencias</span>
              </div>
              {availability.map((item) => (
                <div key={`${item.businessId}-${item.warehouseId}`} className="grid grid-cols-[minmax(0,1fr)_minmax(0,1fr)_110px] items-center gap-3 border-b border-slate-100 px-3 py-3 text-sm last:border-b-0">
                  <span className="truncate font-medium text-slate-800">{item.businessName}{item.isCurrentBusiness && <small className="ml-2 rounded-full bg-teal-100 px-2 py-0.5 text-[10px] font-bold text-teal-800">Actual</small>}</span>
                  <span className="flex min-w-0 items-center gap-2 truncate text-slate-600"><Warehouse className="h-3.5 w-3.5 shrink-0 text-teal-700" />{item.warehouseName} · {item.warehouseCode}</span>
                  <strong className={`text-right tabular-nums ${item.quantityOnHand < 0 ? "text-red-700" : "text-slate-900"}`}>{item.quantityOnHand.toLocaleString("es-CO", { maximumFractionDigits: 3 })}</strong>
                </div>
              ))}
              {availability.length === 0 && (
                <p className="grid min-h-28 place-items-center p-4 text-center text-sm text-slate-500">No hay bodegas operativas para este producto.</p>
              )}
            </div>
          )}
        </div>
      </section>
    </div>
  );
}
