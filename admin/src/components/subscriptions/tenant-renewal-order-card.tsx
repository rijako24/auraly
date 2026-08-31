"use client";

import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CalendarClock, FileText, ShieldCheck } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { TenantCommercialPlanStep, useTenantQuote } from "@/components/tenants/tenant-commercial-plan-step";
import { tenantCommercialApi, type TenantCommercialCatalog, type TenantCommercialSubscription, type TenantQuoteRequest } from "@/services/api/tenants";
import { TenantRenewalPayment } from "@/components/subscriptions/tenant-renewal-payment";

const emptyRequest: TenantQuoteRequest = {
  planCode: "", billingPeriod: "Annual", additionalFullUsers: 0,
  sellerUsers: 0, additionalPosDevices: 0, dianDocumentPacks: 0,
  payrollEmployeePacks: 0,
};

export function TenantRenewalOrderCard() {
  const queryClient = useQueryClient();
  const subscription = useQuery({ queryKey: ["tenant-commercial", "subscription"], queryFn: tenantCommercialApi.subscription });
  const catalog = useQuery({ queryKey: ["tenant-commercial", "catalog"], queryFn: tenantCommercialApi.catalog, staleTime: 300_000 });
  const order = useQuery({ queryKey: ["tenant-commercial", "renewal-order"], queryFn: tenantCommercialApi.renewalOrder, retry: false });
  const [request, setRequest] = useState<TenantQuoteRequest>(emptyRequest);

  useEffect(() => {
    if (!catalog.data || !subscription.data || request.planCode) return;
    setRequest(requestFromCurrent(catalog.data, subscription.data, order.data ?? undefined));
  }, [catalog.data, order.data, request.planCode, subscription.data]);

  const quote = useTenantQuote(request, Boolean(request.planCode));
  const invalidUse = useMemo(() => {
    if (!quote.data || !order.data) return [];
    const usage = order.data.usage;
    return [
      quote.data.fullUserLimit < usage.fullUsers && `usuarios completos (${usage.fullUsers} activos)`,
      quote.data.sellerUserLimit < usage.sellerUsers && `vendedores (${usage.sellerUsers} activos)`,
      quote.data.posDeviceLimit < usage.posDevices && `cajas (${usage.posDevices} enroladas)`,
      quote.data.payrollEmployeeLimit < usage.payrollEmployees && `empleados (${usage.payrollEmployees} activos)`,
    ].filter(Boolean) as string[];
  }, [order.data, quote.data]);

  const save = useMutation({
    mutationFn: () => tenantCommercialApi.reviseRenewalOrder(request),
    onSuccess: async (value) => {
      await queryClient.invalidateQueries({ queryKey: ["tenant-commercial", "renewal-order"] });
      toast.success(`Orden de renovación actualizada · revisión ${value.revision}`);
    },
    onError: (error) => toast.error(error instanceof Error ? error.message : "No fue posible actualizar la renovación."),
  });

  if (order.isError || subscription.isLoading || catalog.isLoading || !subscription.data || !catalog.data) return null;
  const due = order.data ? new Intl.DateTimeFormat("es-CO", { dateStyle: "long" }).format(new Date(order.data.dueAt)) : null;
  return <Card className="overflow-hidden border-slate-200">
    <CardHeader className="border-b bg-gradient-to-r from-slate-50 to-teal-50">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div><CardDescription className="flex items-center gap-2"><CalendarClock className="h-4 w-4"/>Próximo período</CardDescription><CardTitle className="mt-1">Orden de renovación</CardTitle><p className="mt-1 text-sm text-muted-foreground">Revisa exactamente qué se cobrará. No genera factura, cartera ni contabilidad antes del pago.</p></div>
        {order.data && <div className="rounded-2xl border bg-white px-4 py-3 text-right text-sm"><strong>Revisión {order.data.revision}</strong><p className="text-muted-foreground">Vence el {due}</p></div>}
      </div>
    </CardHeader>
    <CardContent className="space-y-5 pt-6">
      <TenantCommercialPlanStep catalog={catalog.data} request={request} quote={quote.data} loading={quote.isFetching} onChange={setRequest}/>
      {order.data && <div className="grid gap-3 rounded-2xl border bg-muted/20 p-4 text-sm sm:grid-cols-4"><Usage label="Usuarios usados" value={order.data.usage.fullUsers}/><Usage label="Vendedores usados" value={order.data.usage.sellerUsers}/><Usage label="Cajas enroladas" value={order.data.usage.posDevices}/><Usage label="Empleados activos" value={order.data.usage.payrollEmployees}/></div>}
      {invalidUse.length > 0 && <p className="rounded-2xl border border-amber-300 bg-amber-50 p-4 text-sm text-amber-950">No puedes reducir todavía: {invalidUse.join(", ")}.</p>}
      <div className="flex flex-wrap items-center justify-between gap-4 border-t pt-5"><p className="flex items-center gap-2 text-sm text-muted-foreground"><ShieldCheck className="h-4 w-4 text-emerald-600"/>El servidor vuelve a calcular precios, IVA, capacidad y uso activo.</p><div className="flex flex-wrap gap-2"><Button disabled={!quote.data || quote.isFetching || invalidUse.length > 0 || save.isPending || order.data?.status === "PendingPayment"} onClick={() => save.mutate()}><FileText className="mr-2 h-4 w-4"/>{save.isPending ? "Guardando…" : order.data ? "Actualizar orden" : "Preparar renovación"}</Button>{order.data && <TenantRenewalPayment orderId={order.data.renewalOrderId} status={order.data.status}/>}</div></div>
    </CardContent>
  </Card>;
}

function Usage({ label, value }: { label: string; value: number }) { return <div><strong className="text-xl">{value.toLocaleString("es-CO")}</strong><p className="text-xs text-muted-foreground">{label}</p></div>; }

function requestFromCurrent(catalog: TenantCommercialCatalog, subscription: TenantCommercialSubscription, order?: { quote: { planCode: string; billingPeriod: "Monthly" | "Annual"; lines?: { code: string; quantity: number }[] } }): TenantQuoteRequest {
  if (order) {
    const quantities = new Map(order.quote.lines?.map(line => [line.code, line.quantity]) ?? []);
    return { planCode: order.quote.planCode, billingPeriod: order.quote.billingPeriod,
      additionalFullUsers: quantities.get("full_user") ?? 0, sellerUsers: quantities.get("seller_user") ?? 0,
      additionalPosDevices: quantities.get("pos_device") ?? 0, dianDocumentPacks: quantities.get("dian_document_pack") ?? 0,
      payrollEmployeePacks: quantities.get("payroll_employee_pack") ?? 0 };
  }
  const plan = catalog.plans.find(value => value.code === subscription.planCode) ?? catalog.plans.find(value => value.code === "company")!;
  const floor = plan.isCustom ? catalog.plans.find(value => value.code === "company")! : plan;
  return { planCode: plan.code, billingPeriod: subscription.billingPeriod,
    additionalFullUsers: Math.max(0, subscription.fullUserLimit - floor.includedFullUsers),
    sellerUsers: Math.max(0, subscription.sellerUserLimit - floor.includedSellerUsers),
    additionalPosDevices: Math.max(0, subscription.posDeviceLimit - floor.includedPosDevices),
    dianDocumentPacks: Math.max(0, (subscription.dianDocumentMonthlyLimit - floor.includedDianDocuments) / 1_000),
    payrollEmployeePacks: Math.max(0, (subscription.payrollEmployeeLimit - floor.includedPayrollEmployees) / 10) };
}
