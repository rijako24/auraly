"use client";

import { CircleDollarSign, PencilLine, ShieldCheck } from "lucide-react";
import { FormEvent, KeyboardEvent as ReactKeyboardEvent, useEffect, useMemo, useRef, useState } from "react";

import type { PosDraftLine, PosDraftLineUpdate } from "@/services/pos/pos-edge-client";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { formatMoneyDraft, formatMoneyValue, parseMoneyDraft } from "./pos-money-input";
import { lineDiscountPercent, lineMarginPercent, nextFocusableIndex, nextGridPosition, prorateAdditionalSaleValue, salePriceForMargin, type GridDirection } from "./pos-line-editor-calculation";

type EditableLine = {
  lineId: string;
  productCode: string;
  quantity: number;
  taxRate: number;
  allowsDocumentCostOverride: boolean;
  description: string;
  unitCost: string;
  margin: string;
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
  const [chargeOpen, setChargeOpen] = useState(false);
  const [additionalCharge, setAdditionalCharge] = useState("");
  const discountInputs = useRef<Array<HTMLInputElement | null>>([]);

  useEffect(() => {
    discountInputs.current[0]?.focus();
    discountInputs.current[0]?.select();
  }, []);

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
    unitPrice: exclusive(parseMoneyDraft(line.unitPrice), line.taxRate),
    discount: exclusive(parseMoneyDraft(line.discount), line.taxRate),
    documentUnitCost: parseMoneyDraft(line.unitCost),
  })), [drafts]);
  const valid = parsed.every((line, index) =>
    Boolean(line.description) &&
    Number.isFinite(line.unitPrice) && line.unitPrice >= 0 &&
    Number.isFinite(line.documentUnitCost) && line.documentUnitCost >= 0 &&
    Number.isFinite(line.discount) && line.discount >= 0 &&
    line.discount <= drafts[index].quantity * line.unitPrice);

  const change = (lineId: string, patch: Partial<EditableLine>) =>
    setDrafts((current) => current.map((line) => line.lineId === lineId ? { ...line, ...patch } : line));

  const changeEconomics = (line: EditableLine, field: "cost" | "margin" | "price" | "discount" | "percentage", raw: string) => {
    const currentPrice = parseMoneyDraft(line.unitPrice);
    const currentDiscount = parseMoneyDraft(line.discount);
    const currentCost = parseMoneyDraft(line.unitCost);
    let price = currentPrice, discount = currentDiscount, cost = currentCost;
    let margin = Number(line.margin.replace(",", ".")) || 0;
    if (field === "cost") cost = parseMoneyDraft(raw);
    if (field === "price") price = parseMoneyDraft(raw);
    if (field === "discount") discount = parseMoneyDraft(raw);
    if (field === "percentage") {
      const value = Number(raw.replace(",", "."));
      if (!Number.isFinite(value) || value < 0 || value > 100) return;
      discount = line.quantity * price * value / 100;
    }
    if (field === "margin") {
      const value = Number(raw.replace(",", "."));
      if (!Number.isFinite(value) || value >= 100) return;
      margin = value;
      const discountPercent = lineDiscountPercent(discount, line.quantity, price);
      price = salePriceForMargin(cost, margin, discountPercent, line.taxRate);
      discount = line.quantity * price * discountPercent / 100;
    } else {
      margin = lineMarginPercent(cost, line.quantity, price, discount, line.taxRate);
    }
    change(line.lineId, {
      unitCost: formatMoneyValue(cost),
      unitPrice: formatMoneyValue(price),
      discount: formatMoneyValue(discount),
      margin: decimalDraft(margin),
    });
  };

  const moveInGrid = (event: ReactKeyboardEvent<HTMLFormElement>) => {
    if (!["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown"].includes(event.key)) return false;
    const current = event.target as HTMLInputElement;
    const row = Number(current.dataset.editorRow);
    const column = Number(current.dataset.editorColumn);
    if (!Number.isInteger(row) || !Number.isInteger(column)) return false;
    event.preventDefault();
    const available = drafts.map((line) => line.allowsDocumentCostOverride ? [0, 1, 2, 3, 4, 5] : [0, 2, 3, 4, 5]);
    const next = nextGridPosition(row, column, available, event.key as GridDirection);
    const target = next ? event.currentTarget.querySelector<HTMLInputElement>(`input[data-editor-row="${next.row}"][data-editor-column="${next.column}"]:not(:disabled)`) : null;
    target?.focus({ preventScroll: true });
    target?.select();
    return true;
  };

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (valid && !busy) await onConfirm(parsed);
  }

  const distributeAdditionalCharge = () => {
    const amount = parseMoneyDraft(additionalCharge);
    if (amount <= 0) return;
    const allocations = new Map(prorateAdditionalSaleValue(
      drafts.map(line => ({
        lineId: line.lineId,
        quantity: line.quantity,
        unitPrice: parseMoneyDraft(line.unitPrice),
      })),
      amount,
    ).map(line => [line.lineId, line]));
    setDrafts(current => current.map(line => {
      const allocation = allocations.get(line.lineId)!;
      const discount = parseMoneyDraft(line.discount);
      const cost = parseMoneyDraft(line.unitCost);
      return {
        ...line,
        unitPrice: preciseMoneyDraft(allocation.unitPrice),
        margin: decimalDraft(lineMarginPercent(
          cost, line.quantity, allocation.unitPrice, discount, line.taxRate)),
      };
    }));
    setAdditionalCharge("");
    setChargeOpen(false);
  };

  return <div className="fixed inset-0 z-[60] flex items-end justify-center bg-slate-950/70 sm:items-center sm:p-4">
    <form role="dialog" aria-modal="true" aria-labelledby="pos-line-editor-title" aria-keyshortcuts="Enter Escape" onSubmit={submit} onKeyDown={(event)=>{
      if (moveInGrid(event)) return;
      if (event.key === "Tab") {
        event.preventDefault();
        const controls = Array.from(event.currentTarget.querySelectorAll<HTMLInputElement | HTMLButtonElement>("input:not(:disabled), button:not(:disabled)"));
        const current = controls.indexOf(document.activeElement as HTMLInputElement | HTMLButtonElement);
        const next = nextFocusableIndex(current, controls.length, event.shiftKey);
        const target = controls[next];
        target?.focus({ preventScroll: true });
        if (target instanceof HTMLInputElement) target.select();
        return;
      }
      if(event.key==="Enter"){
        event.preventDefault();
        event.currentTarget.requestSubmit();
      }
    }} className="flex max-h-[94vh] w-full max-w-6xl flex-col overflow-hidden rounded-t-3xl bg-slate-50 shadow-2xl sm:rounded-3xl">
      <header className="flex shrink-0 items-start justify-between gap-4 bg-gradient-to-r from-slate-950 to-teal-950 px-5 py-5 text-white sm:px-6">
        <div className="flex items-start gap-3"><span className="grid h-11 w-11 shrink-0 place-items-center rounded-2xl bg-white/10 text-teal-200"><PencilLine className="h-5 w-5"/></span><div><p className="text-xs font-bold uppercase tracking-[.18em] text-teal-300">Cambio puntual</p><h2 id="pos-line-editor-title" className="text-xl font-bold">Editar líneas de esta venta</h2><p className="mt-1 text-sm text-slate-300">Nombre, costo para margen, descuento y precio. El producto maestro no se modifica.</p></div></div>
        <span className="hidden items-center gap-2 rounded-full border border-teal-300/30 bg-teal-300/10 px-3 py-1.5 text-xs font-semibold text-teal-100 sm:flex"><ShieldCheck className="h-4 w-4"/>Solo este documento</span>
      </header>
      <div className="min-h-0 flex-1 space-y-3 overflow-y-auto p-4 sm:p-6">
        <section className="flex flex-col gap-4 rounded-2xl border border-cyan-200 bg-gradient-to-r from-cyan-50 to-teal-50 p-4 sm:flex-row sm:items-center sm:justify-between">
          <div className="flex items-start gap-3"><span className="grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-teal-700 text-white"><CircleDollarSign className="h-5 w-5" /></span><div><strong className="text-slate-950">Distribuir cargo adicional</strong><p className="mt-1 text-sm text-slate-600">Suma un valor a la venta y lo reparte por igual por cada unidad, respetando la cantidad de cada línea.</p></div></div>
          <Button type="button" variant="outline" className="shrink-0 border-teal-300 bg-white" onClick={() => setChargeOpen(true)}>Agregar y distribuir</Button>
        </section>
        {drafts.map((line, index) => {
          const price = parseMoneyDraft(line.unitPrice);
          const discount = parseMoneyDraft(line.discount);
          const total = Math.max(0, line.quantity * price - discount);
          const invalidDiscount = discount > line.quantity * price;
          return <article key={line.lineId} className="overflow-hidden rounded-2xl border bg-white shadow-sm">
            <div className="flex items-center justify-between gap-3 border-b bg-slate-50 px-4 py-3"><div><p className="text-xs font-bold uppercase tracking-wide text-teal-700">Línea {index + 1} · {line.productCode}</p><p className="text-xs text-slate-500">Cantidad: {line.quantity}</p></div><strong className="tabular-nums text-slate-950">{formatMoneyValue(total)}</strong></div>
            <div className="grid gap-x-4 gap-y-3 p-4 sm:grid-cols-2 xl:grid-cols-[minmax(240px,2fr)_repeat(5,minmax(120px,1fr))]">
              <label className="space-y-1.5 text-sm font-semibold text-slate-700 sm:col-span-2 xl:col-span-1">Nombre del producto en el documento<input data-editor-row={index} data-editor-column={0} maxLength={250} value={line.description} onChange={(event)=>change(line.lineId,{description:event.target.value})} className="h-11 w-full rounded-xl border border-slate-300 bg-white px-3 font-normal text-slate-950 outline-none focus:border-teal-600 focus:ring-4 focus:ring-teal-600/10"/></label>
              <label className="space-y-1.5 text-sm font-semibold text-slate-700">Costo<input data-editor-row={index} data-editor-column={1} inputMode="decimal" disabled={!line.allowsDocumentCostOverride} value={line.unitCost} onFocus={(event)=>event.currentTarget.select()} onChange={(event)=>changeEconomics(line,"cost",event.target.value)} className="h-11 w-full rounded-xl border border-slate-300 bg-white px-3 text-right font-semibold tabular-nums text-slate-950 outline-none disabled:bg-slate-100 disabled:text-slate-500 focus:border-teal-600 focus:ring-4 focus:ring-teal-600/10"/>{!line.allowsDocumentCostOverride&&<span className="block text-xs font-normal text-slate-500">Definido por inventario.</span>}</label>
              <label className="space-y-1.5 text-sm font-semibold text-slate-700">Margen %<input data-editor-row={index} data-editor-column={2} inputMode="decimal" value={line.margin} onFocus={(event)=>event.currentTarget.select()} onChange={(event)=>changeEconomics(line,"margin",event.target.value)} className="h-11 w-full rounded-xl border border-slate-300 bg-white px-3 text-right font-semibold tabular-nums text-slate-950 outline-none focus:border-teal-600 focus:ring-4 focus:ring-teal-600/10"/></label>
              <label className="space-y-1.5 text-sm font-semibold text-slate-700">Descuento<input data-editor-row={index} data-editor-column={3} ref={(element)=>{discountInputs.current[index]=element;}} inputMode="decimal" value={line.discount} onFocus={(event)=>event.currentTarget.select()} onChange={(event)=>changeEconomics(line,"discount",event.target.value)} className={`h-11 w-full rounded-xl border bg-white px-3 text-right font-semibold tabular-nums text-slate-950 outline-none focus:ring-4 ${invalidDiscount?"border-red-500 focus:ring-red-500/10":"border-slate-300 focus:border-teal-600 focus:ring-teal-600/10"}`}/>{invalidDiscount&&<span className="block text-xs font-normal text-red-700">No puede superar el valor de la línea.</span>}</label>
              <label className="space-y-1.5 text-sm font-semibold text-slate-700">Descuento %<input data-editor-row={index} data-editor-column={4} inputMode="decimal" value={percentage(discount, line.quantity * price)} onFocus={(event)=>event.currentTarget.select()} onChange={(event)=>changeEconomics(line,"percentage",event.target.value)} className="h-11 w-full rounded-xl border border-slate-300 bg-white px-3 text-right font-semibold tabular-nums text-slate-950 outline-none focus:border-teal-600 focus:ring-4 focus:ring-teal-600/10"/></label>
              <label className="space-y-1.5 text-sm font-semibold text-slate-700">Precio de venta<input data-editor-row={index} data-editor-column={5} inputMode="decimal" value={line.unitPrice} onFocus={(event)=>event.currentTarget.select()} onChange={(event)=>changeEconomics(line,"price",event.target.value)} className="h-11 w-full rounded-xl border border-slate-300 bg-white px-3 text-right font-semibold tabular-nums text-slate-950 outline-none focus:border-teal-600 focus:ring-4 focus:ring-teal-600/10"/></label>
            </div>
          </article>;
        })}
      </div>
      <footer className="flex shrink-0 flex-col gap-3 border-t bg-white px-4 py-4 sm:flex-row sm:items-center sm:justify-between sm:px-6">
        <div className="flex flex-wrap items-center gap-2 text-xs font-semibold text-slate-600" aria-label="Atajos del editor">
          <span className="rounded-lg bg-slate-100 px-2.5 py-1.5"><kbd>←</kbd><kbd>↑</kbd><kbd>↓</kbd><kbd>→</kbd> campos y líneas</span>
          <span className="rounded-lg bg-slate-100 px-2.5 py-1.5"><kbd>Tab</kbd> campos</span>
          <span className="rounded-lg bg-teal-50 px-2.5 py-1.5 text-teal-800"><kbd>Enter</kbd> aplicar todo</span>
          <span className="rounded-lg bg-slate-100 px-2.5 py-1.5"><kbd>Esc</kbd> cerrar</span>
        </div>
        <div className="flex flex-col-reverse gap-2 sm:flex-row">
          <button type="button" aria-keyshortcuts="Escape" onClick={onCancel} disabled={busy} className="h-11 rounded-xl border border-slate-300 px-5 font-semibold text-slate-700">Cancelar <span className="ml-2 text-xs text-slate-400">Esc</span></button>
          <button type="submit" aria-keyshortcuts="Enter" disabled={busy||!valid} className="h-11 rounded-xl bg-teal-700 px-6 font-bold text-white shadow-sm disabled:opacity-50">{busy?"Guardando…":<>Aplicar cambios <span className="ml-2 text-xs text-teal-100">Enter</span></>}</button>
        </div>
      </footer>
    </form>
    <Dialog open={chargeOpen} onOpenChange={setChargeOpen}>
      <DialogContent className="max-w-md" onKeyDown={event => event.stopPropagation()}>
        <DialogHeader><DialogTitle>Distribuir cargo adicional</DialogTitle><DialogDescription>El valor se dividirá entre la suma de todas las cantidades y aumentará el precio de venta unitario de cada línea.</DialogDescription></DialogHeader>
        <div className="space-y-2"><Label htmlFor="additional-sale-charge">Valor total que deseas agregar</Label><Input id="additional-sale-charge" autoFocus inputMode="decimal" value={additionalCharge} onChange={event => setAdditionalCharge(formatMoneyDraft(event.target.value))} onKeyDown={event => { if (event.key === "Enter") { event.preventDefault(); distributeAdditionalCharge(); } }} placeholder="0" className="h-12 text-lg font-bold" /></div>
        <div className="rounded-xl bg-slate-50 p-3 text-sm text-slate-700">Cantidad total: <strong>{drafts.reduce((sum, line) => sum + line.quantity, 0)}</strong>{parseMoneyDraft(additionalCharge) > 0 && <> · Incremento por unidad: <strong>{formatMoneyValue(parseMoneyDraft(additionalCharge) / drafts.reduce((sum, line) => sum + line.quantity, 0))}</strong></>}</div>
        <div className="flex justify-end gap-2"><Button type="button" variant="outline" onClick={() => setChargeOpen(false)}>Cancelar</Button><Button type="button" disabled={parseMoneyDraft(additionalCharge) <= 0} onClick={distributeAdditionalCharge}>Distribuir en las líneas</Button></div>
      </DialogContent>
    </Dialog>
  </div>;
}

