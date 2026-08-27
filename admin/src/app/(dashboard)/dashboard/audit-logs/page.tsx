"use client";
import { useMemo, useState } from "react";
import type { ColumnDef } from "@tanstack/react-table";
import { MoreHorizontal } from "lucide-react";
import { DataTable } from "@/components/tables/data-table";
import { Button } from "@/components/ui/button";
import { DatePicker } from "@/components/ui/date-picker";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { PageLoading } from "@/components/ui/page-loading";
import { PageError } from "@/components/ui/page-error";
import type { AuditLog } from "@/types/entities";
import { formatDateTime } from "@/lib/utils";
import { cn } from "@/lib/utils";
import { useAuditLogs } from "@/hooks/use-audit-logs";

const ACTION_LABELS: Record<string, string> = { Create: "Crear", Update: "Actualizar", Delete: "Eliminar", Login: "Login" };

export default function AuditLogsPage() {
  const [dateFrom, setDateFrom] = useState("");
  const [dateTo, setDateTo] = useState("");
  const [actionFilter, setActionFilter] = useState<string>("all");
  const [entityFilter, setEntityFilter] = useState<string>("all");
  const [selectedLog, setSelectedLog] = useState<AuditLog | null>(null);
  const [sheetOpen, setSheetOpen] = useState(false);

  const filters = useMemo(() => ({
    ...(dateFrom ? { fromDate: dateFrom } : {}),
    ...(dateTo ? { toDate: dateTo } : {}),
    ...(actionFilter !== "all" ? { action: actionFilter } : {}),
    ...(entityFilter !== "all" ? { entityType: entityFilter } : {}),
  }), [dateFrom, dateTo, actionFilter, entityFilter]);

  const { data, isLoading, isError, refetch } = useAuditLogs(filters);
  const logs = data?.items ?? [];

  const columns: ColumnDef<AuditLog>[] = useMemo(() => [
    { accessorKey: "timestamp", header: "Fecha", cell: ({ row }) => formatDateTime(row.original.timestamp) },
    { accessorKey: "user", header: "Usuario", cell: ({ row }) => { const u = row.original.user; return u ? `${u.firstName} ${u.lastName}` : "Sistema"; } },
    { accessorKey: "action", header: "Acción", cell: ({ row }) => <span className="font-medium">{ACTION_LABELS[row.original.action] ?? row.original.action}</span> },
    { accessorKey: "entityType", header: "Entidad" },
    { accessorKey: "entityId", header: "ID Entidad", cell: ({ row }) => row.original.entityId ?? "—" },
    { accessorKey: "ipAddress", header: "IP", cell: ({ row }) => row.original.ipAddress ?? "—" },
    { id: "actions", cell: ({ row }) => (<DropdownMenu><DropdownMenuTrigger asChild><Button variant="ghost" size="icon" className="h-8 w-8"><MoreHorizontal className="h-4 w-4" /></Button></DropdownMenuTrigger><DropdownMenuContent align="end"><DropdownMenuItem onClick={() => { setSelectedLog(row.original); setSheetOpen(true); }}>Ver detalle</DropdownMenuItem></DropdownMenuContent></DropdownMenu>) },
  ], []);

  const parseJsonSafe = (str: string | null): Record<string, unknown> | null => { if (!str) return null; try { return JSON.parse(str) as Record<string, unknown>; } catch { return null; } };

  if (isLoading) return <PageLoading cards={0} />;
  if (isError) return <PageError onRetry={refetch} />;

  return (
    <div className="space-y-6">
      <div><h1 className="text-2xl font-semibold tracking-tight">Registro de Auditoría</h1><p className="text-muted-foreground">Historial de acciones en el sistema</p></div>
      <div className="flex flex-wrap gap-4 rounded-lg border p-4">
        <div className="space-y-2"><Label>Desde</Label><DatePicker value={dateFrom} max={dateTo || undefined} onChange={setDateFrom} className="w-[190px]" /></div>
        <div className="space-y-2"><Label>Hasta</Label><DatePicker value={dateTo} min={dateFrom || undefined} onChange={setDateTo} className="w-[190px]" /></div>
        <div className="space-y-2"><Label>Tipo de acción</Label><Select value={actionFilter} onValueChange={setActionFilter}><SelectTrigger className="w-[160px]"><SelectValue placeholder="Todos" /></SelectTrigger><SelectContent><SelectItem value="all">Todos</SelectItem><SelectItem value="Create">Crear</SelectItem><SelectItem value="Update">Actualizar</SelectItem><SelectItem value="Delete">Eliminar</SelectItem><SelectItem value="Login">Login</SelectItem></SelectContent></Select></div>
        <div className="space-y-2"><Label>Tipo de entidad</Label><Select value={entityFilter} onValueChange={setEntityFilter}><SelectTrigger className="w-[180px]"><SelectValue placeholder="Todos" /></SelectTrigger><SelectContent><SelectItem value="all">Todos</SelectItem><SelectItem value="User">Usuario</SelectItem><SelectItem value="Reservation">Reservación</SelectItem><SelectItem value="Lead">Lead</SelectItem><SelectItem value="PaymentTransaction">Pago</SelectItem><SelectItem value="AppRole">Rol</SelectItem><SelectItem value="Service">Servicio</SelectItem><SelectItem value="Conversation">Conversación</SelectItem><SelectItem value="SystemConfiguration">Config. Sistema</SelectItem></SelectContent></Select></div>
      </div>
      <div className="rounded-md border"><DataTable columns={columns} data={logs} searchKey="action" searchPlaceholder="Buscar por acción..." enableRowSelection={false} /></div>
      <Sheet open={sheetOpen} onOpenChange={setSheetOpen}>
        <SheetContent className="overflow-y-auto sm:max-w-xl">
          <SheetHeader><SheetTitle>Detalle del registro</SheetTitle></SheetHeader>
          {selectedLog && (
            <div className="mt-6 space-y-4">
              <div className="grid gap-2 text-sm">
                <div className="flex justify-between"><span className="text-muted-foreground">ID</span><span className="font-mono">{selectedLog.auditLogId}</span></div>
                <div className="flex justify-between"><span className="text-muted-foreground">Fecha</span><span>{formatDateTime(selectedLog.timestamp)}</span></div>
                <div className="flex justify-between"><span className="text-muted-foreground">Usuario</span><span>{selectedLog.user ? `${selectedLog.user.firstName} ${selectedLog.user.lastName}` : "Sistema"}</span></div>
                <div className="flex justify-between"><span className="text-muted-foreground">Acción</span><span>{ACTION_LABELS[selectedLog.action] ?? selectedLog.action}</span></div>
                <div className="flex justify-between"><span className="text-muted-foreground">Entidad</span><span>{selectedLog.entityType}{selectedLog.entityId ? ` (${selectedLog.entityId})` : ""}</span></div>
                <div className="flex justify-between"><span className="text-muted-foreground">IP</span><span>{selectedLog.ipAddress ?? "—"}</span></div>
                {selectedLog.userAgent && <div className="flex flex-col gap-1"><span className="text-muted-foreground">User Agent</span><span className="break-all text-xs">{selectedLog.userAgent}</span></div>}
              </div>
              {(selectedLog.oldValues || selectedLog.newValues) && (
                <div className="space-y-3 border-t pt-4">
                  <h4 className="font-medium">Cambios</h4>
                  <div className="grid gap-3 sm:grid-cols-2">
                    <div><p className="mb-1 text-xs font-medium text-muted-foreground">Valores anteriores</p><pre className={cn("overflow-auto rounded-md border bg-muted/50 p-2 text-xs", !selectedLog.oldValues && "italic text-muted-foreground")}>{selectedLog.oldValues ? JSON.stringify(parseJsonSafe(selectedLog.oldValues) ?? selectedLog.oldValues, null, 2) : "—"}</pre></div>
                    <div><p className="mb-1 text-xs font-medium text-muted-foreground">Valores nuevos</p><pre className={cn("overflow-auto rounded-md border bg-muted/50 p-2 text-xs", !selectedLog.newValues && "italic text-muted-foreground")}>{selectedLog.newValues ? JSON.stringify(parseJsonSafe(selectedLog.newValues) ?? selectedLog.newValues, null, 2) : "—"}</pre></div>
                  </div>
                </div>
              )}
            </div>
          )}
        </SheetContent>
      </Sheet>
    </div>
  );
}

