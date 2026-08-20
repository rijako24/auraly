"use client";

import { useMemo, useState } from "react";
import { ChevronDown, ChevronRight, FolderPlus, Network, Pencil, Search, UnfoldVertical } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";

export type MasterHierarchyNode = {
  id: string;
  parentId: string | null;
  level: number;
  name: string;
  code?: string;
  active: boolean;
};

export function MasterHierarchyExplorer({
  title,
  description,
  levels,
  nodes,
  loading,
  error,
  onRetry,
  canManage,
  onCreate,
  onEdit,
  rootCreateLabel,
}: {
  title: string;
  description: string;
  levels: readonly string[];
  nodes: MasterHierarchyNode[];
  loading?: boolean;
  error?: string | null;
  onRetry?: () => void;
  canManage: boolean;
  onCreate: (level: number, parent: MasterHierarchyNode | null) => void;
  onEdit: (node: MasterHierarchyNode) => void;
  rootCreateLabel?: string;
}) {
  const [query, setQuery] = useState("");
  const [showInactive, setShowInactive] = useState(false);
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set());
  const children = useMemo(() => {
    const map = new Map<string | null, MasterHierarchyNode[]>();
    for (const node of nodes) {
      const bucket = map.get(node.parentId) ?? [];
      bucket.push(node);
      map.set(node.parentId, bucket);
    }
    for (const bucket of map.values()) bucket.sort((a, b) => a.name.localeCompare(b.name, "es"));
    return map;
  }, [nodes]);
  const byId = useMemo(() => new Map(nodes.map((node) => [node.id, node])), [nodes]);
  const normalized = query.trim().toLocaleLowerCase("es");
  const matching = useMemo(() => {
    if (!normalized) return null;
    const result = new Set<string>();
    for (const node of nodes) {
      if (`${node.name} ${node.code ?? ""} ${levels[node.level] ?? ""}`.toLocaleLowerCase("es").includes(normalized)) {
        let current: MasterHierarchyNode | undefined = node;
        while (current) {
          result.add(current.id);
          current = current.parentId ? byId.get(current.parentId) : undefined;
        }
      }
    }
    return result;
  }, [byId, levels, nodes, normalized]);

  const visible = useMemo(() => {
    const result: MasterHierarchyNode[] = [];
    const visit = (parentId: string | null) => {
      for (const node of children.get(parentId) ?? []) {
        if (!showInactive && !node.active) continue;
        if (matching && !matching.has(node.id)) continue;
        result.push(node);
        if (matching || expanded.has(node.id)) visit(node.id);
      }
    };
    visit(null);
    return result;
  }, [children, expanded, matching, showInactive]);

  const expandAll = () => setExpanded(new Set(nodes.filter((node) => (children.get(node.id)?.length ?? 0) > 0).map((node) => node.id)));
  const toggle = (id: string) => setExpanded((current) => {
    const next = new Set(current);
    if (next.has(id)) next.delete(id); else next.add(id);
    return next;
  });

  return <section className="overflow-hidden rounded-3xl border bg-card shadow-sm">
    <header className="border-b bg-gradient-to-r from-teal-50 via-white to-cyan-50 p-5 sm:p-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="flex gap-3">
          <span className="mt-0.5 rounded-2xl bg-teal-600 p-3 text-white shadow-sm"><Network className="h-5 w-5" /></span>
          <div><h2 className="text-xl font-bold">{title}</h2><p className="mt-1 max-w-2xl text-sm text-muted-foreground">{description}</p></div>
        </div>
        <Button disabled={!canManage} onClick={() => onCreate(0, null)}><FolderPlus className="mr-2 h-4 w-4" />{rootCreateLabel ?? `Nuevo ${levels[0].toLocaleLowerCase("es")}`}</Button>
      </div>
      <div className="mt-5 grid gap-3 md:grid-cols-[minmax(260px,1fr)_auto_auto]">
        <div className="relative"><Search className="absolute left-3 top-3 h-4 w-4 text-muted-foreground" /><Input value={query} onChange={(event) => setQuery(event.target.value)} className="pl-9" placeholder={`Buscar en toda la jerarquía de ${title.toLocaleLowerCase("es")}`} /></div>
        <label className="flex min-h-10 items-center justify-between gap-3 rounded-xl border bg-white px-3 text-sm"><span>Mostrar inactivos</span><Switch checked={showInactive} onCheckedChange={setShowInactive} /></label>
        <Button type="button" variant="outline" onClick={expandAll}><UnfoldVertical className="mr-2 h-4 w-4" />Expandir todo</Button>
      </div>
    </header>
    <div className="grid border-b bg-muted/25 px-5 py-3 text-xs font-bold uppercase tracking-wide text-muted-foreground" style={{ gridTemplateColumns: `repeat(${levels.length}, minmax(0, 1fr))` }}>
      {levels.map((level, index) => <span key={level} className="flex items-center gap-2"><span className="grid h-6 w-6 place-items-center rounded-full bg-teal-100 text-teal-800">{index + 1}</span>{level}</span>)}
    </div>
    <div className="min-h-64 p-3">
      {loading ? <p className="p-12 text-center text-sm text-muted-foreground">Cargando jerarquía…</p> : error ? <div className="grid place-items-center gap-3 p-12 text-center"><p className="text-sm font-medium text-destructive">{error}</p>{onRetry&&<Button variant="outline" onClick={onRetry}>Reintentar</Button>}</div> : visible.length === 0 ? <p className="p-12 text-center text-sm text-muted-foreground">{query ? "No hay coincidencias." : "Aún no hay registros."}</p> : visible.map((node) => {
        const childCount = (children.get(node.id) ?? []).filter((child) => showInactive || child.active).length;
        const isExpanded = matching ? true : expanded.has(node.id);
        return <div key={node.id} data-testid={`master-node-${node.id}`} className="group mb-1 grid min-h-12 items-center rounded-xl border border-transparent hover:border-teal-200 hover:bg-teal-50/60" style={{ gridTemplateColumns: `repeat(${levels.length}, minmax(0, 1fr))` }}>
          <div style={{ gridColumnStart: node.level + 1 }} className="flex min-w-0 items-center gap-1 px-2 py-2">
            {childCount > 0 ? <Button size="icon" variant="ghost" className="h-8 w-8 shrink-0" onClick={() => toggle(node.id)} aria-label={`${isExpanded ? "Contraer" : "Expandir"} ${node.name}`}>{isExpanded ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}</Button> : <span className="w-8" />}
            <button type="button" onClick={() => childCount > 0 && toggle(node.id)} className="min-w-0 flex-1 text-left"><span className="block truncate font-semibold">{node.name}</span><span className="block truncate text-xs text-muted-foreground">{node.code ? `${node.code} · ` : ""}{levels[node.level]}{childCount ? ` · ${childCount} ${childCount === 1 ? "hijo" : "hijos"}` : ""}</span></button>
            {!node.active && <Badge variant="outline">Inactivo</Badge>}
            <Button size="icon" variant="ghost" disabled={!canManage} onClick={() => onEdit(node)} aria-label={`Editar ${node.name}`}><Pencil className="h-4 w-4" /></Button>
            {node.level < levels.length - 1 && <Button size="icon" variant="ghost" disabled={!canManage || !node.active} onClick={() => onCreate(node.level + 1, node)} aria-label={`Crear ${levels[node.level + 1].toLocaleLowerCase("es")} en ${node.name}`}><FolderPlus className="h-4 w-4" /></Button>}
          </div>
        </div>;
      })}
    </div>
  </section>;
}
