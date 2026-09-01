"use client";

import { FormEvent, useEffect, useRef, useState } from "react";
import { AlertTriangle, PackageX } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { acceptsPosQuantityDraft, blocksPosQuantityKey, validatePosQuantity } from "./pos-quantity-validation";

export type PosQuantityShortage = {
  lineId: string | null;
  captureValue?: string | null;
  productName: string;
  requestedQuantity: number;
  availableQuantity: number;
  maximumLineQuantity: number;
  allowsFractionalSale: boolean;
  managesInventory: boolean;
};

export function PosQuantityAvailabilityDialog({ value, busy, onConfirm, onCancel }: {
  value: PosQuantityShortage;
  busy: boolean;
  onConfirm: (quantity: number) => Promise<void>;
  onCancel: () => void;
}) {
  const canApplyQuantity = value.maximumLineQuantity > 0;
  const [quantity, setQuantity] = useState(String(value.maximumLineQuantity));
  const input = useRef<HTMLInputElement>(null);
  const acknowledgeButton = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    setQuantity(String(value.maximumLineQuantity));
    window.requestAnimationFrame(() => {
      if (canApplyQuantity) {
        input.current?.focus();
        input.current?.select();
      } else {
        acknowledgeButton.current?.focus();
      }
    });
  }, [canApplyQuantity, value]);
  const rules = { allowsFractionalSale: value.allowsFractionalSale, managesInventory: value.managesInventory, maximumQuantity: value.maximumLineQuantity };
  const validation = validatePosQuantity(quantity, rules);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (validation.valid && !busy) await onConfirm(validation.quantity);
  }

  return <Dialog open onOpenChange={(open) => { if (!open && !busy) onCancel(); }}>
    <DialogContent onOpenAutoFocus={(event) => {
      event.preventDefault();
      if (canApplyQuantity) {
        input.current?.focus();
        input.current?.select();
      } else {
        acknowledgeButton.current?.focus();
      }
    }} data-pos-focus-surface="modal" className="w-[calc(100%-1.5rem)] overflow-hidden rounded-3xl p-0 sm:max-w-lg">
      <form onSubmit={submit}>
        <div className="bg-gradient-to-br from-slate-950 via-slate-900 to-teal-950 px-6 py-6 text-white">
          <span className="mb-4 grid h-12 w-12 place-items-center rounded-2xl bg-amber-400/15 text-amber-300"><PackageX className="h-6 w-6" /></span>
          <DialogHeader><DialogTitle className="text-xl text-white">No alcanzan las existencias</DialogTitle><DialogDescription className="text-slate-300">Pediste {value.requestedQuantity}, pero hay {value.availableQuantity} disponibles de {value.productName}. {value.lineId ? "La venta todavía conserva la cantidad anterior." : "El producto no fue agregado a la venta."}</DialogDescription></DialogHeader>
        </div>
        <div className="space-y-5 p-6">
          <div className="grid grid-cols-2 gap-3">
            <div className="rounded-2xl border border-red-200 bg-red-50 p-4"><p className="text-xs font-semibold uppercase tracking-wide text-red-700">Cantidad escrita</p><p className="mt-1 text-2xl font-black tabular-nums text-red-950">{value.requestedQuantity}</p></div>
            <div className="rounded-2xl border border-emerald-200 bg-emerald-50 p-4"><p className="text-xs font-semibold uppercase tracking-wide text-emerald-700">Puedes dejar</p><p className="mt-1 text-2xl font-black tabular-nums text-emerald-950">{value.maximumLineQuantity}</p></div>
          </div>
          {canApplyQuantity && <><div className="space-y-2"><Label htmlFor="available-quantity">Corrige la cantidad</Label><Input ref={input} id="available-quantity" className="h-16 rounded-2xl text-center text-3xl font-black tabular-nums" inputMode={value.allowsFractionalSale ? "decimal" : "numeric"} value={quantity} onFocus={(event) => event.currentTarget.select()} onKeyDown={(event) => { if (blocksPosQuantityKey(event.key, rules)) event.preventDefault(); }} onChange={(event) => { if (acceptsPosQuantityDraft(event.target.value, rules)) setQuantity(event.target.value.replace(",", ".")); }} aria-describedby="available-quantity-help" /></div>
          {!validation.valid && quantity !== "" && <p className="flex gap-2 rounded-xl border border-amber-200 bg-amber-50 p-3 text-sm text-amber-900"><AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />Escribe una cantidad mayor que cero y máximo {value.maximumLineQuantity}{value.allowsFractionalSale ? "." : " en unidades completas."}</p>}
          <p id="available-quantity-help" className="text-xs text-muted-foreground">La cantidad disponible quedó seleccionada para que puedas reemplazarla de inmediato. Esc cierra sin cambiar la venta.</p></>}
          {!canApplyQuantity && <p className="rounded-xl border border-amber-200 bg-amber-50 p-3 text-sm text-amber-900">No hay una cantidad disponible que se pueda agregar en este momento.</p>}
        </div>
        {canApplyQuantity ? <DialogFooter className="grid grid-cols-2 gap-2 border-t bg-muted/20 px-6 py-4 sm:grid-cols-2"><Button type="button" variant="outline" disabled={busy} onClick={onCancel}>Cancelar</Button><Button type="submit" className="bg-teal-700 hover:bg-teal-800" disabled={!validation.valid || busy}>{busy ? "Aplicando…" : "Aplicar cantidad"}</Button></DialogFooter> : <DialogFooter className="border-t bg-muted/20 px-6 py-4"><Button ref={acknowledgeButton} type="button" className="w-full bg-teal-700 hover:bg-teal-800" disabled={busy} onClick={onCancel}>Entendido</Button></DialogFooter>}
      </form>
    </DialogContent>
  </Dialog>;
}
