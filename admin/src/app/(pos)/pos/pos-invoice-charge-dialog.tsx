"use client";

import { useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Loader2, ReceiptText, Trash2, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import type { AddInvoiceCharge, InvoiceCharge } from "@/services/api/invoice-charges";
import type { PosClient, PosDraft } from "@/services/pos/pos-edge-client";
import { usePosModalBehavior } from "./use-pos-modal-behavior";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 2 });

export function PosInvoiceChargeDialog({ client, draft, onUpdated, onClose }: {
  client: PosClient; draft: PosDraft; onUpdated: (draft: PosDraft) => void; onClose: () => void;
}) {
  const modal = useRef<HTMLDivElement>(null);
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<InvoiceCharge | null>(null);
  const [supplierId, setSupplierId] = useState("");
  const [manualAmount, setManualAmount] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Only the visible modal owns this paged catalog. Each opening reads current
  // web configuration or the local Edge catalog; no background network polling.
  const query = useQuery({ queryKey: ["pos-invoice-charges", draft.draftId.value, page],
    queryFn: () => client.invoiceCharges(page), staleTime: 0, gcTime: 0,
    refetchOnWindowFocus: false, retry: false });
  usePosModalBehavior({ modalRef: modal, escapeDisabled: busy, onEscape: onClose });

  function selectCharge(value: InvoiceCharge) {
    setSelected(value); setError(null);
    const active = value.suppliers.filter(supplier => supplier.isActive);
    const previous = draft.charges?.find(charge => charge.chargeId === value.chargeId);
    setSupplierId(active.some(supplier => supplier.supplierId === previous?.supplier.supplierId)
      ? previous!.supplier.supplierId : active.length === 1 ? active[0].supplierId : "");
    setManualAmount(String(previous?.manualAmount ?? value.value ?? ""));
  }
  async function save(event: React.FormEvent) {
    event.preventDefault();
    if (!selected || busy) return;
    const input: AddInvoiceCharge = {
      appliedChargeId: draft.charges?.find(charge => charge.chargeId === selected.chargeId)?.appliedChargeId ?? crypto.randomUUID(),
      chargeId: selected.chargeId, chargeVersion: selected.version, supplierId,
      manualAmount: selected.calculationMode === "Manual" ? Number(manualAmount) : null,
    };
    setBusy(true); setError(null);
    try { onUpdated(await client.saveCharge(draft.draftId.value, input)); setSelected(null); }
    catch (failure) { setError(failure instanceof Error ? failure.message : "No fue posible agregar el cargo."); }
    finally { setBusy(false); }
  }
  async function remove(appliedId: string) {
    setBusy(true); setError(null);
    try { onUpdated(await client.removeCharge(draft.draftId.value, appliedId)); }
    catch (failure) { setError(failure instanceof Error ? failure.message : "No fue posible quitar el cargo."); }
    finally { setBusy(false); }
  }
  return <div className="fixed inset-0 z-50 grid place-items-center bg-slate-950/60 p-4">
    <div ref={modal} role="dialog" aria-modal="true" aria-labelledby="invoice-charge-title" tabIndex={-1}
      className="max-h-[90dvh] w-full max-w-2xl overflow-y-auto rounded-2xl bg-white p-6 text-slate-900 shadow-2xl">
      <header className="mb-5 flex items-start justify-between gap-3"><div><h2 id="invoice-charge-title" className="flex items-center gap-2 text-xl font-bold"><ReceiptText className="h-5 w-5 text-teal-700"/>Cargos de facturación</h2><p className="mt-1 text-sm text-slate-500">Agrega el cargo y selecciona quién presta el servicio.</p></div><Button variant="ghost" size="icon" disabled={busy} onClick={onClose} aria-label="Cerrar cargos"><X className="h-5 w-5"/></Button></header>
      {(draft.charges?.length ?? 0) > 0 && <section aria-label="Cargos agregados" className="mb-5 space-y-2">{draft.charges?.map(charge => <div key={charge.appliedChargeId} className="flex items-center gap-3 rounded-xl border border-teal-200 bg-teal-50 p-3"><div className="min-w-0 flex-1"><b>{charge.name}</b><p className="text-xs text-slate-600">{charge.supplier.name}</p><p className="mt-1 text-xs font-medium text-teal-800">{charge.invoicedAmount > 0 ? "Incluido en factura" : "Gasto · no incrementa el cobro"}</p></div><strong className="whitespace-nowrap">{money.format(charge.amount)}</strong><Button variant="ghost" size="icon" disabled={busy} onClick={() => void remove(charge.appliedChargeId)} aria-label={`Quitar ${charge.name}`}><Trash2 className="h-4 w-4"/></Button></div>)}</section>}
      {selected ? <form onSubmit={event => void save(event)} className="space-y-4 rounded-xl border p-4"><h3 className="font-semibold">{selected.name}</h3>
        <div className="rounded-lg bg-slate-50 p-3 text-sm text-slate-600">{selected.inclusionMode === "Never" ? "Se registra como gasto y no se suma al cobro de la factura." : selected.inclusionMode === "Always" ? "Se suma al importe de la factura." : `Se suma a la factura hasta ${money.format(selected.invoiceAmountLimit ?? 0)} inclusive. Por encima se registra como gasto.`}<p className="mt-1">El cálculo usa el total de productos antes de cargos y retenciones.</p></div>
        <fieldset disabled={busy} className="space-y-4"><div className="space-y-2"><Label htmlFor="pos-charge-supplier">Proveedor</Label><Select value={supplierId} onValueChange={setSupplierId} disabled={busy}><SelectTrigger id="pos-charge-supplier"><SelectValue placeholder="Selecciona el proveedor"/></SelectTrigger><SelectContent>{selected.suppliers.filter(supplier => supplier.isActive).map(supplier => <SelectItem key={supplier.supplierId} value={supplier.supplierId}>{supplier.name}</SelectItem>)}</SelectContent></Select></div>
        {selected.calculationMode === "Manual" && <div className="space-y-2"><Label htmlFor="pos-charge-amount">Valor del cargo (COP)</Label><Input id="pos-charge-amount" type="number" required min={0} step="0.01" value={manualAmount} onChange={event => setManualAmount(event.target.value)}/></div>}
        <div className="flex justify-end gap-2"><Button type="button" variant="outline" onClick={() => setSelected(null)}>Volver</Button><Button type="submit" disabled={!supplierId || busy || (selected.calculationMode === "Manual" && manualAmount === "")}>{busy && <Loader2 className="mr-2 h-4 w-4 animate-spin"/>}Aplicar cargo</Button></div></fieldset>
      </form> : query.isPending ? <p role="status" className="py-8 text-center">Cargando cargos…</p> : query.isError ? <div role="alert"><p>No fue posible consultar los cargos.</p><Button variant="outline" onClick={() => void query.refetch()}>Reintentar</Button></div> : <section className="space-y-2" aria-label="Cargos disponibles">
        {query.data.items.filter(charge => charge.isActive).map(charge => <button type="button" key={charge.chargeId} disabled={busy || (!draft.charges?.some(value => value.chargeId === charge.chargeId) && (draft.charges?.length ?? 0) >= 10)} onClick={() => selectCharge(charge)} className="flex w-full items-center justify-between gap-3 rounded-xl border border-slate-200 px-4 py-3 text-left transition hover:border-teal-500 hover:bg-teal-50 disabled:opacity-40"><span><b>{charge.name}</b><span className="block text-xs text-slate-500">{charge.code}</span></span><span className="text-sm font-semibold text-teal-700">{draft.charges?.some(value => value.chargeId === charge.chargeId) ? "Editar" : "Agregar"}</span></button>)}
        {query.data.items.length === 0 && <p className="py-6 text-center text-sm text-slate-500">No hay cargos activos para esta sede.</p>}
        {query.data.totalPages > 1 && <div className="flex items-center justify-between pt-2"><Button variant="outline" disabled={page === 1 || busy} onClick={() => setPage(page - 1)}>Anterior</Button><span className="text-sm">{page} / {query.data.totalPages}</span><Button variant="outline" disabled={page === query.data.totalPages || busy} onClick={() => setPage(page + 1)}>Siguiente</Button></div>}
      </section>}
      {error && <p role="alert" className="mt-4 rounded-lg bg-red-50 p-3 text-sm text-red-700">{error}</p>}
      <footer className="mt-5 flex justify-between border-t pt-4"><span className="text-sm text-slate-600">Total factura <strong className="block text-lg text-slate-900">{money.format(draft.payableAmount)}</strong></span><Button variant="outline" disabled={busy} onClick={onClose}>Listo</Button></footer>
    </div>
  </div>;
}
