"use client";

import { Check, FileText, Loader2, Receipt, X } from "lucide-react";
import { useEffect } from "react";

import type { PosSaleDocumentType } from "@/services/pos/pos-edge-client";

const options: ReadonlyArray<{
  value: PosSaleDocumentType;
  title: string;
  description: string;
  icon: typeof FileText;
}> = [
  {
    value: "SalesInvoice",
    title: "Factura electrónica",
    description: "Usa numeración DIAN, CUFE y código QR.",
    icon: FileText,
  },
  {
    value: "SalesReceipt",
    title: "Comprobante de venta",
    description: "Usa la numeración operativa CVI.",
    icon: Receipt,
  },
];

export function PosDocumentTypeDialog({
  value,
  busy,
  onSelect,
  onCancel,
}: {
  value: PosSaleDocumentType;
  busy: boolean;
  onSelect: (value: PosSaleDocumentType) => Promise<void>;
  onCancel: () => void;
}) {
  useEffect(() => {
    const handleEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape" && !busy) {
        event.preventDefault();
        onCancel();
      }
    };

    window.addEventListener("keydown", handleEscape);
    return () => window.removeEventListener("keydown", handleEscape);
  }, [busy, onCancel]);

  return (
    <div
      className="fixed inset-0 z-[60] grid place-items-center bg-slate-950/65 p-4"
      onMouseDown={(event) => {
        if (event.currentTarget === event.target && !busy) onCancel();
      }}
    >
      <section
        role="dialog"
        aria-modal="true"
        aria-labelledby="pos-document-type-title"
        aria-describedby="pos-document-type-description"
        className="w-full max-w-lg overflow-hidden rounded-3xl bg-white shadow-2xl"
      >
        <header className="flex items-start justify-between gap-4 border-b border-slate-200 px-6 py-5">
          <div>
            <p className="text-xs font-bold uppercase tracking-[0.18em] text-teal-700">
              Nueva venta
            </p>
            <h2 id="pos-document-type-title" className="mt-1 text-xl font-bold text-slate-950">
              Tipo de documento
            </h2>
            <p id="pos-document-type-description" className="mt-1 text-sm text-slate-600">
              Elige el documento que emitirá esta venta.
            </p>
          </div>
          <button
            type="button"
            onClick={onCancel}
            disabled={busy}
            className="grid h-10 w-10 shrink-0 place-items-center rounded-xl text-slate-500 outline-none transition hover:bg-slate-100 hover:text-slate-900 focus:ring-2 focus:ring-teal-500/30 disabled:opacity-50"
            aria-label="Cerrar"
          >
            <X className="h-5 w-5" />
          </button>
        </header>

        <div className="grid gap-3 p-6 sm:grid-cols-2">
          {options.map((option) => {
            const selected = option.value === value;
            const Icon = option.icon;
            return (
              <button
                key={option.value}
                autoFocus={selected}
                type="button"
                disabled={busy}
                onClick={() => void onSelect(option.value)}
                className={`relative min-h-40 rounded-2xl border-2 p-5 text-left outline-none transition focus:ring-4 focus:ring-teal-500/20 disabled:cursor-wait disabled:opacity-60 ${
                  selected
                    ? "border-teal-500 bg-teal-50 shadow-sm"
                    : "border-slate-200 bg-white hover:border-teal-300 hover:bg-slate-50"
                }`}
              >
                <span
                  className={`grid h-11 w-11 place-items-center rounded-xl ${
                    selected ? "bg-teal-600 text-white" : "bg-slate-100 text-slate-700"
                  }`}
                >
                  {busy && selected ? (
                    <Loader2 className="h-5 w-5 animate-spin" />
                  ) : (
                    <Icon className="h-5 w-5" />
                  )}
                </span>
                <span className="mt-4 block font-bold text-slate-950">{option.title}</span>
                <span className="mt-1 block text-sm leading-5 text-slate-600">
                  {option.description}
                </span>
                {selected && (
                  <span className="absolute right-4 top-4 grid h-7 w-7 place-items-center rounded-full bg-teal-600 text-white">
                    <Check className="h-4 w-4" />
                  </span>
                )}
              </button>
            );
          })}
        </div>
      </section>
    </div>
  );
}
