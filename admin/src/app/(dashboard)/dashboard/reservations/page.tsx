"use client";

import { useMemo, useState } from "react";
import Link from "next/link";
import type { ColumnDef } from "@tanstack/react-table";
import { MoreHorizontal, Plus, Eye, CheckCircle, XCircle, CalendarDays, Clock, User } from "lucide-react";

import { DataTable } from "@/components/tables/data-table";
import { StatCard } from "@/components/cards/stat-card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent } from "@/components/ui/card";
import { DatePicker } from "@/components/ui/date-picker";
import { Label } from "@/components/ui/label";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { PageLoading } from "@/components/ui/page-loading";
import { PageError } from "@/components/ui/page-error";
import { ReservationStatus, ReservationStatusLabels, ReservationStatusColors } from "@/types/enums";
import type { Reservation } from "@/types/entities";
import { formatCurrency, formatDateTime, truncate, cn } from "@/lib/utils";
import { useReservations } from "@/hooks/use-reservations";

export default function ReservationsPage() {
  const [viewMode, setViewMode] = useState<"table" | "card" | "list">("table");
  const [startDate, setStartDate] = useState(todayInputValue);
  const [endDate, setEndDate] = useState(todayInputValue);
  const { data, isLoading, isError, refetch } = useReservations({ startDate: `${startDate}T00:00:00`, endDate: `${endDate}T23:59:59`, page: 1, pageSize: 100 });
  const reservations = useMemo(() => data?.items ?? [], [data?.items]);

  const stats = useMemo(() => ({
    total: reservations.length,
    pending: reservations.filter((r) => r.status === ReservationStatus.Pending).length,
    confirmed: reservations.filter((r) => r.status === ReservationStatus.Confirmed).length,
    completed: reservations.filter((r) => r.status === ReservationStatus.Completed).length,
  }), [reservations]);

  const columns: ColumnDef<Reservation>[] = useMemo(() => [
    { accessorKey: "reservationId", header: "ID", cell: ({ row }) => <span className="font-mono text-xs">{truncate(row.original.reservationId, 12)}</span> },
    { accessorKey: "serviceName", header: "Servicio", cell: ({ row }) => row.original.serviceName ?? row.original.service?.serviceName ?? "—" },
    { accessorKey: "employeeName", header: "Empleado", cell: ({ row }) => row.original.employeeName ?? row.original.employee?.name ?? "—" },
    { accessorKey: "reservationDateTime", header: "Fecha/Hora", cell: ({ row }) => row.original.reservationDateTime ? formatDateTime(row.original.reservationDateTime) : "—" },
    { accessorKey: "durationMinutes", header: "Duración", cell: ({ row }) => row.original.durationMinutes ? `${row.original.durationMinutes} min` : "—" },
    { accessorKey: "status", header: "Estado", cell: ({ row }) => { const status = row.original.status; return <Badge variant="secondary" className={cn(ReservationStatusColors[status])}>{ReservationStatusLabels[status]}</Badge>; } },
    { id: "actions", cell: ({ row }) => { const res = row.original; return (<DropdownMenu><DropdownMenuTrigger asChild><Button variant="ghost" size="icon" className="h-8 w-8"><MoreHorizontal className="h-4 w-4" /></Button></DropdownMenuTrigger><DropdownMenuContent align="end"><DropdownMenuItem asChild><Link href={`/dashboard/reservations/${res.reservationId}`}><Eye className="mr-2 h-4 w-4" />Ver</Link></DropdownMenuItem>{res.status === ReservationStatus.Pending && <DropdownMenuItem><CheckCircle className="mr-2 h-4 w-4" />Confirmar</DropdownMenuItem>}{res.status !== ReservationStatus.Cancelled && res.status !== ReservationStatus.Completed && <DropdownMenuItem className="text-destructive"><XCircle className="mr-2 h-4 w-4" />Cancelar</DropdownMenuItem>}</DropdownMenuContent></DropdownMenu>); } },
  ], []);

  const facetedFilters = useMemo(() => [{ column: "status", title: "Estado", options: Object.entries(ReservationStatusLabels).map(([value, label]) => ({ label, value })) }], []);

  const statusColorMap: Record<ReservationStatus, string> = {
    [ReservationStatus.Pending]: "border-l-yellow-500",
    [ReservationStatus.Confirmed]: "border-l-green-500",
    [ReservationStatus.Completed]: "border-l-blue-500",
    [ReservationStatus.Cancelled]: "border-l-red-500",
    [ReservationStatus.PendingCalendar]: "border-l-orange-500",
    [ReservationStatus.OnHold]: "border-l-gray-500",
  };

  const cardRenderer = (item: Reservation) => (
    <Card key={item.reservationId} className={cn("overflow-hidden border-l-4", statusColorMap[item.status])}>
      <CardContent className="pt-4">
        <div className="flex items-start justify-between gap-2">
          <div className="min-w-0 flex-1">
            <h3 className="font-semibold">{item.serviceName ?? item.service?.serviceName ?? "—"}</h3>
            <p className="text-sm text-muted-foreground">{item.employeeName ?? item.employee?.name ?? "—"}</p>
            <div className="mt-2 flex flex-wrap gap-1 text-xs text-muted-foreground">
              <span className="flex items-center gap-1"><CalendarDays className="h-3 w-3" />{item.reservationDateTime ? formatDateTime(item.reservationDateTime) : "—"}</span>
              <span className="flex items-center gap-1"><Clock className="h-3 w-3" />{item.durationMinutes ? `${item.durationMinutes} min` : "—"}</span>
            </div>
          </div>
          <Badge variant="secondary" className={cn(ReservationStatusColors[item.status])}>{ReservationStatusLabels[item.status]}</Badge>
        </div>
      </CardContent>
    </Card>
  );

  if (isLoading) return <PageLoading />;
  if (isError) return <PageError onRetry={refetch} />;

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Reservaciones</h1>
          <p className="text-muted-foreground">Gestiona las reservaciones del negocio seleccionado</p>
        </div>
        <div className="flex gap-2">
          <Button variant="outline" asChild><Link href="/dashboard/reservations/calendar"><CalendarDays className="mr-2 h-4 w-4" />Calendario</Link></Button>
          <Button asChild><Link href="/dashboard/reservations/new"><Plus className="mr-2 h-4 w-4" />Nueva Reservación</Link></Button>
        </div>
      </div>
      <Card>
        <CardContent className="grid gap-3 pt-5 sm:grid-cols-[1fr_1fr_auto] sm:items-end">
          <div className="space-y-1.5"><Label>Desde</Label><DatePicker value={startDate} onChange={setStartDate} /></div>
          <div className="space-y-1.5"><Label>Hasta</Label><DatePicker value={endDate} onChange={setEndDate} /></div>
          <Button variant="outline" onClick={() => { const today = todayInputValue(); setStartDate(today); setEndDate(today); }}>Hoy</Button>
        </CardContent>
      </Card>      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard title="Total" value={stats.total} icon={CalendarDays} />
        <StatCard title="Pendientes" value={stats.pending} icon={Clock} />
        <StatCard title="Confirmadas" value={stats.confirmed} icon={CheckCircle} />
        <StatCard title="Completadas" value={stats.completed} icon={User} />
      </div>
      <DataTable columns={columns} data={reservations} searchKey="reservationId" searchPlaceholder="Buscar por ID..." facetedFilters={facetedFilters} viewMode={viewMode} onViewModeChange={setViewMode} cardRenderer={cardRenderer} enableRowSelection={false} />
    </div>
  );
}

function todayInputValue() {
  const date = new Date();
  date.setMinutes(date.getMinutes() - date.getTimezoneOffset());
  return date.toISOString().slice(0, 10);
}