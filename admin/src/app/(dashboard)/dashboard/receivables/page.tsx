"use client";

import { useMemo, useState } from "react";
import type { ColumnDef } from "@tanstack/react-table";
import { CalendarClock, CircleDollarSign, Download, FileUp } from "lucide-react";
import { useReceivableDetail, useReceivables } from "@/hooks/use-receivables";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { type ReceivableDetail, type ReceivableListItem, type ReceivableStatus } from "@/services/api/receivables";
import { DataTable } from "@/components/tables/data-table";
import { ServerSearchInput } from "@/components/tables/server-search-input";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { formatCurrency, formatDate, formatDateTime } from "@/lib/utils";
import { PortfolioPaymentWizard } from "@/components/payments/portfolio-payment-wizard";
import { useQueryClient } from "@tanstack/react-query";
import { PortfolioLedgerTabs, type PortfolioLedgerTab, type PartyRow } from "@/components/payments/portfolio-ledger-tabs";
import { PartyRoleSelect, type PartyRoleSelection } from "@/components/parties/party-role-select";
import { partiesApi } from "@/services/api/parties";
import { DatePicker } from "@/components/ui/date-picker";
import { PreexistingReceivablesImport, downloadPreexistingReceivablesTemplate } from "@/components/payments/preexisting-receivables-import";

