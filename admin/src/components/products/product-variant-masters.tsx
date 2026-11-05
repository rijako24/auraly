"use client";

import { useAuthStore } from "@/stores/auth-store";
import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Palette, Ruler } from "lucide-react";
import { toast } from "sonner";
import { MasterListPanel } from "@/components/masters/master-list-panel";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { productMerchandisingApi, type ProductVariantAttribute, type ProductVariantOption } from "@/services/api/product-merchandising";
import { useProductVariantOptions } from "./product-variant-fields";
import { useTenantContextStore } from "@/stores/tenant-context-store";

export function ProductVariantMasters() {
  const canManage = useAuthStore(state => state.user?.permissions.includes("catalog.update") ?? false);
  return <div className="grid gap-5 xl:grid-cols-2"><VariantMaster attribute="colors" title="Colores" singular="color" canManage={canManage} /><VariantMaster attribute="sizes" title="Tallas" singular="talla" canManage={canManage} /></div>;
}

function VariantMaster({ attribute, title, singular, canManage }: {
  attribute: ProductVariantAttribute; title: string; singular: string; canManage: boolean;
}) {
  const options = useProductVariantOptions(attribute);
  const queryClient = useQueryClient();
  const tenantId = useTenantContextStore(state => state.selectedTenantId);
  const [editor, setEditor] = useState<ProductVariantOption | null>(null);
  const save = useMutation({ mutationFn: () => productMerchandisingApi.saveVariantOption(attribute, editor!.id || null, editor!),
    onSuccess: value => {
      queryClient.setQueryData<ProductVariantOption[]>(["product-variant-options", tenantId, attribute], current =>
        [...(current ?? []).filter(item => item.id !== value.id), value].sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name)));
      setEditor(null); toast.success("Registro guardado");
    }, onError: error => toast.error(error instanceof Error ? error.message : "No fue posible guardar") });
  return <div className="space-y-2">
    {options.isLoading && <p role="status">Cargando {title.toLocaleLowerCase()}…</p>}
    {options.isError && <p role="alert" className="text-sm text-destructive">No fue posible cargar {title.toLocaleLowerCase()}. <button type="button" className="underline" onClick={() => void options.refetch()}>Reintentar</button></p>}
    <MasterListPanel title={title} description="Catálogo opcional compartido por las sedes de la empresa." createLabel={`Crear ${singular}`} rows={(options.data ?? []).map(item => ({ id: item.id, name: item.name, detail: `${item.code} · Orden ${item.sortOrder}`, active: item.isActive }))} canManage={canManage} icon={attribute === "colors" ? <Palette className="h-5 w-5" /> : <Ruler className="h-5 w-5" />}
      onCreate={() => setEditor({ id: "", code: "", name: "", isActive: true, sortOrder: 0 })} onEdit={id => setEditor(options.data!.find(item => item.id === id)!)} />
    <Dialog open={!!editor} onOpenChange={open => { if (!open && !save.isPending) setEditor(null); }}><DialogContent><DialogHeader><DialogTitle>{editor?.id ? "Editar" : "Crear"} {singular}</DialogTitle></DialogHeader>
      {editor && <div className="space-y-4"><div><Label htmlFor={`${attribute}-code`}>Código</Label><Input id={`${attribute}-code`} disabled={!!editor.id || save.isPending} maxLength={32} value={editor.code} onChange={event => setEditor({ ...editor, code: event.target.value })} /></div>
        <div><Label htmlFor={`${attribute}-name`}>Nombre</Label><Input id={`${attribute}-name`} maxLength={120} disabled={save.isPending} value={editor.name} onChange={event => setEditor({ ...editor, name: event.target.value })} /></div>
        <div><Label htmlFor={`${attribute}-order`}>Orden</Label><Input id={`${attribute}-order`} type="number" min={0} step={1} disabled={save.isPending} value={editor.sortOrder} onChange={event => setEditor({ ...editor, sortOrder: Number(event.target.value) })} /></div>
        <label className="flex items-center justify-between">Activo<Switch checked={editor.isActive} disabled={save.isPending} onCheckedChange={isActive => setEditor({ ...editor, isActive })} /></label></div>}
      <DialogFooter><Button variant="outline" disabled={save.isPending} onClick={() => setEditor(null)}>Cancelar</Button><Button disabled={!editor?.code.trim() || !editor.name.trim() || !Number.isInteger(editor.sortOrder) || editor.sortOrder < 0 || save.isPending} onClick={() => save.mutate()}>Guardar</Button></DialogFooter>
    </DialogContent></Dialog>
  </div>;
}
