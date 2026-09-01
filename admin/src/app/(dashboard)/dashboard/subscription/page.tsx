"use client";

import {
  Bot,
  CalendarClock,
  Check,
  CreditCard,
  Gauge,
  RefreshCw,
  Users,
} from "lucide-react";
import { useQuery } from "@tanstack/react-query";

import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { PageError } from "@/components/ui/page-error";
import { PageLoading } from "@/components/ui/page-loading";
import { Progress } from "@/components/ui/progress";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useSubscriptionDetails } from "@/hooks/use-dashboard";
import { formatCurrency, formatDateTime } from "@/lib/utils";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { TenantCommercialSubscriptionCard } from "@/components/subscriptions/tenant-commercial-subscription-card";
import { TenantRenewalOrderCard } from "@/components/subscriptions/tenant-renewal-order-card";
import { PlatformTenantSubscriptions } from "@/components/subscriptions/platform-tenant-subscriptions";
import { useAuthStore } from "@/stores/auth-store";
import { tenantCommercialApi } from "@/services/api/tenants";

const operationLabels: Record<string, string> = {
  "1": "Turno del agente",
  "2": "Respuesta de IA",
  "3": "Ejecucion de operacion",
  "4": "Transcripcion de audio",
  "5": "Mensaje de sesion de WhatsApp",
  "6": "Plantilla utilitaria de WhatsApp",
  "7": "Plantilla de marketing de WhatsApp",
  "8": "Secuencia saliente",
  AgentTurn: "Turno del agente",
  AiResponse: "Respuesta de IA",
  OperationExecution: "Ejecucion de operacion",
  AudioTranscription: "Transcripcion de audio",
  WhatsappSessionMessage: "Mensaje de sesion de WhatsApp",
  WhatsappUtilityTemplate: "Plantilla utilitaria de WhatsApp",
  WhatsappMarketingTemplate: "Plantilla de marketing de WhatsApp",
  OutboundSequence: "Secuencia saliente",
};

function operationLabel(value: string | number) {
  return operationLabels[String(value)] ?? "Uso del agente";
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat("es-CO", {
    day: "numeric",
    month: "long",
    year: "numeric",
  }).format(new Date(value));
}

function isActive(status: string | number) {
  return status === "Active" || status === 1;
}

function isExceeded(status: string | number) {
  return status === "Exceeded" || status === 2;
}

