"use client";

import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { ChevronDown, ChevronUp, FilePlus2, Landmark, Loader2, ReceiptText, Settings2, SlidersHorizontal } from "lucide-react";
import { toast } from "sonner";
import { DataTablePagination } from "@/components/tables/data-table-pagination";
import { ServerSearchInput } from "@/components/tables/server-search-input";
import { AccountingDocumentDialog } from "@/components/accounting/accounting-document-dialog";
import { PartyRoleSelect, type PartyRoleSelection } from "@/components/parties/party-role-select";
import { PortfolioPaymentWizard } from "@/components/payments/portfolio-payment-wizard";
import { ExpenseForm } from "@/components/expenses/expense-form";
import { ExpenseBreakdown } from "@/components/expenses/expense-breakdown";
import { ExpenseConceptEditor } from "@/components/expenses/expense-concepts-master";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { DatePicker } from "@/components/ui/date-picker";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { expensesApi, type ExpenseDetail, type ExpenseOptions } from "@/services/api/expenses";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import {businessStatusLabel,fiscalStatusLabel} from "@/lib/accounting-labels";
import { useReferenceOptions } from "@/hooks/use-reference-options";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });


export default function ExpensesPage() {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  return <ExpenseWorkspace key={businessId} businessId={businessId}/>;
}

