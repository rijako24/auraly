"use client";

import { useMemo, useState } from "react";
import { ChevronRight, FolderPlus, Layers3, Pencil, Save, X } from "lucide-react";
import { useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { useCreateProductCategory, useProductCategories } from "@/hooks/use-products";
import { productsApi, type ProductCategory } from "@/services/api/products";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { ProductCommercialMasters } from "@/components/products/product-commercial-masters";

const LEVEL_NAMES = ["Área", "Línea", "Grupo", "Subgrupo"] as const;

type EditorState = {
  category: ProductCategory | null;
  parent: ProductCategory | null;
  depth: number;
  name: string;
  displayOrder: string;
  isActive: boolean;
};

export function ProductCategoriesMaster({ canManage }: { canManage: boolean }) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const categories = useProductCategories(true);
  const createCategory = useCreateProductCategory();
  const queryClient = useQueryClient();
  const [selection, setSelection] = useState<Array<ProductCategory | null>>([null, null, null, null]);
  const [editor, setEditor] = useState<EditorState | null>(null);
  const [saving, setSaving] = useState(false);

  const columns = useMemo(() => {
    const all = categories.data ?? [];
    return LEVEL_NAMES.map((_, index) => {
      const parentId = index === 0 ? null : selection[index - 1]?.productCategoryId ?? "__none__";
      if (parentId === "__none__") return [];
      return all.filter((item) => item.depth === index + 1 && item.parentProductCategoryId === parentId);
    });
  }, [categories.data, selection]);

  function select(level: number, category: ProductCategory) {
    setSelection((current) => current.map((item, index) => index < level ? item : index === level ? category : null));
    setEditor(null);
  }

  function beginCreate(depth: number) {
    const parent = depth === 1 ? null : selection[depth - 2];
    if (depth > 1 && !parent) return;
    setEditor({ category: null, parent, depth, name: "", displayOrder: "0", isActive: true });
  }

  function beginEdit(category: ProductCategory) {
    const parent = (categories.data ?? []).find((item) => item.productCategoryId === category.parentProductCategoryId) ?? null;
    setEditor({
      category,
      parent,
      depth: category.depth,
      name: category.name,
      displayOrder: String(category.displayOrder),
      isActive: category.isActive,
    });
  }

  async function save() {
    if (!businessId || !editor?.name.trim()) return;
    setSaving(true);
    const request = {
      parentProductCategoryId: editor.parent?.productCategoryId ?? null,
      name: editor.name.trim(),
      displayOrder: Number(editor.displayOrder) || 0,
      isBrowsable: true,
      isActive: editor.isActive,
    };
    try {
      const saved = editor.category
        ? await productsApi.updateCategory(businessId, editor.category.productCategoryId, request)
        : await createCategory.mutateAsync(request);
      await queryClient.invalidateQueries({ queryKey: ["product-categories", businessId] });
      setSelection((current) => current.map((item, index) => index < saved.depth - 1 ? item : index === saved.depth - 1 ? saved : null));
      setEditor(null);
      toast.success(`${LEVEL_NAMES[saved.depth - 1]} ${editor.category ? "actualizada" : "creada"}`);
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No fue posible guardar la clasificación");
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="space-y-5">
      <Card className="overflow-hidden">
        <CardHeader className="border-b bg-muted/20">
          <div className="flex flex-wrap items-start justify-between gap-4">
            <div>
              <CardTitle className="flex items-center gap-2"><Layers3 className="h-5 w-5 text-primary" />Clasificación comercial</CardTitle>
              <CardDescription className="mt-2 max-w-2xl">
                Construye el árbol en contexto. Selecciona un nivel para habilitar el siguiente; Auraly conserva la ruta completa del producto.
              </CardDescription>
            </div>
            <Button disabled={!canManage} onClick={() => beginCreate(1)} >
              <FolderPlus className="mr-2 h-4 w-4" />Nueva área
            </Button>
          </div>
        </CardHeader>
        <CardContent className="p-0">
          <div className="grid min-h-[360px] lg:grid-cols-4">
            {LEVEL_NAMES.map((levelName, levelIndex) => {
              const parent = levelIndex === 0 ? null : selection[levelIndex - 1];
              const enabled = levelIndex === 0 || !!parent;
              return (
                <section key={levelName} className="border-b p-4 last:border-b-0 lg:border-b-0 lg:border-r lg:last:border-r-0">
                  <div className="mb-3 flex items-center justify-between gap-2">
                    <div>
                      <p className="text-xs font-bold uppercase tracking-wider text-muted-foreground">Nivel {levelIndex + 1}</p>
                      <h3 className="font-semibold">{levelName}</h3>
                    </div>
                    {enabled && canManage && (
                      <Button size="sm" variant="ghost" onClick={() => beginCreate(levelIndex + 1)} aria-label={`Crear ${levelName.toLowerCase()}`}>
                        <FolderPlus className="h-4 w-4" />
                      </Button>
                    )}
                  </div>
                  {!enabled ? (
                    <p className="rounded-xl border border-dashed p-4 text-center text-xs text-muted-foreground">Selecciona {LEVEL_NAMES[levelIndex - 1].toLowerCase()} primero.</p>
                  ) : categories.isLoading ? (
                    <p className="text-sm text-muted-foreground">Cargando…</p>
                  ) : columns[levelIndex].length ? (
                    <div className="space-y-2">
                      {columns[levelIndex].map((category) => {
                        const selected = selection[levelIndex]?.productCategoryId === category.productCategoryId;
                        return (
                          <div key={category.productCategoryId} className={`group flex items-center gap-1 rounded-xl border transition ${selected ? "border-primary bg-primary/5 shadow-sm" : "hover:bg-muted/40"}`}>
                            <button type="button" onClick={() => select(levelIndex, category)} className="min-w-0 flex-1 p-3 text-left">
                              <span className="flex items-center gap-2 font-medium"><span className="truncate">{category.name}</span>{!category.isActive && <Badge variant="outline">Inactiva</Badge>}</span>
                            </button>
                            <Button size="icon" variant="ghost" disabled={!canManage} onClick={() => beginEdit(category)} aria-label={`Editar ${category.name}`}>
                              <Pencil className="h-4 w-4" />
                            </Button>
                            {levelIndex < 3 && <ChevronRight className="mr-2 h-4 w-4 text-muted-foreground" />}
                          </div>
                        );
                      })}
                    </div>
                  ) : (
                    <div className="rounded-xl border border-dashed p-4 text-center">
                      <p className="text-sm font-medium">Aún no hay {levelName.toLowerCase()}</p>
                      <p className="mt-1 text-xs text-muted-foreground">Créala aquí; no necesitas escoger IDs ni padres manualmente.</p>
                    </div>
                  )}
                </section>
              );
            })}
          </div>
        </CardContent>
      </Card>

      {editor && (
        <Card className="border-primary/30 shadow-sm">
          <CardHeader>
            <div className="flex items-start justify-between gap-3">
              <div>
                <CardTitle>{editor.category ? "Editar" : "Crear"} {LEVEL_NAMES[editor.depth - 1].toLowerCase()}</CardTitle>
                <CardDescription>
                  {editor.parent ? <>Se guardará dentro de <b>{editor.parent.path}</b>.</> : "Será el primer nivel del árbol."}
                </CardDescription>
              </div>
              <Button size="icon" variant="ghost" onClick={() => setEditor(null)} aria-label="Cerrar editor"><X className="h-4 w-4" /></Button>
            </div>
          </CardHeader>
          <CardContent className="grid gap-5 md:grid-cols-[1fr_180px_auto] md:items-end">
            <div className="space-y-2"><Label htmlFor="category-name">Nombre</Label><Input id="category-name" autoFocus value={editor.name} onChange={(event) => setEditor({ ...editor, name: event.target.value })} onKeyDown={(event) => event.key === "Enter" && void save()} /></div>
            <div className="space-y-2"><Label htmlFor="category-order">Orden visual</Label><Input id="category-order" type="number" value={editor.displayOrder} onChange={(event) => setEditor({ ...editor, displayOrder: event.target.value })} /></div>
            <label className="flex h-10 items-center justify-between gap-3 rounded-xl border px-3 text-sm"><span>Activa</span><Switch checked={editor.isActive} onCheckedChange={(checked) => setEditor({ ...editor, isActive: checked })} /></label>
            <div className="flex gap-2 md:col-span-3 md:justify-end"><Button variant="outline" onClick={() => setEditor(null)}>Cancelar</Button><Button onClick={() => void save()} disabled={saving || !editor.name.trim()}><Save className="mr-2 h-4 w-4" />{saving ? "Guardando…" : "Guardar"}</Button></div>
          </CardContent>
        </Card>
      )}
      <ProductCommercialMasters canManage={canManage} />
    </div>
  );
}