export default function SubscriptionPage() {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const canReadAllTenants = useAuthStore((state) =>
    Boolean(state.user?.permissions.includes("tenants.read")));
  const subscriptionQuery = useSubscriptionDetails();
  const commercialQuery = useQuery({ queryKey: ["tenant-commercial", "subscription"], queryFn: tenantCommercialApi.subscription });

  if (canReadAllTenants) return <PlatformTenantSubscriptions />;

  if (commercialQuery.isLoading) return <PageLoading cards={3} />;
  if (commercialQuery.isError) return <PageError onRetry={commercialQuery.refetch} />;

  if (commercialQuery.data && (!businessId || subscriptionQuery.isLoading || subscriptionQuery.isError || !subscriptionQuery.data)) {
    return <CommercialSubscriptionView />;
  }

  if (!businessId) {
    return (
      <div className="mx-auto max-w-[1600px] space-y-7">
        <div>
          <p className="mb-1 text-sm font-medium text-primary">Facturacion</p>
          <h1 className="text-3xl font-semibold tracking-tight">Suscripcion</h1>
          <p className="mt-1 text-muted-foreground">
            Selecciona un negocio para consultar su suscripcion y consumo.
          </p>
        </div>
        <TenantCommercialSubscriptionCard />
      </div>
    );
  }

  if (subscriptionQuery.isLoading) return <PageLoading cards={4} />;
  if (subscriptionQuery.isError) return <PageError onRetry={subscriptionQuery.refetch} />;

  const subscription = subscriptionQuery.data;
  if (!subscription) {
    return (
      <div className="mx-auto max-w-[1600px] space-y-7">
        <div>
          <p className="mb-1 text-sm font-medium text-primary">Facturacion</p>
          <h1 className="text-3xl font-semibold tracking-tight">Suscripcion</h1>
          <p className="mt-1 text-muted-foreground">Plan, vigencia y consumo de creditos.</p>
        </div>
        <TenantCommercialSubscriptionCard />
        <Card>
          <CardContent className="flex min-h-56 flex-col items-center justify-center text-center">
            <CreditCard className="mb-4 h-10 w-10 text-muted-foreground" />
            <p className="font-medium">Este negocio no tiene una suscripcion activa</p>
            <p className="mt-1 text-sm text-muted-foreground">
              Cuando se asigne un plan, su vigencia y consumo apareceran aqui.
            </p>
          </CardContent>
        </Card>
      </div>
    );
  }

  const statusActive = isActive(subscription.status);
  const usageExceeded = isExceeded(subscription.usageStatus);
  const usagePercent = Math.min(100, Math.max(0, subscription.creditsUsagePercent));

  return (
    <div className="mx-auto max-w-[1600px] space-y-7">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <p className="mb-1 text-sm font-medium text-primary">Facturacion</p>
          <h1 className="text-3xl font-semibold tracking-tight">Suscripcion</h1>
          <p className="mt-1 text-muted-foreground">
            Informacion del plan, ciclo vigente y uso detallado de creditos.
          </p>
        </div>
        <Badge variant={statusActive ? "secondary" : "destructive"} className="w-fit px-3 py-1">
          {statusActive ? "Suscripcion activa" : "Suscripcion inactiva"}
        </Badge>
      </div>

      <TenantCommercialSubscriptionCard />
      <TenantRenewalOrderCard />

      <div className="grid gap-5 xl:grid-cols-[1.15fr_1.85fr]">
        <Card className="overflow-hidden">
          <CardHeader className="border-b bg-muted/30">
            <div className="flex items-start justify-between gap-4">
              <div>
                <CardDescription>Plan actual</CardDescription>
                <CardTitle className="mt-1 text-2xl">{subscription.planName}</CardTitle>
              </div>
              <Badge variant="outline">{subscription.planCode}</Badge>
            </div>
          </CardHeader>
          <CardContent className="space-y-5 pt-6">
            <div>
              <span className="text-3xl font-semibold">
                {formatCurrency(subscription.monthlyPriceCop)}
              </span>
              <span className="ml-1 text-sm text-muted-foreground">/ mes</span>
            </div>
            <div className="grid grid-cols-3 gap-3">
              <div className="rounded-lg border bg-muted/20 p-3 text-center">
                <Bot className="mx-auto mb-1.5 h-4 w-4 text-primary" />
                <p className="text-lg font-semibold">{subscription.includedAgents}</p>
                <p className="text-xs text-muted-foreground">Agentes</p>
              </div>
              <div className="rounded-lg border bg-muted/20 p-3 text-center">
                <Users className="mx-auto mb-1.5 h-4 w-4 text-primary" />
                <p className="text-lg font-semibold">{subscription.includedUsers}</p>
                <p className="text-xs text-muted-foreground">Usuarios</p>
              </div>
              <div className="rounded-lg border bg-muted/20 p-3 text-center">
                <CreditCard className="mx-auto mb-1.5 h-4 w-4 text-primary" />
                <p className="text-lg font-semibold">{subscription.includedWorkspaces}</p>
                <p className="text-xs text-muted-foreground">Negocios</p>
              </div>
            </div>
            {subscription.features.length > 0 && (
              <div className="space-y-2">
                <p className="text-sm font-medium">Incluido en tu plan</p>
                {subscription.features.map((feature) => (
                  <div key={feature} className="flex items-start gap-2 text-sm text-muted-foreground">
                    <Check className="mt-0.5 h-4 w-4 shrink-0 text-primary" />
                    <span>{feature}</span>
                  </div>
                ))}
              </div>
            )}
          </CardContent>
        </Card>

        <div className="space-y-5">
          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2 text-base">
                <Gauge className="h-4 w-4 text-primary" />
                Creditos del ciclo actual
              </CardTitle>
              <CardDescription>
                Del {formatDate(subscription.periodStart)} al {formatDate(subscription.periodEnd)}
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-5">
              <div className="flex items-end justify-between gap-4">
                <div>
                  <p className="text-3xl font-semibold">
                    {subscription.creditsUsed.toLocaleString("es-CO")}
                  </p>
                  <p className="text-sm text-muted-foreground">
                    de {subscription.creditsLimit.toLocaleString("es-CO")} creditos usados
                  </p>
                </div>
                <div className="text-right">
                  <p className="text-2xl font-semibold">{subscription.creditsUsagePercent}%</p>
                  <p className="text-xs text-muted-foreground">consumido</p>
                </div>
              </div>
              <Progress value={usagePercent} />
              <div className="grid gap-3 sm:grid-cols-3">
                <div className="rounded-lg border p-3">
                  <p className="text-xs text-muted-foreground">Incluidos</p>
                  <p className="mt-1 text-lg font-semibold">{subscription.creditsIncluded.toLocaleString("es-CO")}</p>
                </div>
                <div className="rounded-lg border p-3">
                  <p className="text-xs text-muted-foreground">Adicionales</p>
                  <p className="mt-1 text-lg font-semibold">{subscription.creditsExtra.toLocaleString("es-CO")}</p>
                </div>
                <div className="rounded-lg border p-3">
                  <p className="text-xs text-muted-foreground">Disponibles</p>
                  <p className="mt-1 text-lg font-semibold">{subscription.creditsRemaining.toLocaleString("es-CO")}</p>
                </div>
              </div>
              {usageExceeded && (
                <p className="rounded-lg bg-destructive/10 px-3 py-2 text-sm text-destructive">
                  Se alcanzo el limite de creditos del ciclo actual.
                </p>
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2 text-base">
                <CalendarClock className="h-4 w-4 text-primary" />
                Vigencia
              </CardTitle>
            </CardHeader>
            <CardContent className="grid gap-4 text-sm sm:grid-cols-3">
              <div>
                <p className="text-muted-foreground">Inicio de suscripcion</p>
                <p className="mt-1 font-medium">{formatDate(subscription.subscriptionStartedAt)}</p>
              </div>
              <div>
                <p className="text-muted-foreground">Inicio del ciclo</p>
                <p className="mt-1 font-medium">{formatDate(subscription.periodStart)}</p>
              </div>
              <div>
                <p className="text-muted-foreground">
                  {subscription.autoRenew ? "Proxima renovacion" : "Finaliza"}
                </p>
                <p className="mt-1 font-medium">{formatDate(subscription.periodEnd)}</p>
              </div>
              <div className="flex items-center gap-2 sm:col-span-3">
                <RefreshCw className="h-4 w-4 text-primary" />
                <span className="text-muted-foreground">
                  Renovacion automatica {subscription.autoRenew ? "activada" : "desactivada"}
                </span>
              </div>
            </CardContent>
          </Card>
        </div>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Consumo por tipo de uso</CardTitle>
          <CardDescription>Distribucion de los creditos consumidos durante el ciclo vigente.</CardDescription>
        </CardHeader>
        <CardContent>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Tipo de uso</TableHead>
                <TableHead className="text-right">Operaciones</TableHead>
                <TableHead className="text-right">Creditos</TableHead>
                <TableHead className="text-right">Participacion</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {subscription.usageBreakdown.map((item) => (
                <TableRow key={String(item.operationType)}>
                  <TableCell className="font-medium">{operationLabel(item.operationType)}</TableCell>
                  <TableCell className="text-right">{item.operationCount.toLocaleString("es-CO")}</TableCell>
                  <TableCell className="text-right">{item.creditsUsed.toLocaleString("es-CO")}</TableCell>
                  <TableCell className="text-right">{item.creditsPercent}%</TableCell>
                </TableRow>
              ))}
              {subscription.usageBreakdown.length === 0 && (
                <TableRow>
                  <TableCell colSpan={4} className="h-24 text-center text-muted-foreground">
                    Aun no hay consumo registrado en este ciclo.
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Actividad reciente</CardTitle>
          <CardDescription>Ultimos movimientos de creditos del ciclo actual.</CardDescription>
        </CardHeader>
        <CardContent>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Fecha y hora</TableHead>
                <TableHead>Tipo de uso</TableHead>
                <TableHead className="text-right">Creditos</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {subscription.recentUsage.map((item) => (
                <TableRow key={item.usageId}>
                  <TableCell className="text-muted-foreground">{formatDateTime(item.createdAt)}</TableCell>
                  <TableCell className="font-medium">{operationLabel(item.operationType)}</TableCell>
                  <TableCell className="text-right font-medium">{item.creditsUsed.toLocaleString("es-CO")}</TableCell>
                </TableRow>
              ))}
              {subscription.recentUsage.length === 0 && (
                <TableRow>
                  <TableCell colSpan={3} className="h-24 text-center text-muted-foreground">
                    No hay movimientos de creditos para mostrar.
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
    </div>
  );
}

function CommercialSubscriptionView() {
  return <div className="mx-auto max-w-[1600px] space-y-7">
    <div><p className="mb-1 text-sm font-medium text-primary">FACTURACIÓN</p><h1 className="text-3xl font-semibold tracking-tight">Suscripción</h1><p className="mt-1 text-muted-foreground">Plan contratado, cupos, vigencia y próxima renovación.</p></div>
    <TenantCommercialSubscriptionCard />
    <TenantRenewalOrderCard />
  </div>;
}