const statusLabels: Record<ReceivableStatus, string> = {
  Open: "Pendiente",
  PartiallyPaid: "Abono parcial",
  Paid: "Pagada",
  Cancelled: "Cancelada",
};
export default function ReceivablesPage() {
  const queryClient = useQueryClient();
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const permissions = useAuthStore((state) => state.user?.permissions);
  const canReceive = permissions?.includes("receivables.payments.create") ?? false;
  const canImport = permissions?.includes("receivables.credit.manage") ?? false;
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState<ReceivableStatus | "all">("all");
  const [overdue, setOverdue] = useState(false);
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [activeTab, setActiveTab] = useState<PortfolioLedgerTab>("parties");
  const [customerId, setCustomerId] = useState<string>();
  const [customerFilter, setCustomerFilter] = useState<PartyRoleSelection | null>(null);
  const [paymentParty, setPaymentParty] = useState<PartyRoleSelection | null>(null);
  const [selectedId, setSelectedId] = useState<string>();
  const [importOpen, setImportOpen] = useState(false);
  const [portfolioPaymentOpen,setPortfolioPaymentOpen]=useState(false);
  const [paymentTarget,setPaymentTarget]=useState<ReceivableDetail>();

  const query = useReceivables({
    page, pageSize, search: search.trim() || undefined,
    customerId,
    status: status === "all" ? undefined : status,
    overdue: overdue || undefined,
    from: from || undefined,
    to: to || undefined,
    enabled: activeTab === "invoices",
  });
  const detailQuery = useReceivableDetail(selectedId);
  const detail = detailQuery.data;

  const columns = useMemo<ColumnDef<ReceivableListItem>[]>(() => [
    { accessorKey: "documentNumber", header: "Factura", cell: ({ row }) => <div><p className="font-semibold">{row.original.documentNumber}</p><p className="text-xs text-muted-foreground">{receivableCustomerLabel(row.original)}</p></div> },
    { accessorKey: "dueDate", header: "Vencimiento", cell: ({ row }) => <span className={row.original.isOverdue ? "font-semibold text-destructive" : ""}>{formatDate(row.original.dueDate)}</span> },
    { accessorKey: "originalAmount", header: "Valor original", cell: ({ row }) => formatCurrency(row.original.originalAmount, row.original.currencyCode) },
    { accessorKey: "outstandingAmount", header: "Saldo", cell: ({ row }) => <span className="font-semibold">{formatCurrency(row.original.outstandingAmount, row.original.currencyCode)}</span> },
    { accessorKey: "status", header: "Estado", cell: ({ row }) => <Badge variant={row.original.status === "Paid" ? "secondary" : row.original.isOverdue ? "destructive" : "outline"}>{row.original.isOverdue ? "Vencida" : statusLabels[row.original.status]}</Badge> },
  ], []);

  const openPayment = () => {
    if (!detail || detail.outstandingAmount <= 0) return;
    setPaymentTarget(detail); setSelectedId(undefined); setPortfolioPaymentOpen(true);
  };
  const openPartyPayment = (item: PartyRow) => {
    if (!canReceive) return;
    setPaymentTarget(undefined);
    setPaymentParty({partyId:"",roleId:item.id,role:"Customer",displayName:item.name,identification:item.identification,supplierPurchaseEvidencePolicy:null,supplierDefaultPaymentDueDays:null,customerId:item.id,supplierId:null,sellerId:null,carrierId:null,employeeId:null,userId:null});
    setPortfolioPaymentOpen(true);
  };

  return <div className="space-y-6">
    <header className="flex flex-col justify-between gap-3 sm:flex-row sm:items-end"><div><h1 className="text-2xl font-semibold tracking-tight">Cuentas por cobrar</h1><p className="text-muted-foreground">Facturas financiadas, vencimientos y recaudos aplicados por el motor contable.</p></div><div className="ml-auto flex flex-wrap justify-end gap-2"><Button variant="outline" onClick={downloadPreexistingReceivablesTemplate}><Download className="mr-2 h-4 w-4"/>Descargar plantilla</Button>{canImport&&<Button variant="outline" onClick={()=>setImportOpen(true)}><FileUp className="mr-2 h-4 w-4"/>Importar cartera</Button>}{canReceive&&<Button onClick={()=>{setPaymentTarget(undefined);setPaymentParty(null);setPortfolioPaymentOpen(true)}}><CircleDollarSign className="mr-2 h-4 w-4"/>Abono a cartera</Button>}</div></header>
    <PortfolioLedgerTabs direction="receivable" value={activeTab} onValueChange={setActiveTab} search={search.trim()||undefined} partyId={customerId} status={status==="all"?undefined:status} overdue={overdue} from={from||undefined} to={to||undefined} onRefreshInvoices={()=>void query.refetch()} onPartyClick={openPartyPayment} onInvoiceClick={setSelectedId} filters={
    <details className="rounded-xl border bg-card p-4"><summary className="cursor-pointer font-medium">Filtros</summary><div className="mt-4 grid gap-3 md:grid-cols-2 lg:grid-cols-4">
      <PartyRoleSelect role="Customer" value={customerId??""} sourceKey="web-portfolio" loadPage={(search,page,pageSize)=>partiesApi.portfolioRoleOptions({role:"Customer",search,page,pageSize})} selectedOption={customerFilter?{value:customerFilter.roleId,label:customerFilter.displayName}:null} placeholder="Filtrar por cliente" onChange={(id,party)=>{setCustomerId(id);setCustomerFilter(party??null);setPage(1)}}/>
      <ServerSearchInput value={search} onSearch={(value) => { setSearch(value); setPage(1); }} isSearching={query.isFetching} placeholder="Número de documento o identificación" />
      <Select value={status} onValueChange={(value) => { setStatus(value as ReceivableStatus | "all"); setPage(1); }}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value="all">Todos los estados</SelectItem><SelectItem value="Open">Pendientes</SelectItem><SelectItem value="PartiallyPaid">Abono parcial</SelectItem><SelectItem value="Paid">Pagadas</SelectItem><SelectItem value="Cancelled">Canceladas</SelectItem></SelectContent></Select>
      <Button variant={overdue ? "destructive" : "outline"} onClick={() => { setOverdue((value) => !value); setPage(1); }}><CalendarClock className="mr-2 h-4 w-4" /> Solo vencidas</Button>
      <label className="space-y-1 text-sm">Desde<DatePicker value={from} max={to || undefined} onChange={value=>{setFrom(value);setPage(1)}} placeholder="Fecha inicial"/></label>
      <label className="space-y-1 text-sm">Hasta<DatePicker value={to} min={from || undefined} onChange={value=>{setTo(value);setPage(1)}} placeholder="Fecha final"/></label>
      <div className="col-span-full flex justify-end"><Button variant="ghost" onClick={()=>{setSearch("");setStatus("all");setOverdue(false);setCustomerId(undefined);setCustomerFilter(null);setFrom("");setTo("");setPage(1)}}>Limpiar filtros</Button></div>
    </div></details>
    }>
      {query.isError ? <div className="rounded-xl border border-destructive/30 p-6 text-sm">No se pudo cargar la cartera. <Button variant="link" onClick={() => query.refetch()}>Reintentar</Button></div> : <><div className="mb-3 flex items-center justify-between">{customerId?<Badge variant="secondary">Cartera del cliente seleccionado</Badge>:<span/>}{customerId&&<Button size="sm" variant="ghost" onClick={()=>{setCustomerId(undefined);setPage(1)}}>Ver todos</Button>}</div><DataTable columns={columns} data={query.data?.items ?? []} isLoading={query.isLoading} page={query.data?.page} pageSize={query.data?.pageSize} pageCount={query.data?.totalPages} totalItems={query.data?.totalCount} onPaginationChange={(nextPage, nextSize) => { setPage(nextPage); setPageSize(nextSize); }} onRowClick={(item) => setSelectedId(item.receivableId)} enableRowSelection={false} /></>}
    </PortfolioLedgerTabs>

    <Dialog open={!!selectedId} onOpenChange={(open) => !open && setSelectedId(undefined)}><DialogContent className="max-h-[90dvh] overflow-y-auto sm:max-w-2xl"><DialogHeader><DialogTitle>{detail?.documentNumber ?? "Detalle de cartera"}</DialogTitle><DialogDescription>{detail ? `${receivableCustomerLabel(detail)}${detail.customerIdentification ? ` · ${detail.customerIdentification}` : ""}` : "Cargando información..."}</DialogDescription></DialogHeader>{detailQuery.isLoading ? <p className="py-8 text-center text-muted-foreground">Cargando trazabilidad...</p> : detail ? <div className="space-y-5"><dl className="grid gap-3 rounded-xl border bg-muted/20 p-4 sm:grid-cols-3"><Metric label="Valor original" value={formatCurrency(detail.originalAmount, detail.currencyCode)} /><Metric label="Saldo actual" value={formatCurrency(detail.outstandingAmount, detail.currencyCode)} emphasized /><Metric label="Vence" value={formatDate(detail.dueDate)} /></dl><section><h3 className="mb-3 text-sm font-semibold">Movimientos</h3><div className="space-y-2">{detail.transactions.map((transaction) => <div key={transaction.transactionId} className="flex items-center justify-between rounded-lg border p-3 text-sm"><div><p className="font-medium">{transaction.type === "Opening" ? "Cuenta por cobrar creada" : "Abono aplicado"}</p><p className="text-xs text-muted-foreground">{formatDateTime(transaction.occurredAt)}</p></div><span className={transaction.type === "Payment" ? "font-semibold text-emerald-700" : "font-semibold"}>{transaction.type === "Payment" ? "−" : "+"}{formatCurrency(transaction.amount, detail.currencyCode)}</span></div>)}</div></section><DialogFooter><Button variant="outline" onClick={() => setSelectedId(undefined)}>Cerrar</Button>{canReceive && detail.outstandingAmount > 0 && <Button onClick={openPayment}><CircleDollarSign className="mr-2 h-4 w-4" /> Registrar abono</Button>}</DialogFooter></div> : <p className="py-8 text-center text-destructive">No fue posible cargar la cuenta por cobrar.</p>}</DialogContent></Dialog>

    <PortfolioPaymentWizard direction="receivable" open={portfolioPaymentOpen} onOpenChange={open=>{setPortfolioPaymentOpen(open);if(!open){setPaymentTarget(undefined);setPaymentParty(null)}}} onCompleted={()=>{for(const key of ["receivables","receivable-customers","receivable-payments","receivable"]){void queryClient.invalidateQueries({queryKey:[key,businessId]});}}} initialInvoice={paymentTarget?{id:paymentTarget.receivableId,number:paymentTarget.documentNumber,dueDate:paymentTarget.dueDate,outstanding:paymentTarget.outstandingAmount,currency:paymentTarget.currencyCode,overdue:false}:null} initialParty={paymentTarget?{partyId:"",roleId:paymentTarget.customerId,role:"Customer",displayName:paymentTarget.customerName,identification:paymentTarget.customerIdentification,supplierPurchaseEvidencePolicy:null,supplierDefaultPaymentDueDays:null,customerId:paymentTarget.customerId,supplierId:null,sellerId:null,carrierId:null,employeeId:null,userId:null}:paymentParty}/>
    {businessId && <PreexistingReceivablesImport
      businessId={businessId}
      open={importOpen}
      onOpenChange={setImportOpen}
      onCompleted={() => setActiveTab("invoices")}
    />}
  </div>;
}

function Metric({ label, value, emphasized = false }: { label: string; value: string; emphasized?: boolean }) {
  return <div><dt className="text-xs text-muted-foreground">{label}</dt><dd className={emphasized ? "mt-1 text-lg font-semibold" : "mt-1 font-medium"}>{value}</dd></div>;
}
function receivableCustomerLabel(value: Pick<ReceivableListItem, "customerName" | "partySiteName">) {
  return value.partySiteName ? `${value.customerName} · ${value.partySiteName}` : value.customerName;
}
