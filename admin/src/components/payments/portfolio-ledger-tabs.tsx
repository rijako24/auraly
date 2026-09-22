"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { useEffect, useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { payablesApi } from "@/services/api/payables";
import { receivablesApi } from "@/services/api/receivables";
import { formatCurrency, formatDate } from "@/lib/utils";
import { useBusinessContextStore } from "@/stores/business-context-store";

export type PortfolioLedgerTab = "parties" | "invoices" | "payments";
type PartyRow={id:string;name:string;identification:string;invoiceCount:number;originalAmount:number;paidAmount:number;outstandingAmount:number;overdueAmount:number};
type PaymentRow={paymentId:string;paidAt:string;partyName:string|null;documentNumber:string;appliedDocumentCount:number;payments:Array<{methodCode:string}>;applications:Array<{invoiceId:string;documentNumber:string;amount:number}>;totalAmount:number;currencyCode:string};
type Page<T>={items:T[];page:number;pageSize:number;totalCount:number;totalPages:number};

export function PortfolioLedgerTabs({
  direction,
  value,
  onValueChange,
  search,
  overdue,
  onPartyClick,
  onInvoiceClick,
  children,
}: {
  direction: "receivable" | "payable";
  value: PortfolioLedgerTab;
  onValueChange: (value: PortfolioLedgerTab) => void;
  search?: string;
  overdue?: boolean;
  onPartyClick: (partyId: string) => void;
  onInvoiceClick: (invoiceId: string) => void;
  children: ReactNode;
}) {
  const businessId=useBusinessContextStore(state=>state.selectedBusinessId);
  const [page, setPage] = useState(1);
  useEffect(() => setPage(1), [direction, search, overdue, value]);
  const parties = useQuery<Page<PartyRow>>({
    queryKey: [direction === "receivable" ? "receivable-customers" : "payable-suppliers",businessId, page, search, overdue],
    queryFn: async () => {
      if(direction === "receivable") { const result=await receivablesApi.customerPortfolio({ page, pageSize: 20, search, overdue: overdue || undefined }); return {...result,items:result.items.map(item=>({id:item.customerId,name:item.customerName,identification:item.identification,invoiceCount:item.invoiceCount,originalAmount:item.originalAmount,paidAmount:item.paidAmount,outstandingAmount:item.outstandingAmount,overdueAmount:item.overdueAmount}))}; }
      const result=await payablesApi.supplierPortfolio({ page, pageSize: 20, search, overdue: overdue || undefined }); return {...result,items:result.items.map(item=>({id:item.supplierId,name:item.supplierName,identification:item.identification,invoiceCount:item.invoiceCount,originalAmount:item.originalAmount,paidAmount:item.paidAmount,outstandingAmount:item.outstandingAmount,overdueAmount:item.overdueAmount}))};
    },
    enabled: !!businessId && value === "parties",
    placeholderData: keepPreviousData,
  });
  const payments = useQuery<Page<PaymentRow>>({
    queryKey: [direction === "receivable" ? "receivable-payments" : "payable-payments",businessId, page, search],
    queryFn: async () => {
      if(direction === "receivable") {
        const result=await receivablesApi.payments({ page, pageSize: 20, search });
        return {...result,items:result.items.map(item=>({...item,partyName:item.customerName,
          applications:item.applications.map(application=>({...application,invoiceId:application.receivableId}))}))};
      }
      const result=await payablesApi.payments({ page, pageSize: 20, search });
      return {...result,items:result.items.map(item=>({...item,partyName:item.supplierName,
        applications:item.applications.map(application=>({...application,invoiceId:application.payableId}))}))};
    },
    enabled: !!businessId && value === "payments",
    placeholderData: keepPreviousData,
  });

  const partyItems = parties.data?.items ?? [];
  const paymentItems = payments.data?.items ?? [];
  const current = value === "parties" ? parties.data : payments.data;
  const loading = value === "parties" ? parties.isLoading : payments.isLoading;
  const failed = value === "parties" ? parties.isError : payments.isError;

  return <Tabs value={value} onValueChange={next => onValueChange(next as PortfolioLedgerTab)} className="space-y-4">
    <TabsList className="grid h-auto w-full grid-cols-3">
      <TabsTrigger value="parties">{direction === "receivable" ? "Clientes" : "Proveedores"}</TabsTrigger>
      <TabsTrigger value="invoices">Facturas</TabsTrigger>
      <TabsTrigger value="payments">{direction === "receivable" ? "Recaudos" : "Pagos"}</TabsTrigger>
    </TabsList>
    <TabsContent value="invoices" className="mt-0">{children}</TabsContent>
    <TabsContent value="parties" className="mt-0">
      <LedgerTable loading={loading} failed={failed} isEmpty={partyItems.length === 0} empty="No hay terceros con cartera para estos filtros.">
        <thead className="bg-muted/40 text-left text-xs uppercase tracking-wide text-muted-foreground"><tr><th className="p-3">{direction === "receivable" ? "Cliente" : "Proveedor"}</th><th>Facturas</th><th>Valor original</th><th>Pagado</th><th>Saldo</th><th className="pr-3">Vencido</th></tr></thead>
        <tbody>{partyItems.map(item => <tr key={item.id} className="cursor-pointer border-t hover:bg-muted/40" onClick={() => onPartyClick(item.id)}><td className="p-3"><b>{item.name}</b><p className="text-xs text-muted-foreground">{item.identification}</p></td><td>{item.invoiceCount}</td><td>{formatCurrency(item.originalAmount)}</td><td>{formatCurrency(item.paidAmount)}</td><td className="font-semibold">{formatCurrency(item.outstandingAmount)}</td><td className="pr-3 text-destructive">{formatCurrency(item.overdueAmount)}</td></tr>)}</tbody>
      </LedgerTable>
      <Pager page={current?.page ?? page} pages={current?.totalPages ?? 0} total={current?.totalCount ?? 0} onPage={setPage}/>
    </TabsContent>
    <TabsContent value="payments" className="mt-0">
      <LedgerTable loading={loading} failed={failed} isEmpty={paymentItems.length === 0} empty={`No hay ${direction === "receivable" ? "recaudos" : "pagos"} para estos filtros.`}>
        <thead className="bg-muted/40 text-left text-xs uppercase tracking-wide text-muted-foreground"><tr><th className="p-3">Fecha</th><th>{direction === "receivable" ? "Cliente" : "Proveedor"}</th><th>Comprobante</th><th>Facturas</th><th>Medios</th><th className="pr-3 text-right">Total</th></tr></thead>
        <tbody>{paymentItems.map(item => <tr key={item.paymentId} className="border-t"><td className="p-3">{formatDate(item.paidAt)}</td><td>{item.partyName ?? "—"}</td><td className="font-mono text-xs">{item.documentNumber}</td><td><details><summary className="cursor-pointer">{item.appliedDocumentCount} factura{item.appliedDocumentCount===1?"":"s"}</summary><div className="mt-2 space-y-1">{item.applications.map(application=><div key={application.invoiceId} className="flex items-center gap-2 whitespace-nowrap"><button type="button" className="text-primary underline-offset-4 hover:underline" onClick={()=>onInvoiceClick(application.invoiceId)}>{application.documentNumber}</button><span>{formatCurrency(application.amount,item.currencyCode)}</span></div>)}</div></details></td><td>{item.payments.map(payment => paymentLabel(payment.methodCode)).join(" + ")}</td><td className="pr-3 text-right font-semibold">{formatCurrency(item.totalAmount, item.currencyCode)}</td></tr>)}</tbody>
      </LedgerTable>
      <Pager page={current?.page ?? page} pages={current?.totalPages ?? 0} total={current?.totalCount ?? 0} onPage={setPage}/>
    </TabsContent>
  </Tabs>;
}

