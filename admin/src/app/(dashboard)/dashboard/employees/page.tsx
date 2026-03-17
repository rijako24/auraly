"use client";

import { useMemo, useState } from "react";
import Link from "next/link";
import type { ColumnDef } from "@tanstack/react-table";
import { MoreHorizontal, Plus, Eye, Pencil, PowerOff } from "lucide-react";

import { DataTable } from "@/components/tables/data-table";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Card, CardHeader } from "@/components/ui/card";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { PageLoading } from "@/components/ui/page-loading";
import { PageError } from "@/components/ui/page-error";
import { formatDate, getInitials } from "@/lib/utils";
import type { Employee } from "@/types/entities";
import { useEmployees } from "@/hooks/use-employees";

export default function EmployeesPage() {
  const [viewMode, setViewMode] = useState<"table" | "card" | "list">("table");
  const { data, isLoading, isError, refetch } = useEmployees();
  const employees = data?.items ?? [];

  const columns: ColumnDef<Employee>[] = useMemo(
    () => [
      {
        accessorKey: "name",
        header: "Nombre",
        cell: ({ row }) => (
          <div className="flex items-center gap-3">
            <Avatar className="h-8 w-8">
              <AvatarFallback className="text-xs">{getInitials(row.original.name)}</AvatarFallback>
            </Avatar>
            <span className="font-medium">{row.original.name}</span>
          </div>
        ),
      },
      {
        accessorKey: "services",
        header: "Servicios",
        cell: ({ row }) => row.original.services?.length ?? 0,
      },
      {
        accessorKey: "isActive",
        header: "Estado",
        cell: ({ row }) => (
          <Badge variant={row.original.isActive ? "default" : "secondary"}>
            {row.original.isActive ? "Activo" : "Inactivo"}
          </Badge>
        ),
      },
      {
        accessorKey: "createdAt",
        header: "Creado",
        cell: ({ row }) => formatDate(row.original.createdAt),
      },
      {
        id: "actions",
        cell: ({ row }) => {
          const emp = row.original;
          return (
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="ghost" size="icon" className="h-8 w-8">
                  <MoreHorizontal className="h-4 w-4" />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <DropdownMenuItem asChild>
                  <Link href={`/dashboard/employees/${emp.employeeId}`}>
                    <Eye className="mr-2 h-4 w-4" />
                    Ver
                  </Link>
                </DropdownMenuItem>
                <DropdownMenuItem asChild>
                  <Link href={`/dashboard/employees/${emp.employeeId}/edit`}>
                    <Pencil className="mr-2 h-4 w-4" />
                    Editar
                  </Link>
                </DropdownMenuItem>
                <DropdownMenuSeparator />
                <DropdownMenuItem className="text-destructive">
                  <PowerOff className="mr-2 h-4 w-4" />
                  Desactivar
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          );
        },
      },
    ],
    []
  );

  const cardRenderer = (item: Employee) => (
    <Card key={item.employeeId} className="overflow-hidden">
      <CardHeader className="pb-2">
        <div className="flex items-start gap-3">
          <Avatar className="h-12 w-12 shrink-0">
            <AvatarFallback>{getInitials(item.name)}</AvatarFallback>
          </Avatar>
          <div className="min-w-0 flex-1">
            <h3 className="font-semibold">{item.name}</h3>
            <Badge variant={item.isActive ? "default" : "secondary"} className="mt-1">
              {item.isActive ? "Activo" : "Inactivo"}
            </Badge>
            {item.services && item.services.length > 0 && (
              <div className="mt-2 flex flex-wrap gap-1">
                {item.services.slice(0, 3).map((s) => (
                  <Badge key={s.serviceId} variant="outline" className="text-xs">{s.serviceName}</Badge>
                ))}
                {item.services.length > 3 && (
                  <Badge variant="outline" className="text-xs">+{item.services.length - 3}</Badge>
                )}
              </div>
            )}
          </div>
        </div>
      </CardHeader>
    </Card>
  );

  if (isLoading) return <PageLoading cards={0} />;
  if (isError) return <PageError onRetry={refetch} />;

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Empleados</h1>
          <p className="text-muted-foreground">Gestiona el equipo de terapeutas y personal del spa</p>
        </div>
        <Button asChild>
          <Link href="/dashboard/employees/new">
            <Plus className="mr-2 h-4 w-4" />
            Nuevo Empleado
          </Link>
        </Button>
      </div>
      <DataTable
        columns={columns}
        data={employees}
        searchKey="name"
        searchPlaceholder="Buscar por nombre..."
        viewMode={viewMode}
        onViewModeChange={setViewMode}
        cardRenderer={cardRenderer}
        enableRowSelection={false}
      />
    </div>
  );
}
