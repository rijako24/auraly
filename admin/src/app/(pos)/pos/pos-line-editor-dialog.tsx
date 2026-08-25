"use client";

import { PencilLine, ShieldCheck } from "lucide-react";
import { FormEvent, useEffect, useMemo, useState } from "react";

import type { PosDraftLine, PosDraftLineUpdate } from "@/services/pos/pos-edge-client";
import { formatMoneyDraft, formatMoneyValue, parseMoneyDraft } from "./pos-money-input";

type EditableLine = {
  lineId: string;
  productCode: string;
  quantity: number;
  description: string;
  unitPrice: string;
  discount: string;
};

export function PosLineEditorDialog({
  lines,
  busy,
  onConfirm,
  onCancel,
}: {
  lines: PosDraftLine[];
  busy: boolean;
  onConfirm: (updates: PosDraftLineUpdate[]) => Promise<void>;
  onCancel: () => void;
}) {
  const [drafts, setDrafts] = useState<EditableLine[]>(() => lines.map(toEditable));

  useEffect(() => {
    const close = (event: KeyboardEvent) => {
      if (event.key === "Escape" && !busy) {
        event.preventDefault();
        onCancel();
      }
    };
    window.addEventListener("keydown", close);
    return () => window.removeEventListener("keydown", close);
  }, [busy, onCancel]);

  const parsed = useMemo(() => drafts.map((line) => ({
    lineId: line.lineId,
    description: line.description.trim(),
    unitPrice: parseMoneyDraft(line.unitPrice),
    discount: parseMoneyDraft(line.discount),
  })), [drafts]);
  const valid = parsed.every((line, index) =>
    Boolean(line.description) &&
    Number.isFinite(line.unitPrice) && line.unitPrice >= 0 &&
    Number.isFinite(line.discount) && line.discount >= 0 &&
    line.discount <= drafts[index].quantity * line.unitPrice);

  const change = (lineId: string, patch: Partial<EditableLine>) =>
    setDrafts((current) => current.map((line) => line.lineId === lineId ? { ...line, ...patch } : line));

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (valid && !busy) await onConfirm(parsed);
  }

  return <div className="fixed inset-0 z-[60] flex items-end justify-center bg-slate-950/70 sm:items-center sm:p-4">
    <form onSubmit={submit} className="flex max-h-[94vh] w-full max-w-6xl flex-col overflow-hidden rounded-t-3xl bg-slate-50 shadow-2xl sm:rounded-3xl">
      <header className="flex shrink-0 items-start justify-between gap-4 bg-gradient-to-r from-slate-950 to-teal-950 px-5 py-5 text-white sm:px-6">
        <div className="flex items-start gap-3"><span className="grid h-11 w-11 shrink-0 place-items-center rounded-2xl bg-white/10 text-teal-200"><PencilLine className="h-5 w-5"/></span><div><p className="text-xs font-bold uppercase tracking-[.18em] text-teal-300">Cambio puntual</p><h2 className="text-xl font-bold">Editar líneas de esta venta</h2><p className="mt-1 text-sm text-slate-300">Descripción, precio de venta y descuento. El catálogo y el costo contable no se modifican.</p></div></div>
        <span className="hidden items-center gap-2 rounded-full border border-teal-300/30 bg-teal-300/10 px-3 py-1.5 text-xs font-semibold text-teal-100 sm:flex"><ShieldCheck className="h-4 w-4"/>Solo este documento</span>
      </header>
      <div className="min-h-0 flex-1 space-y-3 overflow-y-auto p-4 sm:p-6">
        {drafts.map((line, index) => {
          const price = parseMoneyDraft(line.unitPrice);
          const discount = parseMoneyDraft(line.discount);
          const total = Math.max(0, line.quantity * price - discount);
          const invalidDiscount = discount > line.quantity * price;
          return <article key={line.lineId} className="overflow-hidden rounded-2xl border bg-white shadow-sm">
            <div className="flex items-center justify-between gap-3 border-b bg-slate-50 px-4 py-3"><div><p className="text-xs font-bold uppercase tracking-wide text-teal-700">Línea {index + 1} · {line.productCode}</p><p className="text-xs text-slate-500">Cantidad: {line.quantity}</p></div><strong className="tabular-nums text-slate-950">{formatMoneyValue(total)}</strong></div>
            <div className="grid gap-x-4 gap-y-3 p-4 sm:grid-cols-2 xl:grid-cols-[minmax(260px,2fr)_minmax(145px,1fr)_minmax(145px,1fr)_minmax(110px,.7fr)]">
              <label className="space-y-1.5 text-sm font-semibold text-slate-700">Descripción en el documento<input autoFocus={index===0} maxLength={250} value={line.description} onChange={(event)=>change(line.lineId,{description:event.target.value})} className="h-11 w-full rounded-xl border border-slate-300 bg-white px-3 font-normal text-slate-950 outline-none focus:border-teal-600 focus:ring-4 focus:ring-teal-600/10"/></label>
              <label className="space-y-1.5 text-sm font-semibold text-slate-700">Precio de venta<input inputMode="decimal" value={line.unitPrice} onFocus={(event)=>event.currentTarget.select()} onChange={(event)=>change(line.lineId,{unitPrice:formatMoneyDraft(event.target.value)})} className="h-11 w-full rounded-xl border border-slate-300 bg-white px-3 text-right font-semibold tabular-nums text-slate-950 outline-none focus:border-teal-600 focus:ring-4 focus:ring-teal-600/10"/></label>
              <label className="space-y-1.5 text-sm font-semibold text-slate-700">Descuento<input inputMode="decimal" value={line.discount} onFocus={(event)=>event.currentTarget.select()} onChange={(event)=>change(line.lineId,{discount:formatMoneyDraft(event.target.value)})} className={`h-11 w-full rounded-xl border bg-white px-3 text-right font-semibold tabular-nums text-slate-950 outline-none focus:ring-4 ${invalidDiscount?"border-red-500 focus:ring-red-500/10":"border-slate-300 focus:border-teal-600 focus:ring-teal-600/10"}`}/>{invalidDiscount&&<span className="block text-xs font-normal text-red-700">No puede superar el valor de la línea.</span>}</label>
              <label className="space-y-1.5 text-sm font-semibold text-slate-700">Descuento %<input inputMode="decimal" value={percentage(discount, line.quantity * price)} onFocus={(event)=>event.currentTarget.select()} onChange={(event)=>{const value=Number(event.target.value.replace(",","."));if(Number.isFinite(value)&&value>=0&&value<=100)change(line.lineId,{discount:formatMoneyValue(line.quantity*price*value/100)})}} className="h-11 w-full rounded-xl border border-slate-300 bg-white px-3 text-right font-semibold tabular-nums text-slate-950 outline-none focus:border-teal-600 focus:ring-4 focus:ring-teal-600/10"/></label>
            </div>
          </article>;
        })}
      </div>
      <footer className="flex shrink-0 flex-col-reverse gap-2 border-t bg-white px-4 py-4 sm:flex-row sm:justify-end sm:px-6"><button type="button" onClick={onCancel} disabled={busy} className="h-11 rounded-xl border border-slate-300 px-5 font-semibold text-slate-700">Cancelar</button><button type="submit" disabled={busy||!valid} className="h-11 rounded-xl bg-teal-700 px-6 font-bold text-white shadow-sm disabled:opacity-50">{busy?"Guardando…":"Guardar cambios de la venta"}</button></footer>
    </form>
  </div>;
}

function toEditable(line: PosDraftLine): EditableLine {
  return {
    lineId: line.lineId,
    productCode: line.productCode,
    quantity: line.quantity,
    description: line.description,
    unitPrice: formatMoneyValue(line.unitPrice),
    discount: formatMoneyValue(line.discount),
  };
}

function percentage(discount: number, maximum: number): string {
  if (!maximum) return "0";
  return String(Math.round(discount * 10_000 / maximum) / 100);
}
