"use client";

import { useEffect, useState } from "react";
import { AlertTriangle, PackageCheck, RefreshCw, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import type { PosInventoryValidation } from "@/services/pos/pos-edge-client";

export function PosInventoryResolutionDialog({ value, busy, onChangeQuantity, onRemove, onRetry }: {
  value: PosInventoryValidation;
  busy: boolean;
  onChangeQuantity: (lineId: string, quantity: number) => Promise<void>;
  onRemove: (lineId: string, productName: string) => void;
  onRetry: () => Promise<void>;
}) {
  const [quantities, setQuantities] = useState<Record<string, string>>({});
  useEffect(() => setQuantities(Object.fromEntries(value.issues.map((issue) => [issue.lineId, String(issue.availableQuantity)]))), [value]);

  return <Dialog open>
    <DialogContent className="max-h-[92vh] max-w-2xl overflow-y-auto p-0" onEscapeKeyDown={(event) => event.preventDefault()} onPointerDownOutside={(event) => event.preventDefault()}>
      <div className="bg-gradient-to-br from-slate-950 via-slate-900 to-teal-950 px-6 py-5 text-white">
        <span className="mb-3 grid h-12 w-12 place-items-center rounded-2xl bg-amber-400/15 text-amber-300"><PackageCheck className="h-6 w-6" /></span>
        <DialogHeader><DialogTitle className="text-xl text-white">El inventario cambió mientras la venta estaba en espera</DialogTitle><DialogDescription className="text-slate-300">Ajusta las cantidades disponibles o elimina los productos sin saldo. La venta no podrá cobrarse hasta quedar válida.</DialogDescription></DialogHeader>
      </div>
      <div className="space-y-3 p-6">
        {!value.wasValidated ? <div className="rounded-2xl border border-amber-200 bg-amber-50 p-5 text-amber-950"><div className="flex gap-3"><AlertTriangle className="mt-0.5 h-5 w-5 shrink-0" /><div><p className="font-semibold">No fue posible consultar el inventario actual</p><p className="mt-1 text-sm">Conecta la caja y vuelve a validar antes de continuar esta venta recuperada.</p></div></div></div> : value.issues.map((issue) => <article key={issue.lineId} className="rounded-2xl border bg-card p-4 shadow-sm">
          <div className="flex flex-col justify-between gap-2 sm:flex-row sm:items-start"><div><p className="font-semibold text-slate-950">{issue.description}</p><p className="text-xs text-muted-foreground">{issue.productCode}</p></div><span className="w-fit rounded-full bg-red-50 px-3 py-1 text-xs font-bold text-red-700">Solicitadas {issue.requestedQuantity} · disponibles {issue.availableQuantity}</span></div>
          <div className="mt-4 grid gap-2 sm:grid-cols-[minmax(0,1fr)_auto_auto] sm:items-end"><label className="space-y-1.5 text-sm font-medium">Nueva cantidad<Input inputMode="decimal" value={quantities[issue.lineId] ?? ""} onChange={(event) => setQuantities((current) => ({ ...current, [issue.lineId]: event.target.value }))} /></label><Button type="button" disabled={busy || !(Number(quantities[issue.lineId]) > 0) || Number(quantities[issue.lineId]) > issue.availableQuantity} onClick={() => onChangeQuantity(issue.lineId, Number(quantities[issue.lineId]))}>Aplicar cantidad</Button><Button type="button" variant="outline" className="text-red-700" disabled={busy} onClick={() => onRemove(issue.lineId, issue.description)}><Trash2 className="mr-2 h-4 w-4" />Eliminar</Button></div>
        </article>)}
      </div>
      <DialogFooter className="border-t bg-muted/20 px-6 py-4"><Button type="button" variant="outline" disabled={busy} onClick={() => void onRetry()}><RefreshCw className={`mr-2 h-4 w-4 ${busy ? "animate-spin" : ""}`} />Volver a validar</Button></DialogFooter>
    </DialogContent>
  </Dialog>;
}
