"use client";

import { useQuery } from "@tanstack/react-query";
import { BadgeCheck, Boxes, Check, CircleDollarSign, Crown, ReceiptText, Sparkles, Store, UserRoundCog, Users } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle, DialogTrigger } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { tenantCommercialApi, type TenantCommercialCatalog, type TenantQuote, type TenantQuoteRequest } from "@/services/api/tenants";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });

export function useTenantCommercialCatalog() {
  return useQuery({ queryKey: ["tenant-commercial", "catalog"], queryFn: tenantCommercialApi.catalog, staleTime: 300_000 });
}

export function useTenantQuote(request: TenantQuoteRequest, enabled = true) {
  return useQuery({
    queryKey: ["tenant-commercial", "quote", request],
    queryFn: () => tenantCommercialApi.quote(request),
    enabled: enabled && Boolean(request.planCode),
  });
}

export function TenantCommercialPlanStep({ catalog, request, quote, loading, onChange }: {
  catalog: TenantCommercialCatalog; request: TenantQuoteRequest; quote?: TenantQuote; loading: boolean;
  onChange: (next: TenantQuoteRequest) => void;
}) {
  const selected = catalog.plans.find((plan) => plan.code === request.planCode);
  const updateNumber = (key: keyof TenantQuoteRequest, value: string) =>
    onChange({ ...request, [key]: Math.max(0, Number(value.replace(/\D/g, ""))) });
  return <div className="space-y-6">
    <section>
      <div className="mb-4 flex flex-wrap items-end justify-between gap-4"><div><h3 className="text-xl font-semibold">Elige la capacidad inicial</h3><p className="text-sm text-muted-foreground">El plan y los adicionales forman una cotización inmutable que después se valida contra lo aprovisionado.</p></div><label className="flex items-center gap-3 rounded-full border bg-muted/20 px-4 py-2"><span className={request.billingPeriod === "Monthly" ? "font-semibold" : "text-muted-foreground"}>Mensual</span><Switch checked={request.billingPeriod === "Annual"} onCheckedChange={(annual) => onChange({ ...request, billingPeriod: annual ? "Annual" : "Monthly" })}/><span className={request.billingPeriod === "Annual" ? "font-semibold text-emerald-700" : "text-muted-foreground"}>Anual · ahorra 15%</span></label></div>
      <div className="grid gap-4 lg:grid-cols-4">{catalog.plans.map((plan) => <button key={plan.code} type="button" onClick={() => onChange({ ...request, planCode: plan.code })} className={`relative rounded-3xl border p-5 text-left transition ${request.planCode === plan.code ? "border-teal-500 bg-gradient-to-b from-teal-50 to-white shadow-lg shadow-teal-900/10 ring-2 ring-teal-400/20" : "bg-card hover:-translate-y-0.5 hover:border-teal-200"}`}>
        {plan.isRecommended && <span className="absolute -top-3 left-5 rounded-full bg-slate-950 px-3 py-1 text-[10px] font-bold uppercase tracking-widest text-white">Recomendado</span>}
        <span className="grid h-11 w-11 place-items-center rounded-2xl bg-teal-100 text-teal-800">{plan.isCustom ? <Crown className="h-5 w-5"/> : <Sparkles className="h-5 w-5"/>}</span><h4 className="mt-4 text-lg font-semibold">{plan.name}</h4><p className="mt-1 text-2xl font-bold">{plan.isCustom ? "A tu medida" : money.format(plan.monthlyPriceCop)}{!plan.isCustom && <small className="text-xs font-normal text-muted-foreground"> / mes</small>}</p>{!plan.isCustom && <p className="mt-1 text-[11px] text-muted-foreground">Antes de IVA · tarifa configurada {plan.salesTaxRate}%</p>}
        <ul className="mt-4 space-y-2 text-sm text-muted-foreground">{plan.isCustom ? <><li>Piso: capacidades de Empresa</li><li>Debe superar al menos una capacidad</li><li>Precio Empresa + adicionales</li></> : <><li>{plan.includedFullUsers} usuarios completos</li><li>{plan.includedPosDevices} cajas</li><li>{plan.includedDianDocuments.toLocaleString("es-CO")} documentos DIAN / mes</li><li>{plan.includedPayrollEmployees} empleados de nómina</li></>}</ul>
        <Dialog><DialogTrigger asChild><Button type="button" variant="ghost" className="mt-4 w-full" onClick={(event) => event.stopPropagation()}>Ver todo el plan</Button></DialogTrigger><DialogContent><DialogHeader><DialogTitle>{plan.name}</DialogTitle><DialogDescription>Capacidad incluida y características del plan.</DialogDescription></DialogHeader><div className="grid gap-3 sm:grid-cols-2">{plan.features.map((feature) => <span key={feature} className="flex items-center gap-2 rounded-xl border p-3 text-sm"><Check className="h-4 w-4 text-emerald-600"/>{feature}</span>)}</div></DialogContent></Dialog>
      </button>)}</div>
    </section>
    {selected && <section className="rounded-3xl border bg-slate-950 p-6 text-white"><div className="flex items-center gap-3"><Boxes className="text-teal-300"/><div><h3 className="font-semibold">{selected.isCustom ? "Capacidad superior a Empresa" : "Adicionales para empezar"}</h3><p className="text-sm text-slate-300">{selected.isCustom ? "Estas cantidades se suman al piso del plan Empresa; al menos una debe ser mayor que cero." : "Puedes ampliarlos ahora; documentos DIAN y empleados de nómina se agregan únicamente por paquetes."}</p></div></div><div className="mt-5 grid gap-4 md:grid-cols-2 xl:grid-cols-5"><Counter icon={Users} label="Usuarios completos" value={request.additionalFullUsers} onChange={(value) => updateNumber("additionalFullUsers", value)}/><Counter icon={BadgeCheck} label="Usuarios vendedor" value={request.sellerUsers} onChange={(value) => updateNumber("sellerUsers", value)}/><Counter icon={Store} label="Cajas adicionales" value={request.additionalPosDevices} onChange={(value) => updateNumber("additionalPosDevices", value)}/><Counter icon={ReceiptText} label="Paquetes de 1.000 DIAN" value={request.dianDocumentPacks} onChange={(value) => updateNumber("dianDocumentPacks", value)}/><Counter icon={UserRoundCog} label="Paquetes de 10 empleados" value={request.payrollEmployeePacks} onChange={(value) => updateNumber("payrollEmployeePacks", value)}/></div></section>}
    <QuoteSummary quote={quote} loading={loading}/>
  </div>;
}

