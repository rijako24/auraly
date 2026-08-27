"use client";

import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ClipboardList, Pencil, Search } from "lucide-react";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { inventoryApi, type InventoryReasonItem } from "@/services/api/inventory";
import { accountingApi } from "@/services/api/accounting";
import { useReferenceOptions } from "@/hooks/use-reference-options";
const emptyForm = { operationType: "StockCount", name: "", isActive: true, displayOrder: 10, counterpartAccountingCategory: "InventoryDifferences" as string | null, defaultCostCenterId: null as string | null, requiresReference: false };
const accountableTypes = new Set(["StockCount", "InventoryAdjustment", "ProductConversion", "Damage"]);

export function InventoryReasonMaster({ canManage }: { canManage: boolean }) {
  const queryClient = useQueryClient();
  const operationTypeCatalog = useReferenceOptions("inventory-operation-type");
  const operationTypes = (operationTypeCatalog.data ?? []).map((option) => ({ value: option.code, label: option.label }));
  const query = useQuery({ queryKey: ["inventory-reasons", "master"], queryFn: () => inventoryApi.reasons({ includeInactive: true }) });
  const categories = useQuery({ queryKey: ["accounting-category-definitions"], queryFn: accountingApi.categoryDefinitions });
  const costCenters = useQuery({ queryKey: ["accounting-cost-centers"], queryFn: accountingApi.costCenters });
  const [search, setSearch] = useState("");
  const [showInactive, setShowInactive] = useState(false);
  const [selected, setSelected] = useState<InventoryReasonItem | null>(null);
  const [form, setForm] = useState(emptyForm);
  const rows = useMemo(() => (query.data ?? []).filter((item) => (showInactive || item.isActive) && (!search.trim() || item.name.toLocaleLowerCase("es").includes(search.trim().toLocaleLowerCase("es")))), [query.data, search, showInactive]);
  const save = useMutation({
    mutationFn: () => inventoryApi.saveReason(selected?.inventoryReasonId ?? null, form),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["inventory-reasons"] });
      toast.success(selected ? "Motivo actualizado" : "Motivo creado");
      setSelected(null); setForm(emptyForm);
    },
    onError: (error) => toast.error(error instanceof Error ? error.message : "No fue posible guardar el motivo"),
  });
  const edit = (item: InventoryReasonItem) => { setSelected(item); setForm({ operationType: item.operationType, name: item.name, isActive: item.isActive, displayOrder: item.displayOrder, counterpartAccountingCategory: item.counterpartAccountingCategory, defaultCostCenterId: item.defaultCostCenterId, requiresReference: item.requiresReference }); };
  const typeLabel = (value: string) => operationTypes.find((item) => item.value === value)?.label ?? value;
  const accountingRequired = accountableTypes.has(form.operationType);
  const categoryOptions = (categories.data ?? []).filter((item) => item.category !== "Inventory");

  return <div className="grid gap-5 xl:grid-cols-[1.2fr_.8fr]">
    <Card><CardHeader><CardTitle className="flex items-center gap-2"><ClipboardList className="h-5 w-5 text-primary" />Motivos de inventario</CardTitle><CardDescription>Cada operación muestra únicamente motivos activos compatibles. El documento conserva el motivo seleccionado.</CardDescription></CardHeader><CardContent>
      <div className="mb-4 flex flex-col gap-3 sm:flex-row"><div className="relative flex-1"><Search className="absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" /><Input value={search} onChange={(event) => setSearch(event.target.value)} className="pl-9" placeholder="Buscar motivo" /></div><label className="flex items-center gap-2 rounded-xl border px-3 text-sm"><Switch checked={showInactive} onCheckedChange={setShowInactive} />Mostrar inactivos</label></div>
      <div className="space-y-2">{rows.map((item) => <button key={item.inventoryReasonId} type="button" onClick={() => edit(item)} className="flex w-full items-center gap-3 rounded-xl border p-4 text-left hover:bg-muted/40"><span className="flex-1"><b>{item.name}</b><small className="mt-1 block text-muted-foreground">{typeLabel(item.operationType)}{item.counterpartAccountingCategory ? ` · ${item.counterpartAccountingCategory}` : ""}</small></span><Badge variant={item.isActive ? "secondary" : "outline"}>{item.isActive ? "Activo" : "Inactivo"}</Badge><Pencil className="h-4 w-4 text-muted-foreground" /></button>)}{!rows.length && <p className="rounded-xl border border-dashed p-8 text-center text-sm text-muted-foreground">No hay motivos que coincidan.</p>}</div>
    </CardContent></Card>
    <Card><CardHeader><CardTitle>{selected ? "Editar motivo" : "Nuevo motivo"}</CardTitle><CardDescription>El código interno lo genera Auraly y no se solicita al usuario.</CardDescription></CardHeader><CardContent className="space-y-4">
      <div className="space-y-2"><Label>Operación</Label><Select value={form.operationType} onValueChange={(value) => setForm({ ...form, operationType: value })} disabled={!canManage}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent>{operationTypes.map((item) => <SelectItem key={item.value} value={item.value}>{item.label}</SelectItem>)}</SelectContent></Select></div>
      <div className="space-y-2"><Label>Nombre visible</Label><Input value={form.name} onChange={(event) => setForm({ ...form, name: event.target.value })} disabled={!canManage} /></div>
      <div className="space-y-2"><Label>Categoría contable contrapartida{accountingRequired ? " *" : ""}</Label><Select value={form.counterpartAccountingCategory ?? "__none__"} onValueChange={(value) => setForm({ ...form, counterpartAccountingCategory: value === "__none__" ? null : value })} disabled={!canManage}><SelectTrigger><SelectValue placeholder="Sin efecto contable" /></SelectTrigger><SelectContent>{!accountingRequired && <SelectItem value="__none__">Sin efecto contable</SelectItem>}{categoryOptions.map((item) => <SelectItem key={item.category} value={item.category}>{item.displayName}</SelectItem>)}</SelectContent></Select><p className="text-xs text-muted-foreground">La cuenta concreta se resuelve desde el mapeo contable vigente al confirmar el documento.</p></div>
      <div className="space-y-2"><Label>Centro de costo por defecto</Label><Select value={form.defaultCostCenterId ?? "__default__"} onValueChange={(value) => setForm({ ...form, defaultCostCenterId: value === "__default__" ? null : value })} disabled={!canManage}><SelectTrigger><SelectValue placeholder="General del negocio" /></SelectTrigger><SelectContent><SelectItem value="__default__">General del negocio</SelectItem>{(costCenters.data ?? []).filter((item) => item.isActive).map((item) => <SelectItem key={item.costCenterId} value={item.costCenterId}>{item.code} · {item.name}</SelectItem>)}</SelectContent></Select></div>
      <div className="space-y-2"><Label>Orden</Label><Input type="number" min={0} max={9999} value={form.displayOrder} onChange={(event) => setForm({ ...form, displayOrder: Number(event.target.value) })} disabled={!canManage} /></div>
      <label className="flex items-center justify-between rounded-xl border p-3"><span><b className="block text-sm">Exigir referencia o soporte</b><small className="text-muted-foreground">La operación no podrá confirmarse sin una nota de referencia.</small></span><Switch checked={form.requiresReference} onCheckedChange={(value) => setForm({ ...form, requiresReference: value })} disabled={!canManage} /></label>
      <label className="flex items-center justify-between rounded-xl border p-3"><span><b className="block text-sm">Motivo activo</b><small className="text-muted-foreground">Los documentos anteriores conservan su trazabilidad.</small></span><Switch checked={form.isActive} onCheckedChange={(value) => setForm({ ...form, isActive: value })} disabled={!canManage} /></label>
      <div className="flex gap-2"><Button className="flex-1" onClick={() => save.mutate()} disabled={!canManage || !form.name.trim() || (accountingRequired && !form.counterpartAccountingCategory) || save.isPending}>{selected ? "Guardar cambios" : "Crear motivo"}</Button>{selected && <Button variant="outline" onClick={() => { setSelected(null); setForm(emptyForm); }}>Cancelar</Button>}</div>
    </CardContent></Card>
  </div>;
}
