"use client";

import { FormEvent, KeyboardEvent, useRef, useState } from "react";
import { CircleDollarSign } from "lucide-react";

import type { PosDraftLine } from "@/services/pos/pos-edge-client";
import { formatMoneyDraft, formatMoneyValue, parseMoneyDraft } from "./pos-money-input";
import {
  genericProductFromMargin,
  genericProductFromSalePrice,
  initializeGenericProductPrice,
  nextGenericProductFieldIndex,
} from "./pos-generic-product-calculation";
import { usePosModalBehavior } from "./use-pos-modal-behavior";

export function PosGenericProductDialog({
  line,
  busy,
  onConfirm,
  onCancel,
}: {
  line: PosDraftLine;
  busy: boolean;
  onConfirm: (publicSalePrice: number, documentUnitCost: number) => Promise<void>;
  onCancel: () => Promise<void>;
}) {
  const form = useRef<HTMLFormElement>(null);
  const fields = useRef<Array<HTMLInputElement | null>>([]);
  const economicsCustomized = useRef(line.publicUnitPrice > 0);
  const [salePrice, setSalePrice] = useState(line.publicUnitPrice > 0 ? formatMoneyValue(line.publicUnitPrice) : "");
  const [margin, setMargin] = useState("0");
  const [cost, setCost] = useState(line.documentUnitCost > 0 ? formatMoneyValue(line.documentUnitCost) : "0");

  usePosModalBehavior({ modalRef: form, escapeDisabled: busy, onEscape: () => void onCancel() });

  const apply = (next: { publicSalePrice: number; documentUnitCost: number; marginPercent: number }) => {
    setSalePrice(formatMoneyValue(next.publicSalePrice));
    setCost(formatMoneyValue(next.documentUnitCost));
    setMargin(String(Math.round(next.marginPercent * 100) / 100).replace(".", ","));
  };

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    const price = parseMoneyDraft(salePrice);
    const unitCost = parseMoneyDraft(cost);
    if (!busy && price > 0 && unitCost >= 0) await onConfirm(price, unitCost);
  };

  const moveField = (event: KeyboardEvent<HTMLInputElement>, currentIndex: number) => {
    const nextIndex = nextGenericProductFieldIndex(currentIndex, 3, event.key);
    if (nextIndex === null) return;
    event.preventDefault();
    fields.current[nextIndex]?.focus();
  };

  return <div className="fixed inset-0 z-[75] grid place-items-center bg-slate-950/70 p-4">
    <form ref={form} role="dialog" aria-modal="true" aria-labelledby="generic-product-title" onSubmit={submit} className="w-full max-w-md overflow-hidden rounded-3xl bg-white shadow-2xl">
      <header className="bg-gradient-to-r from-slate-950 to-teal-950 px-6 py-5 text-white">
        <span className="grid h-11 w-11 place-items-center rounded-2xl bg-white/10 text-teal-200"><CircleDollarSign className="h-5 w-5" /></span>
        <h2 id="generic-product-title" className="mt-3 text-xl font-bold">Define el valor del producto genérico</h2>
        <p className="mt-1 text-sm text-slate-300">{line.description} · descuento siempre en cero</p>
      </header>
      <div className="space-y-4 p-6">
        <label className="block rounded-2xl border-2 border-teal-600 bg-teal-50/60 p-4 text-sm font-bold text-teal-950">Precio de venta · IVA incluido
          <input ref={element => { fields.current[0] = element; }} autoFocus inputMode="decimal" value={salePrice} onFocus={event => event.currentTarget.select()} onKeyDown={event => moveField(event, 0)} onChange={event => {
            const raw = formatMoneyDraft(event.target.value);
            const value = parseMoneyDraft(raw);
            setSalePrice(raw);
            if (!economicsCustomized.current) {
              apply(initializeGenericProductPrice(value, line.taxRate));
            } else apply(genericProductFromSalePrice(value, parseMoneyDraft(cost), line.taxRate));
          }} className="mt-2 h-16 w-full rounded-xl border border-teal-300 bg-white px-4 text-right text-3xl font-black tabular-nums outline-none focus:ring-4 focus:ring-teal-600/15" />
        </label>
        <label className="block space-y-2 text-sm font-semibold text-slate-700">Margen %
          <input ref={element => { fields.current[1] = element; }} inputMode="decimal" value={margin} onFocus={event => event.currentTarget.select()} onKeyDown={event => moveField(event, 1)} onChange={event => {
            const value = Number(event.target.value.replace(",", "."));
            setMargin(event.target.value);
            if (Number.isFinite(value) && value < 100) {
              economicsCustomized.current = true;
              apply(genericProductFromMargin(parseMoneyDraft(cost), value, line.taxRate));
            }
          }} className="h-12 w-full rounded-xl border border-slate-300 px-3 text-right text-lg font-bold outline-none focus:border-teal-600 focus:ring-4 focus:ring-teal-600/10" />
        </label>
        <label className="block space-y-2 text-sm font-semibold text-slate-700">Precio costo · sin IVA
          <input ref={element => { fields.current[2] = element; }} inputMode="decimal" value={cost} onFocus={event => event.currentTarget.select()} onKeyDown={event => moveField(event, 2)} onChange={event => {
            const raw = formatMoneyDraft(event.target.value);
            setCost(raw);
            const marginValue = Number(margin.replace(",", "."));
            if (Number.isFinite(marginValue) && marginValue < 100) {
              economicsCustomized.current = true;
              apply(genericProductFromMargin(parseMoneyDraft(raw), marginValue, line.taxRate));
            }
          }} className="h-12 w-full rounded-xl border border-slate-300 px-3 text-right text-lg font-bold outline-none focus:border-teal-600 focus:ring-4 focus:ring-teal-600/10" />
        </label>
      </div>
      <footer className="flex justify-end gap-2 border-t bg-slate-50 px-6 py-4">
        <button type="button" disabled={busy} onClick={() => void onCancel()} className="h-11 rounded-xl border border-slate-300 px-5 font-semibold">Cancelar</button>
        <button type="submit" disabled={busy || parseMoneyDraft(salePrice) <= 0} className="h-11 rounded-xl bg-teal-700 px-6 font-bold text-white disabled:opacity-50">{busy ? "Agregando…" : "Agregar · Enter"}</button>
      </footer>
    </form>
  </div>;
}
