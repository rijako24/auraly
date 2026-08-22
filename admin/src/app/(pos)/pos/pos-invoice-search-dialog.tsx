"use client";

import { Loader2, Printer, ReceiptText, Search, X } from "lucide-react";
import { useCallback, useEffect, useRef, useState } from "react";

import type {
  PosIssuedSaleSearchPage,
  PosIssuedSaleSummary,
} from "@/services/pos/pos-edge-client";

const money = new Intl.NumberFormat("es-CO", {
  style: "currency",
  currency: "COP",
  maximumFractionDigits: 0,
});

const dateTime = new Intl.DateTimeFormat("es-CO", {
  dateStyle: "short",
  timeStyle: "short",
});

export function PosInvoiceSearchDialog({
  busy,
  onSearch,
  onReprint,
  onCancel,
}: {
  busy: boolean;
  onSearch: (term: string, skip: number) => Promise<PosIssuedSaleSearchPage>;
  onReprint: (sale: PosIssuedSaleSummary) => Promise<void>;
  onCancel: () => void;
}) {
  const [term, setTerm] = useState("");
  const [results, setResults] = useState<PosIssuedSaleSummary[]>([]);
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
          setError("No fue posible consultar los comprobantes de esta sede.");
        })
        .finally(() => {
          if (requestVersion.current === version) setLoading(false);
        });
    }, term.trim() ? 180 : 0);
    return () => window.clearTimeout(timer);
  }, [onSearch, term]);

  useEffect(() => {
    const handleKey = (event: globalThis.KeyboardEvent) => {
      if (event.key === "Escape" && !busy) {
        event.preventDefault();
        onCancel();
      }
    };
    window.addEventListener("keydown", handleKey);
    return () => window.removeEventListener("keydown", handleKey);
  }, [busy, onCancel]);

  const loadMore = useCallback(async () => {
    if (busy || loading || loadingMore || !hasMore || nextOffset === null) return;
    setLoadingMore(true);
    try {
      const page = await onSearch(term.trim(), nextOffset);
      setResults((current) => {
        const known = new Set(current.map((sale) => sale.documentId.value));
        return [...current, ...page.items.filter((sale) => !known.has(sale.documentId.value))];
      });
      setHasMore(page.hasMore);
      setNextOffset(page.nextOffset);
    } catch {
      setError("No fue posible cargar más comprobantes.");
    } finally {
      setLoadingMore(false);
    }
  }, [busy, hasMore, loading, loadingMore, nextOffset, onSearch, term]);

  return (
    <div className="fixed inset-0 z-50 grid place-items-center bg-slate-950/60 p-4">
      <section className="flex max-h-[86vh] w-full max-w-4xl flex-col overflow-hidden rounded-2xl bg-white shadow-2xl"
        aria-labelledby="invoice-search-title">
        <header className="flex items-start justify-between gap-4 border-b border-slate-200 p-5">
          <div>
            <h2 id="invoice-search-title" className="flex items-center gap-2 text-xl font-semibold">
              <ReceiptText className="h-5 w-5 text-teal-700" />
              Buscar y reimprimir comprobante
            </h2>
            <p className="mt-1 text-sm text-slate-500">
              Facturas y comprobantes POS por número Auraly o número DIAN. La copia usa el snapshot original e incluye auditoría.
            </p>
          </div>
          <button type="button" onClick={onCancel} disabled={busy}
            className="grid h-10 w-10 place-items-center rounded-lg text-slate-500 hover:bg-slate-100"
            aria-label="Cerrar búsqueda de comprobantes">
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
                  void onReprint(results[selected]);
                }
              }}
              className="h-14 w-full rounded-xl border-2 border-teal-700/25 bg-slate-50 pl-12 pr-12 text-lg font-medium outline-none focus:border-teal-600 focus:bg-white focus:ring-4 focus:ring-teal-600/10"
              placeholder="Número de factura o comprobante"
              aria-label="Número de factura o comprobante" />
            {loading && <Loader2 className="absolute right-4 top-1/2 h-5 w-5 -translate-y-1/2 animate-spin text-teal-700" />}
          </label>
          <p className="mt-2 text-xs text-slate-500">
            Se muestran las 50 más recientes. Tab recorre; Enter reimprime; Esc cierra.
          </p>
        </div>

        <div className="min-h-60 flex-1 overflow-auto px-5 pb-5"
          onScroll={(event) => {
            const list = event.currentTarget;
            if (list.scrollHeight - list.scrollTop - list.clientHeight < 120) void loadMore();
          }}>
          {results.map((sale, index) => (
            <button key={sale.documentId.value} type="button"
              onFocus={() => {
                setSelected(index);
                if (index === results.length - 1) void loadMore();
              }}
              onMouseEnter={() => setSelected(index)}
              onClick={() => void onReprint(sale)}
              disabled={busy}
              className={`grid w-full grid-cols-[minmax(0,1fr)_140px] items-center gap-4 border-b border-slate-100 px-3 py-3 text-left outline-none transition sm:grid-cols-[minmax(0,1fr)_150px_140px] ${
                selected === index ? "bg-teal-50 ring-2 ring-inset ring-teal-600/25" : "hover:bg-slate-50"
              }`}>
              <span className="min-w-0">
                <span className="flex items-center gap-2"><span className="font-bold text-slate-950">{sale.documentNumber}</span><span className="rounded-full bg-slate-100 px-2 py-0.5 text-[10px] font-bold uppercase text-slate-600">{sale.documentType === "SalesReceipt" ? "Comprobante POS" : "Factura"}</span></span>
                <span className="mt-0.5 block truncate text-xs text-slate-500">
                  DIAN {sale.fiscalNumber} · {sale.customerName} · {sale.customerIdentification}
                </span>
              </span>
              <span className="hidden text-sm text-slate-600 sm:block">
                {dateTime.format(new Date(sale.issuedAt))}
              </span>
              <span className="flex items-center justify-end gap-2 font-bold tabular-nums text-teal-800">
                {money.format(sale.total)}
                <Printer className="h-4 w-4" />
              </span>
            </button>
          ))}
          {!loading && !results.length && !error && (
            <p className="grid min-h-40 place-items-center text-center text-sm text-slate-500">
              No encontramos facturas ni comprobantes con ese número.
            </p>
          )}
          {loadingMore && <p className="py-4 text-center text-sm text-teal-800">Cargando más comprobantes…</p>}
          {error && <p className="py-5 text-center text-sm font-medium text-red-700" role="alert">{error}</p>}
        </div>
      </section>
    </div>
  );
}
