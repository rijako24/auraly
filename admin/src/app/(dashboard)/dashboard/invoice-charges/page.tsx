"use client";

import { InvoiceChargeHistory } from "./invoice-charge-history";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Loader2, Plus, ReceiptText, Settings2, Trash2, Truck } from "lucide-react";
import { toast } from "sonner";
import { PartyRoleSelect } from "@/components/parties/party-role-select";
import { DataTablePagination } from "@/components/tables/data-table-pagination";
import { ServerSearchInput } from "@/components/tables/server-search-input";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { useReferenceOptions } from "@/hooks/use-reference-options";
import { invoiceChargesApi, type InvoiceCharge, type InvoiceChargePage, type SaveInvoiceCharge } from "@/services/api/invoice-charges";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 2 });

export default function InvoiceChargesPage() {
  const businessId = useBusinessContextStore(state => state.selectedBusinessId);
  if (!businessId) return <Card><CardContent className="p-8 text-center text-muted-foreground">Selecciona una sede para administrar sus cargos.</CardContent></Card>;
  return <InvoiceChargesWorkspace key={businessId} businessId={businessId}/>;
}

function InvoiceChargesWorkspace({ businessId }: { businessId: string }) {
  const permissions = useAuthStore(state => state.user?.permissions ?? []);
  const canRead = permissions.includes("invoice-charges.read");
  const canConfigure = permissions.includes("invoice-charges.configure");
  const [tab, setTab] = useState("configuration");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [search, setSearch] = useState("");
  const [editing, setEditing] = useState<InvoiceCharge | "new" | null>(null);
  const queryClient = useQueryClient();
  const queryKey = ["invoice-charges", businessId, page, pageSize, search] as const;
  const query = useQuery({ queryKey, queryFn: () => invoiceChargesApi.list({ page, pageSize, search, includeInactive: true }), enabled: Boolean(businessId) && canRead && tab === "configuration" });
  const modes = useReferenceOptions("invoice-charge-calculation", canRead && tab === "configuration");
  const inclusionModes = useReferenceOptions("invoice-charge-inclusion", canRead && tab === "configuration");

  function saved(value: InvoiceCharge) {
    const changesPageMembership = editing === "new" || Boolean(search) ||
      (editing !== null && editing.sortOrder !== value.sortOrder);
    if (changesPageMembership) {
      // A changed order/filter can pull an unseen row from an adjacent server page.
      // The saved entity cannot reconstruct that page: refresh this owner exactly once.
      void queryClient.invalidateQueries({ queryKey, exact: true });
    } else {
      queryClient.setQueryData<InvoiceChargePage>(queryKey, previous => previous ? {
        ...previous, items: previous.items.map(item => item.chargeId === value.chargeId ? value : item),
      } : previous);
    }
    setEditing(null);
    toast.success("Cargo guardado. Las cajas recibirán la actualización.");
  }

  if (!canRead) return <Card><CardContent className="p-8 text-center text-muted-foreground">No tienes permiso para consultar los cargos de facturación.</CardContent></Card>;
  return <div className="space-y-6">
    <header className="flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
      <div><p className="text-sm font-medium text-emerald-600">Facturación</p><h1 className="text-3xl font-bold tracking-tight">Cargos de facturación</h1><p className="mt-2 max-w-2xl text-muted-foreground">Domicilios y otros cargos, con reglas claras de cobro, proveedor y gasto.</p></div>
      {canConfigure && tab === "configuration" && <Button disabled={modes.isPending || inclusionModes.isPending || modes.isError || inclusionModes.isError} onClick={() => setEditing("new")}><Plus className="mr-2 h-4 w-4"/>Crear cargo</Button>}
    </header>
    <Tabs value={tab} onValueChange={value => { setEditing(null); setTab(value); }}>
      <TabsList><TabsTrigger value="configuration">Configuración</TabsTrigger><TabsTrigger value="history">Consulta de cargos</TabsTrigger></TabsList>
    </Tabs>
    {tab === "history" ? <InvoiceChargeHistory businessId={businessId}/> : <>
    {(modes.isError || inclusionModes.isError) && <div role="alert" className="rounded-xl border border-destructive/30 p-4"><p>No fue posible cargar las modalidades de cargos.</p><Button variant="outline" className="mt-2" onClick={() => { void modes.refetch(); void inclusionModes.refetch(); }}>Reintentar</Button></div>}
    <Card><CardHeader><CardTitle className="flex items-center gap-2"><Settings2 className="h-5 w-5 text-primary"/>Configuración de cargos</CardTitle></CardHeader><CardContent className="space-y-4">
      <ServerSearchInput value={search} onSearch={value => { setSearch(value); setPage(1); }} isSearching={query.isFetching} placeholder="Buscar por código o nombre"/>
      {query.isPending ? <p role="status" className="flex items-center gap-2 py-10 text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin"/>Cargando cargos…</p> : query.isError ?
        <div role="alert" className="rounded-xl border border-destructive/30 p-5"><p>No fue posible consultar los cargos.</p><Button variant="outline" className="mt-3" onClick={() => void query.refetch()}>Reintentar</Button></div> : query.data.items.length === 0 ?
        <div className="rounded-xl border border-dashed px-6 py-12 text-center"><ReceiptText className="mx-auto mb-3 h-9 w-9 text-muted-foreground"/><h2 className="font-semibold">{search ? "No hay cargos que coincidan" : "Todavía no hay cargos"}</h2><p className="mt-1 text-sm text-muted-foreground">Configura el cálculo y cuándo se incluye en la factura antes de usarlo en una factura.</p></div> :
        <div className="overflow-x-auto rounded-xl border"><table className="w-full text-left text-sm"><thead className="bg-muted/50 text-xs uppercase text-muted-foreground"><tr>{["Cargo", "Cálculo", "Aplicación en factura", "Concepto de gasto", "Estado", ""].map((label, i) => <th key={i} className="p-3 font-semibold">{label}</th>)}</tr></thead><tbody>{query.data.items.map(item => <tr key={item.chargeId} className="border-t hover:bg-muted/30">
          <td className="p-3"><b>{item.name}</b><p className="text-xs text-muted-foreground">{item.code} · v{item.version}</p></td>
          <td className="p-3">{modes.data?.find(mode => mode.code === item.calculationMode)?.label ?? item.calculationMode}<p className="text-xs text-muted-foreground">{item.value !== null ? item.calculationMode === "Percentage" ? `${item.value}%` : money.format(item.value) : item.ranges.length > 0 ? `${item.ranges.length} rangos` : "Por factura"}</p></td>
          <td className="p-3">{inclusionModes.data?.find(mode => mode.code === item.inclusionMode)?.label ?? item.inclusionMode}{item.invoiceAmountLimit !== null && <p className="text-xs text-muted-foreground">Hasta {money.format(item.invoiceAmountLimit)}, inclusive</p>}</td>
          <td className="p-3">{item.expenseConceptName}<p className="text-xs text-muted-foreground">{item.expenseAccountCode} · {item.costCenterName ?? "Centro según política contable"}</p></td>
          <td className="p-3"><span className={`rounded-full px-2.5 py-1 text-xs font-medium ${item.isActive ? "bg-emerald-500/10 text-emerald-700 dark:text-emerald-400" : "bg-muted text-muted-foreground"}`}>{item.isActive ? "Activo" : "Inactivo"}</span></td>
          <td className="p-3">{canConfigure && <Button variant="ghost" disabled={modes.isPending || inclusionModes.isPending || modes.isError || inclusionModes.isError} aria-label={`Editar ${item.name}`} onClick={() => setEditing(item)}>Editar</Button>}</td>
        </tr>)}</tbody></table></div>}
      <DataTablePagination pageIndex={page - 1} pageSize={pageSize} pageCount={query.data?.totalPages ?? 0} totalItems={query.data?.totalCount ?? 0} onPageChange={index => setPage(index + 1)} onPageSizeChange={size => { setPageSize(size); setPage(1); }}/>
    </CardContent></Card>
    <Dialog open={editing !== null} onOpenChange={open => { if (!open) setEditing(null); }}><DialogContent className="max-h-[92dvh] max-w-4xl overflow-y-auto"><DialogHeader><DialogTitle>{editing === "new" ? "Crear cargo de facturación" : "Editar cargo de facturación"}</DialogTitle><DialogDescription>Las facturas emitidas conservan la configuración que usaron.</DialogDescription></DialogHeader>
      {editing && <ChargeForm key={`${businessId}-${editing === "new" ? "new" : editing.chargeId}`} businessId={businessId} initial={editing === "new" ? null : editing} modes={modes.data ?? []} inclusionModes={inclusionModes.data ?? []} onSaved={saved}/>}
    </DialogContent></Dialog>
    </>}
  </div>;
}

