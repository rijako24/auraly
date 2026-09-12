"use client";

import { AlertTriangle, CheckCircle2, Loader2, LockKeyhole, PackageCheck, Save, Trash2 } from "lucide-react";
import { useMemo, useState } from "react";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import type { CommerceOrderDetail } from "@/services/orders/commerce-orders-client";
import { evaluateOrderReviewQuantity, isOrderReviewLinePending } from "./order-review";

export type ReviewOrderLineInput = {
  productId: string;
  quantity: number;
  unitPrice: number;
  discountAmount: number;
  priceSource: string;
};

type Props = {
  order: CommerceOrderDetail;
  onClose: () => void;
  onConfirm: (lines: ReviewOrderLineInput[]) => Promise<void>;
};

const quantity = new Intl.NumberFormat("es-CO", { maximumFractionDigits: 3 });

export function OrderReviewEditor({ order, onClose, onConfirm }: Props) {
  const [values, setValues] = useState<Record<string, string>>(() =>
    Object.fromEntries(order.lines.map((line) => [line.orderItemId, String(line.quantity)])),
  );
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [removed, setRemoved] = useState<Set<string>>(() => new Set());
  const evaluations = useMemo(() => order.lines.map((line) => {
    const evaluation = evaluateOrderReviewQuantity(
      values[line.orderItemId] ?? "",
      line.manageStock,
      line.quantityOnHand,
      line.reservedQuantity,
    );
    return {
      line,
      next: evaluation.quantity,
      validNumber: evaluation.validNumber,
      insufficient: evaluation.insufficient,
      editable: isOrderReviewLinePending(line.manageStock,line.quantity,line.reservedQuantity),
      removed: removed.has(line.orderItemId),
    };
  }), [order.lines, removed, values]);
  const problemCount = evaluations.filter((value) => value.editable && !value.removed).length;
  const remaining = evaluations.filter((value) => !value.removed);
  const invalid = remaining.length===0||remaining.some((value) => !value.line.productId || (value.editable && (!value.validNumber || value.insufficient)));

  async function confirm() {
    if (invalid || busy) return;
    setBusy(true);
    setError(null);
    try {
      await onConfirm(remaining.map(({ line, next }) => ({
        productId: line.productId!,
        quantity: next,
        unitPrice: line.unitPrice,
        discountAmount: line.discountAmount,
        priceSource: line.priceSource,
      })));
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "No fue posible confirmar el pedido.");
    } finally {
      setBusy(false);
    }
  }

  return <Dialog open onOpenChange={(open) => !open && !busy && onClose()}>
    <DialogContent className="flex max-h-[92dvh] w-[calc(100%-1.5rem)] max-w-4xl flex-col overflow-hidden rounded-3xl p-0">
      <DialogHeader className="border-b bg-gradient-to-r from-slate-950 via-teal-950 to-slate-950 px-6 py-5 text-left text-white">
        <div className="flex items-start justify-between gap-4 pr-8">
          <div>
            <DialogTitle className="text-xl text-white">Revisar existencias · {order.orderNumber}</DialogTitle>
            <DialogDescription className="mt-1 text-slate-300">Ajusta únicamente las cantidades. Los precios y descuentos del pedido se conservan exactamente.</DialogDescription>
          </div>
          <Badge className={problemCount ? "bg-amber-300 text-amber-950" : "bg-emerald-300 text-emerald-950"}>
            {problemCount ? `${problemCount} con novedad` : "Todo disponible"}
          </Badge>
        </div>
      </DialogHeader>
      <div className="min-h-0 flex-1 space-y-3 overflow-y-auto bg-slate-50 p-4 sm:p-6">
        {evaluations.map(({ line, validNumber, insufficient, editable, removed: isRemoved }) => {
          if(isRemoved)return <article key={line.orderItemId} className="flex items-center justify-between gap-3 rounded-2xl border border-dashed border-slate-300 bg-white/70 p-4 text-slate-600"><span><strong className="block">{line.productName}</strong><small>Se eliminará del pedido al confirmar.</small></span><Button type="button" variant="outline" disabled={busy} onClick={()=>setRemoved((current)=>{const next=new Set(current);next.delete(line.orderItemId);return next;})}>Deshacer</Button></article>;
          const problem = !line.productId || (editable&&(!validNumber || insufficient));
          return <article key={line.orderItemId} className={`rounded-2xl border bg-white p-4 shadow-sm transition ${problem ? "border-amber-300 ring-2 ring-amber-100" : "border-slate-200"}`}>
            <div className="grid items-center gap-4 md:grid-cols-[minmax(0,1fr)_120px_135px_185px]">
              <div className="min-w-0">
                <div className="flex items-center gap-2">
                  {editable ? <AlertTriangle className="h-5 w-5 shrink-0 text-amber-600" /> : <CheckCircle2 className="h-5 w-5 shrink-0 text-emerald-600" />}
                  <strong className="truncate text-slate-950">{line.productName}</strong>
                </div>
                <p className="mt-1 pl-7 text-xs text-slate-500">{line.productCode || line.sku || "Sin código"} · {line.unitCode}</p>
              </div>
              <Metric label="Pidieron" value={quantity.format(line.quantity)} />
              <Metric label="Existencia actual" value={line.manageStock ? quantity.format(line.quantityOnHand) : "No controla"} warn={insufficient} />
              {editable ? <div className="flex items-end gap-2"><label className="min-w-0 flex-1">
                <span className="mb-1 block text-xs font-bold uppercase tracking-wide text-slate-500">Nueva cantidad</span>
                <Input
                  aria-label={`Nueva cantidad para ${line.productName}`}
                  className={`h-11 rounded-xl text-right text-lg font-black tabular-nums ${problem ? "border-amber-400 focus-visible:ring-amber-300" : "border-teal-300"}`}
                  inputMode="decimal"
                  min="0.001"
                  max={Math.min(line.quantity,line.reservedQuantity+line.quantityOnHand)}
                  step="any"
                  value={values[line.orderItemId] ?? ""}
                  onFocus={(event) => event.currentTarget.select()}
                  onChange={(event) => setValues((current) => ({ ...current, [line.orderItemId]: event.target.value }))}
                />
              </label><Button type="button" size="icon" variant="outline" className="h-11 w-11 shrink-0 border-red-200 text-red-700 hover:bg-red-50" aria-label={`Eliminar ${line.productName}`} disabled={busy} onClick={()=>setRemoved((current)=>new Set(current).add(line.orderItemId))}><Trash2 className="h-4 w-4"/></Button></div>:<div className="rounded-xl bg-emerald-50 px-3 py-2 text-emerald-900"><span className="flex items-center gap-1 text-[11px] font-bold uppercase tracking-wide"><LockKeyhole className="h-3.5 w-3.5"/>Ya reservado</span><strong className="mt-0.5 block text-lg tabular-nums">{quantity.format(line.reservedQuantity)}</strong></div>}
            </div>
            {editable&&<p className="mt-3 rounded-xl bg-amber-50 px-3 py-2 text-sm font-semibold text-amber-900">Pidieron {quantity.format(line.quantity)} y quedaron {quantity.format(line.quantity-line.reservedQuantity)} pendientes. Puedes dejar hasta {quantity.format(Math.min(line.quantity,line.reservedQuantity+line.quantityOnHand))} o eliminar esta línea.</p>}
            {editable&&!validNumber && <p className="mt-3 text-sm font-semibold text-red-700">Ingresa una cantidad mayor que cero o elimina la línea.</p>}
            {!line.productId && <p className="mt-3 text-sm font-semibold text-red-700">Este detalle no está vinculado a un producto y requiere corrección administrativa.</p>}
          </article>;
        })}
        {error && <p role="alert" className="rounded-2xl border border-red-200 bg-red-50 p-4 text-sm font-semibold text-red-800">{error}</p>}
      </div>
      <DialogFooter className="border-t bg-white px-4 py-4 sm:px-6">
        <div className="flex w-full flex-col-reverse gap-2 sm:flex-row sm:justify-between">
          <Button type="button" variant="outline" disabled={busy} onClick={onClose}>Salir sin cambios</Button>
          <Button type="button" className="bg-teal-700 hover:bg-teal-800" disabled={invalid || busy} onClick={() => void confirm()}>
            {busy ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : problemCount ? <Save className="mr-2 h-4 w-4" /> : <PackageCheck className="mr-2 h-4 w-4" />}
            Confirmar y reservar inventario
          </Button>
        </div>
      </DialogFooter>
    </DialogContent>
  </Dialog>;
}

function Metric({ label, value, warn = false }: { label: string; value: string; warn?: boolean }) {
  return <div className={`rounded-xl px-3 py-2 ${warn ? "bg-amber-100 text-amber-950" : "bg-slate-100 text-slate-800"}`}><span className="block text-[11px] font-bold uppercase tracking-wide opacity-70">{label}</span><strong className="mt-0.5 block text-lg tabular-nums">{value}</strong></div>;
}
