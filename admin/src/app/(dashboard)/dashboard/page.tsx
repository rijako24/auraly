"use client";
import { useState } from "react";
import { DollarSign, Users, CalendarDays, MessageSquare, Gauge } from "lucide-react";
import { StatCard } from "@/components/cards/stat-card";
import { RevenueChart } from "@/components/charts/revenue-chart";
import { OverviewChart } from "@/components/charts/overview-chart";
import { PageLoading } from "@/components/ui/page-loading";
import { PageError } from "@/components/ui/page-error";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Progress } from "@/components/ui/progress";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { ReservationStatusLabels } from "@/types/enums";
import type { ReservationStatus } from "@/types/enums";
import { formatCurrency, formatRelativeTime } from "@/lib/utils";
import { useDashboardStats, useRevenueChart, useOverviewChart, useTopServices, useRecentReservations, useBusinessUsage } from "@/hooks/use-dashboard";
import { useBusinessContextStore } from "@/stores/business-context-store";

export default function DashboardPage() {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  const [period, setPeriod] = useState("7d");
  const { data: stats, isLoading, isError, refetch } = useDashboardStats(period);
  const { data: dailyRevenue } = useRevenueChart("daily");
  const { data: monthlyRevenue } = useRevenueChart("monthly");
  const { data: overviewData } = useOverviewChart(period);
  const { data: topServices } = useTopServices(4);
  const { data: recentReservations } = useRecentReservations(5);
  const { data: usage } = useBusinessUsage();

  if (!businessId) {
    return (
      <div className="space-y-6">
        <h1 className="text-2xl font-semibold tracking-tight">Dashboard</h1>
        <p className="text-muted-foreground">
          Selecciona un negocio en el selector superior para ver el resumen
        </p>
      </div>
    );
  }
  if (isLoading) return <PageLoading />;
  if (isError) return <PageError onRetry={refetch} />;

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div><h1 className="text-2xl font-semibold tracking-tight">Dashboard</h1><p className="text-muted-foreground">Resumen financiero y operativo de tu spa de bebés</p></div>
        <Select value={period} onValueChange={setPeriod}><SelectTrigger className="w-[180px]"><SelectValue placeholder="Período" /></SelectTrigger><SelectContent><SelectItem value="today">Hoy</SelectItem><SelectItem value="7d">Últimos 7 días</SelectItem><SelectItem value="30d">Últimos 30 días</SelectItem><SelectItem value="3m">Últimos 3 meses</SelectItem></SelectContent></Select>
      </div>
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard title="Ingresos totales" value={formatCurrency(stats?.totalRevenue ?? 0)} change={stats?.revenueGrowth} icon={DollarSign} />
        <StatCard title="Reservas" value={stats?.totalReservations ?? 0} change={stats?.reservationGrowth} icon={CalendarDays} />
        <StatCard title="Nuevos leads" value={stats?.totalLeads ?? 0} change={stats?.leadGrowth} icon={Users} />
        <StatCard title="Conversaciones activas" value={stats?.totalConversations ?? 0} icon={MessageSquare} />
      </div>
      {usage && (
        <Card>
          <CardHeader className="pb-3">
            <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
              <div>
                <CardTitle className="flex items-center gap-2 text-base"><Gauge className="h-4 w-4 text-primary" />Consumo del agente</CardTitle>
                <CardDescription>Plan {usage.planName} · renovación {new Date(usage.periodEnd).toLocaleDateString("es-CO")}</CardDescription>
              </div>
              <div className="text-left sm:text-right"><p className="text-2xl font-semibold">{usage.creditsUsagePercent}%</p><p className="text-xs text-muted-foreground">uso del plan</p></div>
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <Progress value={Math.min(100, usage.creditsUsagePercent)} />
            <div className="grid gap-3 text-sm sm:grid-cols-3">
              <div><p className="text-muted-foreground">Créditos usados</p><p className="font-medium">{usage.creditsUsed.toLocaleString("es-CO")} / {usage.creditsLimit.toLocaleString("es-CO")}</p></div>
              <div><p className="text-muted-foreground">Costo operativo</p><p className="font-medium">{formatCurrency(usage.variableCostUsedCop)} / {formatCurrency(usage.variableCostLimitCop)}</p></div>
              <div><p className="text-muted-foreground">Estado</p><p className="font-medium">{usage.status === "Exceeded" || usage.status === 2 ? "Límite alcanzado" : "Activo"}</p></div>
            </div>
          </CardContent>
        </Card>
      )}
      <Card><CardHeader><CardTitle>Ingresos</CardTitle><CardDescription>Evolución de ingresos por período</CardDescription></CardHeader><CardContent><Tabs defaultValue="daily"><TabsList><TabsTrigger value="daily">Diario</TabsTrigger><TabsTrigger value="monthly">Mensual</TabsTrigger></TabsList><TabsContent value="daily"><RevenueChart data={dailyRevenue ?? []} /></TabsContent><TabsContent value="monthly"><RevenueChart data={monthlyRevenue ?? []} /></TabsContent></Tabs></CardContent></Card>
      <div className="grid gap-6 lg:grid-cols-2">
        <Card><CardHeader><CardTitle>Vista general</CardTitle><CardDescription>Ingresos y reservas combinados</CardDescription></CardHeader><CardContent><OverviewChart data={overviewData ?? []} /></CardContent></Card>
        <Card><CardHeader><CardTitle>Top servicios</CardTitle><CardDescription>Servicios más reservados y rentables</CardDescription></CardHeader><CardContent><Table><TableHeader><TableRow><TableHead>Servicio</TableHead><TableHead className="text-right">Reservas</TableHead><TableHead className="text-right">Ingresos</TableHead></TableRow></TableHeader><TableBody>{(topServices ?? []).map((svc) => (<TableRow key={svc.serviceId}><TableCell className="font-medium">{svc.serviceName}</TableCell><TableCell className="text-right">{svc.totalReservations}</TableCell><TableCell className="text-right">{formatCurrency(svc.revenue)}</TableCell></TableRow>))}{(!topServices || topServices.length === 0) && <TableRow><TableCell colSpan={3} className="text-center text-muted-foreground">Sin datos disponibles</TableCell></TableRow>}</TableBody></Table></CardContent></Card>
      </div>
      <Card><CardHeader><CardTitle>Actividad reciente</CardTitle><CardDescription>Últimas 5 reservas</CardDescription></CardHeader><CardContent><Table><TableHeader><TableRow><TableHead>Cliente</TableHead><TableHead>Servicio</TableHead><TableHead>Fecha</TableHead><TableHead>Estado</TableHead></TableRow></TableHeader><TableBody>{(recentReservations ?? []).map((r) => (<TableRow key={r.reservationId}><TableCell>{r.customerName ?? "—"}</TableCell><TableCell>{r.serviceName ?? r.service?.serviceName ?? "—"}</TableCell><TableCell className="text-muted-foreground">{formatRelativeTime(r.reservationDateTime)}</TableCell><TableCell>{ReservationStatusLabels[r.status as ReservationStatus]}</TableCell></TableRow>))}{(!recentReservations || recentReservations.length === 0) && <TableRow><TableCell colSpan={4} className="text-center text-muted-foreground">Sin reservas recientes</TableCell></TableRow>}</TableBody></Table></CardContent></Card>
    </div>
  );
}
