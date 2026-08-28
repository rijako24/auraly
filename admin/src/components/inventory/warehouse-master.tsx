"use client";

import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Boxes, Pencil, Search } from "lucide-react";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { inventoryApi, type WarehouseMasterItem } from "@/services/api/inventory";

const emptyForm = { name: "", allowNegativeStockSales: false, priceFormationCostBasis: "LatestReceiptCost", useForSales: true, isActive: true };

export function WarehouseMaster({ canManage }: { canManage: boolean }) {
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: ["warehouse-masters"], queryFn: inventoryApi.warehouseMasters });
  const [search, setSearch] = useState("");
  const [showInactive, setShowInactive] = useState(false);
  const [selected, setSelected] = useState<WarehouseMasterItem | null>(null);
  const [form, setForm] = useState(emptyForm);
  const rows = useMemo(() => (query.data ?? []).filter((item) => (showInactive || item.isActive) && (!search.trim() || `${item.name} ${item.code}`.toLocaleLowerCase("es").includes(search.trim().toLocaleLowerCase("es")))), [query.data, search, showInactive]);
  const save = useMutation({
    mutationFn: () => inventoryApi.saveWarehouse(selected?.warehouseId ?? null, form),
    onSuccess: async () => {
      await Promise.all([queryClient.invalidateQueries({ queryKey: ["warehouse-masters"] }), queryClient.invalidateQueries({ queryKey: ["inventory-warehouses"] })]);
      toast.success(selected ? "Bodega actualizada" : "Bodega creada"); setSelected(null); setForm(emptyForm);
    },
    onError: (error) => toast.error(error instanceof Error ? error.message : "No fue posible guardar la bodega"),
  });
  const edit = (item: WarehouseMasterItem) => { setSelected(item); setForm({ name: item.name, allowNegativeStockSales: item.allowNegativeStockSales, priceFormationCostBasis: item.priceFormationCostBasis, useForSales: item.useForSales, isActive: item.isActive }); };
  return <div className="grid gap-5 xl:grid-cols-[1.2fr_.8fr]">
    <Card><CardHeader><CardTitle className="flex items-center gap-2"><Boxes className="h-5 w-5 text-primary" />Bodegas</CardTitle><CardDescription>El inventario, la política de negativos y el costo se administran por bodega.</CardDescription></CardHeader><CardContent>
      <div className="mb-4 flex flex-col gap-3 sm:flex-row"><div className="relative flex-1"><Search className="absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" /><Input value={search} onChange={(event) => setSearch(event.target.value)} className="pl-9" placeholder="Buscar bodega" /></div><label className="flex items-center gap-2 rounded-xl border px-3 text-sm"><Switch checked={showInactive} onCheckedChange={setShowInactive} />Mostrar inactivas</label></div>
      <div className="space-y-2">{rows.map((item) => <button key={item.warehouseId} type="button" onClick={() => edit(item)} className="flex w-full items-center gap-3 rounded-xl border p-4 text-left hover:bg-muted/40"><span className="flex-1"><b>{item.name}</b><small className="mt-1 block text-muted-foreground">{item.code} · {item.isSystem ? "Bodega interna del sistema" : item.useForSales ? "Bodega de venta" : "No disponible para operar"}</small></span><Badge variant={item.isActive ? "secondary" : "outline"}>{item.isActive ? "Activa" : "Inactiva"}</Badge><Pencil className="h-4 w-4 text-muted-foreground" /></button>)}{!rows.length && <p className="rounded-xl border border-dashed p-8 text-center text-sm text-muted-foreground">No hay bodegas que coincidan.</p>}</div>
    </CardContent></Card>
    <Card><CardHeader><CardTitle>{selected ? "Editar bodega" : "Nueva bodega"}</CardTitle><CardDescription>El código interno lo genera Auraly.</CardDescription></CardHeader><CardContent className="space-y-4">
      {selected?.isSystem && <div className="rounded-xl border border-amber-300/40 bg-amber-50 p-3 text-sm text-amber-900">Esta bodega soporta procesos internos de Auraly. Puedes consultarla, pero no cambiar su uso ni desactivarla.</div>}
      <div className="space-y-2"><Label>Nombre</Label><Input value={form.name} onChange={(event) => setForm({ ...form, name: event.target.value })} disabled={!canManage || selected?.isSystem} /></div>
      <div className="space-y-2"><Label>Base para formar precios</Label><Select value={form.priceFormationCostBasis} onValueChange={(value) => setForm({ ...form, priceFormationCostBasis: value })} disabled={!canManage}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value="LatestReceiptCost">Último costo recibido</SelectItem><SelectItem value="WeightedAverageCost">Costo promedio ponderado</SelectItem></SelectContent></Select></div>
      <label className="flex items-center justify-between rounded-xl border p-3"><span><b className="block text-sm">Permitir inventario negativo</b><small className="text-muted-foreground">Las ventas consultan esta política.</small></span><Switch checked={form.allowNegativeStockSales} onCheckedChange={(value) => setForm({ ...form, allowNegativeStockSales: value })} disabled={!canManage} /></label>
      <label className="flex items-center justify-between rounded-xl border p-3"><span><b className="block text-sm">Bodega de venta</b><small className="text-muted-foreground">Controla únicamente su disponibilidad para enrolamiento y ventas. Toda bodega creada y activa sigue disponible en inventario.</small></span><Switch checked={form.useForSales} onCheckedChange={(value) => setForm({ ...form, useForSales: value })} disabled={!canManage || selected?.isSystem} /></label>
      <label className="flex items-center justify-between rounded-xl border p-3"><span><b className="block text-sm">Bodega activa</b><small className="text-muted-foreground">Conserva el historial cuando se desactiva.</small></span><Switch checked={form.isActive} onCheckedChange={(value) => setForm({ ...form, isActive: value })} disabled={!canManage || selected?.isSystem} /></label>
      <div className="flex gap-2"><Button className="flex-1" onClick={() => save.mutate()} disabled={!canManage || selected?.isSystem || !form.name.trim() || save.isPending}>{selected ? "Guardar cambios" : "Crear bodega"}</Button>{selected && <Button variant="outline" onClick={() => { setSelected(null); setForm(emptyForm); }}>Cancelar</Button>}</div>
    </CardContent></Card>
  </div>;
}
