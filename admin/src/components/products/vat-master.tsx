"use client";

import { useState } from "react";
import { Percent, Pencil, Plus } from "lucide-react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { taxProfilesApi, type TaxProfile } from "@/services/api/tax-profiles";
import { useBusinessContextStore } from "@/stores/business-context-store";

export function VatMaster({ canManage }: { canManage: boolean }) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const client = useQueryClient();
  const query = useQuery({
    queryKey: ["tax-profiles", businessId],
    queryFn: () => taxProfilesApi.list(true),
    enabled: !!businessId,
  });
  const [editing, setEditing] = useState<TaxProfile | null | undefined>();
  const [code, setCode] = useState("");
  const [name, setName] = useState("");
  const [rate, setRate] = useState("");
  const [active, setActive] = useState(true);
  const save = useMutation({
    mutationFn: async () => {
      if (!businessId) throw new Error("Selecciona una sede.");
      const request = {
        businessId,
        code: code.trim().toUpperCase(),
        dianTaxCode: editing?.dianTaxCode ?? "01",
        name: name.trim(),
        rate: Number(rate.replace(",", ".")),
        isActive: active,
      };
      return editing
        ? taxProfilesApi.update(editing.taxProfileId, request)
        : taxProfilesApi.create(request);
    },
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: ["tax-profiles", businessId] });
      toast.success(editing ? "IVA actualizado" : "IVA creado");
      setEditing(undefined);
    },
    onError: () => toast.error("No fue posible guardar el IVA."),
  });

  const open = (value: TaxProfile | null) => {
    setEditing(value);
    setCode(value?.code ?? "");
    setName(value?.name ?? "");
    setRate(value ? String(value.rate) : "");
    setActive(value?.isActive ?? true);
  };

  if (!businessId)
    return <Card><CardContent className="p-8 text-center text-muted-foreground">Selecciona una sede.</CardContent></Card>;

  return <Card>
    <CardHeader>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <CardTitle className="flex items-center gap-2"><Percent className="h-5 w-5 text-primary" />Maestro de IVA</CardTitle>
          <CardDescription>Una sola fuente para IVA de compra e IVA de venta. Los productos seleccionan dos valores de este maestro.</CardDescription>
        </div>
        <Button disabled={!canManage} onClick={() => open(null)}><Plus className="mr-2 h-4 w-4" />Nuevo IVA</Button>
      </div>
    </CardHeader>
    <CardContent>
      <div className="overflow-hidden rounded-2xl border">
        {(query.data ?? []).map((tax) => <div key={tax.taxProfileId} className="flex items-center gap-4 border-t px-4 py-3 first:border-t-0">
          <div className="min-w-0 flex-1"><p className="font-semibold">{tax.name}</p><p className="text-xs text-muted-foreground">{tax.code} · Código DIAN {tax.dianTaxCode}</p></div>
          <p className="text-xl font-bold tabular-nums">{tax.rate.toLocaleString("es-CO")} %</p>
          <Badge variant={tax.isActive ? "secondary" : "outline"}>{tax.isActive ? "Activo" : "Inactivo"}</Badge>
          <Button variant="ghost" size="icon" disabled={!canManage} onClick={() => open(tax)} aria-label={"Editar " + tax.name}><Pencil className="h-4 w-4" /></Button>
        </div>)}
        {!query.isLoading && !(query.data ?? []).length && <p className="p-8 text-center text-sm text-muted-foreground">No hay IVA configurados.</p>}
      </div>
    </CardContent>
    <Dialog open={editing !== undefined} onOpenChange={(value) => !value && setEditing(undefined)}>
      <DialogContent>
        <DialogHeader><DialogTitle>{editing ? "Editar IVA" : "Nuevo IVA"}</DialogTitle></DialogHeader>
        <div className="grid gap-4 sm:grid-cols-2">
          <div className="space-y-2"><Label>Código interno</Label><Input value={code} onChange={(event) => setCode(event.target.value)} placeholder="IVA-19" /></div>
          <div className="space-y-2"><Label>Tarifa %</Label><Input inputMode="decimal" value={rate} onChange={(event) => setRate(event.target.value)} placeholder="19" /></div>
          <div className="space-y-2 sm:col-span-2"><Label>Nombre</Label><Input value={name} onChange={(event) => setName(event.target.value)} placeholder="IVA 19%" /></div>
          <label className="flex items-center gap-3 text-sm sm:col-span-2"><Switch checked={active} onCheckedChange={setActive} />Disponible para seleccionar</label>
        </div>
        <DialogFooter><Button variant="outline" onClick={() => setEditing(undefined)}>Cancelar</Button><Button onClick={() => save.mutate()} disabled={save.isPending || !code.trim() || !name.trim() || !Number.isFinite(Number(rate.replace(",", ".")))}>Guardar</Button></DialogFooter>
      </DialogContent>
    </Dialog>
  </Card>;
}