function toEditable(line: PosDraftLine): EditableLine {
  return {
    lineId: line.lineId,
    productCode: line.productCode,
    quantity: line.quantity,
    taxRate: line.taxRate,
    allowsDocumentCostOverride: line.allowsDocumentCostOverride,
    description: line.description,
    unitCost: formatMoneyValue(line.documentUnitCost),
    unitPrice: formatMoneyValue(inclusive(line.unitPrice, line.taxRate)),
    discount: formatMoneyValue(inclusive(line.discount, line.taxRate)),
    margin: decimalDraft(lineMarginPercent(line.documentUnitCost, line.quantity, inclusive(line.unitPrice, line.taxRate), inclusive(line.discount, line.taxRate), line.taxRate)),
  };
}

function inclusive(value: number, taxRate: number) { return value * (1 + taxRate / 100); }
function exclusive(value: number, taxRate: number) { return value / (1 + taxRate / 100); }
function decimalDraft(value: number) { return String(Math.round(value * 100) / 100).replace(".", ","); }
function preciseMoneyDraft(value: number) {
  return new Intl.NumberFormat("es-CO", {
    useGrouping: true,
    minimumFractionDigits: 0,
    maximumFractionDigits: 6,
  }).format(value);
}

function percentage(discount: number, maximum: number): string {
  if (!maximum) return "0";
  return String(Math.round(discount * 10_000 / maximum) / 100);
}
