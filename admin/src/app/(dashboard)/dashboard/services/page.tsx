"use client";

import { useMemo, useState } from "react";
import Link from "next/link";
import type { ColumnDef } from "@tanstack/react-table";
import {
  MoreHorizontal,
  Plus,
  Eye,
  Pencil,
  Copy,
  PowerOff,
} from "lucide-react";

import { DataTable } from "@/components/tables/data-table";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader } from "@/components/ui/card";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { PageLoading } from "@/components/ui/page-loading";
import { PageError } from "@/components/ui/page-error";
import {
  ServiceTierLabels,
  ServiceTierColors,
  ServiceTypeLabels,
  ServiceTypeColors,
} from "@/types/enums";
import type { Service } from "@/types/entities";
import { formatCurrency, cn } from "@/lib/utils";
import { useServices } from "@/hooks/use-services";

export default function ServicesPage() {
  const [viewMode, setViewMode] = useState<"table" | "card" | "list">("table");
  const { data, isLoading, isError, refetch } = useServices();

  const services = data?.items ?? [];

  const columns: ColumnDef<Service>[] = useMemo(
    () => [
      {
        accessorKey: "serviceName",
        header: "Nombre",
        cell: ({ row }) => (
          <div className="font-medium">{row.original.serviceName}</div>
        ),
      },
      {
        accessorKey: "categoryName",
        header: "Categoría",
        cell: ({ row }) => row.original.categoryName ?? row.original.category?.name ?? "—",
      },
      {
        accessorKey: "durationMinutes",
        header: "Duración",
        cell: ({ row }) => `${row.original.durationMinutes} min`,
      },
      {
        accessorKey: "price",
        header: "Precio",
        cell: ({ row }) => formatCurrency(row.original.price),
      },
      {
        accessorKey: "tier",
        header: "Tier",
        cell: ({ row }) => {
          const tier = row.original.tier as keyof typeof ServiceTierLabels;
          return (
            <Badge variant="secondary" className={cn(ServiceTierColors[tier])}>
              {ServiceTierLabels[tier] ?? "—"}
            </Badge>
          );
        },
      },
      {
        accessorKey: "serviceType",
        header: "Tipo",
        cell: ({ row }) => {
          const type = row.original.serviceType as keyof typeof ServiceTypeLabels;
          return (
            <Badge variant="outline" className={cn(ServiceTypeColors[type])}>
              {ServiceTypeLabels[type] ?? "—"}
            </Badge>
          );
        },
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
        id: "actions",
        cell: ({ row }) => {
          const svc = row.original;
          return (
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="ghost" size="icon" className="h-8 w-8">
                  <MoreHorizontal className="h-4 w-4" />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <DropdownMenuItem asChild>
                  <Link href={`/dashboard/services/${svc.serviceId}`}>
                    <Eye className="mr-2 h-4 w-4" />
                    Ver
                  </Link>
                </DropdownMenuItem>
                <DropdownMenuItem asChild>
                  <Link href={`/dashboard/services/${svc.serviceId}/edit`}>
                    <Pencil className="mr-2 h-4 w-4" />
                    Editar
                  </Link>
                </DropdownMenuItem>
                <DropdownMenuItem>
                  <Copy className="mr-2 h-4 w-4" />
                  Duplicar
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

  const facetedFilters = useMemo(
    () => [
      {
        column: "tier",
        title: "Tier",
        options: Object.entries(ServiceTierLabels).map(([value, label]) => ({
          label,
          value,
        })),
      },
      {
        column: "serviceType",
        title: "Tipo",
        options: Object.entries(ServiceTypeLabels).map(([value, label]) => ({
          label,
          value,
        })),
      },
    ],
    []
  );

  const cardRenderer = (item: Service) => (
    <Card key={item.serviceId} className="overflow-hidden">
      <CardHeader className="pb-2">
        <div className="flex items-start justify-between gap-2">
          <div className="min-w-0 flex-1">
            <h3 className="font-semibold">{item.serviceName}</h3>
            <p className="text-sm text-muted-foreground line-clamp-2 mt-1">
              {item.description}
            </p>
          </div>
          <Badge className={cn("shrink-0", ServiceTierColors[item.tier as keyof typeof ServiceTierColors])}>
            {ServiceTierLabels[item.tier as keyof typeof ServiceTierLabels] ?? "—"}
          </Badge>
        </div>
      </CardHeader>
      <CardContent className="pt-0">
        <div className="flex items-center justify-between text-sm">
          <span className="font-medium text-primary">{formatCurrency(item.price)}</span>
          <span className="text-muted-foreground">{item.durationMinutes} min</span>
        </div>
        <div className="mt-2 flex gap-1">
          <Badge variant="outline" className={cn("text-xs", ServiceTypeColors[item.serviceType as keyof typeof ServiceTypeColors])}>
            {ServiceTypeLabels[item.serviceType as keyof typeof ServiceTypeLabels] ?? "—"}
          </Badge>
          <Badge variant={item.isActive ? "default" : "secondary"}>
            {item.isActive ? "Activo" : "Inactivo"}
          </Badge>
        </div>
      </CardContent>
    </Card>
  );

  if (isLoading) return <PageLoading cards={0} />;
  if (isError) return <PageError onRetry={refetch} />;

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Servicios</h1>
          <p className="text-muted-foreground">
            Gestiona los servicios ofrecidos en tu spa para bebés
          </p>
        </div>
        <Button asChild>
          <Link href="/dashboard/services/new">
            <Plus className="mr-2 h-4 w-4" />
            Nuevo Servicio
          </Link>
        </Button>
      </div>

      <DataTable
        columns={columns}
        data={services}
        searchKey="serviceName"
        searchPlaceholder="Buscar por nombre..."
        facetedFilters={facetedFilters}
        viewMode={viewMode}
        onViewModeChange={setViewMode}
        cardRenderer={cardRenderer}
        enableRowSelection={false}
      />
    </div>
  );
}
