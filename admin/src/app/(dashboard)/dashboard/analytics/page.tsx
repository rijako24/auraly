"use client";
import { useState } from "react";
import { CreditCard, TrendingUp, MessageSquare, Activity } from "lucide-react";
import { LineChart, Line, BarChart, Bar, PieChart, Pie, Cell, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, Legend } from "recharts";
import { StatCard } from "@/components/cards/stat-card";
import { PageLoading } from "@/components/ui/page-loading";
import { PageError } from "@/components/ui/page-error";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Badge } from "@/components/ui/badge";
import { formatCurrency } from "@/lib/utils";
import { useAnalyticsMetrics, useCustomerGrowth, useReservationsByDay, useRevenueByCategory, useLeadFunnel, useTopPerformingServices } from "@/hooks/use-analytics";

export default function AnalyticsPage() {
  const [period, setPeriod] = useState("30d");
  const { data: metrics, isLoading, isError, refetch } = useAnalyticsMetrics(period);
  const { data: customerGrowth } = useCustomerGrowth(period);
  const { data: reservationsByDay } = useReservationsByDay(period);
  const { data: revenueByCategory } = useRevenueByCategory(period);
  const { data: leadFunnel } = useLeadFunnel(period);
  const { data: topPerforming } = useTopPerformingServices(period);

  if (isLoading) return <PageLoading />;
  if (isError) return <PageError onRetry={refetch} />;

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div><h1 className="text-2xl font-semibold tracking-tight">Analytics</h1><p className="text-muted-foreground">Métricas detalladas y análisis de rendimiento</p></div>
        <Select value={period} onValueChange={setPeriod}><SelectTrigger className="w-[180px]"><SelectValue placeholder="Período" /></SelectTrigger><SelectContent><SelectItem value="7d">Últimos 7 días</SelectItem><SelectItem value="30d">Últimos 30 días</SelectItem><SelectItem value="3m">Últimos 3 meses</SelectItem><SelectItem value="12m">Último año</SelectItem></SelectContent></Select>
      </div>
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard title="Tasa de conversión" value={`${metrics?.conversionRate ?? 0}%`} change={metrics?.conversionRateChange} icon={TrendingUp} />
        <StatCard title="Valor promedio reserva" value={formatCurrency(metrics?.avgBookingValue ?? 0)} change={metrics?.avgBookingValueChange} icon={CreditCard} />
        <StatCard title="Clientes recurrentes" value={`${metrics?.repeatCustomerRate ?? 0}%`} change={metrics?.repeatCustomerRateChange} icon={Activity} />
        <StatCard title="Tiempo de respuesta" value={`${metrics?.avgResponseTime ?? 0} min`} change={metrics?.avgResponseTimeChange} icon={MessageSquare} />
      </div>
      <div className="grid gap-6 lg:grid-cols-2">
        <Card><CardHeader><CardTitle>Crecimiento de clientes</CardTitle><CardDescription>Evolución del número de clientes únicos en el tiempo</CardDescription></CardHeader><CardContent><div className="h-[300px] w-full"><ResponsiveContainer width="100%" height="100%"><LineChart data={customerGrowth ?? []} margin={{ top: 10, right: 10, left: 0, bottom: 0 }}><CartesianGrid strokeDasharray="3 3" className="stroke-muted" /><XAxis dataKey="date" stroke="hsl(var(--muted-foreground))" fontSize={12} tickLine={false} axisLine={false} /><YAxis stroke="hsl(var(--muted-foreground))" fontSize={12} tickLine={false} axisLine={false} /><Tooltip content={({ active, payload }) => { if (active && payload?.[0]) { return (<div className="rounded-lg border bg-background px-3 py-2 shadow-sm"><p className="text-sm font-medium">{payload[0].payload.date}: {payload[0].value} clientes</p></div>); } return null; }} /><Line type="monotone" dataKey="value" stroke="hsl(var(--primary))" strokeWidth={2} dot={{ fill: "hsl(var(--primary))" }} /></LineChart></ResponsiveContainer></div></CardContent></Card>
        <Card><CardHeader><CardTitle>Reservas por día de la semana</CardTitle><CardDescription>Distribución de reservas según el día</CardDescription></CardHeader><CardContent><div className="h-[300px] w-full"><ResponsiveContainer width="100%" height="100%"><BarChart data={reservationsByDay ?? []} margin={{ top: 10, right: 10, left: 0, bottom: 0 }}><CartesianGrid strokeDasharray="3 3" className="stroke-muted" /><XAxis dataKey="day" stroke="hsl(var(--muted-foreground))" fontSize={12} tickLine={false} axisLine={false} /><YAxis stroke="hsl(var(--muted-foreground))" fontSize={12} tickLine={false} axisLine={false} /><Tooltip content={({ active, payload }) => { if (active && payload?.[0]) { return (<div className="rounded-lg border bg-background px-3 py-2 shadow-sm"><p className="text-sm font-medium">{payload[0].payload.day}: {payload[0].value} reservas</p></div>); } return null; }} /><Bar dataKey="count" fill="hsl(var(--chart-2))" radius={[4, 4, 0, 0]} /></BarChart></ResponsiveContainer></div></CardContent></Card>
        <Card><CardHeader><CardTitle>Ingresos por categoría</CardTitle><CardDescription>Distribución de ingresos según tipo de servicio</CardDescription></CardHeader><CardContent><div className="h-[300px] w-full"><ResponsiveContainer width="100%" height="100%"><PieChart><Pie data={revenueByCategory ?? []} cx="50%" cy="50%" innerRadius={60} outerRadius={100} paddingAngle={2} dataKey="value">{(revenueByCategory ?? []).map((entry, index) => (<Cell key={`cell-${index}`} fill={entry.color} />))}</Pie><Tooltip content={({ active, payload }) => { if (active && payload?.[0]) { const data = payload[0].payload; const total = (revenueByCategory ?? []).reduce((s, d) => s + d.value, 0); const pct = total > 0 ? ((data.value / total) * 100).toFixed(1) : "0"; return (<div className="rounded-lg border bg-background px-3 py-2 shadow-sm"><p className="text-sm font-medium">{data.name}</p><p className="text-xs text-muted-foreground">{formatCurrency(data.value)} ({pct}%)</p></div>); } return null; }} /><Legend /></PieChart></ResponsiveContainer></div></CardContent></Card>
        <Card><CardHeader><CardTitle>Embudo de leads</CardTitle><CardDescription>Conversión desde contacto inicial hasta cliente recurrente</CardDescription></CardHeader><CardContent><div className="h-[300px] w-full"><ResponsiveContainer width="100%" height="100%"><BarChart data={leadFunnel ?? []} layout="vertical" margin={{ top: 10, right: 30, left: 80, bottom: 0 }}><CartesianGrid strokeDasharray="3 3" className="stroke-muted" horizontal={false} /><XAxis type="number" stroke="hsl(var(--muted-foreground))" fontSize={12} tickLine={false} axisLine={false} /><YAxis type="category" dataKey="stage" stroke="hsl(var(--muted-foreground))" fontSize={12} tickLine={false} axisLine={false} width={75} /><Tooltip content={({ active, payload }) => { if (active && payload?.[0]) { return (<div className="rounded-lg border bg-background px-3 py-2 shadow-sm"><p className="text-sm font-medium">{payload[0].payload.stage}: {payload[0].value} leads</p></div>); } return null; }} /><Bar dataKey="count" fill="hsl(var(--primary))" radius={[0, 4, 4, 0]} /></BarChart></ResponsiveContainer></div></CardContent></Card>
      </div>
      <Card><CardHeader><CardTitle>Servicios con mejor rendimiento</CardTitle><CardDescription>Top servicios por reservas, ingresos y crecimiento</CardDescription></CardHeader><CardContent><div className="overflow-x-auto"><Table><TableHeader><TableRow><TableHead>Servicio</TableHead><TableHead className="text-right">Reservas</TableHead><TableHead className="text-right">Ingresos</TableHead><TableHead className="text-right">Crecimiento</TableHead></TableRow></TableHeader><TableBody>{(topPerforming ?? []).map((svc) => (<TableRow key={svc.serviceId}><TableCell className="font-medium">{svc.serviceName}</TableCell><TableCell className="text-right">{svc.totalReservations}</TableCell><TableCell className="text-right">{formatCurrency(svc.revenue)}</TableCell><TableCell className="text-right"><Badge variant={svc.growthPercent >= 0 ? "default" : "secondary"} className={svc.growthPercent >= 0 ? "bg-emerald-500/20 text-emerald-600 dark:text-emerald-400" : "bg-red-500/20 text-red-600 dark:text-red-400"}>{svc.growthPercent >= 0 ? "+" : ""}{svc.growthPercent}%</Badge></TableCell></TableRow>))}{(!topPerforming || topPerforming.length === 0) && <TableRow><TableCell colSpan={4} className="text-center text-muted-foreground">Sin datos disponibles</TableCell></TableRow>}</TableBody></Table></div></CardContent></Card>
    </div>
  );
}
