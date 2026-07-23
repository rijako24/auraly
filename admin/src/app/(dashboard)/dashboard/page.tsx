"use client";

import { useState } from "react";
import { CalendarDays, DollarSign, Gauge, MessageSquare, Users } from "lucide-react";

import { StatCard } from "@/components/cards/stat-card";
import { OverviewChart } from "@/components/charts/overview-chart";
import { RevenueChart } from "@/components/charts/revenue-chart";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { PageError } from "@/components/ui/page-error";
import { PageLoading } from "@/components/ui/page-loading";
import { Progress } from "@/components/ui/progress";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import {
  useBusinessUsage,
  useDashboardStats,
  useOverviewChart,
  useRecentReservations,
  useRevenueChart,
  useTopServices,
} from "@/hooks/use-dashboard";
import { formatCurrency, formatRelativeTime } from "@/lib/utils";
import { useBusinessContextStore } from "@/stores/business-context-store";

function reservationStatusLabel(status: string | number) {
  if (typeof status === "number") {
    return {
      0: "Pendiente",
      1: "Confirmada",
      2: "Completada",
      3: "Cancelada",
      4: "Pendiente Calendario",
      5: "En Espera",
    }[status] ?? "Sin estado";
  }

  return {
    Pending: "Pendiente",
    Confirmed: "Confirmada",
    Completed: "Completada",
    Cancelled: "Cancelada",
    PendingCalendar: "Pendiente Calendario",
    OnHold: "En Espera",
  }[status] ?? status;
}

