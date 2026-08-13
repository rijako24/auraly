"use client";

import { useMemo, useState, type ReactNode } from "react";
import { Pencil, Plus, Search } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";

export type MasterListRow = { id: string; name: string; detail?: string; active: boolean };
export function MasterListPanel({ title, description, createLabel, rows, canManage, icon, onCreate, onEdit }: { title: string; description: string; createLabel: string; rows: MasterListRow[]; canManage: boolean; icon: ReactNode; onCreate: () => void; onEdit: (id: string) => void }) {
  const [search, setSearch] = useState("");
  const [showInactive, setShowInactive] = useState(false);
  const filtered = useMemo(() => rows.filter((row) => (showInactive || row.active) && (!search.trim() || `${row.name} ${row.detail ?? ""}`.toLocaleLowerCase("es").includes(search.trim().toLocaleLowerCase("es")))), [rows, search, showInactive]);
  return <section className="overflow-hidden rounded-3xl border bg-card shadow-sm">
    <header className="border-b bg-muted/20 p-5"><div className="flex items-start justify-between gap-4"><div className="flex gap-3"><span className="rounded-xl bg-primary/10 p-2 text-primary">{icon}</span><div><h3 className="font-bold">{title}</h3><p className="mt-1 text-xs text-muted-foreground">{description}</p></div></div><Button size="sm" disabled={!canManage} onClick={onCreate}><Plus className="mr-2 h-4 w-4" />{createLabel}</Button></div>
      <div className="mt-4 flex flex-col gap-2 sm:flex-row"><div className="relative flex-1"><Search className="absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" /><Input value={search} onChange={(event) => setSearch(event.target.value)} className="pl-9" placeholder={`Buscar ${title.toLocaleLowerCase("es")}`} /></div><label className="flex items-center justify-between gap-3 rounded-xl border bg-background px-3 text-sm"><span>Mostrar inactivos</span><Switch checked={showInactive} onCheckedChange={setShowInactive} /></label></div>
    </header>
    <div className="max-h-[28rem] space-y-2 overflow-auto p-3">{filtered.map((row) => <button key={row.id} type="button" onClick={() => onEdit(row.id)} className="flex w-full items-center gap-3 rounded-xl border p-3 text-left transition hover:border-primary/30 hover:bg-primary/5"><span className="min-w-0 flex-1"><strong className="block truncate">{row.name}</strong>{row.detail && <small className="block truncate text-muted-foreground">{row.detail}</small>}</span><Badge variant={row.active ? "secondary" : "outline"}>{row.active ? "Activo" : "Inactivo"}</Badge><Pencil className="h-4 w-4 text-muted-foreground" /></button>)}{!filtered.length && <p className="p-8 text-center text-sm text-muted-foreground">No hay registros que coincidan.</p>}</div>
  </section>;
}