function ChargeForm({ businessId, initial, modes, inclusionModes, onSaved }: { businessId: string; initial: InvoiceCharge | null;
  modes: Array<{ code: string; label: string }>; inclusionModes: Array<{ code: string; label: string }>;
  onSaved: (value: InvoiceCharge) => void }) {
  const [form, setForm] = useState<SaveInvoiceCharge>(() => ({ chargeId: initial?.chargeId ?? crypto.randomUUID(), expectedVersion: initial?.version ?? 0,
    code: initial?.code ?? "", name: initial?.name ?? "", isActive: initial?.isActive ?? true, sortOrder: initial?.sortOrder ?? 0,
    calculationMode: initial?.calculationMode ?? "", value: initial?.value ?? null, inclusionMode: initial?.inclusionMode ?? "", invoiceAmountLimit: initial?.invoiceAmountLimit ?? null,
    expenseConceptId: initial?.expenseConceptId ?? "", salesTaxProfileId: initial?.salesTaxProfileId ?? "", purchaseTaxProfileId: initial?.purchaseTaxProfileId ?? "", ranges: initial?.ranges ?? [], supplierIds: initial?.suppliers.map(x => x.supplierId) ?? [] }));
  const [suppliers, setSuppliers] = useState(initial?.suppliers ?? []);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const options = useQuery({ queryKey: ["invoice-charge-options", businessId], queryFn: invoiceChargesApi.options });
  const concept = options.data?.concepts.find(x => x.conceptId === form.expenseConceptId);
  async function submit(event: React.FormEvent) {
    event.preventDefault(); setBusy(true); setError(null);
    try { onSaved(await invoiceChargesApi.save(form)); }
    catch (failure) { setError(failure instanceof Error ? failure.message : "No fue posible guardar el cargo."); }
    finally { setBusy(false); }
  }
  if (options.isPending) return <p role="status" className="py-8">Cargando configuración…</p>;
  if (options.isError) return <div role="alert" className="space-y-3"><p>No fue posible cargar las opciones de configuración.</p><Button variant="outline" onClick={() => { void options.refetch(); }}>Reintentar</Button></div>;
  return <form onSubmit={event => void submit(event)} className="space-y-6">
    <fieldset disabled={busy} className="space-y-6 disabled:opacity-60">
      <div className="grid gap-4 sm:grid-cols-2"><div className="space-y-2"><Label htmlFor="charge-name">Nombre</Label><Input id="charge-name" required maxLength={120} value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} placeholder="Ej. Domicilio"/></div>
      <div className="space-y-2"><Label htmlFor="charge-code">Código</Label><Input id="charge-code" required maxLength={32} disabled={initial !== null} value={form.code} onChange={e => setForm({ ...form, code: e.target.value.toUpperCase() })}/></div></div>
      <section className="space-y-4 rounded-xl border p-4"><h2 className="font-semibold">Cómo se calcula</h2><p className="text-sm text-muted-foreground">Se usa el total de productos con descuentos e impuestos, antes de cargos y retenciones.</p>
        <div className="grid gap-4 sm:grid-cols-2"><div className="space-y-2"><Label htmlFor="charge-mode">Modalidad</Label><Select value={form.calculationMode} onValueChange={calculationMode => setForm({ ...form, calculationMode, value: null, ranges: [] })}><SelectTrigger id="charge-mode"><SelectValue placeholder="Selecciona la modalidad"/></SelectTrigger><SelectContent>{modes.map(x => <SelectItem key={x.code} value={x.code}>{x.label}</SelectItem>)}</SelectContent></Select></div>
        {(form.calculationMode === "Fixed" || form.calculationMode === "Percentage" || form.calculationMode === "Manual") && <div className="space-y-2"><Label htmlFor="charge-value">{form.calculationMode === "Percentage" ? "Porcentaje (%)" : form.calculationMode === "Manual" ? "Valor sugerido (COP)" : "Valor (COP)"}</Label><Input id="charge-value" type="number" required={form.calculationMode !== "Manual"} min={0} max={form.calculationMode === "Percentage" ? 100 : undefined} step={form.calculationMode === "Percentage" ? "0.000001" : "0.01"} value={form.value ?? ""} onChange={e => setForm({ ...form, value: e.target.value === "" ? null : e.target.valueAsNumber })}/></div>}</div>
        {form.calculationMode === "Ranges" && <div className="space-y-3"><p className="text-xs text-muted-foreground">Desde incluido, hasta excluido. Deja el último límite vacío para continuar sin límite. El porcentaje aplica al total completo.</p>{form.ranges.map((range, index) => <div key={index} className="grid grid-cols-2 items-end gap-2 rounded-lg bg-muted/30 p-3 sm:grid-cols-[1fr_1fr_1fr_1fr_auto]">
          <div><Label htmlFor={`range-from-${index}`}>Desde</Label><Input id={`range-from-${index}`} type="number" min={0} step="0.01" required value={range.fromInclusive} onChange={e => setForm({ ...form, ranges: form.ranges.map((x, i) => i === index ? { ...x, fromInclusive: e.target.valueAsNumber } : x) })}/></div>
          <div><Label htmlFor={`range-to-${index}`}>Hasta</Label><Input id={`range-to-${index}`} type="number" min={0} step="0.01" placeholder="Sin límite" value={range.toExclusive ?? ""} onChange={e => setForm({ ...form, ranges: form.ranges.map((x, i) => i === index ? { ...x, toExclusive: e.target.value === "" ? null : e.target.valueAsNumber } : x) })}/></div>
          <div><Label htmlFor={`range-mode-${index}`}>Tarifa</Label><Select value={range.calculationMode} onValueChange={mode => setForm({ ...form, ranges: form.ranges.map((x, i) => i === index ? { ...x, calculationMode: mode } : x) })}><SelectTrigger id={`range-mode-${index}`}><SelectValue placeholder="Selecciona"/></SelectTrigger><SelectContent>{modes.filter(x => x.code === "Fixed" || x.code === "Percentage").map(x => <SelectItem key={x.code} value={x.code}>{x.label}</SelectItem>)}</SelectContent></Select></div>
          <div><Label htmlFor={`range-value-${index}`}>Valor</Label><Input id={`range-value-${index}`} type="number" required min={0} step={range.calculationMode === "Percentage" ? "0.000001" : "0.01"} value={range.value} onChange={e => setForm({ ...form, ranges: form.ranges.map((x, i) => i === index ? { ...x, value: e.target.valueAsNumber } : x) })}/></div>
          <Button type="button" variant="ghost" aria-label={`Eliminar rango ${index + 1}`} onClick={() => setForm({ ...form, ranges: form.ranges.filter((_, i) => i !== index) })}><Trash2 className="h-4 w-4"/></Button>
        </div>)}<Button type="button" variant="outline" disabled={form.ranges.length >= 32} onClick={() => setForm({ ...form, ranges: [...form.ranges, { fromInclusive: form.ranges.at(-1)?.toExclusive ?? 0, toExclusive: null, calculationMode: "", value: 0 }] })}><Plus className="mr-2 h-4 w-4"/>Agregar rango</Button></div>}
      </section>
      <section className="grid gap-4 rounded-xl border p-4 sm:grid-cols-2"><h2 className="font-semibold sm:col-span-2">Cuándo se incluye en la factura</h2><div className="space-y-2"><Label htmlFor="charge-inclusion">Aplicación del cargo</Label><Select value={form.inclusionMode} onValueChange={inclusionMode => setForm({ ...form, inclusionMode, invoiceAmountLimit: null })}><SelectTrigger id="charge-inclusion"><SelectValue placeholder="Selecciona la regla"/></SelectTrigger><SelectContent>{inclusionModes.map(x => <SelectItem key={x.code} value={x.code}>{x.label}</SelectItem>)}</SelectContent></Select></div>
        {form.inclusionMode === "UpToInvoiceAmount" && <div className="space-y-2"><Label htmlFor="charge-threshold">Incluir hasta un importe de factura de (COP)</Label><Input id="charge-threshold" required type="number" min={0} step="0.01" value={form.invoiceAmountLimit ?? ""} onChange={e => setForm({ ...form, invoiceAmountLimit: e.target.value === "" ? null : e.target.valueAsNumber })}/><p className="text-xs text-muted-foreground">Se compara el total de la factura antes de cargos. Hasta este importe, inclusive, el cargo se suma a la factura. Por encima se registra como gasto de la empresa activa.</p></div>}
      </section>
      <section className="space-y-4 rounded-xl border p-4"><h2 className="font-semibold">Concepto e impuesto</h2><div className="grid gap-4 sm:grid-cols-2"><div className="space-y-2"><Label htmlFor="charge-concept">Concepto de gasto</Label><Select value={form.expenseConceptId} onValueChange={expenseConceptId => setForm({ ...form, expenseConceptId })}><SelectTrigger id="charge-concept"><SelectValue placeholder="Selecciona un concepto"/></SelectTrigger><SelectContent>{options.data.concepts.map(x => <SelectItem key={x.conceptId} value={x.conceptId}>{x.name}</SelectItem>)}</SelectContent></Select></div>
        <div className="space-y-2"><Label htmlFor="charge-tax">Impuesto al facturar</Label><Select value={form.salesTaxProfileId} onValueChange={salesTaxProfileId => setForm({ ...form, salesTaxProfileId })}><SelectTrigger id="charge-tax"><SelectValue placeholder="Selecciona el perfil tributario"/></SelectTrigger><SelectContent>{options.data.taxProfiles.map(x => <SelectItem key={x.taxProfileId} value={x.taxProfileId}>{x.name}</SelectItem>)}</SelectContent></Select></div></div>
        <div className="space-y-2"><Label htmlFor="charge-purchase-tax">Impuesto del costo del proveedor</Label><Select value={form.purchaseTaxProfileId} onValueChange={purchaseTaxProfileId => setForm({ ...form, purchaseTaxProfileId })}><SelectTrigger id="charge-purchase-tax"><SelectValue placeholder="Selecciona el perfil tributario"/></SelectTrigger><SelectContent>{options.data.taxProfiles.map(x => <SelectItem key={x.taxProfileId} value={x.taxProfileId}>{x.name}</SelectItem>)}</SelectContent></Select><p className="text-xs text-muted-foreground">La tarifa incluye impuestos. Auraly separa el impuesto de venta y el del costo según estos perfiles.</p></div>
        <div className="rounded-lg bg-muted/40 p-3 text-sm"><p><b>Cuenta:</b> {concept ? `${concept.expenseAccountCode} · ${concept.expenseAccountName}` : "La define el concepto"}</p><p className="mt-1"><b>Centro de costo:</b> {concept?.defaultCostCenterName ?? "Según la política contable de la sede"}</p></div>
      </section>
      <section className="space-y-3 rounded-xl border p-4"><h2 className="flex items-center gap-2 font-semibold"><Truck className="h-4 w-4"/>Proveedores del cargo</h2><p className="text-sm text-muted-foreground">Si solo hay uno activo, se seleccionará automáticamente al agregar el cargo a la factura.</p>
        <PartyRoleSelect role="Supplier" value="" placeholder="Buscar y agregar proveedor" onChange={(id, party) => { if (party && !form.supplierIds.includes(id)) { setSuppliers([...suppliers, { supplierId: id, name: party.displayName, identification: party.identification ?? null, defaultPaymentDueDays: party.supplierDefaultPaymentDueDays ?? 0, isActive: true }]); setForm({ ...form, supplierIds: [...form.supplierIds, id] }); } }}/>
        {suppliers.map(supplier => <div key={supplier.supplierId} className="flex items-center justify-between gap-3 rounded-lg bg-muted/30 px-3 py-2 text-sm"><div><b>{supplier.name}</b><p className="text-xs text-muted-foreground">{supplier.identification}{!supplier.isActive && " · Inactivo"}</p></div><Button type="button" variant="ghost" aria-label={`Quitar ${supplier.name}`} onClick={() => { setSuppliers(suppliers.filter(x => x.supplierId !== supplier.supplierId)); setForm({ ...form, supplierIds: form.supplierIds.filter(id => id !== supplier.supplierId) }); }}><Trash2 className="h-4 w-4"/></Button></div>)}
      </section>
      <div className="flex flex-wrap items-center justify-between gap-4"><div className="flex items-center gap-3"><Switch id="charge-active" checked={form.isActive} onCheckedChange={isActive => setForm({ ...form, isActive })}/><Label htmlFor="charge-active">Cargo activo</Label></div><div className="flex items-center gap-3"><Label htmlFor="charge-order">Orden</Label><Input id="charge-order" className="w-24" type="number" required min={0} step={1} value={form.sortOrder} onChange={e => setForm({ ...form, sortOrder: e.target.valueAsNumber })}/></div></div>
      {error && <p role="alert" className="rounded-lg bg-destructive/10 p-3 text-sm text-destructive">{error}</p>}
      <div className="flex justify-end border-t pt-4"><Button type="submit" disabled={busy || !form.calculationMode || !form.inclusionMode || !form.expenseConceptId || !form.salesTaxProfileId || !form.purchaseTaxProfileId || form.supplierIds.length === 0}>{busy && <Loader2 className="mr-2 h-4 w-4 animate-spin"/>}Guardar cargo</Button></div>
    </fieldset>
  </form>;
}
