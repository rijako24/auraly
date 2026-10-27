"use client";

import { useReferenceOptions } from "@/hooks/use-reference-options";
import { BadgeCheck, Bot, FileCheck2, Store, UsersRound } from "lucide-react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Progress } from "@/components/ui/progress";
import { type TenantCommercialSubscription } from "@/services/api/tenants";

export function TenantCommercialSubscriptionCard({ value }: { value: TenantCommercialSubscription | null | undefined }) {
  const statuses = useReferenceOptions("tenant-subscription-status");
  if (!value) return null;
  const documentPercent = value.dianDocumentMonthlyLimit > 0
    ? Math.min(100, value.dianDocumentsUsed * 100 / value.dianDocumentMonthlyLimit) : 100;
  const end = new Intl.DateTimeFormat("es-CO", { dateStyle: "long" }).format(new Date(value.currentPeriodEnd));
  return <Card className="overflow-hidden border-teal-200">
    <CardHeader className="bg-gradient-to-r from-slate-950 to-emerald-950 text-white">
      <div className="flex flex-wrap items-start justify-between gap-4"><div><CardDescription className="text-teal-200">Suscripción de plataforma</CardDescription><CardTitle className="mt-1 text-2xl">{value.planName}</CardTitle><p className="mt-1 text-sm text-slate-300">{value.billingPeriod === "Annual" ? `Anual · cubierta hasta el ${end}` : `Mensual · renueva el ${end}`}</p></div><span className="rounded-full bg-emerald-400/15 px-3 py-1 text-xs font-semibold text-emerald-200">{statuses.data?.find(option => option.code === value.status)?.label ?? value.status}</span></div>
    </CardHeader>
    <CardContent className="space-y-5 pt-6">
      {(value.status === "PastDue" || value.status === "Suspended") && <div role="status" className="rounded-2xl border border-amber-300 bg-amber-50 p-4 text-sm text-amber-950"><strong>Tienes una renovación pendiente de pago.</strong><p className="mt-1">Puedes seguir trabajando y registrando comprobantes. Los cupos DIAN vigentes siguen disponibles; los nuevos se habilitan al confirmar el pago.</p></div>}
      {value.hasDianDocumentQuota === false && <div role="status" className="rounded-2xl border border-teal-200 bg-teal-50 p-4 text-sm text-teal-950"><strong>No tienes documentos DIAN disponibles.</strong><p className="mt-1">Puedes continuar con tus comprobantes y entradas de mercancía. Renueva o amplía tu cupo para emitir documentos electrónicos.</p></div>}
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5"><Limit icon={UsersRound} label="Usuarios completos" value={value.fullUserLimit}/><Limit icon={BadgeCheck} label="Vendedores" value={value.sellerUserLimit}/><Limit icon={Store} label="Cajas" value={value.posDeviceLimit}/><Limit icon={Bot} label="Empleados nómina" value={value.payrollEmployeeLimit}/><Limit icon={FileCheck2} label="Documentos DIAN / mes" value={value.dianDocumentMonthlyLimit}/></div>
      <div className="rounded-2xl border p-4"><div className="mb-2 flex items-center justify-between gap-4 text-sm"><span>Documentos DIAN consumidos este mes</span><strong>{value.dianDocumentsUsed.toLocaleString("es-CO")} de {value.dianDocumentMonthlyLimit.toLocaleString("es-CO")}</strong></div><Progress value={documentPercent}/><p className="mt-2 text-xs text-muted-foreground">El cupo documental se renueva mensualmente dentro del periodo pagado, también en planes anuales. Una renovación pendiente no habilita cupos nuevos.</p></div>
    </CardContent>
  </Card>;
}

function Limit({ icon: Icon, label, value }: { icon: typeof UsersRound; label: string; value: number }) {
  return <div className="rounded-2xl border bg-muted/20 p-4"><Icon className="mb-3 h-5 w-5 text-teal-700"/><strong className="text-2xl">{value.toLocaleString("es-CO")}</strong><p className="text-xs text-muted-foreground">{label}</p></div>;
}
