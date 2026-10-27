"use client";

import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { Pencil, Plus, RefreshCw } from "lucide-react";
import { toast } from "sonner";
import { accountingApi, type AccountingAccount, type AccountingAccountPage } from "@/services/api/accounting";
import { useAuthStore } from "@/stores/auth-store";
import { useReferenceOptions } from "@/hooks/use-reference-options";
import { DataTable } from "@/components/tables/data-table";
import { ServerSearchInput } from "@/components/tables/server-search-input";
import { AccountEditor } from "@/components/accounting/account-editor";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

export function AccountsSection({ tenantId }: { tenantId: string }) {
  const client = useQueryClient();
  const canConfigure = useAuthStore(state => state.user?.permissions.includes("accounting.configure") ?? false);
  const [page, setPage] = useState(1), [pageSize, setPageSize] = useState(20), [search, setSearch] = useState("");
  const [editing, setEditing] = useState<AccountingAccount | null | undefined>(undefined);
  const types = useReferenceOptions("accounting-account-type");
  const key = ["accounting-accounts", tenantId, page, pageSize, search];
  const list = useQuery({ queryKey: key, queryFn: () => accountingApi.accountPage({ page, pageSize, search }) });

  const saved = async (account: AccountingAccount, created: boolean) => {
    // Reuse the authoritative account. Other cached pages are stale until explicitly opened.
    client.setQueryData<AccountingAccountPage>(key, current => current ? {
      ...current, items: current.items.map(item => item.accountId === account.accountId ? account : item),
    } : current);
    client.setQueriesData<AccountingAccount[]>({ predicate: query => query.queryKey[0] === "accounting" &&
      query.queryKey[1] === tenantId && query.queryKey[3] === "accounts" }, current => current?.map(item =>
      item.accountId === account.accountId ? account : item));
    await client.invalidateQueries({ queryKey: ["accounting-accounts", tenantId], refetchType: "none" });
    await client.invalidateQueries({ queryKey: ["account-options", tenantId], refetchType: "none" });
    await client.invalidateQueries({ queryKey: ["account-option", tenantId], refetchType: "none" });
    await client.invalidateQueries({ queryKey: ["accounting", tenantId], refetchType: "none" });
    setEditing(undefined);
    toast.success(created ? "Cuenta creada" : "Cuenta actualizada");
    // A creation or rename under search can change membership/count and requires one bounded page read.
    if (created || search) await list.refetch();
  };
  const columns: ColumnDef<AccountingAccount>[] = [
    { accessorKey: "code", header: "Código", cell: ({ row }) => <span className="font-mono font-semibold">{row.original.code}</span> },
    { accessorKey: "name", header: "Nombre" },
    { accessorKey: "accountType", header: "Tipo de cuenta", cell: ({ row }) => types.data?.find(type => type.code === row.original.accountType)?.label ?? row.original.accountType },
    { accessorKey: "allowsPosting", header: "Movimientos", cell: ({ row }) => row.original.allowsPosting ? "Sí" : "Agrupación" },
    { accessorKey: "requiresParty", header: "Tercero en cada partida", cell: ({ row }) => row.original.requiresParty ? "Obligatorio" : "Opcional" },
    { accessorKey: "isActive", header: "Estado", cell: ({ row }) => <Badge variant={row.original.isActive ? "secondary" : "outline"}>{row.original.isActive ? "Activa" : "Inactiva"}</Badge> },
    { id: "actions", header: "Acciones", cell: ({ row }) => canConfigure && <Button variant="outline" size="sm" onClick={() => setEditing(row.original)}><Pencil className="mr-2 h-4 w-4"/>Editar</Button> },
  ];
  return <Card className="rounded-3xl">
    <CardHeader className="flex flex-row flex-wrap items-center justify-between gap-3">
      <div><CardTitle>Plan Único de Cuentas operativo</CardTitle><p className="mt-1 text-sm text-muted-foreground">Consulta, crea y edita las cuentas de la empresa.</p></div>
      <div className="flex gap-2"><Button variant="outline" disabled={list.isFetching} onClick={() => void list.refetch()}><RefreshCw className="mr-2 h-4 w-4"/>Actualizar</Button>{canConfigure && <Button onClick={() => setEditing(null)}><Plus className="mr-2 h-4 w-4"/>Nueva cuenta</Button>}</div>
    </CardHeader>
    <CardContent className="space-y-4">
      <ServerSearchInput value={search} onSearch={value => { setSearch(value); setPage(1); }} isSearching={list.isFetching} placeholder="Buscar cuenta por código o nombre"/>
      {list.isError ? <div role="alert"><p>{list.error.message}</p><Button variant="outline" onClick={() => void list.refetch()}>Reintentar</Button></div> :
        <DataTable columns={columns} data={list.data?.items ?? []} isLoading={list.isLoading} page={page} pageSize={pageSize} pageCount={list.data?.totalPages} totalItems={list.data?.totalCount} enableRowSelection={false} onRowClick={canConfigure ? setEditing : undefined} onPaginationChange={(next, size) => { setPage(next); setPageSize(size); }}/>}
    </CardContent>
    {editing !== undefined && <AccountEditor tenantId={tenantId} account={editing} types={types.data ?? []}
      typesLoading={types.isPending} typesError={types.error} onRetryTypes={() => void types.refetch()}
      onClose={() => setEditing(undefined)} onSaved={saved}/>}
  </Card>;
}
