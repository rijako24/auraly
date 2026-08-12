"use client";
import { useQuery } from "@tanstack/react-query";
import { Boxes } from "lucide-react";
import { inventoryApi } from "@/services/api/inventory";

export function ProductInventoryByWarehouse({ productId, manageInventory }: { productId: string; manageInventory: boolean }) {
  const warehouses = useQuery({ queryKey: ["inventory-warehouses"], queryFn: inventoryApi.warehouses, enabled: manageInventory });
  const balances = useQuery({ queryKey: ["product-inventory-by-warehouse", productId], queryFn: () => inventoryApi.balances({ productId, page: 1, pageSize: 200 }), enabled: manageInventory });
  if (!manageInventory) return <section className="rounded-2xl border bg-background p-5"><h3 className="flex items-center gap-2 font-semibold"><Boxes className="h-5 w-5 text-primary" />Existencias por bodega</h3><p className="mt-3 text-sm text-muted-foreground">Este producto no controla existencias.</p></section>;
  const byWarehouse = new Map((balances.data?.items ?? []).map((item) => [item.warehouseId, item]));
  return <section className="rounded-2xl border bg-background p-5 shadow-sm"><header className="mb-4"><h3 className="flex items-center gap-2 font-semibold"><Boxes className="h-5 w-5 text-primary" />Existencias por bodega</h3><p className="mt-1 text-xs text-muted-foreground">Consulta informativa; los movimientos se realizan desde Inventario.</p></header><div className="grid gap-2 sm:grid-cols-2">{(warehouses.data ?? []).map((warehouse) => <div key={warehouse.warehouseId} className="flex items-center justify-between rounded-xl border p-3"><span><b className="block text-sm">{warehouse.name}</b><small className="text-muted-foreground">{warehouse.code}</small></span><strong className="text-lg">{(byWarehouse.get(warehouse.warehouseId)?.quantityOnHand ?? 0).toLocaleString("es-CO", { maximumFractionDigits: 3 })}</strong></div>)}</div>{!warehouses.isLoading && !(warehouses.data ?? []).length && <p className="rounded-xl border border-dashed p-5 text-center text-sm text-muted-foreground">No hay bodegas activas configuradas.</p>}</section>;
}
