"use client";

import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { BellRing, Loader2, Mail, ShieldAlert } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { tenantsApi, type UpdatePlatformBillingPolicy } from "@/services/api/tenants";

const empty: UpdatePlatformBillingPolicy = {
  emailRemindersEnabled: true, preDueReminderDays: 5,
  overdueReminderIntervalDays: 3, gracePeriodDays: 10,
  billingTimeZoneId: "America/Bogota", reason: "", version: "",
};

export function PlatformBillingPolicyCard() {
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: ["tenants", "billing-policy"], queryFn: tenantsApi.billingPolicy });
  const [value, setValue] = useState(empty);
  useEffect(() => {
    if (!query.data) return;
    setValue({ ...query.data, reason: "" });
  }, [query.data]);
  const save = useMutation({
    mutationFn: () => tenantsApi.updateBillingPolicy(value),
    onSuccess: async (result) => {
      queryClient.setQueryData(["tenants", "billing-policy"], result);
      setValue({ ...result, reason: "" });
      await queryClient.invalidateQueries({ queryKey: ["tenants", "billing-policy"] });
      toast.success("Política global de cobranza actualizada");
    },
    onError: (error) => toast.error(error instanceof Error ? error.message : "No fue posible guardar la política."),
  });
  if (query.isLoading) return <Card><CardContent className="flex min-h-64 items-center justify-center"><Loader2 className="h-6 w-6 animate-spin text-teal-700"/></CardContent></Card>;
  if (query.isError) return <Card><CardContent className="p-6 text-sm text-destructive">No fue posible cargar la política global.</CardContent></Card>;
  const valid = value.preDueReminderDays >= 0 && value.overdueReminderIntervalDays > 0
    && value.gracePeriodDays > value.overdueReminderIntervalDays && value.reason.trim().length >= 5;
  return <Card className="overflow-hidden border-teal-200">
    <CardHeader className="border-b bg-gradient-to-r from-slate-950 to-teal-950 text-white">
      <CardDescription className="text-teal-200">Configuración exclusiva de plataforma</CardDescription>
      <CardTitle className="flex items-center gap-2"><BellRing className="h-5 w-5"/>Política global de cobranza</CardTitle>
      <p className="max-w-3xl text-sm text-slate-300">Aplica a todos los tenants. La campanita siempre se envía; este interruptor controla únicamente el correo.</p>
    </CardHeader>
    <CardContent className="space-y-6 pt-6">
      <div className="flex items-center justify-between gap-5 rounded-2xl border p-4"><div><Label className="flex items-center gap-2 text-base"><Mail className="h-4 w-4 text-teal-700"/>Recordatorios por correo</Label><p className="mt-1 text-sm text-muted-foreground">Habilitado por defecto para todos los administradores de tenant.</p></div><Switch checked={value.emailRemindersEnabled} onCheckedChange={(checked) => setValue((current) => ({ ...current, emailRemindersEnabled: checked }))}/></div>
      <div className="grid gap-4 md:grid-cols-3">
        <NumberField label="Aviso antes de vencer" suffix="días" value={value.preDueReminderDays} min={0} max={90} onChange={(number) => setValue((current) => ({ ...current, preDueReminderDays: number }))}/>
        <NumberField label="Frecuencia después de vencer" suffix="días" value={value.overdueReminderIntervalDays} min={1} max={30} onChange={(number) => setValue((current) => ({ ...current, overdueReminderIntervalDays: number }))}/>
        <NumberField label="Periodo de gracia" suffix="días" value={value.gracePeriodDays} min={1} max={90} onChange={(number) => setValue((current) => ({ ...current, gracePeriodDays: number }))}/>
      </div>
      <div className="rounded-2xl border border-amber-200 bg-amber-50 p-4 text-sm text-amber-950"><ShieldAlert className="mr-2 inline h-4 w-4"/>Con la configuración predeterminada se avisa 5 días antes, luego en los días 3, 6 y 9 de mora, y al iniciar el día 10 la suscripción queda suspendida.</div>
      <div className="grid gap-4 md:grid-cols-2"><div className="space-y-2"><Label>Zona horaria de corte</Label><Select value={value.billingTimeZoneId} onValueChange={(billingTimeZoneId) => setValue((current) => ({ ...current, billingTimeZoneId }))}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent><SelectItem value="America/Bogota">Colombia · Bogotá</SelectItem><SelectItem value="America/Lima">Perú · Lima</SelectItem><SelectItem value="America/Mexico_City">México · Ciudad de México</SelectItem><SelectItem value="America/New_York">Estados Unidos · Nueva York</SelectItem><SelectItem value="UTC">UTC</SelectItem></SelectContent></Select></div><div className="space-y-2"><Label htmlFor="billing-policy-reason">Motivo del cambio</Label><Input id="billing-policy-reason" value={value.reason} maxLength={300} onChange={(event) => setValue((current) => ({ ...current, reason: event.target.value }))} placeholder="Ej. Ajuste aprobado de periodo de gracia"/></div></div>
      <div className="flex items-center justify-between gap-4 border-t pt-5"><p className="text-xs text-muted-foreground">Cada cambio guarda actor, valores anteriores/nuevos, motivo y fecha.</p><Button disabled={!valid || save.isPending} onClick={() => save.mutate()}>{save.isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin"/>}Guardar política</Button></div>
    </CardContent>
  </Card>;
}

function NumberField({ label, suffix, value, min, max, onChange }: { label: string; suffix: string; value: number; min: number; max: number; onChange: (value: number) => void }) {
  return <div className="space-y-2"><Label>{label}</Label><div className="relative"><Input type="number" min={min} max={max} value={value} onChange={(event) => onChange(Number(event.target.value))}/><span className="pointer-events-none absolute right-3 top-2.5 text-sm text-muted-foreground">{suffix}</span></div></div>;
}
