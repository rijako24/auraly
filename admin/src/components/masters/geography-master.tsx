"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { MasterHierarchyExplorer, type MasterHierarchyNode } from "@/components/masters/master-hierarchy-explorer";
import { partiesApi, type GeographyHierarchyItem } from "@/services/api/parties";

const levels = ["País", "Departamento", "Ciudad"] as const;
type Editor = { item: GeographyHierarchyItem | null; parent: MasterHierarchyNode | null; level: number; code: string; name: string; active: boolean };

export function GeographyMaster({ canManage }: { canManage: boolean }) {
  const client = useQueryClient();
  const query = useQuery({ queryKey: ["geography-hierarchy"], queryFn: () => partiesApi.geographyHierarchy(true) });
  const [editor, setEditor] = useState<Editor | null>(null);
  const nodes: MasterHierarchyNode[] = (query.data ?? []).map((item) => ({ id: item.id, parentId: item.parentId, level: item.level === "Country" ? 0 : item.level === "Division" ? 1 : 2, name: item.name, code: item.code, active: item.isActive }));
  const save = useMutation({
    mutationFn: async () => {
      if (!editor) throw new Error("No hay cambios para guardar.");
      const request = { code: editor.code.trim().toUpperCase(), name: editor.name.trim(), isActive: editor.active };
      if (editor.level === 0) return editor.item ? partiesApi.updateCountry(editor.item.id, request) : partiesApi.createCountry(request);
      if (editor.level === 1) {
        const body = { ...request, countryId: editor.parent!.id, divisionType: "Department" };
        return editor.item ? partiesApi.updateDivision(editor.item.id, body) : partiesApi.createDivision(body);
      }
      const body = { ...request, administrativeDivisionId: editor.parent!.id };
      return editor.item ? partiesApi.updateCity(editor.item.id, body) : partiesApi.createCity(body);
    },
    onSuccess: async () => { await client.invalidateQueries({ queryKey: ["geography"] }); await client.invalidateQueries({ queryKey: ["geography-hierarchy"] }); toast.success("Ubicación guardada"); setEditor(null); },
    onError: (error) => toast.error(error instanceof Error ? error.message : "No fue posible guardar la ubicación"),
  });
  const openCreate = (level: number, parent: MasterHierarchyNode | null) => setEditor({ item: null, parent, level, code: "", name: "", active: true });
  const openEdit = (node: MasterHierarchyNode) => {
    const item = (query.data ?? []).find((candidate) => candidate.id === node.id)!;
    const parent = node.parentId ? nodes.find((candidate) => candidate.id === node.parentId) ?? null : null;
    setEditor({ item, parent, level: node.level, code: node.code ?? "", name: node.name, active: node.active });
  };
  return <>
    <MasterHierarchyExplorer title="Ubicación geográfica" description="Países, departamentos y ciudades en un solo árbol. Busca en toda la estructura, expande lo necesario y administra cada registro en contexto." levels={levels} nodes={nodes} loading={query.isLoading} canManage={canManage} onCreate={openCreate} onEdit={openEdit} />
    <Dialog open={!!editor} onOpenChange={(open) => !open && setEditor(null)}><DialogContent><DialogHeader><DialogTitle>{editor?.item ? "Editar" : "Crear"} {editor ? levels[editor.level].toLocaleLowerCase("es") : "ubicación"}</DialogTitle></DialogHeader>
      {editor?.parent && <div className="rounded-xl border bg-teal-50 p-3 text-sm"><span className="text-muted-foreground">Se guardará dentro de</span><strong className="ml-2">{editor.parent.name}</strong></div>}
      <div className="grid gap-4 sm:grid-cols-[140px_1fr]"><div className="space-y-2"><Label htmlFor="geography-code">Código</Label><Input id="geography-code" value={editor?.code ?? ""} onChange={(event) => editor && setEditor({ ...editor, code: event.target.value.toUpperCase() })} maxLength={16} /></div><div className="space-y-2"><Label htmlFor="geography-name">Nombre</Label><Input id="geography-name" autoFocus value={editor?.name ?? ""} onChange={(event) => editor && setEditor({ ...editor, name: event.target.value })} /></div></div>
      <label className="flex items-center justify-between rounded-xl border p-3"><span><strong className="block text-sm">Registro activo</strong><small className="text-muted-foreground">Al desactivarlo deja de estar disponible para nuevas selecciones.</small></span><Switch checked={editor?.active ?? true} onCheckedChange={(active) => editor && setEditor({ ...editor, active })} /></label>
      <DialogFooter><Button variant="outline" onClick={() => setEditor(null)}>Cancelar</Button><Button disabled={!editor?.code.trim() || !editor?.name.trim() || save.isPending} onClick={() => save.mutate()}>{save.isPending ? "Guardando…" : "Guardar"}</Button></DialogFooter>
    </DialogContent></Dialog>
  </>;
}