export default function DashboardPage() {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const [period, setPeriod] = useState("30d");
  const statsQuery = useDashboardStats(period);
  const dailyRevenueQuery = useRevenueChart("daily");
  const monthlyRevenueQuery = useRevenueChart("monthly");
  const overviewQuery = useOverviewChart(period);
  const topServicesQuery = useTopServices(4);
  const recentReservationsQuery = useRecentReservations(5);
  const usageQuery = useBusinessUsage();

  if (!businessId) {
    return (
      <div className="mx-auto max-w-[1600px] space-y-7">
        <p className="mb-1 text-sm font-medium text-primary">Vista general</p>
          <h1 className="text-3xl font-semibold tracking-tight">Resumen</h1>
        <p className="text-muted-foreground">
          Selecciona un negocio en el selector superior para ver el resumen.
        </p>
      </div>
    );
  }

  if (statsQuery.isLoading) return <PageLoading />;
  if (statsQuery.isError) return <PageError onRetry={statsQuery.refetch} />;

  const stats = statsQuery.data;
  const usage = usageQuery.data;
  const topServices = topServicesQuery.data ?? [];
  const recentReservations = recentReservationsQuery.data ?? [];

  return (
    <div className="mx-auto max-w-[1600px] space-y-7">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <p className="mb-1 text-sm font-medium text-primary">Vista general</p>
          <h1 className="text-3xl font-semibold tracking-tight">Resumen</h1>
          <p className="mt-1 text-muted-foreground">Resumen financiero y operativo de tu negocio.</p>
        </div>
        <Select value={period} onValueChange={setPeriod}>
          <SelectTrigger className="w-[180px] bg-card shadow-sm"><SelectValue placeholder="Periodo" /></SelectTrigger>
          <SelectContent>
            <SelectItem value="today">Hoy</SelectItem>
            <SelectItem value="7d">Ultimos 7 dias</SelectItem>
            <SelectItem value="30d">Ultimos 30 dias</SelectItem>
            <SelectItem value="90d">Ultimos 90 dias</SelectItem>
          </SelectContent>
        </Select>
      </div>

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
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
                <CardTitle className="flex items-center gap-2 text-base">
                  <Gauge className="h-4 w-4 text-primary" />
                  Consumo del agente
                </CardTitle>
                <CardDescription>
                  Plan {usage.planName} - renovacion {new Date(usage.periodEnd).toLocaleDateString("es-CO")}
                </CardDescription>
              </div>
              <div className="text-left sm:text-right">
                <p className="text-2xl font-semibold">{usage.creditsUsagePercent}%</p>
                <p className="text-xs text-muted-foreground">uso del plan</p>
              </div>
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <Progress value={Math.min(100, usage.creditsUsagePercent)} />
            <div className="grid gap-3 text-sm sm:grid-cols-2">
              <div><p className="text-muted-foreground">Creditos usados</p><p className="font-medium">{usage.creditsUsed.toLocaleString("es-CO")} / {usage.creditsLimit.toLocaleString("es-CO")}</p></div>
              <div><p className="text-muted-foreground">Estado</p><p className="font-medium">{usage.status === "Exceeded" || usage.status === 2 ? "Limite alcanzado" : "Activo"}</p></div>
            </div>
          </CardContent>
        </Card>
      )}

      <Card>
        <CardHeader>
          <CardTitle>Ingresos</CardTitle>
          <CardDescription>Evolucion de ingresos por periodo</CardDescription>
        </CardHeader>
        <CardContent>
          <Tabs defaultValue="daily">
            <TabsList>
              <TabsTrigger value="daily">Diario</TabsTrigger>
              <TabsTrigger value="monthly">Mensual</TabsTrigger>
            </TabsList>
            <TabsContent value="daily"><RevenueChart data={dailyRevenueQuery.data ?? []} /></TabsContent>
            <TabsContent value="monthly"><RevenueChart data={monthlyRevenueQuery.data ?? []} /></TabsContent>
          </Tabs>
        </CardContent>
      </Card>

      <div className="grid gap-6 xl:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle>Vista general</CardTitle>
            <CardDescription>Ingresos y reservas combinados</CardDescription>
          </CardHeader>
          <CardContent><OverviewChart data={overviewQuery.data ?? []} /></CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle>Top servicios</CardTitle>
            <CardDescription>Servicios mas reservados y rentables</CardDescription>
          </CardHeader>
          <CardContent>
            <Table>
              <TableHeader><TableRow><TableHead>Servicio</TableHead><TableHead className="text-right">Reservas</TableHead><TableHead className="text-right">Ingresos</TableHead></TableRow></TableHeader>
              <TableBody>
                {topServices.map((service) => (
                  <TableRow key={service.serviceId}>
                    <TableCell className="font-medium">{service.serviceName}</TableCell>
                    <TableCell className="text-right">{service.totalReservations}</TableCell>
                    <TableCell className="text-right">{formatCurrency(service.revenue)}</TableCell>
                  </TableRow>
                ))}
                {topServices.length === 0 && <TableRow><TableCell colSpan={3} className="text-center text-muted-foreground">Sin datos disponibles</TableCell></TableRow>}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Actividad reciente</CardTitle>
          <CardDescription>Ultimas 5 reservas</CardDescription>
        </CardHeader>
        <CardContent>
          <Table>
            <TableHeader><TableRow><TableHead>Cliente</TableHead><TableHead>Servicio</TableHead><TableHead>Fecha</TableHead><TableHead>Estado</TableHead></TableRow></TableHeader>
            <TableBody>
              {recentReservations.map((reservation) => (
                <TableRow key={reservation.reservationId}>
                  <TableCell>{reservation.customerName ?? "Sin cliente"}</TableCell>
                  <TableCell>{reservation.serviceName || "Sin servicio"}</TableCell>
                  <TableCell className="text-muted-foreground">{formatRelativeTime(reservation.reservationDateTime)}</TableCell>
                  <TableCell>{reservationStatusLabel(reservation.status)}</TableCell>
                </TableRow>
              ))}
              {recentReservations.length === 0 && <TableRow><TableCell colSpan={4} className="text-center text-muted-foreground">Sin reservas recientes</TableCell></TableRow>}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
    </div>
  );
}
