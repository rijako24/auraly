"use client";

import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { MasterHierarchyExplorer, type MasterHierarchyNode } from "@/components/masters/master-hierarchy-explorer";
import { ProductCommercialMasters } from "@/components/products/product-commercial-masters";
import { useCreateProductCategory, useProductCategories } from "@/hooks/use-products";
import { productsApi, type ProductCategory } from "@/services/api/products";
import { useBusinessContextStore } from "@/stores/business-context-store";

const levels = ["Área", "Línea", "Grupo", "Subgrupo"] as const;
type Editor = { category: ProductCategory | null; parent: MasterHierarchyNode | null; level: number; name: string; order: string; active: boolean };

export function ProductCategoriesMaster({ canManage }: { canManage: boolean }) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const categories = useProductCategories(true);
  const create = useCreateProductCategory();
  const client = useQueryClient();
  const [editor, setEditor] = useState<Editor | null>(null);
  const [saving, setSaving] = useState(false);
  const nodes: MasterHierarchyNode[] = (categories.data ?? []).map((item) => ({ id: item.productCategoryId, parentId: item.parentProductCategoryId, level: item.depth - 1, name: item.name, active: item.isActive }));
  const openCreate = (level: number, parent: MasterHierarchyNode | null) => setEditor({ category: null, parent, level, name: "", order: "0", active: true });
  const openEdit = (node: MasterHierarchyNode) => setEditor({ category: (categories.data ?? []).find((item) => item.productCategoryId === node.id)!, parent: node.parentId ? nodes.find((item) => item.id === node.parentId) ?? null : null, level: node.level, name: node.name, order: String((categories.data ?? []).find((item) => item.productCategoryId === node.id)?.displayOrder ?? 0), active: node.active });
  const save = async () => {
    if (!businessId || !editor?.name.trim()) return;
    setSaving(true);
    const request = { parentProductCategoryId: editor.parent?.id ?? null, name: editor.name.trim(), displayOrder: Number(editor.order) || 0, isBrowsable: true, isActive: editor.active };
    try {
      if (editor.category) await productsApi.updateCategory(businessId, editor.category.productCategoryId, request); else await create.mutateAsync(request);
      await client.invalidateQueries({ queryKey: ["product-categories", businessId] });
      toast.success(`${levels[editor.level]} guardada`);
      setEditor(null);
    } catch (error) { toast.error(error instanceof Error ? error.message : "No fue posible guardar la clasificación"); }
    finally { setSaving(false); }
  };
  return <div className="space-y-5">
    <MasterHierarchyExplorer title="Clasificación comercial" description="Áreas, líneas, grupos y subgrupos en un árbol único. Puedes buscar cualquier nivel, ver su contexto y administrar hijos sin saltar entre columnas." levels={levels} nodes={nodes} loading={categories.isLoading} canManage={canManage} onCreate={openCreate} onEdit={openEdit} rootCreateLabel="Nueva área" />
    <Dialog open={!!editor} onOpenChange={(open) => !open && setEditor(null)}><DialogContent><DialogHeader><DialogTitle>{editor?.category ? "Editar" : "Crear"} {editor ? levels[editor.level].toLocaleLowerCase("es") : "clasificación"}</DialogTitle></DialogHeader>
      {editor?.parent && <div className="rounded-xl border bg-teal-50 p-3 text-sm"><span className="text-muted-foreground">Se guardará dentro de</span><strong className="ml-2">{editor.parent.name}</strong></div>}
      <div className="grid gap-4 sm:grid-cols-[1fr_140px]"><div className="space-y-2"><Label htmlFor="category-name">Nombre</Label><Input id="category-name" autoFocus value={editor?.name ?? ""} onChange={(event) => editor && setEditor({ ...editor, name: event.target.value })} /></div><div className="space-y-2"><Label htmlFor="category-order">Orden visual</Label><Input id="category-order" type="number" value={editor?.order ?? "0"} onChange={(event) => editor && setEditor({ ...editor, order: event.target.value })} /></div></div>
      <label className="flex items-center justify-between rounded-xl border p-3"><span><strong className="block text-sm">Clasificación activa</strong><small className="text-muted-foreground">Al desactivarla deja de estar disponible para nuevos productos.</small></span><Switch checked={editor?.active ?? true} onCheckedChange={(active) => editor && setEditor({ ...editor, active })} /></label>
      <DialogFooter><Button variant="outline" onClick={() => setEditor(null)}>Cancelar</Button><Button disabled={!editor?.name.trim() || saving} onClick={() => void save()}>{saving ? "Guardando…" : "Guardar"}</Button></DialogFooter>
    </DialogContent></Dialog>
    <ProductCommercialMasters canManage={canManage} />
  </div>;
}
