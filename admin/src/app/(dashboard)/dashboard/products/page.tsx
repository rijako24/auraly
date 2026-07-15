"use client";

import { useMemo, useState } from "react";
import type { ColumnDef } from "@tanstack/react-table";
import { Power, Search } from "lucide-react";
import { toast } from "sonner";

import { DataTable } from "@/components/tables/data-table";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { useProducts, useUpdateProductStatus } from "@/hooks/use-products";
import { formatCurrency } from "@/lib/utils";
import type { Product } from "@/services/api/products";

export default function ProductsPage() {
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState("");
  const [includeInactive, setIncludeInactive] = useState(true);
  const { data, isLoading, isError, refetch } = useProducts({ page, pageSize: 20, search: search || undefined, includeInactive });
  const updateStatus = useUpdateProductStatus();
  const columns = useMemo<ColumnDef<Product>[]>(() => [
    { accessorKey: "name", header: "Producto", cell: ({ row }) => <div><p className="font-medium">{row.original.name}</p><p className="text-xs text-muted-foreground">{row.original.sku || "Sin SKU"}</p></div> },
    { accessorKey: "categoryName", header: "Categoría", cell: ({ row }) => row.original.categoryName || "Sin categoría" },
    { accessorKey: "unitPrice", header: "Precio", cell: ({ row }) => formatCurrency(row.original.unitPrice, row.original.currency || "COP") },
    { accessorKey: "stockQuantity", header: "Inventario", cell: ({ row }) => row.original.manageStock ? `${row.original.stockQuantity ?? 0} unidades` : <span className="text-muted-foreground">No controlado</span> },
    { accessorKey: "isActive", header: "Estado", cell: ({ row }) => <Badge variant={row.original.isActive ? "default" : "secondary"}>{row.original.isActive ? "Activo" : "Inactivo"}</Badge> },
    { id: "actions", header: "", cell: ({ row }) => <Button type="button" variant="ghost" size="sm" disabled={updateStatus.isPending} onClick={async () => { try { await updateStatus.mutateAsync({ productId: row.original.productId, isActive: !row.original.isActive }); toast.success(row.original.isActive ? "Producto desactivado" : "Producto activado"); } catch { toast.error("No se pudo actualizar el producto"); } }}><Power className="mr-2 h-4 w-4" />{row.original.isActive ? "Desactivar" : "Activar"}</Button> },
  ], [updateStatus]);
  return <div className="space-y-6"><div><h1 className="text-2xl font-semibold tracking-tight">Productos</h1><p className="text-muted-foreground">Catálogo sincronizado para ventas, pedidos y recomendaciones del agente.</p></div><div className="flex flex-col gap-3 rounded-xl border bg-card p-4 sm:flex-row sm:items-center"><div className="relative flex-1"><Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" /><Input className="pl-9" value={search} onChange={(event) => { setSearch(event.target.value); setPage(1); }} placeholder="Buscar por nombre o SKU" /></div><label className="flex items-center gap-2 text-sm"><Switch checked={includeInactive} onCheckedChange={(checked) => { setIncludeInactive(checked); setPage(1); }} />Incluir inactivos</label></div>{isError ? <div className="rounded-xl border border-destructive/30 p-6 text-sm">No se pudo cargar el catálogo. <Button variant="link" onClick={() => refetch()}>Reintentar</Button></div> : <DataTable columns={columns} data={data?.items ?? []} isLoading={isLoading} page={data?.page} pageSize={data?.pageSize} pageCount={data?.totalPages} totalItems={data?.totalCount} onPaginationChange={(nextPage) => setPage(nextPage)} searchKey="name" searchPlaceholder="Buscar en esta página..." enableRowSelection={false} />}</div>;
}