function Counter({ icon: Icon, label, value, onChange }: { icon: typeof Users; label: string; value: number; onChange: (value: string) => void }) { return <div className="rounded-2xl border border-white/15 bg-white/5 p-4"><Label className="flex items-center gap-2 text-slate-200"><Icon className="h-4 w-4 text-teal-300"/>{label}</Label><Input className="mt-3 border-white/20 bg-white/10 text-white" inputMode="numeric" value={value} onChange={(event) => onChange(event.target.value)}/></div>; }
function QuoteSummary({ quote, loading }: { quote?: TenantQuote; loading: boolean }) { if (loading) return <div className="rounded-3xl border p-6 text-sm text-muted-foreground">Recalculando el valor…</div>; if (!quote) return null; const netAfterDiscount=quote.grossPeriodAmountCop-quote.discountAmountCop; return <section className="grid gap-5 rounded-3xl border border-emerald-200 bg-gradient-to-r from-emerald-50 to-teal-50 p-6 lg:grid-cols-[1fr_auto] lg:items-center"><div><span className="inline-flex items-center gap-2 text-xs font-bold uppercase tracking-widest text-emerald-700"><CircleDollarSign className="h-4 w-4"/>Resumen de inversión</span><h3 className="mt-2 text-2xl font-bold">{quote.planName} · {quote.billingPeriod === "Annual" ? "Anual" : "Mensual"}</h3><p className="mt-2 text-sm text-emerald-950/70">{quote.fullUserLimit} usuarios completos + {quote.sellerUserLimit} vendedores · {quote.posDeviceLimit} cajas · {quote.dianDocumentMonthlyLimit.toLocaleString("es-CO")} documentos DIAN/mes · {quote.payrollEmployeeLimit} empleados de nómina.</p>{quote.discountAmountCop > 0 && <p className="mt-3 inline-flex rounded-full bg-emerald-600 px-3 py-1 text-sm font-semibold text-white">Ahorras {money.format(quote.discountAmountCop)} pagando el año</p>}</div><div className="min-w-60 text-left lg:text-right"><div className="space-y-1 text-xs text-muted-foreground"><p>Base antes de IVA: {money.format(netAfterDiscount)}</p><p>IVA: {money.format(quote.taxAmountCop)}</p></div><p className="mt-2 text-sm text-muted-foreground">Total a pagar</p><p className="text-3xl font-black text-emerald-950">{money.format(quote.payableAmountCop)}</p><p className="text-xs text-muted-foreground">Equivale a {money.format(quote.monthlyEquivalentCop)} / mes con IVA</p></div></section>; }
