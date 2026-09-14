"use client";

import { useRef, useState } from "react";
import { Coins, Printer } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { normalizeWorkSessionCountInput } from "@/services/pos/pos-work-session-close";
import { usePosReferenceOptions, type PosReferenceOptionsClient } from "./use-pos-reference-options";

const money = new Intl.NumberFormat("es-CO", {
  style: "currency",
  currency: "COP",
  maximumFractionDigits: 0,
});

export function PosCashDenominationDialog({ client, businessName, userName, onClose }: {
  client: PosReferenceOptionsClient;
  businessName: string;
  userName: string;
  onClose: () => void;
}) {
  const denominations = usePosReferenceOptions(client, "cash-denomination");
  const [quantities, setQuantities] = useState<Record<string, string>>({});
  const [printing, setPrinting] = useState(false);
  const [printError, setPrintError] = useState<string | null>(null);
  const denominationInputs = useRef(new Map<string, HTMLInputElement>());
  const rows = denominations.data ?? [];
  const moveDenomination = (code: string, key: string) => {
    const current = rows.findIndex((row) => row.code === code);
    const step = key === "ArrowLeft" ? -1 : key === "ArrowRight" ? 1 : key === "ArrowUp" ? -2 : key === "ArrowDown" ? 2 : 0;
    if (current < 0 || step === 0 || rows.length === 0) return false;
    const next = (current + step + rows.length) % rows.length;
    const target = denominationInputs.current.get(rows[next].code);
    target?.focus();
    target?.select();
    return true;
  };
  const total = rows.reduce((sum, denomination) => {
    const value = Number(denomination.code);
    const quantity = Number(quantities[denomination.code] ?? 0);
    return sum + (Number.isFinite(value * quantity) ? value * quantity : 0);
  }, 0);
  const countedLines = rows.map((denomination) => ({
    label: denomination.label,
    value: Number(denomination.code),
    quantity: Number(quantities[denomination.code] ?? 0),
    subtotal: Number(denomination.code) * Number(quantities[denomination.code] ?? 0),
  })).filter((line) => line.quantity > 0);

  const printCount = async () => {
    setPrinting(true);
    setPrintError(null);
    try {
      await client.printCashDenominationCount({
        businessName,
        userName,
        countedAt: new Date().toISOString(),
        lines: countedLines,
        total,
      });
    } catch (caught) {
      setPrintError(caught instanceof Error ? caught.message : "No fue posible imprimir el conteo.");
    } finally {
      setPrinting(false);
    }
  };

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-h-[90vh] max-w-2xl overflow-y-auto p-0">
        <DialogHeader className="border-b bg-gradient-to-r from-teal-50 to-cyan-50 px-6 py-5 text-left">
          <div className="flex items-center gap-3">
            <span className="grid h-11 w-11 place-items-center rounded-2xl bg-teal-600 text-white shadow-sm"><Coins className="h-5 w-5" /></span>
            <div><DialogTitle>Calculadora de denominaciones</DialogTitle><DialogDescription>Indica cuántas monedas y billetes tienes. Auraly hará la suma y puede imprimir el conteo.</DialogDescription></div>
          </div>
        </DialogHeader>
        <div className="space-y-5 p-6">
          {denominations.isLoading && <p className="text-sm text-slate-500">Cargando denominaciones…</p>}
          {denominations.isError && <p className="rounded-xl border border-red-200 bg-red-50 p-3 text-sm text-red-800">No fue posible cargar las denominaciones configuradas.</p>}
          {(["Billete", "Moneda"] as const).map((kind) => {
            const group = rows.filter((item) => item.description === kind);
            if (!group.length) return null;
            return <section key={kind} className="space-y-3"><h3 className="text-sm font-bold uppercase tracking-wide text-slate-500">{kind}s</h3><div className="grid gap-3 sm:grid-cols-2">{group.map((denomination) => {
              const quantity = quantities[denomination.code] ?? "";
              const subtotal = Number(denomination.code) * Number(quantity || 0);
              return <label key={denomination.id} className="flex items-center gap-3 rounded-2xl border bg-white p-3 shadow-sm transition focus-within:border-teal-500 focus-within:ring-2 focus-within:ring-teal-100"><span className="min-w-20 font-bold text-slate-800">{denomination.label}</span><span className="text-slate-400">×</span><Input ref={(element) => { if (element) denominationInputs.current.set(denomination.code, element); else denominationInputs.current.delete(denomination.code); }} aria-label={`Cantidad de ${denomination.label}`} inputMode="numeric" value={quantity} onKeyDown={(event) => { if (moveDenomination(denomination.code, event.key)) event.preventDefault(); }} onChange={(event) => setQuantities((current) => ({ ...current, [denomination.code]: normalizeWorkSessionCountInput(event.target.value) }))} className="h-10 w-20 text-center font-bold" placeholder="0"/><strong className="ml-auto text-sm tabular-nums text-teal-800">{money.format(subtotal)}</strong></label>;
            })}</div></section>;
          })}
          <div className="sticky bottom-0 flex items-center justify-between rounded-2xl bg-slate-950 p-5 text-white shadow-xl"><span><small className="block text-slate-300">Efectivo contado</small><strong className="text-sm">Total calculado</strong></span><strong className="text-3xl tabular-nums text-teal-300">{money.format(total)}</strong></div>
          {printError && <p className="rounded-xl border border-red-200 bg-red-50 p-3 text-sm text-red-800">{printError}</p>}
        </div>
        <footer className="flex flex-wrap justify-end gap-2 border-t bg-slate-50 px-6 py-4"><Button type="button" variant="outline" onClick={onClose}>Cerrar</Button><Button type="button" disabled={!countedLines.length || printing} onClick={() => void printCount()}><Printer className="mr-2 h-4 w-4" />{printing ? "Imprimiendo…" : "Imprimir conteo"}</Button></footer>
      </DialogContent>
    </Dialog>
  );
}
