"use client";
import { useMemo } from "react";
import Link from "next/link";
import type { ColumnDef } from "@tanstack/react-table";
import { MoreHorizontal, Eye, DollarSign, CreditCard, XCircle, CheckCircle } from "lucide-react";
import { DataTable } from "@/components/tables/data-table";
import { StatCard } from "@/components/cards/stat-card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { PageLoading } from "@/components/ui/page-loading";
import { PageError } from "@/components/ui/page-error";
import { PaymentTransactionStatus, PaymentStatusLabels, PaymentStatusColors, PaymentSourceLabels } from "@/types/enums";
import type { PaymentTransaction } from "@/types/entities";
import { formatCurrency, formatDateTime, cn } from "@/lib/utils";
import { usePayments } from "@/hooks/use-payments";

export default function PaymentsPage() {
  const { data, isLoading, isError, refetch } = usePayments();
  const payments = data?.items ?? [];
  const { totalRevenue, confirmedCount, pendingCount, failedCount } = useMemo(() => {
    const confirmed = payments.filter((p) => p.status === PaymentTransactionStatus.Confirmed);
    const pending = payments.filter((p) => p.status === PaymentTransactionStatus.Created);
    const failed = payments.filter((p) => p.status === PaymentTransactionStatus.Failed || p.status === PaymentTransactionStatus.Expired);
    return { totalRevenue: confirmed.reduce((acc, p) => acc + p.amountInCents, 0), confirmedCount: confirmed.length, pendingCount: pending.length, failedCount: failed.length };
  }, [payments]);

  const columns: ColumnDef<PaymentTransaction>[] = useMemo(() => [
    { accessorKey: "paymentReferenceId", header: "ID Referencia", cell: ({ row }) => <span className="font-mono text-sm">{row.original.paymentReferenceId}</span> },
    { accessorKey: "amountInCents", header: "Monto", cell: ({ row }) => formatCurrency(row.original.amountInCents, row.original.currency) },
    { accessorKey: "currency", header: "Moneda" },
    { accessorKey: "status", header: "Estado", cell: ({ row }) => { const status = row.original.status; return <Badge variant="secondary" className={cn(PaymentStatusColors[status])}>{PaymentStatusLabels[status]}</Badge>; } },
    { accessorKey: "source", header: "Origen", cell: ({ row }) => <Badge variant="outline">{PaymentSourceLabels[row.original.source]}</Badge> },
    { accessorKey: "createdAt", header: "Creado", cell: ({ row }) => formatDateTime(row.original.createdAt) },
    { accessorKey: "confirmedAt", header: "Confirmado", cell: ({ row }) => row.original.confirmedAt ? formatDateTime(row.original.confirmedAt) : "—" },
    { id: "actions", cell: ({ row }) => { const payment = row.original; return (<DropdownMenu><DropdownMenuTrigger asChild><Button variant="ghost" size="icon" className="h-8 w-8"><MoreHorizontal className="h-4 w-4" /></Button></DropdownMenuTrigger><DropdownMenuContent align="end"><DropdownMenuItem asChild><Link href={`/dashboard/payments/${payment.paymentTransactionId}`}><Eye className="mr-2 h-4 w-4" />Ver Detalle</Link></DropdownMenuItem></DropdownMenuContent></DropdownMenu>); } },
  ], []);

  const facetedFilters = useMemo(() => [
    { column: "status", title: "Estado", options: Object.entries(PaymentStatusLabels).map(([value, label]) => ({ label, value: String(value) })) },
    { column: "source", title: "Origen", options: Object.entries(PaymentSourceLabels).map(([value, label]) => ({ label, value: String(value) })) },
  ], []);

  if (isLoading) return <PageLoading />;
  if (isError) return <PageError onRetry={refetch} />;

  return (
    <div className="space-y-6">
      <div><h1 className="text-2xl font-semibold tracking-tight">Pagos</h1><p className="text-muted-foreground">Transacciones de pago y estado de confirmación</p></div>
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard title="Ingresos totales" value={formatCurrency(totalRevenue)} icon={DollarSign} />
        <StatCard title="Pagos confirmados" value={confirmedCount} icon={CheckCircle} />
        <StatCard title="Pendientes" value={pendingCount} icon={CreditCard} />
        <StatCard title="Fallidos / Expirados" value={failedCount} icon={XCircle} />
      </div>
      <DataTable columns={columns} data={payments} searchKey="paymentReferenceId" searchPlaceholder="Buscar por referencia..." facetedFilters={facetedFilters} enableRowSelection={false} />
    </div>
  );
}
