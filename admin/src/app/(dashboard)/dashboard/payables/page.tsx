"use client";

import { useEffect, useMemo, useState, type FormEvent } from "react";
import type { ColumnDef } from "@tanstack/react-table";
import { AlertTriangle, CalendarClock, Landmark, ReceiptText, Search, WalletCards } from "lucide-react";
import { toast } from "sonner";
import { useConfirmSupplierPayment, usePayableDetail, usePayables } from "@/hooks/use-payables";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import type { PayableListItem, PayableStatus } from "@/services/api/payables";
import { DataTable } from "@/components/tables/data-table";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import { formatCurrency, formatDate, formatDateTime } from "@/lib/utils";

const statusLabels: Record<PayableStatus, string> = {
  Open: "Pendiente",
  PartiallyPaid: "Pago parcial",
  Paid: "Pagada",
  Cancelled: "Cancelada",
};

export default function PayablesPage() {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const permissions = useAuthStore((state) => new Set(state.user?.permissions ?? []));
  const canPay = permissions.has("payables.payments.create");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState<PayableStatus | "all">("all");
  const [overdue, setOverdue] = useState(false);
  const [selectedId, setSelectedId] = useState<string>();
  const [paymentOpen, setPaymentOpen] = useState(false);
  const [amount, setAmount] = useState("");
  const [method, setMethod] = useState<"Cash" | "BankTransfer">("BankTransfer");
  const [reference, setReference] = useState("");
  const [notes, setNotes] = useState("");

  const query = usePayables({
    page, pageSize,
    search: search.trim() || undefined,
    status: status === "all" ? undefined : status,
    overdue: overdue || undefined,
  });
  const detailQuery = usePayableDetail(selectedId);
  const confirmPayment = useConfirmSupplierPayment();
  const detail = detailQuery.data;

  useEffect(() => {
    if (paymentOpen && detail) setAmount(String(detail.outstandingAmount));
  }, [paymentOpen, detail]);

  const columns = useMemo<ColumnDef<PayableListItem>[]>(() => [
    {
      accessorKey: "documentNumber",
      header: "Documento",
      cell: ({ row }) => (
        <div>
          <p className="font-semibold">{row.original.documentNumber}</p>
          <p className="text-xs text-muted-foreground">{row.original.supplierName}</p>
        </div>
      ),
    },
    {
      accessorKey: "dueDate",
      header: "Vencimiento",
      cell: ({ row }) => (
        <span className={row.original.isOverdue ? "font-semibold text-destructive" : ""}>
          {formatDate(row.original.dueDate)}
        </span>
      ),
    },
    {
      accessorKey: "originalAmount",
      header: "Valor original",
      cell: ({ row }) => formatCurrency(row.original.originalAmount, row.original.currencyCode),
    },
    {
      accessorKey: "outstandingAmount",
      header: "Saldo",
      cell: ({ row }) => (
        <span className="font-semibold">
          {formatCurrency(row.original.outstandingAmount, row.original.currencyCode)}
        </span>
      ),
    },
    {
      accessorKey: "status",
      header: "Estado",
      cell: ({ row }) => (
        <Badge variant={row.original.status === "Paid" ? "secondary" : row.original.isOverdue ? "destructive" : "outline"}>
          {row.original.isOverdue ? "Vencida" : statusLabels[row.original.status]}
        </Badge>
      ),
    },
  ], []);

  const openPayment = () => {
    if (!detail || detail.outstandingAmount <= 0) return;
    setMethod("BankTransfer"); setReference(""); setNotes("");
    setAmount(String(detail.outstandingAmount)); setPaymentOpen(true);
  };

  const submitPayment = async (event: FormEvent) => {
    event.preventDefault();
    if (!detail || !businessId) return;
    const parsed = Number(amount);
    if (!Number.isFinite(parsed) || parsed <= 0 || parsed > detail.outstandingAmount) {
      toast.error("El valor debe ser mayor que cero y no superar el saldo.");
      return;
    }
    try {
      const accepted = await confirmPayment.mutateAsync({
        paymentId: crypto.randomUUID(),
        businessId,
        supplierId: detail.supplierId,
        paidAt: new Date().toISOString(),
        currencyCode: detail.currencyCode,
        paymentMethod: method,
        reference: reference.trim() || null,
        notes: notes.trim() || null,
        allocations: [{ payableId: detail.payableId, amount: parsed }],
      });
      setPaymentOpen(false);
      toast.success(`${accepted.documentNumber} fue recibido y quedó en procesamiento.`);
    } catch {
      toast.error("No fue posible registrar el pago. El saldo pudo cambiar; actualiza el detalle.");
    }
  };

  return (
    <div className="space-y-6">
      <header>
        <h1 className="text-2xl font-semibold tracking-tight">Cuentas por pagar</h1>
        <p className="text-muted-foreground">Obligaciones creadas por las entradas de mercancía y sus pagos aplicados.</p>
      </header>

      <section className="grid gap-3 md:grid-cols-3">
        <SummaryCard icon={WalletCards} label="Saldo pendiente" value={formatCurrency(query.data?.totalOutstanding ?? 0)} />
        <SummaryCard icon={AlertTriangle} label="Saldo vencido" value={formatCurrency(query.data?.totalOverdue ?? 0)} danger={(query.data?.totalOverdue ?? 0) > 0} />
        <SummaryCard icon={ReceiptText} label="Obligaciones encontradas" value={String(query.data?.totalCount ?? 0)} />
      </section>

      <section className="grid gap-3 rounded-xl border bg-card p-4 md:grid-cols-[minmax(0,1fr)_13rem_12rem]">
        <div className="relative">
          <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" />
          <Input value={search} onChange={(event) => { setSearch(event.target.value); setPage(1); }} className="pl-9" placeholder="Documento, proveedor o identificación" />
        </div>
        <Select value={status} onValueChange={(value) => { setStatus(value as PayableStatus | "all"); setPage(1); }}>
          <SelectTrigger><SelectValue placeholder="Todos los estados" /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">Todos los estados</SelectItem>
            <SelectItem value="Open">Pendientes</SelectItem>
            <SelectItem value="PartiallyPaid">Pago parcial</SelectItem>
            <SelectItem value="Paid">Pagadas</SelectItem>
            <SelectItem value="Cancelled">Canceladas</SelectItem>
          </SelectContent>
        </Select>
        <Button variant={overdue ? "destructive" : "outline"} onClick={() => { setOverdue((value) => !value); setPage(1); }}>
          <CalendarClock className="mr-2 h-4 w-4" /> Solo vencidas
        </Button>
      </section>

      {query.isError ? (
        <div className="rounded-xl border border-destructive/30 p-6 text-sm">No se pudieron cargar las obligaciones. <Button variant="link" onClick={() => query.refetch()}>Reintentar</Button></div>
      ) : (
        <DataTable columns={columns} data={query.data?.items ?? []} isLoading={query.isLoading}
          page={query.data?.page} pageSize={query.data?.pageSize} pageCount={query.data?.totalPages}
          totalItems={query.data?.totalCount} onPaginationChange={(nextPage, nextSize) => { setPage(nextPage); setPageSize(nextSize); }}
          onRowClick={(item) => setSelectedId(item.payableId)} enableRowSelection={false} />
      )}

      <Dialog open={!!selectedId} onOpenChange={(open) => !open && setSelectedId(undefined)}>
        <DialogContent className="max-h-[90dvh] overflow-y-auto sm:max-w-2xl">
          <DialogHeader>
            <DialogTitle>{detail?.documentNumber ?? "Detalle de obligación"}</DialogTitle>
            <DialogDescription>{detail ? `${detail.supplierName} · ${detail.supplierIdentification}` : "Cargando información..."}</DialogDescription>
          </DialogHeader>
          {detailQuery.isLoading ? <p className="py-8 text-center text-muted-foreground">Cargando trazabilidad...</p> : detail ? (
            <div className="space-y-5">
              <dl className="grid gap-3 rounded-xl border bg-muted/20 p-4 sm:grid-cols-3">
                <Metric label="Valor original" value={formatCurrency(detail.originalAmount, detail.currencyCode)} />
                <Metric label="Saldo actual" value={formatCurrency(detail.outstandingAmount, detail.currencyCode)} emphasized />
                <Metric label="Vence" value={formatDate(detail.dueDate)} />
              </dl>
              <section>
                <h3 className="mb-3 text-sm font-semibold">Movimientos</h3>
                <div className="space-y-2">
                  {detail.transactions.map((transaction) => (
                    <div key={transaction.transactionId} className="flex items-center justify-between rounded-lg border p-3 text-sm">
                      <div><p className="font-medium">{transaction.type === "Opening" ? "Obligación creada" : "Pago aplicado"}</p><p className="text-xs text-muted-foreground">{formatDateTime(transaction.occurredAt)}</p></div>
                      <span className={transaction.type === "Payment" ? "font-semibold text-emerald-700" : "font-semibold"}>{transaction.type === "Payment" ? "−" : "+"}{formatCurrency(transaction.amount, detail.currencyCode)}</span>
                    </div>
                  ))}
                </div>
              </section>
              <DialogFooter>
                <Button variant="outline" onClick={() => setSelectedId(undefined)}>Cerrar</Button>
                {canPay && detail.outstandingAmount > 0 && <Button onClick={openPayment}><Landmark className="mr-2 h-4 w-4" /> Registrar pago</Button>}
              </DialogFooter>
            </div>
          ) : <p className="py-8 text-center text-destructive">No fue posible cargar la obligación.</p>}
        </DialogContent>
      </Dialog>

      <Dialog open={paymentOpen} onOpenChange={setPaymentOpen}>
        <DialogContent className="sm:max-w-lg">
          <form className="space-y-5" onSubmit={submitPayment}>
            <DialogHeader><DialogTitle>Registrar pago</DialogTitle><DialogDescription>El pago se aplicará a {detail?.documentNumber} mediante el motor transaccional.</DialogDescription></DialogHeader>
            <div className="space-y-2"><Label htmlFor="payable-amount">Valor</Label><Input id="payable-amount" type="number" min="0.01" step="0.01" max={detail?.outstandingAmount} value={amount} onChange={(event) => setAmount(event.target.value)} required /></div>
            <div className="space-y-2"><Label>Medio de pago</Label><Select value={method} onValueChange={(value) => setMethod(value as "Cash" | "BankTransfer")}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value="BankTransfer">Transferencia bancaria</SelectItem><SelectItem value="Cash">Efectivo</SelectItem></SelectContent></Select></div>
            <div className="space-y-2"><Label htmlFor="payable-reference">Referencia</Label><Input id="payable-reference" maxLength={120} value={reference} onChange={(event) => setReference(event.target.value)} placeholder="Comprobante o referencia bancaria" /></div>
            <div className="space-y-2"><Label htmlFor="payable-notes">Notas</Label><Textarea id="payable-notes" maxLength={1000} value={notes} onChange={(event) => setNotes(event.target.value)} /></div>
            <DialogFooter><Button type="button" variant="outline" onClick={() => setPaymentOpen(false)}>Cancelar</Button><Button type="submit" disabled={confirmPayment.isPending}>{confirmPayment.isPending ? "Registrando..." : "Registrar pago"}</Button></DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
    </div>
  );
}

function SummaryCard({ icon: Icon, label, value, danger = false }: { icon: typeof WalletCards; label: string; value: string; danger?: boolean }) {
  return <Card><CardContent className="flex items-center gap-4 p-5"><div className={`rounded-xl p-3 ${danger ? "bg-destructive/10 text-destructive" : "bg-primary/10 text-primary"}`}><Icon className="h-5 w-5" /></div><div><p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{label}</p><p className="mt-1 text-2xl font-semibold">{value}</p></div></CardContent></Card>;
}
function Metric({ label, value, emphasized = false }: { label: string; value: string; emphasized?: boolean }) {
  return <div><dt className="text-xs text-muted-foreground">{label}</dt><dd className={emphasized ? "mt-1 text-lg font-semibold" : "mt-1 font-medium"}>{value}</dd></div>;
}
