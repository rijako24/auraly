"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Percent } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { MasterListPanel } from "@/components/masters/master-list-panel";
import { taxProfilesApi, type TaxProfile } from "@/services/api/tax-profiles";
import { useBusinessContextStore } from "@/stores/business-context-store";

export function VatMaster({ canManage }: { canManage: boolean }) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const client = useQueryClient();
  const query = useQuery({ queryKey: ["tax-profiles", businessId], queryFn: () => taxProfilesApi.list(true), enabled: !!businessId });
  const [editing, setEditing] = useState<TaxProfile | null | undefined>();
  const [code, setCode] = useState(""); const [name, setName] = useState(""); const [rate, setRate] = useState(""); const [active, setActive] = useState(true);
  const open = (value: TaxProfile | null) => { setEditing(value); setCode(value?.code ?? ""); setName(value?.name ?? ""); setRate(value ? String(value.rate) : ""); setActive(value?.isActive ?? true); };
  const save = useMutation({ mutationFn: async () => {
    if (!businessId) throw new Error("Selecciona una sede.");
    const request = { businessId, code: code.trim().toUpperCase(), dianTaxCode: editing?.dianTaxCode ?? "01", name: name.trim(), rate: Number(rate.replace(",", ".")), isActive: active };
    return editing ? taxProfilesApi.update(editing.taxProfileId, request) : taxProfilesApi.create(request);
  }, onSuccess: async () => { await client.invalidateQueries({ queryKey: ["tax-profiles", businessId] }); toast.success("IVA guardado"); setEditing(undefined); }, onError: (error) => toast.error(error instanceof Error ? error.message : "No fue posible guardar el IVA") });
  if (!businessId) return <Card><CardContent className="p-8 text-center text-muted-foreground">Selecciona una sede.</CardContent></Card>;
  return <>
    <MasterListPanel title="Tarifas de IVA" description="Una sola fuente para IVA de compra y de venta." createLabel="Nuevo IVA" rows={(query.data ?? []).map((tax) => ({ id: tax.taxProfileId, name: tax.name, detail: `${tax.code} · ${tax.rate.toLocaleString("es-CO")} % · Código DIAN ${tax.dianTaxCode}`, active: tax.isActive }))} canManage={canManage} icon={<Percent className="h-5 w-5" />} onCreate={() => open(null)} onEdit={(id) => open((query.data ?? []).find((tax) => tax.taxProfileId === id)!)} />
    <Dialog open={editing !== undefined} onOpenChange={(value) => !value && setEditing(undefined)}><DialogContent><DialogHeader><DialogTitle>{editing ? "Editar" : "Nuevo"} IVA</DialogTitle></DialogHeader><div className="grid gap-4 sm:grid-cols-2"><div className="space-y-2"><Label>Código interno</Label><Input value={code} onChange={(event) => setCode(event.target.value)} placeholder="IVA-19" /></div><div className="space-y-2"><Label>Tarifa %</Label><Input inputMode="decimal" value={rate} onChange={(event) => setRate(event.target.value)} placeholder="19" /></div><div className="space-y-2 sm:col-span-2"><Label>Nombre</Label><Input value={name} onChange={(event) => setName(event.target.value)} placeholder="IVA 19%" /></div></div><label className="flex items-center justify-between rounded-xl border p-3"><span><strong className="block text-sm">IVA activo</strong><small className="text-muted-foreground">Los productos anteriores conservan el valor usado.</small></span><Switch checked={active} onCheckedChange={setActive} /></label><DialogFooter><Button variant="outline" onClick={() => setEditing(undefined)}>Cancelar</Button><Button onClick={() => save.mutate()} disabled={save.isPending || !code.trim() || !name.trim() || !Number.isFinite(Number(rate.replace(",", ".")))}>Guardar</Button></DialogFooter></DialogContent></Dialog>
  </>;
}
