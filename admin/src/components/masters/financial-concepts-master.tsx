"use client";

import { useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AccountSelect } from "@/components/accounting/account-select";
import { ServerSearchInput } from "@/components/tables/server-search-input";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { useReferenceOptions } from "@/hooks/use-reference-options";
import { financialConceptsApi, type FinancialConcept, type SaveFinancialConcept } from "@/services/api/financial-concepts";
import { useTenantContextStore } from "@/stores/tenant-context-store";
import { useBusinessContextStore } from "@/stores/business-context-store";

export function FinancialConceptsMaster({ canManage }: { canManage: boolean }) {
  const tenant = useTenantContextStore(state => state.selectedTenantId);
  const business = useBusinessContextStore(state => state.selectedBusinessId);
  const context = `${tenant}/${business}`;
  const active = useRef(context); active.current = context;
  const cache = useQueryClient();
  const [page, setPage] = useState(1), [search, setSearch] = useState("");
  const [editing, setEditing] = useState<{ context: string; value: SaveFinancialConcept; account: { accountId: string; code: string; name: string } | null } | null>(null);
  const filters = { page, pageSize: 50, search: search || undefined };
  const key = ["financial-concepts", tenant, filters];
  const list = useQuery({ queryKey: key, queryFn: () => financialConceptsApi.list(filters), enabled: !!tenant, staleTime: Infinity });
  const sides = useReferenceOptions("accounting-balance-side", editing?.context === context);
  const save = useMutation({ mutationFn: (command: { context: string; value: SaveFinancialConcept; filters: typeof filters; key: typeof key }) => {
    if (command.context !== active.current) throw new Error("Cambió la empresa o sede. Abre de nuevo el concepto.");
    return financialConceptsApi.save(command.value, command.filters);
  }, onSuccess: (result, command) => {
    void cache.invalidateQueries({ queryKey: command.key.slice(0,2), refetchType: "none" });
    cache.setQueryData(command.key, result.page);
    void cache.invalidateQueries({ queryKey: ["financial-concept-options",command.key[1]], refetchType: "none" });
    if (command.context === active.current) setEditing(null);
  } });
  function edit(concept?: FinancialConcept) {
    save.reset(); setEditing({ context, value: concept ? { ...concept } : { financialConceptId: crypto.randomUUID(), code: "", name: "", accountId: "", sideOptionId: "",
      useForReceipts: false, useForSupplierPayments: false, useForVouchers: false, isActive: true, sortOrder: 10 },
      account: concept ? { accountId: concept.accountId, code: concept.accountCode, name: concept.accountName } : null });
  }
  const draft = editing?.context === context ? editing : null;
  function change(patch: Partial<SaveFinancialConcept>) { if (draft) setEditing({ ...draft, value: { ...draft.value, ...patch } }); }
  return <section className="space-y-4 rounded-2xl border bg-card p-5">
    <div className="flex items-center justify-between gap-3"><div><h2 className="text-xl font-semibold">Conceptos financieros</h2><p className="text-sm text-muted-foreground">Configura la cuenta y naturaleza de las contrapartidas y anticipos. La forma de pago identifica el dinero.</p></div><Button disabled={!canManage || !tenant} onClick={() => edit()}>Nuevo concepto</Button></div>
    <div className="flex gap-2"><ServerSearchInput value={search} onSearch={value => { setSearch(value); setPage(1); }} isSearching={list.isFetching} placeholder="Buscar concepto financiero" /><Button variant="outline" disabled={list.isFetching} onClick={() => void list.refetch()}>Actualizar</Button></div>
    {list.isError && <p role="alert">No se pudieron consultar los conceptos financieros.</p>}
    {list.isPending ? <p>Cargando conceptos…</p> : list.data && <><div className="overflow-x-auto"><table className="w-full text-left text-sm"><thead><tr><th>Código</th><th>Nombre</th><th>Cuenta</th><th>Naturaleza</th><th>Estado</th><th /></tr></thead><tbody>{list.data.items.map(concept => <tr key={concept.financialConceptId} className="border-t"><td className="p-2">{concept.code}</td><td>{concept.name}</td><td>{concept.accountCode} · {concept.accountName}</td><td>{concept.sideLabel}</td><td>{concept.isActive ? "Activo" : "Inactivo"}</td><td><Button variant="ghost" disabled={!canManage} onClick={() => edit(concept)}>Editar</Button></td></tr>)}</tbody></table>{!list.data.items.length && <p className="p-4">No hay conceptos financieros para este filtro.</p>}</div>
      <div className="flex items-center gap-3"><Button variant="outline" disabled={page <= 1} onClick={() => setPage(value => value - 1)}>Anterior</Button><span>Página {page} · {list.data.totalCount} conceptos</span><Button variant="outline" disabled={page * 50 >= list.data.totalCount} onClick={() => setPage(value => value + 1)}>Siguiente</Button></div></>}
    {draft && <form className="space-y-4 rounded-xl border p-4" onSubmit={event => { event.preventDefault(); save.mutate({ context, value: draft.value, key, filters }); }}><fieldset disabled={save.isPending} className="grid gap-4 sm:grid-cols-2">
      <Label>Código<Input required maxLength={64} disabled={!!draft.value.rowVersion} value={draft.value.code} onChange={event => change({ code: event.target.value })} /></Label>
      <Label>Nombre<Input required maxLength={160} value={draft.value.name} onChange={event => change({ name: event.target.value })} /></Label>
      <div><Label>Cuenta del concepto</Label><AccountSelect value={draft.value.accountId} accounts={draft.account ? [draft.account] : []} onChange={(accountId, account) => setEditing({ ...draft, account: account ?? null, value: { ...draft.value, accountId } })} /></div>
      <div><Label htmlFor="financial-concept-side">Naturaleza</Label><Select value={draft.value.sideOptionId} disabled={sides.isPending || sides.isError} onValueChange={sideOptionId => change({ sideOptionId })}><SelectTrigger id="financial-concept-side"><SelectValue placeholder="Seleccionar naturaleza" /></SelectTrigger><SelectContent>{sides.data?.map(side => <SelectItem key={side.id} value={side.id}>{side.label}</SelectItem>)}</SelectContent></Select></div>
      <Label>Orden<Input type="number" required min={0} max={9999} value={draft.value.sortOrder} onChange={event => change({ sortOrder: Number(event.target.value) })} /></Label>
      <div className="flex flex-wrap gap-3"><Label className="flex items-center gap-2"><Checkbox checked={draft.value.useForReceipts} onCheckedChange={value => change({ useForReceipts: value === true })} />Recibos de caja</Label><Label className="flex items-center gap-2"><Checkbox checked={draft.value.useForSupplierPayments} onCheckedChange={value => change({ useForSupplierPayments: value === true })} />Egresos</Label><Label className="flex items-center gap-2"><Checkbox checked={draft.value.useForVouchers} onCheckedChange={value => change({ useForVouchers: value === true })} />Comprobantes</Label><Label className="flex items-center gap-2"><Checkbox checked={draft.value.isActive} onCheckedChange={value => change({ isActive: value === true })} />Activo</Label></div>
    </fieldset>{(save.isError || sides.isError) && <p role="alert">{save.error?.message ?? "No se pudieron consultar las naturalezas."}</p>}<div className="flex gap-2"><Button type="submit" disabled={save.isPending || !draft.value.accountId || !draft.value.sideOptionId}>Guardar concepto</Button><Button type="button" variant="outline" disabled={save.isPending} onClick={() => setEditing(null)}>Cancelar</Button></div></form>}
  </section>;
}
