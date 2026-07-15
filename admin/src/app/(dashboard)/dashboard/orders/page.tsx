"use client";

import { useMemo, useState } from "react";
import Link from "next/link";
import type { ColumnDef } from "@tanstack/react-table";
import {
  CheckCircle,
  Eye,
  MoreHorizontal,
  PackageCheck,
  ReceiptText,
  ShoppingCart,
  X,
} from "lucide-react";

import { StatCard } from "@/components/cards/stat-card";
import { DataTable } from "@/components/tables/data-table";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { DatePicker } from "@/components/ui/date-picker";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Input } from "@/components/ui/input";
import { PageError } from "@/components/ui/page-error";
import { PageLoading } from "@/components/ui/page-loading";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { useOrders, useOrderSummary } from "@/hooks/use-orders";
import { cn, formatCurrency, formatDateTime, truncate } from "@/lib/utils";
import { useBusinessContextStore } from "@/stores/business-context-store";
import {
  OrderFulfillmentModeLabels,
  OrderSourceLabels,
  OrderStatus,
  OrderStatusColors,
  OrderStatusLabels,
} from "@/types/enums";
import type { Order } from "@/types/entities";

export default function OrdersPage() {
  const selectedBusinessId = useBusinessContextStore((s) => s.selectedBusinessId);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [search, setSearch] = useState("");
  const [customer, setCustomer] = useState("");
  const [createdFrom, setCreatedFrom] = useState("");
  const [createdTo, setCreatedTo] = useState("");
  const [status, setStatus] = useState<OrderStatus | "all">("all");

  const filters = {
    search: search || undefined,
    customer: customer || undefined,
    createdFrom: createdFrom || undefined,
    createdTo: createdTo || undefined,
    status: status === "all" ? undefined : status,
  };

  const { data, isLoading, isError, refetch } = useOrders({
    page,
    pageSize,
    ...filters,
  });
  const {
    data: summary,
    isLoading: isSummaryLoading,
    isError: isSummaryError,
    refetch: refetchSummary,
  } = useOrderSummary(filters);

  const orders = data?.items ?? [];

  const columns: ColumnDef<Order>[] = useMemo(
    () => [
      {
        accessorKey: "orderId",
        header: "Pedido",
        cell: ({ row }) => (
          <div className="space-y-1">
            <span className="font-mono text-xs">{truncate(row.original.orderId, 12)}</span>
            {row.original.externalDocumentNumber && (
              <p className="text-xs text-muted-foreground">
                Doc. {row.original.externalDocumentNumber}
              </p>
            )}
          </div>
        ),
      },
      {
        accessorKey: "customerName",
        header: "Cliente",
        cell: ({ row }) => (
          <div className="min-w-[160px] space-y-1">
            <p className="font-medium">{row.original.customerName ?? "Sin cliente"}</p>
            <p className="text-xs text-muted-foreground">
              {row.original.customerPhone ?? row.original.customerEmail ?? "Sin contacto"}
            </p>
          </div>
        ),
      },
      {
        accessorKey: "total",
        header: "Total",
        cell: ({ row }) => (
          <span className="font-medium">
            {formatCurrency(row.original.total, row.original.currency)}
          </span>
        ),
      },
      {
        accessorKey: "items",
        header: "Items",
        cell: ({ row }) => row.original.items.length,
      },
      {
        accessorKey: "status",
        header: "Estado",
        cell: ({ row }) => {
          const statusKey = row.original.status;
          return (
            <Badge variant="secondary" className={cn(OrderStatusColors[statusKey])}>
              {OrderStatusLabels[statusKey]}
            </Badge>
          );
        },
      },
      {
        accessorKey: "source",
        header: "Origen",
        cell: ({ row }) => (
          <Badge variant="outline">{OrderSourceLabels[row.original.source]}</Badge>
        ),
      },
      {
        accessorKey: "fulfillmentMode",
        header: "Entrega",
        cell: ({ row }) => OrderFulfillmentModeLabels[row.original.fulfillmentMode],
      },
      {
        accessorKey: "createdAt",
        header: "Creado",
        cell: ({ row }) => formatDateTime(row.original.createdAt),
      },
      {
        id: "actions",
        cell: ({ row }) => {
          const order = row.original;
          return (
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="ghost" size="icon" className="h-8 w-8">
                  <MoreHorizontal className="h-4 w-4" />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <DropdownMenuItem asChild>
                  <Link href={`/dashboard/orders/${order.orderId}`}>
                    <Eye className="mr-2 h-4 w-4" />
                    Ver detalle
                  </Link>
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          );
        },
      },
    ],
    []
  );

  const resetFilters = () => {
    setCustomer("");
    setCreatedFrom("");
    setCreatedTo("");
    setStatus("all");
    setPage(1);
  };

  if (!selectedBusinessId) {
    return <PageError message="Selecciona un negocio para ver los pedidos." />;
  }

  if (isLoading) return <PageLoading />;
  if (isError || isSummaryError) {
    return (
      <PageError
        onRetry={() => {
          refetch();
          refetchSummary();
        }}
      />
    );
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Pedidos</h1>
        <p className="text-muted-foreground">Pedidos creados por el bot, admin o integraciones</p>
      </div>

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard
          title="Total filtrado"
          value={isSummaryLoading ? "..." : formatCurrency(summary?.totalAmount ?? 0)}
          icon={ReceiptText}
        />
        <StatCard
          title="Pedidos"
          value={isSummaryLoading ? "..." : summary?.totalOrders ?? 0}
          icon={ShoppingCart}
        />
        <StatCard
          title="Confirmados"
          value={isSummaryLoading ? "..." : summary?.confirmedCount ?? 0}
          icon={CheckCircle}
        />
        <StatCard
          title="Sincronizados"
          value={isSummaryLoading ? "..." : summary?.syncedCount ?? 0}
          icon={PackageCheck}
        />
      </div>

      <Card>
        <CardContent className="grid gap-3 pt-6 sm:grid-cols-2 lg:grid-cols-[1.2fr_1fr_1fr_1fr_auto]">
          <Input
            value={customer}
            onChange={(event) => {
              setCustomer(event.target.value);
              setPage(1);
            }}
            placeholder="Filtrar por cliente"
          />
          <DatePicker
            value={createdFrom}
            placeholder="Desde"
            onChange={(date) => {
              setCreatedFrom(date);
              setPage(1);
            }}
          />
          <DatePicker
            value={createdTo}
            placeholder="Hasta"
            onChange={(date) => {
              setCreatedTo(date);
              setPage(1);
            }}
          />
          <Select
            value={status}
            onValueChange={(value) => {
              setStatus(value as OrderStatus | "all");
              setPage(1);
            }}
          >
            <SelectTrigger>
              <SelectValue placeholder="Estado" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">Todos los estados</SelectItem>
              {Object.entries(OrderStatusLabels).map(([value, label]) => (
                <SelectItem key={value} value={value}>
                  {label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <Button variant="outline" size="icon" onClick={resetFilters}>
            <X className="h-4 w-4" />
          </Button>
        </CardContent>
      </Card>

      <DataTable
        columns={columns}
        data={orders}
        searchKey="orderId"
        searchPlaceholder="Buscar pedido, doc. o producto..."
        enableRowSelection={false}
        page={page}
        pageSize={pageSize}
        pageCount={data?.totalPages}
        totalItems={data?.totalCount}
        onPaginationChange={(nextPage, nextPageSize) => {
          setPage(nextPageSize === pageSize ? nextPage : 1);
          setPageSize(nextPageSize);
        }}
        onSearch={(value) => {
          setSearch(value);
          setPage(1);
        }}
      />

    </div>
  );
}
