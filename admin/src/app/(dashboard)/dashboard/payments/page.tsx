"use client";

import { useMemo, useState } from "react";
import Link from "next/link";
import type { ColumnDef } from "@tanstack/react-table";
import { MoreHorizontal, Eye, DollarSign, CreditCard, XCircle, CheckCircle } from "lucide-react";
import { toast } from "sonner";

import { DataTable } from "@/components/tables/data-table";
import { StatCard } from "@/components/cards/stat-card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { PageLoading } from "@/components/ui/page-loading";
import { PageError } from "@/components/ui/page-error";
import { PaymentTransactionStatus, PaymentStatusLabels, PaymentStatusColors, PaymentSourceLabels } from "@/types/enums";
import type { PaymentTransaction } from "@/types/entities";
import { formatCurrencyFromCents, formatDateTime, cn } from "@/lib/utils";
import { useConfirmManualPayment, usePayments } from "@/hooks/use-payments";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";

function getErrorMessage(error: unknown) {
  if (error && typeof error === "object" && "message" in error) {
    const message = (error as { message?: unknown }).message;
    if (typeof message === "string") return message;
  }
  return "No se pudo confirmar el pago";
}

export default function PaymentsPage() {
  const selectedBusinessId = useBusinessContextStore((s) => s.selectedBusinessId);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [search, setSearch] = useState("");
  const [confirmPayment, setConfirmPayment] = useState<PaymentTransaction | null>(null);
  const user = useAuthStore((s) => s.user);
  const canConfirmManual = user?.permissions?.includes("payments.confirm_manual") ?? false;
  const confirmManual = useConfirmManualPayment();
  const { data, isLoading, isError, refetch } = usePayments({
    page,
    pageSize,
    search: search || undefined,
  });
  const payments = data?.items ?? [];

  const { totalRevenue, confirmedCount, pendingCount, failedCount } = useMemo(() => {
    const confirmed = payments.filter((p) => p.status === PaymentTransactionStatus.Confirmed);
    const pending = payments.filter((p) => p.status === PaymentTransactionStatus.Created);
    const failed = payments.filter((p) => p.status === PaymentTransactionStatus.Failed || p.status === PaymentTransactionStatus.Expired);
    return {
      totalRevenue: confirmed.reduce((acc, p) => acc + p.amountInCents, 0),
      confirmedCount: confirmed.length,
      pendingCount: pending.length,
      failedCount: failed.length,
    };
  }, [payments]);

  const columns: ColumnDef<PaymentTransaction>[] = useMemo(() => [
    {
      accessorKey: "paymentReferenceId",
      header: "ID Referencia",
      cell: ({ row }) => <span className="font-mono text-sm">{row.original.paymentReferenceId}</span>,
    },
    {
      accessorKey: "amountInCents",
      header: "Monto",
      cell: ({ row }) => formatCurrencyFromCents(row.original.amountInCents, row.original.currency),
    },
    { accessorKey: "currency", header: "Moneda" },
    {
      accessorKey: "status",
      header: "Estado",
      cell: ({ row }) => {
        const status = row.original.status;
        return (
          <Badge variant="secondary" className={cn(PaymentStatusColors[status])}>
            {PaymentStatusLabels[status]}
          </Badge>
        );
      },
    },
    {
      accessorKey: "source",
      header: "Origen",
      cell: ({ row }) => <Badge variant="outline">{PaymentSourceLabels[row.original.source]}</Badge>,
    },
    { accessorKey: "createdAt", header: "Creado", cell: ({ row }) => formatDateTime(row.original.createdAt) },
    {
      accessorKey: "confirmedAt",
      header: "Confirmado",
      cell: ({ row }) => row.original.confirmedAt ? formatDateTime(row.original.confirmedAt) : "-",
    },
    {
      id: "actions",
      cell: ({ row }) => {
        const payment = row.original;
        return (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="ghost" size="icon" className="h-8 w-8">
                <MoreHorizontal className="h-4 w-4" />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              <DropdownMenuItem asChild>
                <Link href={`/dashboard/payments/${payment.paymentTransactionId}`}>
                  <Eye className="mr-2 h-4 w-4" />
                  Ver detalle
                </Link>
              </DropdownMenuItem>
              {canConfirmManual && payment.status === PaymentTransactionStatus.Created && (
                <DropdownMenuItem onSelect={() => setConfirmPayment(payment)}>
                  <CheckCircle className="mr-2 h-4 w-4" />
                  Confirmar manualmente
                </DropdownMenuItem>
              )}
            </DropdownMenuContent>
          </DropdownMenu>
        );
      },
    },
  ], [canConfirmManual]);

  const facetedFilters = useMemo(() => [
    {
      column: "status",
      title: "Estado",
      options: Object.entries(PaymentStatusLabels).map(([value, label]) => ({ label, value: String(value) })),
    },
    {
      column: "source",
      title: "Origen",
      options: Object.entries(PaymentSourceLabels).map(([value, label]) => ({ label, value: String(value) })),
    },
  ], []);

  const handleConfirmManual = async () => {
    if (!confirmPayment) return;
    try {
      await confirmManual.mutateAsync(confirmPayment.paymentTransactionId);
      toast.success("Pago confirmado manualmente");
      setConfirmPayment(null);
    } catch (error) {
      toast.error(getErrorMessage(error));
    }
  };

  if (!selectedBusinessId) {
    return <PageError message="Selecciona un negocio para ver los pagos." />;
  }

  if (isLoading) return <PageLoading />;
  if (isError) return <PageError onRetry={refetch} />;

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Pagos</h1>
        <p className="text-muted-foreground">Transacciones de pago y estado de confirmacion</p>
      </div>
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard title="Ingresos totales" value={formatCurrencyFromCents(totalRevenue)} icon={DollarSign} />
        <StatCard title="Pagos confirmados" value={confirmedCount} icon={CheckCircle} />
        <StatCard title="Pendientes" value={pendingCount} icon={CreditCard} />
        <StatCard title="Fallidos / Expirados" value={failedCount} icon={XCircle} />
      </div>
      <DataTable
        columns={columns}
        data={payments}
        searchKey="paymentReferenceId"
        searchPlaceholder="Buscar por referencia..."
        facetedFilters={facetedFilters}
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

      <Dialog open={!!confirmPayment} onOpenChange={(open) => !open && setConfirmPayment(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Confirmar pago manualmente</DialogTitle>
            <DialogDescription>
              Usa esta accion solo si verificaste el pago fuera del proveedor automatico.
            </DialogDescription>
          </DialogHeader>
          {confirmPayment && (
            <div className="space-y-3 rounded-md border bg-muted/30 p-4 text-sm">
              <div className="flex items-center justify-between gap-4">
                <span className="text-muted-foreground">Referencia</span>
                <span className="break-all font-mono">{confirmPayment.paymentReferenceId}</span>
              </div>
              <div className="flex items-center justify-between gap-4">
                <span className="text-muted-foreground">Monto</span>
                <span className="font-medium">
                  {formatCurrencyFromCents(confirmPayment.amountInCents, confirmPayment.currency)}
                </span>
              </div>
            </div>
          )}
          <DialogFooter>
            <Button variant="outline" onClick={() => setConfirmPayment(null)} disabled={confirmManual.isPending}>
              Cancelar
            </Button>
            <Button onClick={handleConfirmManual} disabled={confirmManual.isPending}>
              {confirmManual.isPending ? "Confirmando..." : "Confirmar pago"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}