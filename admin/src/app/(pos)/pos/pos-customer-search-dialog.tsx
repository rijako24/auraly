"use client";

import { Loader2, Search, UserRound, UserRoundX, X } from "lucide-react";
import { useCallback, useEffect, useRef, useState } from "react";

import type {
  PosCustomer,
  PosCustomerSearchPage,
} from "@/services/pos/pos-edge-client";

export function PosCustomerSearchDialog({
  busy,
  onSearch,
  onSelect,
  onCancel,
}: {
  busy: boolean;
  onSearch: (term: string, skip: number) => Promise<PosCustomerSearchPage>;
  onSelect: (customer: PosCustomer | null) => Promise<void>;
  onCancel: () => void;
}) {
  const [term, setTerm] = useState("");
  const [results, setResults] = useState<PosCustomer[]>([]);
  const [selected, setSelected] = useState(0);
  const [hasMore, setHasMore] = useState(false);
  const [nextOffset, setNextOffset] = useState<number | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const requestVersion = useRef(0);

  useEffect(() => {
    const version = ++requestVersion.current;
    const timer = window.setTimeout(() => {
      setLoading(true);
      setError(null);
      void onSearch(term.trim(), 0)
        .then((page) => {
          if (requestVersion.current !== version) return;
          setResults(page.items);
          setSelected(0);
          setHasMore(page.hasMore);
          setNextOffset(page.nextOffset);
        })
        .catch(() => {
          if (requestVersion.current !== version) return;
          setResults([]);
          setHasMore(false);
          setNextOffset(null);
          setError("No fue posible consultar los clientes sincronizados.");
        })
        .finally(() => {
          if (requestVersion.current === version) setLoading(false);
        });
    }, term.trim() ? 180 : 0);
    return () => window.clearTimeout(timer);
  }, [onSearch, term]);

  useEffect(() => {
    const close = (event: KeyboardEvent) => {
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
    try {
      const page = await onSearch(term.trim(), nextOffset);
      setResults((current) => {
        const known = new Set(current.map((customer) => customer.customerId));
        return [...current, ...page.items.filter((customer) => !known.has(customer.customerId))];
      });
      setHasMore(page.hasMore);
      setNextOffset(page.nextOffset);
    } catch {
      setError("No fue posible cargar más clientes.");
    } finally {
      setLoadingMore(false);
    }
  }, [busy, hasMore, loading, loadingMore, nextOffset, onSearch, term]);

  return (
    <div className="fixed inset-0 z-50 grid place-items-center bg-slate-950/60 p-4">
      <section className="flex max-h-[82vh] w-full max-w-3xl flex-col overflow-hidden rounded-2xl bg-white shadow-2xl">
        <header className="flex items-start justify-between border-b border-slate-200 p-5">
          <div>
            <h2 className="flex items-center gap-2 text-xl font-semibold">
              <UserRound className="h-5 w-5 text-teal-700" />
              Buscar cliente
            </h2>
            <p className="mt-1 text-sm text-slate-500">
              Busca por nombre o identificación. La selección recalcula los precios.
            </p>
          </div>
          <button type="button" onClick={onCancel} aria-label="Cerrar búsqueda"
            className="grid h-10 w-10 place-items-center rounded-lg text-slate-500 hover:bg-slate-100">
            <X className="h-5 w-5" />
          </button>
        </header>
        <div className="p-5 pb-3">
          <label className="relative block">
            <Search className="pointer-events-none absolute left-4 top-1/2 h-5 w-5 -translate-y-1/2 text-slate-400" />
            <input autoFocus value={term} onChange={(event) => setTerm(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === "Enter" && results[selected]) {
                  event.preventDefault();
                  void onSelect(results[selected]);
                }
              }}
              className="h-14 w-full rounded-xl border-2 border-teal-700/25 bg-slate-50 pl-12 pr-12 text-lg font-medium outline-none focus:border-teal-600 focus:bg-white focus:ring-4 focus:ring-teal-600/10"
              placeholder="Nombre o identificación" />
            {loading && <Loader2 className="absolute right-4 top-1/2 h-5 w-5 -translate-y-1/2 animate-spin text-teal-700" />}
          </label>
          <button type="button" disabled={busy} onClick={() => void onSelect(null)}
            className="mt-3 flex h-11 w-full items-center gap-3 rounded-xl border border-slate-200 px-4 text-left font-semibold text-slate-700 hover:bg-slate-50">
            <UserRoundX className="h-5 w-5 text-slate-500" />
            Consumidor final
          </button>
        </div>
        <div className="min-h-52 flex-1 overflow-auto px-5 pb-5"
          onScroll={(event) => {
            const list = event.currentTarget;
            if (list.scrollHeight - list.scrollTop - list.clientHeight < 100) void loadMore();
          }}>
          {results.map((customer, index) => (
            <button key={customer.customerId} type="button" disabled={busy}
              onFocus={() => { setSelected(index); if (index === results.length - 1) void loadMore(); }}
              onMouseEnter={() => setSelected(index)}
              onClick={() => void onSelect(customer)}
              className={`grid w-full grid-cols-[minmax(0,1fr)_180px] items-center gap-4 border-b border-slate-100 px-3 py-3 text-left outline-none ${
                selected === index ? "bg-teal-50 ring-2 ring-inset ring-teal-600/25" : "hover:bg-slate-50"
              }`}>
              <span className="truncate font-semibold text-slate-900">{customer.name}</span>
              <span className="text-right text-sm tabular-nums text-slate-600">{customer.identification}</span>
            </button>
          ))}
          {!loading && !results.length && !error && (
            <p className="grid min-h-40 place-items-center text-sm text-slate-500">No encontramos clientes.</p>
          )}
          {loadingMore && <p className="flex justify-center gap-2 py-4 text-sm text-teal-800">
            <Loader2 className="h-4 w-4 animate-spin" /> Cargando más clientes
          </p>}
          {error && <p className="py-5 text-center text-sm font-medium text-red-700">{error}</p>}
        </div>
      </section>
    </div>
  );
}