function ExpenseWorkspace({businessId}:{businessId:string|null}) {
  const queryClient=useQueryClient();
  const userPermissions = useAuthStore((state) => state.user?.permissions);
  const permissions = new Set(userPermissions ?? []);
  const [creating,setCreating]=useState(false),[configuring,setConfiguring]=useState(false),[formBusy,setFormBusy]=useState(false),[conceptBusy,setConceptBusy]=useState(false);
  const [page,setPage]=useState(1),[pageSize,setPageSize]=useState(25),[search,setSearch]=useState("");
  const [supplierId,setSupplierId]=useState<string>(),[supplierFilter,setSupplierFilter]=useState<PartyRoleSelection|null>(null);
  const [conceptId,setConceptId]=useState<string>(),[from,setFrom]=useState(""),[to,setTo]=useState(""),[status,setStatus]=useState("all"),[payableStatus,setPayableStatus]=useState("all");
  const [filtersExpanded,setFiltersExpanded]=useState(false);
  const expenseStatuses=useReferenceOptions("expense-status",filtersExpanded);
  const payableStatuses=useReferenceOptions("payable-status",filtersExpanded);
  const activeFilterCount=[supplierId,conceptId,from,to,status==="all"?null:status,payableStatus==="all"?null:payableStatus].filter(Boolean).length;
  const [selectedId,setSelectedId]=useState<string>();
  const [paymentTarget,setPaymentTarget]=useState<ExpenseDetail|null>(null);
  const [cancelling,setCancelling]=useState(false),[cancelReasonOptionId,setCancelReasonOptionId]=useState(""),[cancelBusy,setCancelBusy]=useState(false),[cancellationId,setCancellationId]=useState("");
  const cancellationReasons=useReferenceOptions("expense-cancellation-reason",cancelling);
  const selectedCancellationReason=cancellationReasons.data?.find(option=>option.id===cancelReasonOptionId);
  const [accountingDocumentId,setAccountingDocumentId]=useState<string>();
  const filters={page,pageSize,search:search.trim()||undefined,supplierId,conceptId,from:from||undefined,to:to||undefined,status:status==="all"?undefined:status,payableStatus:payableStatus==="all"?undefined:payableStatus};
  const reportKey=["expenses",businessId,filters];
  const reportQuery=useQuery({queryKey:reportKey,queryFn:()=>expensesApi.list(filters),enabled:!!businessId,refetchOnWindowFocus:false,refetchOnReconnect:false});
  const optionsQuery=useQuery({queryKey:["expense-options",businessId],queryFn:expensesApi.options,enabled:!!businessId,staleTime:5*60*1000,refetchOnWindowFocus:false,refetchOnReconnect:false});
  const detailKey=["expense-detail",businessId,selectedId];
  const detailQuery=useQuery({queryKey:detailKey,queryFn:()=>expensesApi.get(selectedId!),enabled:!!businessId&&!!selectedId,refetchOnWindowFocus:false,refetchOnReconnect:false});
  const report=reportQuery.data,options=optionsQuery.data,detail=detailQuery.data;
  const loading=reportQuery.isFetching,detailLoading=detailQuery.isPending;
  const load=async()=>{await reportQuery.refetch()};
  async function cancelExpense(){if(!detail||!selectedCancellationReason)return;setCancelBusy(true);try{
    await expensesApi.cancel(detail.expenseId,cancellationId,selectedCancellationReason.id);
    setCancelling(false);setCancelReasonOptionId("");toast.success("Anulación aceptada para procesamiento contable y fiscal.");
    // Cancellation returns an acknowledgement, not the resulting expense or page.
    // Read each visible resource once: processing may already have changed its state,
    // payable balance and membership in the current status filters.
    await Promise.all([detailQuery.refetch(),load()]);
  }catch(error){toast.error(error instanceof Error?error.message:"No fue posible anular el gasto.")}finally{setCancelBusy(false)}}
  if(!businessId)return <Card><CardContent className="p-8 text-center text-muted-foreground">Selecciona una sede para consultar sus gastos.</CardContent></Card>;
  return <div className="space-y-6">
    <header className="flex flex-col gap-4 md:flex-row md:items-end md:justify-between"><div><p className="text-sm font-medium text-emerald-600">Operación y contabilidad</p><h1 className="text-3xl font-bold tracking-tight">Gastos</h1><p className="mt-1 text-muted-foreground">Registra el soporte; Auraly calcula retenciones, la cuenta por pagar y la contabilización.</p></div><div className="flex flex-wrap gap-2">{permissions.has("expenses.configure")&&<Button variant="outline" onClick={()=>setConfiguring(true)}><Settings2 className="mr-2 h-4 w-4"/>Conceptos</Button>}{permissions.has("expenses.create")&&<Button onClick={()=>setCreating(true)}><FilePlus2 className="mr-2 h-4 w-4"/>Nuevo gasto</Button>}</div></header>
    <div className="grid gap-4 sm:grid-cols-3"><Metric label="Bruto registrado" value={report?.grossTotal??0}/><Metric label="Retenciones registradas" value={report?.withholdingTotal??0}/><Metric label="Neto original" value={report?.netPayableTotal??0}/></div>
    <Card><CardHeader><CardTitle className="flex items-center gap-2"><ReceiptText className="h-5 w-5 text-primary"/>Documentos registrados</CardTitle><CardDescription>La búsqueda y la paginación consultan siempre al servidor.</CardDescription></CardHeader><CardContent>
      <div className="mb-4 grid gap-2 sm:grid-cols-[minmax(0,1fr)_auto]">
        <ServerSearchInput value={search} onSearch={value=>{setSearch(value);setPage(1)}} isSearching={loading} placeholder="Documento, proveedor o concepto"/>
        <Button type="button" variant="outline" aria-expanded={filtersExpanded} onClick={()=>setFiltersExpanded(value=>!value)}><SlidersHorizontal className="mr-2 h-4 w-4"/>Filtros{activeFilterCount>0&&<span className="ml-2 rounded-full bg-teal-100 px-2 py-0.5 text-xs font-bold text-teal-800">{activeFilterCount}</span>}{filtersExpanded?<ChevronUp className="ml-2 h-4 w-4"/>:<ChevronDown className="ml-2 h-4 w-4"/>}</Button>
      </div>
      {filtersExpanded&&<section className="mb-4 rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
        <div className="flex items-start justify-between gap-3 border-b border-slate-100 pb-3"><div><h3 className="font-semibold text-slate-950">Filtrar gastos</h3><p className="text-sm text-slate-500">Combina proveedor, concepto, estados y rango de emisión.</p></div><Button type="button" variant="ghost" size="sm" disabled={activeFilterCount===0} onClick={()=>{setSupplierId(undefined);setSupplierFilter(null);setConceptId(undefined);setFrom("");setTo("");setStatus("all");setPayableStatus("all");setPage(1)}}>Limpiar</Button></div>
        {(expenseStatuses.isError||payableStatuses.isError)&&<p role="alert" className="mt-3 text-sm text-destructive">No fue posible cargar los estados. <Button variant="outline" onClick={()=>{void expenseStatuses.refetch();void payableStatuses.refetch()}}>Reintentar</Button></p>}
        <div className="mt-4 grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          <Field label="Proveedor"><PartyRoleSelect role="Supplier" value={supplierId??""} selectedOption={supplierFilter?{value:supplierFilter.roleId,label:supplierFilter.displayName}:null} placeholder="Todos los proveedores" onChange={(id,party)=>{setSupplierId(id||undefined);setSupplierFilter(party??null);setPage(1)}}/></Field>
          <Field label="Concepto"><Select value={conceptId??"all"} onValueChange={value=>{setConceptId(value==="all"?undefined:value);setPage(1)}}><SelectTrigger><SelectValue placeholder="Todos los conceptos"/></SelectTrigger><SelectContent><SelectItem value="all">Todos los conceptos</SelectItem>{options?.concepts.map(item=><SelectItem key={item.conceptId} value={item.conceptId}>{item.name}</SelectItem>)}</SelectContent></Select></Field>
          <Field label="Estado del gasto"><Select value={status} onValueChange={value=>{setStatus(value);setPage(1)}}><SelectTrigger><SelectValue placeholder="Todos los estados"/></SelectTrigger><SelectContent><SelectItem value="all">Todos los estados</SelectItem>{expenseStatuses.data?.map(option=><SelectItem key={option.id} value={option.code}>{option.label}</SelectItem>)}</SelectContent></Select></Field>
          <Field label="Estado de la cuenta por pagar"><Select value={payableStatus} onValueChange={value=>{setPayableStatus(value);setPage(1)}}><SelectTrigger><SelectValue placeholder="Todos los estados"/></SelectTrigger><SelectContent><SelectItem value="all">Todos los estados</SelectItem>{payableStatuses.data?.map(option=><SelectItem key={option.id} value={option.code}>{option.label}</SelectItem>)}</SelectContent></Select></Field>
          <Field label="Desde"><DatePicker value={from} max={to||undefined} onChange={value=>{setFrom(value);setPage(1)}} placeholder="Fecha inicial"/></Field>
          <Field label="Hasta"><DatePicker value={to} min={from||undefined} onChange={value=>{setTo(value);setPage(1)}} placeholder="Fecha final"/></Field>
        </div>
      </section>}
      {reportQuery.isError?<p role="alert" className="py-6 text-destructive">No fue posible consultar los gastos. <Button variant="outline" onClick={()=>void load()}>Reintentar</Button></p>:loading?<p className="flex items-center gap-2 py-8 text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin"/>Cargando…</p>:<div className="overflow-x-auto rounded-xl border"><table className="w-full text-sm"><thead className="bg-muted/50 text-xs uppercase tracking-wide text-muted-foreground"><tr>{["Documento","Proveedor","Concepto","Bruto","Retención","Neto original","Saldo por pagar","Estado del gasto","Estado CxP"].map(label=><th key={label} className={`p-3 font-semibold ${["Bruto","Retención","Neto original","Saldo por pagar"].includes(label)?"text-right":"text-left"}`}>{label}</th>)}</tr></thead><tbody>{report?.items.map(item=><tr onClick={()=>setSelectedId(item.expenseId)} onKeyDown={event=>{if(event.key==="Enter"||event.key===" "){event.preventDefault();setSelectedId(item.expenseId)}}} tabIndex={0} role="button" aria-label={`Ver gasto ${item.documentNumber}`} key={item.expenseId} className="cursor-pointer border-t transition hover:bg-muted/40 focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary"><td className="p-3"><b>{item.documentNumber}</b>{item.supplierDocumentNumber && <small className="block text-muted-foreground">Ref. {item.supplierDocumentNumber}</small>}</td><td className="p-3">{item.supplierName}</td><td className="p-3">{item.conceptName}</td><td className="p-3 text-right">{money.format(item.grossAmount)}</td><td className="p-3 text-right">{money.format(item.withholdingAmount)}</td><td className="p-3 text-right font-bold">{money.format(item.netPayable)}</td><td className="p-3 text-right font-semibold">{item.outstandingAmount===null?"—":money.format(item.outstandingAmount)}</td><td className="p-3">{item.chargeReturned&&item.status==="Processed"?"Devuelto en factura":businessStatusLabel(item.status)}</td><td className="p-3">{item.payableStatus?businessStatusLabel(item.payableStatus):"Sin cuenta"}</td></tr>)}</tbody></table>{!report?.items.length&&<p className="p-8 text-center text-sm text-muted-foreground">No hay gastos que coincidan con los filtros.</p>}</div>}
      <DataTablePagination pageIndex={Math.max(0,(report?.page??page)-1)} pageSize={report?.pageSize??pageSize} pageCount={report?.totalPages??0} totalItems={report?.totalCount??0} onPageChange={index=>setPage(index+1)} onPageSizeChange={size=>{setPageSize(size);setPage(1)}}/>
    </CardContent></Card>
    <Dialog open={creating} onOpenChange={open=>{if(!formBusy)setCreating(open)}}><DialogContent className="max-h-[92dvh] overflow-y-auto sm:max-w-6xl"><DialogHeader><DialogTitle>Registrar gasto</DialogTitle><DialogDescription>Distribuye el gasto por cuentas, revisa impuestos y retenciones, y confirma el neto por pagar.</DialogDescription></DialogHeader>{optionsQuery.isPending&&<p>Cargando opciones del gasto…</p>}{optionsQuery.isError&&<p role="alert">No fue posible cargar las opciones. <Button variant="outline" onClick={()=>void optionsQuery.refetch()}>Reintentar</Button></p>}{options&&<ExpenseForm businessId={businessId} options={options} onBusyChange={setFormBusy} onSaved={async()=>{setCreating(false);await load()}}/>}</DialogContent></Dialog>
    <Dialog open={configuring} onOpenChange={open=>{if(!conceptBusy)setConfiguring(open)}}><DialogContent className="max-h-[92dvh] max-w-2xl overflow-y-auto"><DialogHeader><DialogTitle>Conceptos de gasto</DialogTitle><DialogDescription>Clasifican el gasto y determinan su cuenta contable.</DialogDescription></DialogHeader>{options&&<ExpenseConceptEditor businessId={businessId} options={options} onBusyChange={setConceptBusy} onSaved={async saved=>{queryClient.setQueryData<ExpenseOptions>(["expense-options",businessId],current=>current?{...current,concepts:[...current.concepts.filter(item=>item.conceptId!==saved.conceptId),saved].sort((a,b)=>a.name.localeCompare(b.name))}:current)}}/>}</DialogContent></Dialog>
    <Dialog open={!!selectedId} onOpenChange={open=>{if(!open)setSelectedId(undefined)}}><DialogContent className="max-h-[92dvh] max-w-2xl overflow-y-auto"><DialogHeader><DialogTitle>{detail?.documentNumber??"Detalle del gasto"}</DialogTitle><DialogDescription>{detail?.supplierName??"Cargando gasto..."}</DialogDescription></DialogHeader>
      {detailLoading?<p className="py-8 text-center text-muted-foreground">Cargando...</p>:detail?<div className="space-y-4 text-sm">
        <dl className="grid gap-3 rounded-xl border p-4 sm:grid-cols-2">
          <div><dt className="text-muted-foreground">Concepto</dt><dd>{detail.conceptName}</dd></div><div><dt className="text-muted-foreground">Estado</dt><dd>{detail.chargeReturned&&detail.status==="Processed"?"Devuelto en factura":businessStatusLabel(detail.status)}</dd></div>
          <div><dt className="text-muted-foreground">Emisión</dt><dd>{detail.issuedAt.slice(0,10)}</dd></div><div><dt className="text-muted-foreground">Vencimiento</dt><dd>{detail.dueDate.slice(0,10)}</dd></div>
          <div><dt className="text-muted-foreground">Base / IVA</dt><dd>{money.format(detail.taxExclusiveAmount)} / {money.format(detail.vatAmount)}</dd></div><div><dt className="text-muted-foreground">Retención / Neto</dt><dd>{money.format(detail.withholdingAmount)} / {money.format(detail.netPayable)}</dd></div>
          <div className="sm:col-span-2"><dt className="text-muted-foreground">Descripción</dt><dd>{detail.description}</dd></div>
          {detail.supplierDocumentNumber&&<div><dt className="text-muted-foreground">Documento del proveedor</dt><dd>{detail.supplierDocumentNumber}</dd></div>}
          {detail.sourceInvoiceId&&<div className="sm:col-span-2"><dt className="text-muted-foreground">Origen</dt><dd>Cargo de la factura de venta {detail.sourceInvoiceNumber??detail.sourceInvoiceId}.</dd></div>}
          {detail.fiscalNumber&&<div><dt className="text-muted-foreground">Documento soporte DIAN</dt><dd>{detail.fiscalNumber} · {fiscalStatusLabel(detail.fiscalStatus)}</dd></div>}
          {detail.cancellationReason&&<div className="sm:col-span-2"><dt className="text-muted-foreground">Motivo de anulación</dt><dd>{detail.cancellationReason}</dd></div>}
          {detail.adjustmentFiscalNumber&&<div className="sm:col-span-2"><dt className="text-muted-foreground">Nota de ajuste DIAN</dt><dd>{detail.adjustmentFiscalNumber} · {fiscalStatusLabel(detail.adjustmentFiscalStatus)}</dd></div>}
        </dl>
        <ExpenseBreakdown lines={detail.lines} withholding={detail.withholding}/>
        <section className="rounded-xl border p-4"><h3 className="font-semibold">Cuenta por pagar</h3>{detail.payable?<div className="mt-2 flex flex-wrap items-center justify-between gap-3"><p>Estado: {businessStatusLabel(detail.payable.status)} · Saldo: <b>{money.format(detail.payable.outstandingAmount)}</b></p>{permissions.has("payables.payments.create")&&detail.status==="Processed"&&!detail.chargeReturned&&detail.payable.outstandingAmount>0&&<Button onClick={()=>{setPaymentTarget(detail);setSelectedId(undefined)}}><Landmark className="mr-2 h-4 w-4"/>Pagar</Button>}</div>:<p className="mt-2 text-muted-foreground">La obligación aún no se ha abierto o el gasto no genera saldo.</p>}</section>
        <DialogFooter>{permissions.has("expenses.cancel")&&detail.status==="Processed"&&!detail.chargeReturned&&<Button variant="destructive" onClick={()=>{setCancellationId(newExpenseId());setCancelReasonOptionId("");setCancelling(true)}}>Anular gasto</Button>}<Button variant="outline" onClick={()=>setAccountingDocumentId(detail.expenseId)}>Ver asiento</Button>{detail.cancellationId&&<Button variant="outline" onClick={()=>setAccountingDocumentId(detail.cancellationId??undefined)}>Ver reversión</Button>}<Button variant="outline" onClick={()=>setSelectedId(undefined)}>Cerrar</Button></DialogFooter>
      </div>:<p className="py-8 text-center text-destructive">No fue posible cargar el gasto.</p>}</DialogContent></Dialog>
    <Dialog open={cancelling} onOpenChange={open=>{if(!cancelBusy)setCancelling(open)}}><DialogContent><DialogHeader><DialogTitle>Anular gasto</DialogTitle><DialogDescription>Se reversarán el gasto, IVA, retenciones y saldo por pagar. Si ya se pagó, quedará un saldo a favor frente al proveedor. El documento soporte aceptado generará una nota de ajuste ante la DIAN; si la factura la emitió el proveedor, solicita la nota crédito correspondiente.</DialogDescription></DialogHeader><Field label="Motivo de anulación"><Select value={cancelReasonOptionId} onValueChange={setCancelReasonOptionId} disabled={cancelBusy||cancellationReasons.isPending||cancellationReasons.isError||!cancellationReasons.data?.length}><SelectTrigger><SelectValue placeholder={cancellationReasons.isPending?"Cargando motivos...":"Selecciona un motivo"}/></SelectTrigger><SelectContent>{cancellationReasons.data?.map(option=><SelectItem key={option.id} value={option.id}>{option.label}</SelectItem>)}</SelectContent></Select>{cancellationReasons.isError&&<div className="mt-2 flex items-center gap-2 text-sm text-destructive">No fue posible cargar los motivos.<Button size="sm" variant="outline" onClick={()=>void cancellationReasons.refetch()}>Reintentar</Button></div>}{!cancellationReasons.isPending&&!cancellationReasons.isError&&!cancellationReasons.data?.length&&<p className="mt-2 text-sm text-destructive">No hay motivos activos para anular gastos.</p>}</Field><DialogFooter><Button variant="outline" disabled={cancelBusy} onClick={()=>setCancelling(false)}>Volver</Button><Button variant="destructive" disabled={cancelBusy||!selectedCancellationReason} onClick={()=>void cancelExpense()}>{cancelBusy&&<Loader2 className="mr-2 h-4 w-4 animate-spin"/>}Confirmar anulación</Button></DialogFooter></DialogContent></Dialog>
    <PortfolioPaymentWizard direction="payable" onCompleted={()=>void load()} open={!!paymentTarget} onOpenChange={open=>{if(!open)setPaymentTarget(null)}} initialInvoice={paymentTarget?.payable?{id:paymentTarget.payable.payableId,number:paymentTarget.documentNumber,dueDate:paymentTarget.dueDate,outstanding:paymentTarget.payable.outstandingAmount,currency:paymentTarget.currencyCode,overdue:false}:null} initialPartySiteId={paymentTarget?.payable?.partySiteId??undefined} initialPartySiteName={paymentTarget?.payable?.partySiteName??undefined} initialParty={paymentTarget?{partyId:"",roleId:paymentTarget.supplierId,role:"Supplier",displayName:paymentTarget.supplierName,identification:"",supplierPurchaseEvidencePolicy:null,supplierDefaultPaymentDueDays:null,customerId:null,supplierId:paymentTarget.supplierId,sellerId:null,carrierId:null,employeeId:null,userId:null}:null}/>
    <AccountingDocumentDialog documentId={accountingDocumentId} sourceLabel={accountingDocumentId===detail?.cancellationId?"Anulación de gasto":"Gasto"} onClose={()=>setAccountingDocumentId(undefined)}/>
  </div>;
}

function Metric({label,value}:{label:string;value:number}){return <Card><CardContent className="p-5"><p className="text-sm text-muted-foreground">{label}</p><p className="mt-1 text-2xl font-bold">{money.format(value)}</p></CardContent></Card>}
function Field({label,children}:{label:string;children:React.ReactNode}){return <div className="space-y-2"><Label>{label}</Label>{children}</div>}
function newExpenseId(){
  if(typeof globalThis.crypto.randomUUID==="function")return globalThis.crypto.randomUUID();
  const bytes=globalThis.crypto.getRandomValues(new Uint8Array(16));
  bytes[6]=(bytes[6]&0x0f)|0x40;bytes[8]=(bytes[8]&0x3f)|0x80;
  const hex=Array.from(bytes,byte=>byte.toString(16).padStart(2,"0")).join("");
  return `${hex.slice(0,8)}-${hex.slice(8,12)}-${hex.slice(12,16)}-${hex.slice(16,20)}-${hex.slice(20)}`;
}
