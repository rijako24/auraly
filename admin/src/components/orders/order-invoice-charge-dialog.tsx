"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Loader2, ReceiptText, X } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import type { InvoiceCharge, InvoiceChargePage } from "@/services/api/invoice-charges";
import type { OrderInvoiceChargeSelection } from "@/services/orders/commerce-orders-client";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });

export function OrderInvoiceChargeDialog({ orderCount, total, busy, loadPage, onInvoice, onClose }: {
  orderCount: number;
  total: number;
  busy: boolean;
  loadPage: (page: number) => Promise<InvoiceChargePage>;
  onInvoice: (charge: OrderInvoiceChargeSelection) => Promise<void>;
  onClose: () => void;
}) {
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<InvoiceCharge | null>(null);
  const [supplierId, setSupplierId] = useState("");
  const [manualAmount, setManualAmount] = useState("");
  const query = useQuery({
    queryKey: ["order-invoice-charges", page],
    queryFn: () => loadPage(page),
    staleTime: 0,
    gcTime: 0,
    retry: false,
    refetchOnWindowFocus: false,
  });

  function selectCharge(charge: InvoiceCharge) {
    const suppliers = charge.suppliers.filter((supplier) => supplier.isActive);
    setSelected(charge);
    setSupplierId(suppliers.length === 1 ? suppliers[0].supplierId : "");
    setManualAmount(String(charge.value ?? ""));
  }

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    if (!selected || !supplierId || busy) return;
    await onInvoice({
      chargeId: selected.chargeId,
      chargeVersion: selected.version,
      supplierId,
      manualAmount: selected.calculationMode === "Manual" ? Number(manualAmount) : null,
    });
  }

  return <div className="fixed inset-0 z-[70] grid place-items-center bg-slate-950/60 p-2 sm:p-4">
    <section role="dialog" aria-modal="true" aria-labelledby="order-charge-title" className="flex max-h-[94dvh] w-full max-w-2xl flex-col overflow-hidden rounded-2xl bg-white text-slate-900 shadow-2xl">
      <header className="flex items-start justify-between gap-3 border-b px-4 py-4 sm:px-6">
        <div><h2 id="order-charge-title" className="flex items-center gap-2 text-xl font-bold"><ReceiptText className="h-5 w-5 text-teal-700"/>Facturar pedidos con cargo</h2><p className="mt-1 text-sm text-slate-500">Se emitirá una factura por pedido y la regla se calculará sobre el total de cada uno.</p></div>
        <Button type="button" variant="ghost" size="icon" disabled={busy} onClick={onClose} aria-label="Cerrar"><X className="h-5 w-5"/></Button>
      </header>
      <div className="min-h-0 flex-1 overflow-y-auto p-4 sm:p-6">
        <div className="mb-5 grid gap-3 rounded-xl bg-slate-50 p-4 sm:grid-cols-2"><div><span className="text-xs text-slate-500">Pedidos seleccionados</span><strong className="block text-lg">{orderCount}</strong></div><div><span className="text-xs text-slate-500">Total actual de productos</span><strong className="block text-lg">{money.format(total)}</strong></div></div>
        {selected ? <form className="space-y-4" onSubmit={(event) => void submit(event)}>
          <div><h3 className="font-semibold">{selected.name}</h3><p className="text-xs text-slate-500">{selected.code}</p></div>
          <div className="rounded-xl border border-teal-200 bg-teal-50 p-3 text-sm text-teal-950">{selected.inclusionMode === "Never" ? "La empresa asumirá este cargo en cada factura." : selected.inclusionMode === "Always" ? "El cargo se sumará al valor de cada factura." : `Se cobrará al cliente cuando el total de ese pedido sea hasta ${money.format(selected.invoiceAmountLimit ?? 0)} inclusive; por encima lo asumirá la empresa.`}</div>
          <div className="space-y-2"><Label htmlFor="order-charge-supplier">Proveedor</Label><Select value={supplierId} onValueChange={setSupplierId} disabled={busy}><SelectTrigger id="order-charge-supplier"><SelectValue placeholder="Selecciona el proveedor"/></SelectTrigger><SelectContent>{selected.suppliers.filter((supplier) => supplier.isActive).map((supplier) => <SelectItem key={supplier.supplierId} value={supplier.supplierId}>{supplier.name}</SelectItem>)}</SelectContent></Select></div>
          {selected.calculationMode === "Manual" && <div className="space-y-2"><Label htmlFor="order-charge-amount">Valor por factura (COP)</Label><Input id="order-charge-amount" type="number" min={0} step="0.01" required value={manualAmount} onChange={(event) => setManualAmount(event.target.value)}/></div>}
          <div className="flex flex-col-reverse gap-2 pt-2 sm:flex-row sm:justify-end">
            <Button type="button" variant="outline" disabled={busy} onClick={() => setSelected(null)}>
              Cambiar cargo
            </Button>
            <Button
              type="submit"
              disabled={busy || !supplierId || (selected.calculationMode === "Manual" && manualAmount === "")}
              className="bg-teal-700 hover:bg-teal-800"
            >
              {busy && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
              Facturar {orderCount} {orderCount === 1 ? "pedido" : "pedidos"} con cargo
            </Button>
          </div>
        </form> : query.isPending ? <p className="py-10 text-center text-sm text-slate-500">Cargando cargos…</p> : query.isError ? <div className="space-y-3 py-8 text-center"><p className="text-sm text-red-700">No fue posible consultar los cargos de esta sede.</p><Button type="button" variant="outline" onClick={() => void query.refetch()}>Reintentar</Button></div> : <div className="space-y-2">{query.data.items.filter((charge) => charge.isActive).map((charge) => <button type="button" key={charge.chargeId} onClick={() => selectCharge(charge)} className="flex w-full items-center justify-between gap-3 rounded-xl border px-4 py-3 text-left transition hover:border-teal-500 hover:bg-teal-50"><span><b>{charge.name}</b><small className="block text-slate-500">{charge.code}</small></span><span className="text-sm font-semibold text-teal-700">Seleccionar</span></button>)}{query.data.items.length === 0 && <p className="py-8 text-center text-sm text-slate-500">No hay cargos activos para esta sede.</p>}{query.data.totalPages > 1 && <div className="flex items-center justify-between pt-3"><Button type="button" variant="outline" disabled={page === 1} onClick={() => setPage((value) => value - 1)}>Anterior</Button><span className="text-sm">{page} / {query.data.totalPages}</span><Button type="button" variant="outline" disabled={page === query.data.totalPages} onClick={() => setPage((value) => value + 1)}>Siguiente</Button></div>}</div>}
      </div>
    </section>
  </div>;
}
