"use client";

import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef, SortingState } from "@tanstack/react-table";
import { toast } from "sonner";
import { accountingApi, type AccountingCostCenter, type AccountingCostCenterAssignment } from "@/services/api/accounting";
import { CostCenterSelect, useCostCenterLabels } from "@/components/accounting/cost-center-select";
import { useAuthStore } from "@/stores/auth-store";
import { useReferenceOptions } from "@/hooks/use-reference-options";
import { DataTable } from "@/components/tables/data-table";
import { ServerSearchInput } from "@/components/tables/server-search-input";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";

export function CostCentersSection({ tenantId, businessId }: { tenantId: string; businessId: string }) {
  const client = useQueryClient();
  const canConfigure = useAuthStore(state => state.user?.permissions.includes("accounting.configure") ?? false);
  const [page, setPage] = useState(1), [pageSize, setPageSize] = useState(20), [search, setSearch] = useState("");
  const [includeInactive, setIncludeInactive] = useState(true), [sorting, setSorting] = useState<SortingState>([]);
  const [editing, setEditing] = useState<AccountingCostCenter | null>(null);
  const [code, setCode] = useState(""), [name, setName] = useState(""), [parent, setParent] = useState<string | null>(null);
  const [isDefault, setDefault] = useState(false);
  const [editingRule, setEditingRule] = useState<AccountingCostCenterAssignment | null>(null);
  const [center, setCenter] = useState<string | null>(null);
  const [operation, setOperation] = useState<AccountingCostCenterAssignment["operationKind"]>("All");
  const labels = useCostCenterLabels([parent, center]);
  const operations = useReferenceOptions("accounting-cost-center-operation");
  const key = ["accounting-cost-centers", tenantId, page, pageSize, search, includeInactive, sorting];
  const list = useQuery({ queryKey: key, queryFn: () => accountingApi.costCenterPage({ page, pageSize, search,
    isActive: includeInactive ? undefined : true, sortBy: sorting[0]?.id === "name" ? "name" : "code",
    sortDirection: sorting[0]?.desc ? "desc" : "asc" }) });
  // Five closed operation kinds plus one unique rule per kind bound this resource.
  const rulesKey = ["accounting-cost-center-assignments", tenantId, businessId];
  const rules = useQuery({ queryKey: rulesKey, queryFn: accountingApi.costCenterAssignments });
  const fail = (error: Error) => toast.error(error.message);
  const reset = () => { setEditing(null); setCode(""); setName(""); setParent(null); setDefault(false); };
  const resetRule = () => { setEditingRule(null); setCenter(null); setOperation("All"); };
  const updateRule = (saved: AccountingCostCenterAssignment) => {
    client.setQueryData<AccountingCostCenterAssignment[]>(rulesKey, old => [...(old ?? []).filter(row => row.assignmentId !== saved.assignmentId), saved]);
    resetRule();
  };
  const saved = async (row: AccountingCostCenter) => {
    client.setQueryData(["accounting-cost-center", tenantId, row.costCenterId], row);
    if (row.isDefault && row.isActive) client.setQueryData(["accounting-default-cost-center", tenantId], {
      items: [row], page: 1, pageSize: 1, totalCount: 1, totalPages: 1,
    });
    client.setQueriesData<Map<string, AccountingCostCenter>>({ queryKey: ["accounting-cost-center-labels", tenantId] }, old => {
      if (!old?.has(row.costCenterId)) return old;
      const next = new Map(old); next.set(row.costCenterId, row); return next;
    });
    client.setQueryData<AccountingCostCenterAssignment[]>(rulesKey, old => old?.map(rule => rule.costCenterId === row.costCenterId
      ? { ...rule, costCenterCode: row.code, costCenterName: row.name } : rule));
    await client.invalidateQueries({ queryKey: ["accounting-cost-center-options", tenantId], refetchType: "none" });
    await client.invalidateQueries({ queryKey: ["accounting-cost-centers", tenantId], refetchType: "none" });
    reset();
    // A creation/rename/activation/default replacement can change page membership,
    // order and other rows' versions. Only this visible bounded page is reread.
    await list.refetch();
    toast.success("Centro guardado para toda la empresa");
  };
  const save = useMutation({ mutationFn: () => editing
    ? accountingApi.updateCostCenter(editing.costCenterId, { code, name, parentCostCenterId: parent, isDefault, isActive: editing.isActive, rowVersion: editing.rowVersion })
    : accountingApi.createCostCenter({ costCenterId: crypto.randomUUID(), tenantId, code, name, parentCostCenterId: parent, isDefault: false }), onSuccess: saved, onError: fail });
  const status = useMutation({ mutationFn: (row: AccountingCostCenter) => accountingApi.setCostCenterStatus(row.costCenterId, !row.isActive, row.rowVersion), onSuccess: saved, onError: fail });
  const assign = useMutation({ mutationFn: () => accountingApi.saveCostCenterAssignment({ assignmentId: editingRule?.assignmentId ?? crypto.randomUUID(),
    businessId, costCenterId: center!, operationKind: operation, isActive: editingRule?.isActive ?? true, rowVersion: editingRule?.rowVersion }),
    onSuccess: row => { updateRule(row); toast.success("Asignación guardada para esta sede"); }, onError: fail });
  const ruleStatus = useMutation({ mutationFn: (row: AccountingCostCenterAssignment) => accountingApi.saveCostCenterAssignment({
    assignmentId: row.assignmentId, businessId: row.businessId, costCenterId: row.costCenterId,
    operationKind: row.operationKind, isActive: !row.isActive, rowVersion: row.rowVersion,
  }), onSuccess: updateRule, onError: fail });
  const edit = (row: AccountingCostCenter) => { setEditing(row); setCode(row.code); setName(row.name); setParent(row.parentCostCenterId); setDefault(row.isDefault); };
  const columns: ColumnDef<AccountingCostCenter>[] = [
    { accessorKey: "code", header: "Código", enableSorting: true },
    { accessorKey: "name", header: "Nombre", enableSorting: true },
    { id: "scope", header: "Alcance", cell: () => "Toda la empresa", enableSorting: false },
    { id: "status", header: "Estado", enableSorting: false, cell: ({ row }) => <Badge variant={row.original.isActive ? "secondary" : "outline"}>{row.original.isDefault ? "Predeterminado de la empresa" : row.original.isActive ? "Activo" : "Inactivo"}</Badge> },
    { id: "actions", header: "Acciones", enableSorting: false, cell: ({ row }) => canConfigure && <div className="flex gap-2"><Button size="sm" variant="outline" onClick={() => edit(row.original)}>Editar centro</Button>{!row.original.isDefault && <Button size="sm" variant="ghost" disabled={status.isPending} onClick={() => status.mutate(row.original)}>{row.original.isActive ? "Desactivar centro" : "Reactivar centro"}</Button>}</div> },
  ];
  const submit = (event: FormEvent) => { event.preventDefault(); save.mutate(); };
  return <div className="space-y-5">
    <div className="grid gap-5 lg:grid-cols-2">
      <Card><CardHeader><CardTitle>{editing ? "Editar centro de costo" : "Nuevo centro de costo"}</CardTitle><p className="text-sm text-muted-foreground">Los centros pertenecen a la empresa y se pueden usar en todas sus sedes.</p></CardHeader><CardContent>
        <form onSubmit={submit}><fieldset disabled={!canConfigure || save.isPending} className="space-y-4">
          <div className="space-y-2"><Label htmlFor="center-code">Código</Label><Input id="center-code" aria-label="Código del centro" value={code} maxLength={32} onChange={event => setCode(event.target.value)} required /></div>
          <div className="space-y-2"><Label htmlFor="center-name">Nombre</Label><Input id="center-name" aria-label="Nombre del centro" value={name} maxLength={160} onChange={event => setName(event.target.value)} required /></div>
          <div className="space-y-2"><Label>Centro superior</Label><CostCenterSelect value={parent} onChange={setParent} selected={parent ? labels.data?.get(parent) : undefined} ariaLabel="Centro superior" emptyLabel="Sin nivel superior" excludeId={editing?.costCenterId} /></div>
          {editing && <label className="flex items-center gap-2 text-sm"><Checkbox checked={isDefault} disabled={editing.isDefault || !editing.isActive} onCheckedChange={value => setDefault(value === true)} />Predeterminado de la empresa</label>}
          <div className="flex gap-2"><Button>{editing ? "Guardar centro" : "Crear centro"}</Button>{editing && <Button type="button" variant="outline" onClick={reset}>Cancelar</Button>}</div>
        </fieldset></form>
      </CardContent></Card>
      <Card><CardHeader><CardTitle>{editingRule ? "Editar asignación" : "Asignación automática"}</CardTitle><p className="text-sm text-muted-foreground">Para esta sede se usa primero la regla de la operación y después la regla general. El documento conserva el centro ya seleccionado.</p></CardHeader><CardContent><fieldset disabled={!canConfigure || assign.isPending} className="space-y-4">
        <div className="space-y-2"><Label>Centro</Label><CostCenterSelect value={center} onChange={setCenter} selected={center ? labels.data?.get(center) : undefined} ariaLabel="Centro de la regla" emptyLabel="Seleccionar centro" /></div>
        <div className="space-y-2"><Label>Operación</Label><Select value={operation} onValueChange={value => setOperation(value as typeof operation)}><SelectTrigger aria-label="Operación de la regla"><SelectValue /></SelectTrigger><SelectContent>{operations.data?.map(row => <SelectItem key={row.code} value={row.code}>{row.label}</SelectItem>)}</SelectContent></Select></div>
        {operations.isError && <p role="alert">No se pudieron consultar las operaciones. <Button variant="link" onClick={() => void operations.refetch()}>Reintentar operaciones</Button></p>}
        <div className="flex gap-2"><Button type="button" disabled={!center || !operations.data?.some(row => row.code === operation)} onClick={() => assign.mutate()}>Guardar asignación</Button>{editingRule && <Button type="button" variant="outline" onClick={resetRule}>Cancelar</Button>}</div>
      </fieldset></CardContent></Card>
    </div>
    {labels.isError && <p role="alert">No se pudieron consultar los centros seleccionados. <Button variant="link" onClick={() => void labels.refetch()}>Reintentar centros</Button></p>}
    <Card><CardHeader><CardTitle>Centros de toda la empresa</CardTitle></CardHeader><CardContent className="space-y-4">
      <ServerSearchInput value={search} onSearch={value => { setSearch(value); setPage(1); }} isSearching={list.isFetching} placeholder="Buscar centro por código o nombre" />
      <label className="flex items-center gap-2 text-sm"><Checkbox checked={includeInactive} onCheckedChange={value => { setIncludeInactive(value === true); setPage(1); }} />Incluir inactivos</label>
      {list.isError ? <p role="alert">{list.error.message} <Button variant="link" onClick={() => void list.refetch()}>Reintentar</Button></p> : <DataTable columns={columns} data={list.data?.items ?? []} isLoading={list.isLoading} page={page} pageSize={pageSize} pageCount={list.data?.totalPages} totalItems={list.data?.totalCount} enableRowSelection={false} sorting={sorting} onSortingChange={next => { setSorting(next); setPage(1); }} onPaginationChange={(next, size) => { setPage(next); setPageSize(size); }} />}
    </CardContent></Card>
    <Card><CardHeader><CardTitle>Reglas de esta sede</CardTitle></CardHeader><CardContent className="space-y-3">
      {rules.isPending && <p role="status">Consultando reglas…</p>}
      {rules.isError && <p role="alert">No se pudieron consultar las reglas. <Button variant="link" onClick={() => void rules.refetch()}>Reintentar reglas</Button></p>}
      {rules.data?.map(row => <div key={row.assignmentId} className="flex flex-wrap items-center justify-between gap-3 rounded-xl border p-3 text-sm"><div><b>{row.costCenterCode} · {row.costCenterName}</b><p>{operations.data?.find(item => item.code === row.operationKind)?.label ?? row.operationKind}{!row.isActive && " · Inactiva"}</p></div>{canConfigure && <div className="flex gap-2"><Button size="sm" variant="outline" onClick={() => { setEditingRule(row); setCenter(row.costCenterId); setOperation(row.operationKind); }}>Editar regla</Button><Button size="sm" variant="ghost" disabled={ruleStatus.isPending} onClick={() => ruleStatus.mutate(row)}>{row.isActive ? "Desactivar regla" : "Reactivar regla"}</Button></div>}</div>)}
      {rules.data?.length === 0 && <p className="text-sm text-muted-foreground">Sin reglas específicas. Se usa el predeterminado de la empresa.</p>}
    </CardContent></Card>
  </div>;
}