function LedgerTable({loading,failed,isEmpty,empty,children}:{loading:boolean;failed:boolean;isEmpty:boolean;empty:string;children:ReactNode}) {
  if (loading) return <div className="rounded-xl border p-10 text-center text-sm text-muted-foreground">Cargando información…</div>;
  if (failed) return <div className="rounded-xl border border-destructive/30 p-6 text-sm text-destructive">No fue posible cargar la información.</div>;
  return <div className="overflow-x-auto rounded-xl border bg-card"><table className="w-full min-w-[760px] text-sm">{children}</table>{isEmpty && <p className="border-t p-8 text-center text-sm text-muted-foreground">{empty}</p>}</div>;
}

function Pager({page,pages,total,onPage}:{page:number;pages:number;total:number;onPage:(page:number)=>void}) {
  return <div className="mt-3 flex items-center justify-between text-sm text-muted-foreground"><span>{total} registro{total === 1 ? "" : "s"}</span><div className="flex items-center gap-2"><Button variant="outline" size="sm" disabled={page <= 1} onClick={() => onPage(page - 1)}>Anterior</Button><span>{pages ? `${page} de ${pages}` : "0 páginas"}</span><Button variant="outline" size="sm" disabled={page >= pages} onClick={() => onPage(page + 1)}>Siguiente</Button></div></div>;
}

function paymentLabel(code: string) {
  return ({ Cash: "Efectivo", BankTransfer: "Transferencia", DebitCard: "Tarjeta débito", CreditCard: "Tarjeta crédito" } as Record<string, string>)[code] ?? code;
}
