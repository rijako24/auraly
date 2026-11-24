"use client";

import { useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { AccountSelect } from "@/components/accounting/account-select";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Checkbox } from "@/components/ui/checkbox";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { ServerSearchInput } from "@/components/tables/server-search-input";
import { useReferenceOptions } from "@/hooks/use-reference-options";
import { paymentMethodsApi, type PaymentMethod, type SavePaymentMethod } from "@/services/api/payment-methods";
import { useTenantContextStore } from "@/stores/tenant-context-store";

export function PaymentMethodsMaster({ canManage }: { canManage: boolean }) {
  const tenantId = useTenantContextStore(state => state.selectedTenantId);
  const cache = useQueryClient();
  const activeTenant = useRef(tenantId);
  activeTenant.current = tenantId;
  const [editingTenant, setEditingTenant] = useState<string | null>(null);
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState("");
  const [draft, setDraft] = useState<SavePaymentMethod | null>(null);
  const [account, setAccount] = useState<{accountId:string;code:string;name:string} | null>(null);
  const filters = { page, pageSize: 50, search: search || undefined };
  const queryKey = ["payment-methods", tenantId, filters];
  const list = useQuery({ queryKey, queryFn: () => paymentMethodsApi.list(filters), enabled: !!tenantId });
  const types = useReferenceOptions("payment-method", Boolean(draft && editingTenant === tenantId));
  const selectedType=types.data?.find(type=>type.id===draft?.paymentTypeId)?.code;
  const salesOnly=selectedType==="Credit";
  const save = useMutation({ mutationFn: (input: {value: SavePaymentMethod; filters: typeof filters; queryKey: typeof queryKey; tenant: string}) => {
      if (input.tenant !== activeTenant.current) throw new Error("El tenant cambió. Abre nuevamente la forma de pago.");
      return paymentMethodsApi.save(input.value, input.filters);
    },
    onSuccess: (result, input) => { cache.setQueryData(input.queryKey, result.page);
      void cache.invalidateQueries({ queryKey: ["payment-method-choices", input.tenant], refetchType: "none" }); if(input.tenant===activeTenant.current) {setDraft(null);toast.success("Forma de pago guardada.");} } });
  function edit(value?: PaymentMethod) {
    save.reset();
    setEditingTenant(tenantId);
    setAccount(value ? {accountId:value.accountId,code:value.accountCode,name:value.accountName} : null);
    setDraft(value ? { ...value } : { paymentMethodId: crypto.randomUUID(), code: "", name: "", paymentTypeId: "",
      accountId: "", useForSales: false, useForReceipts: false, useForSupplierPayments: false,
      useForPayroll: false, useForVouchers: false, isActive: true, sortOrder: 10 });
  }
  return <section className="space-y-4 rounded-2xl border bg-card p-5">
    <div className="flex flex-wrap items-center justify-between gap-3"><div><h2 className="text-xl font-semibold">Formas de pago</h2><p className="text-sm text-muted-foreground">Compartidas por todas las sedes de la empresa.</p></div><Button disabled={!canManage || !tenantId} onClick={() => edit()}>Nueva forma</Button></div>
    <div className="flex gap-2"><ServerSearchInput placeholder="Buscar código o nombre" value={search} onSearch={value => {setSearch(value);setPage(1);}} isSearching={list.isFetching}/><Button variant="outline" onClick={() => void list.refetch()}>Actualizar</Button></div>
    {list.isPending ? <p>Cargando formas de pago…</p> : list.isError ? <p role="alert">No se pudieron cargar las formas de pago.</p> : <>
      <div className="overflow-x-auto"><table className="w-full text-left text-sm"><thead><tr><th>Código</th><th>Nombre</th><th>Tipo</th><th>Cuenta</th><th>Estado</th><th /></tr></thead><tbody>{list.data.items.map(value => <tr key={value.paymentMethodId} className="border-t"><td className="p-2">{value.code}</td><td>{value.name}</td><td>{value.typeLabel}</td><td>{value.accountCode} · {value.accountName}</td><td>{value.isActive ? "Activa" : "Inactiva"}</td><td><Button variant="ghost" disabled={!canManage} onClick={() => edit(value)}>Editar</Button></td></tr>)}</tbody></table>{!list.data.items.length && <p className="p-4 text-muted-foreground">No hay formas de pago para este filtro.</p>}</div>
      <div className="flex items-center gap-3"><Button variant="outline" disabled={page===1} onClick={() => setPage(value=>value-1)}>Anterior</Button><span>Página {page} · {list.data.totalCount} formas</span><Button variant="outline" disabled={page*50>=list.data.totalCount} onClick={() => setPage(value=>value+1)}>Siguiente</Button></div>
    </>}
    {draft && editingTenant === tenantId && <Dialog open onOpenChange={open => { if (!open && !save.isPending) setDraft(null); }}>
      <DialogContent className="flex h-[min(92dvh,48rem)] max-h-[92dvh] flex-col gap-0 overflow-clip p-0 sm:max-w-2xl">
        <DialogHeader className="shrink-0 border-b px-5 py-5 text-left sm:px-6">
          <DialogTitle>{draft.rowVersion ? "Editar forma de pago" : "Nueva forma de pago"}</DialogTitle>
          <DialogDescription>Define el tipo, la cuenta contable y dónde se puede utilizar.</DialogDescription>
        </DialogHeader>
        <form className="flex min-h-0 flex-1 flex-col overflow-hidden" onSubmit={event => {event.preventDefault();save.mutate({value:draft,filters,queryKey,tenant:tenantId!});}}>
          <div className="min-h-0 flex-1 space-y-6 overflow-y-auto px-5 py-5 sm:px-6">
            <div className="grid gap-4 sm:grid-cols-6">
              <div className="space-y-2 sm:col-span-2"><Label htmlFor="payment-form-code">Código</Label><Input id="payment-form-code" required maxLength={64} value={draft.code} disabled={!!draft.rowVersion || save.isPending} onChange={event=>setDraft({...draft,code:event.target.value})}/></div>
              <div className="space-y-2 sm:col-span-4"><Label htmlFor="payment-form-name">Nombre</Label><Input id="payment-form-name" required maxLength={160} value={draft.name} disabled={save.isPending} onChange={event=>setDraft({...draft,name:event.target.value})}/></div>
              <div className="space-y-2 sm:col-span-2"><Label htmlFor="payment-form-type">Tipo</Label><Select value={draft.paymentTypeId} disabled={save.isPending || types.isError || types.isPending} onValueChange={value=>{const restricted=types.data?.some(type=>type.id===value&&type.code==="Credit");setDraft({...draft,paymentTypeId:value,...(restricted?{useForSales:true,useForReceipts:false,useForSupplierPayments:false,useForPayroll:false,useForVouchers:false}:{})});}}><SelectTrigger id="payment-form-type"><SelectValue placeholder="Seleccionar tipo"/></SelectTrigger><SelectContent>{types.data?.map(type=><SelectItem key={type.id} value={type.id}>{type.label}</SelectItem>)}</SelectContent></Select></div>
              <div className="space-y-2 sm:col-span-4"><Label>Auxiliar contable</Label><AccountSelect value={draft.accountId} accounts={account?[account]:[]} onChange={(id,value)=>{setDraft({...draft,accountId:id});setAccount(value??null);}} disabled={save.isPending}/></div>
            </div>
            <fieldset disabled={save.isPending} className="space-y-3">
              <legend className="mb-3 text-sm font-semibold">Dónde se puede usar</legend>
              <div className="grid gap-3 sm:grid-cols-2">
                <Label className="flex min-h-12 items-center gap-3 rounded-lg border p-3"><Checkbox checked={draft.useForSales} onCheckedChange={value=>setDraft({...draft,useForSales:value===true})}/><span>Ventas y pedidos</span></Label>
                <Label className="flex min-h-12 items-center gap-3 rounded-lg border p-3"><Checkbox disabled={salesOnly} checked={draft.useForReceipts} onCheckedChange={value=>setDraft({...draft,useForReceipts:value===true})}/><span>Recibos de caja</span></Label>
                <Label className="flex min-h-12 items-center gap-3 rounded-lg border p-3"><Checkbox disabled={salesOnly} checked={draft.useForSupplierPayments} onCheckedChange={value=>setDraft({...draft,useForSupplierPayments:value===true})}/><span>Pagos a proveedores</span></Label>
                <Label className="flex min-h-12 items-center gap-3 rounded-lg border p-3"><Checkbox disabled={salesOnly} checked={draft.useForPayroll} onCheckedChange={value=>setDraft({...draft,useForPayroll:value===true})}/><span>Pagos de nómina</span></Label>
                <Label className="flex min-h-12 items-center gap-3 rounded-lg border p-3 sm:col-span-2"><Checkbox disabled={salesOnly} checked={draft.useForVouchers === true} onCheckedChange={value=>setDraft({...draft,useForVouchers:value===true})}/><span>Comprobantes manuales y egresos</span></Label>
              </div>
              {salesOnly && <p className="text-sm text-muted-foreground">Crédito cliente abre cartera. Puedes crear una forma como «Cartera a domicilio» y asignarle su cuenta por cobrar.</p>}
            </fieldset>
            <fieldset disabled={save.isPending} className="border-t pt-4">
              <legend className="pr-2 text-sm font-semibold">Estado y presentación</legend>
              <div className="grid items-end gap-4 sm:grid-cols-2">
                <Label className="flex min-h-11 items-center gap-3"><Checkbox checked={draft.isActive} onCheckedChange={value=>setDraft({...draft,isActive:value===true})}/><span>Forma de pago activa</span></Label>
                <div className="space-y-2"><Label htmlFor="payment-form-order">Orden en la lista</Label><Input id="payment-form-order" className="max-w-28" type="number" required min={0} value={draft.sortOrder} onChange={event=>setDraft({...draft,sortOrder:Number(event.target.value)})}/></div>
              </div>
            </fieldset>
            {(save.isError || types.isError) && <p role="alert" className="text-sm text-destructive">{save.error?.message ?? "No se pudieron cargar los tipos de pago."}</p>}
          </div>
          <div className="flex shrink-0 flex-wrap justify-end gap-3 border-t bg-muted/20 px-5 py-4 sm:px-6">
            <Button type="button" variant="outline" disabled={save.isPending} onClick={()=>setDraft(null)}>Cancelar</Button>
            <Button type="submit" disabled={save.isPending || !draft.accountId || !draft.paymentTypeId}>{save.isPending ? "Guardando…" : "Guardar forma"}</Button>
          </div>
        </form>
      </DialogContent>
    </Dialog>}
  </section>;
}
