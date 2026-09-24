"use client";

import { useCallback, useEffect, useState } from "react";
import { ChevronDown, ChevronUp, FilePlus2, Landmark, Loader2, ReceiptText, Settings2, SlidersHorizontal } from "lucide-react";
import { toast } from "sonner";
import { DataTablePagination } from "@/components/tables/data-table-pagination";
import { ServerSearchInput } from "@/components/tables/server-search-input";
import { AccountingDocumentDialog } from "@/components/accounting/accounting-document-dialog";
import { PartyRoleSelect, type PartyRoleSelection } from "@/components/parties/party-role-select";
import { PortfolioPaymentWizard } from "@/components/payments/portfolio-payment-wizard";
import { ExpenseConceptEditor } from "@/components/expenses/expense-concepts-master";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { DatePicker } from "@/components/ui/date-picker";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { FormattedNumberInput } from "@/components/ui/formatted-number-input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { expensesApi, type ConfirmExpense, type ExpenseDetail, type ExpenseOptions, type ExpensePage } from "@/services/api/expenses";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import {businessStatusLabel,fiscalStatusLabel} from "@/lib/accounting-labels";
import type { PurchaseEvidenceType } from "@/services/api/goods-receipts";
import type { PurchaseEvidencePolicy } from "@/services/api/parties";
import { allowedPurchaseEvidenceTypes } from "@/lib/purchase-evidence-policy";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });
const localNoon = (date:string) => `${date}T12:00:00-05:00`;

export default function ExpensesPage() {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const userPermissions = useAuthStore((state) => state.user?.permissions);
  const permissions = new Set(userPermissions ?? []);
  const [options,setOptions]=useState<ExpenseOptions|null>(null),[report,setReport]=useState<ExpensePage|null>(null);
  const [loading,setLoading]=useState(false),[creating,setCreating]=useState(false),[configuring,setConfiguring]=useState(false);
  const [page,setPage]=useState(1),[pageSize,setPageSize]=useState(25),[search,setSearch]=useState("");
  const [supplierId,setSupplierId]=useState<string>(),[supplierFilter,setSupplierFilter]=useState<PartyRoleSelection|null>(null);
  const [conceptId,setConceptId]=useState<string>(),[from,setFrom]=useState(""),[to,setTo]=useState(""),[status,setStatus]=useState("all"),[payableStatus,setPayableStatus]=useState("all");
  const [filtersExpanded,setFiltersExpanded]=useState(false);
  const activeFilterCount=[supplierId,conceptId,from,to,status==="all"?null:status,payableStatus==="all"?null:payableStatus].filter(Boolean).length;
  const [selectedId,setSelectedId]=useState<string>(),[detail,setDetail]=useState<ExpenseDetail|null>(null),[detailLoading,setDetailLoading]=useState(false);
  const [paymentTarget,setPaymentTarget]=useState<ExpenseDetail|null>(null);
  const [cancelling,setCancelling]=useState(false),[cancelReason,setCancelReason]=useState(""),[cancelBusy,setCancelBusy]=useState(false),[cancellationId,setCancellationId]=useState("");
  const [accountingDocumentId,setAccountingDocumentId]=useState<string>();
  const load=useCallback(async()=>{setLoading(true);try{setReport(await expensesApi.list({page,pageSize,search:search.trim()||undefined,supplierId,conceptId,from:from||undefined,to:to||undefined,status:status==="all"?undefined:status,payableStatus:payableStatus==="all"?undefined:payableStatus}))}catch(error){toast.error(error instanceof Error?error.message:"No fue posible cargar los gastos.")}finally{setLoading(false)}},[page,pageSize,search,supplierId,conceptId,from,to,status,payableStatus]);
  useEffect(()=>{if(businessId)void load()},[businessId,load]);
  useEffect(()=>{if(!businessId)return;let active=true;void expensesApi.options().then(value=>{if(active)setOptions(value)}).catch(error=>toast.error(error instanceof Error?error.message:"No fue posible cargar las opciones de gastos."));return()=>{active=false}},[businessId]);
  useEffect(()=>{if(!businessId||!selectedId){setDetail(null);return}let active=true;setDetailLoading(true);void expensesApi.get(selectedId).then(value=>{if(active)setDetail(value)}).catch(error=>{if(active)toast.error(error instanceof Error?error.message:"No fue posible cargar el gasto.")}).finally(()=>{if(active)setDetailLoading(false)});return()=>{active=false}},[businessId,selectedId]);
  async function cancelExpense(){if(!detail||!cancelReason.trim())return;setCancelBusy(true);try{
    await expensesApi.cancel(detail.expenseId,cancellationId,cancelReason.trim());
    setDetail({...detail,status:"CancellationPending",cancellationId,cancellationReason:cancelReason.trim()});
    setReport(current=>current?{...current,items:current.items.map(item=>item.expenseId===detail.expenseId?{...item,status:"CancellationPending"}:item)}:current);
    setCancelling(false);setCancelReason("");toast.success("Anulación aceptada para procesamiento contable y fiscal.");
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
        <div className="mt-4 grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          <Field label="Proveedor"><PartyRoleSelect role="Supplier" value={supplierId??""} selectedOption={supplierFilter?{value:supplierFilter.roleId,label:supplierFilter.displayName}:null} placeholder="Todos los proveedores" onChange={(id,party)=>{setSupplierId(id||undefined);setSupplierFilter(party??null);setPage(1)}}/></Field>
          <Field label="Concepto"><Select value={conceptId??"all"} onValueChange={value=>{setConceptId(value==="all"?undefined:value);setPage(1)}}><SelectTrigger><SelectValue placeholder="Todos los conceptos"/></SelectTrigger><SelectContent><SelectItem value="all">Todos los conceptos</SelectItem>{options?.concepts.map(item=><SelectItem key={item.conceptId} value={item.conceptId}>{item.name}</SelectItem>)}</SelectContent></Select></Field>
          <Field label="Estado del gasto"><Select value={status} onValueChange={value=>{setStatus(value);setPage(1)}}><SelectTrigger><SelectValue placeholder="Todos los estados"/></SelectTrigger><SelectContent><SelectItem value="all">Todos los estados</SelectItem><SelectItem value="Accepted">Aceptado</SelectItem><SelectItem value="Processed">Procesado</SelectItem><SelectItem value="CancellationPending">Anulación pendiente</SelectItem><SelectItem value="Cancelled">Anulado</SelectItem><SelectItem value="Returned">Devuelto en factura</SelectItem></SelectContent></Select></Field>
          <Field label="Estado de la cuenta por pagar"><Select value={payableStatus} onValueChange={value=>{setPayableStatus(value);setPage(1)}}><SelectTrigger><SelectValue placeholder="Todos los estados"/></SelectTrigger><SelectContent><SelectItem value="all">Todos los estados</SelectItem><SelectItem value="Open">Abierta</SelectItem><SelectItem value="PartiallyPaid">Pagada parcialmente</SelectItem><SelectItem value="Paid">Pagada</SelectItem><SelectItem value="Cancelled">Anulada</SelectItem><SelectItem value="None">Sin cuenta</SelectItem></SelectContent></Select></Field>
          <Field label="Desde"><DatePicker value={from} max={to||undefined} onChange={value=>{setFrom(value);setPage(1)}} placeholder="Fecha inicial"/></Field>
          <Field label="Hasta"><DatePicker value={to} min={from||undefined} onChange={value=>{setTo(value);setPage(1)}} placeholder="Fecha final"/></Field>
        </div>
      </section>}
      {loading?<p className="flex items-center gap-2 py-8 text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin"/>Cargando…</p>:<div className="overflow-x-auto rounded-xl border"><table className="w-full text-sm"><thead className="bg-muted/50 text-xs uppercase tracking-wide text-muted-foreground"><tr>{["Documento","Proveedor","Concepto","Bruto","Retención","Neto original","Saldo por pagar","Estado del gasto","Estado CxP"].map(label=><th key={label} className={`p-3 font-semibold ${["Bruto","Retención","Neto original","Saldo por pagar"].includes(label)?"text-right":"text-left"}`}>{label}</th>)}</tr></thead><tbody>{report?.items.map(item=><tr onClick={()=>setSelectedId(item.expenseId)} onKeyDown={event=>{if(event.key==="Enter"||event.key===" "){event.preventDefault();setSelectedId(item.expenseId)}}} tabIndex={0} role="button" aria-label={`Ver gasto ${item.documentNumber}`} key={item.expenseId} className="cursor-pointer border-t transition hover:bg-muted/40 focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary"><td className="p-3"><b>{item.documentNumber}</b>{item.supplierDocumentNumber && <small className="block text-muted-foreground">Ref. {item.supplierDocumentNumber}</small>}</td><td className="p-3">{item.supplierName}</td><td className="p-3">{item.conceptName}</td><td className="p-3 text-right">{money.format(item.grossAmount)}</td><td className="p-3 text-right">{money.format(item.withholdingAmount)}</td><td className="p-3 text-right font-bold">{money.format(item.netPayable)}</td><td className="p-3 text-right font-semibold">{item.outstandingAmount===null?"—":money.format(item.outstandingAmount)}</td><td className="p-3">{item.chargeReturned&&item.status==="Processed"?"Devuelto en factura":businessStatusLabel(item.status)}</td><td className="p-3">{item.payableStatus?businessStatusLabel(item.payableStatus):"Sin cuenta"}</td></tr>)}</tbody></table>{!report?.items.length&&<p className="p-8 text-center text-sm text-muted-foreground">No hay gastos que coincidan con los filtros.</p>}</div>}
      <DataTablePagination pageIndex={Math.max(0,(report?.page??page)-1)} pageSize={report?.pageSize??pageSize} pageCount={report?.totalPages??0} totalItems={report?.totalCount??0} onPageChange={index=>setPage(index+1)} onPageSizeChange={size=>{setPageSize(size);setPage(1)}}/>
    </CardContent></Card>
    <Dialog open={creating} onOpenChange={setCreating}><DialogContent className="max-h-[92dvh] max-w-3xl overflow-y-auto"><DialogHeader><DialogTitle>Registrar gasto</DialogTitle><DialogDescription>Busca el proveedor o beneficiario; el concepto define la cuenta contable.</DialogDescription></DialogHeader>{options&&<ExpenseForm businessId={businessId} options={options} onSaved={async()=>{setCreating(false);await load()}}/>}</DialogContent></Dialog>
    <Dialog open={configuring} onOpenChange={setConfiguring}><DialogContent className="max-h-[92dvh] max-w-2xl overflow-y-auto"><DialogHeader><DialogTitle>Conceptos de gasto</DialogTitle><DialogDescription>Clasifican el gasto y determinan su cuenta contable.</DialogDescription></DialogHeader>{options&&<ExpenseConceptEditor businessId={businessId} options={options} onSaved={async()=>{setOptions(await expensesApi.options());await load()}}/>}</DialogContent></Dialog>
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
        <section className="rounded-xl border p-4"><h3 className="font-semibold">Cuenta por pagar</h3>{detail.payable?<div className="mt-2 flex flex-wrap items-center justify-between gap-3"><p>Estado: {businessStatusLabel(detail.payable.status)} · Saldo: <b>{money.format(detail.payable.outstandingAmount)}</b></p>{permissions.has("payables.payments.create")&&detail.status==="Processed"&&!detail.chargeReturned&&detail.payable.outstandingAmount>0&&<Button onClick={()=>{setPaymentTarget(detail);setSelectedId(undefined)}}><Landmark className="mr-2 h-4 w-4"/>Pagar</Button>}</div>:<p className="mt-2 text-muted-foreground">La obligación aún no se ha abierto o el gasto no genera saldo.</p>}</section>
        <DialogFooter>{permissions.has("expenses.cancel")&&detail.status==="Processed"&&!detail.chargeReturned&&<Button variant="destructive" onClick={()=>{setCancellationId(newExpenseId());setCancelling(true)}}>Anular gasto</Button>}<Button variant="outline" onClick={()=>setAccountingDocumentId(detail.expenseId)}>Ver asiento</Button>{detail.cancellationId&&<Button variant="outline" onClick={()=>setAccountingDocumentId(detail.cancellationId??undefined)}>Ver reversión</Button>}<Button variant="outline" onClick={()=>setSelectedId(undefined)}>Cerrar</Button></DialogFooter>
      </div>:<p className="py-8 text-center text-destructive">No fue posible cargar el gasto.</p>}</DialogContent></Dialog>
    <Dialog open={cancelling} onOpenChange={setCancelling}><DialogContent><DialogHeader><DialogTitle>Anular gasto</DialogTitle><DialogDescription>Se reversarán el gasto, IVA, retenciones y saldo por pagar. Si ya se pagó, quedará un saldo a favor frente al proveedor. El documento soporte aceptado generará una nota de ajuste ante la DIAN; si la factura la emitió el proveedor, solicita la nota crédito correspondiente.</DialogDescription></DialogHeader><Field label="Motivo de anulación"><Input value={cancelReason} maxLength={300} onChange={event=>setCancelReason(event.target.value)} /></Field><DialogFooter><Button variant="outline" onClick={()=>setCancelling(false)}>Volver</Button><Button variant="destructive" disabled={cancelBusy||!cancelReason.trim()} onClick={()=>void cancelExpense()}>{cancelBusy&&<Loader2 className="mr-2 h-4 w-4 animate-spin"/>}Confirmar anulación</Button></DialogFooter></DialogContent></Dialog>
    <PortfolioPaymentWizard direction="payable" open={!!paymentTarget} onOpenChange={open=>{if(!open)setPaymentTarget(null)}} initialInvoice={paymentTarget?.payable?{id:paymentTarget.payable.payableId,number:paymentTarget.documentNumber,dueDate:paymentTarget.dueDate,outstanding:paymentTarget.payable.outstandingAmount,currency:paymentTarget.currencyCode,overdue:false}:null} initialParty={paymentTarget?{partyId:"",roleId:paymentTarget.supplierId,role:"Supplier",displayName:paymentTarget.supplierName,identification:"",supplierPurchaseEvidencePolicy:null,supplierDefaultPaymentDueDays:null,customerId:null,supplierId:paymentTarget.supplierId,sellerId:null,carrierId:null,employeeId:null,userId:null}:null}/>
    <AccountingDocumentDialog documentId={accountingDocumentId} sourceLabel={accountingDocumentId===detail?.cancellationId?"Anulación de gasto":"Gasto"} onClose={()=>setAccountingDocumentId(undefined)}/>
  </div>;
}

function ExpenseForm({businessId,options,onSaved}:{businessId:string;options:ExpenseOptions;onSaved:()=>Promise<void>}){
  const today=new Date().toISOString().slice(0,10),[busy,setBusy]=useState(false);
  const [supplierPolicy,setSupplierPolicy]=useState<PurchaseEvidencePolicy|null>(null);
  const [form,setForm]=useState<ConfirmExpense>(()=>({expenseId:newExpenseId(),businessId,supplierId:"",conceptId:"",costCenterId:null,supplierDocumentNumber:"",issuedAt:localNoon(today),dueDate:localNoon(today),currencyCode:"COP",description:"",taxExclusiveAmount:0,vatAmount:0,withholdingJurisdictionCode:"CO",evidenceUrl:null,purchaseEvidenceType:"SupplierElectronicInvoice"}));
  const concept=options.concepts.find(item=>item.conceptId===form.conceptId);
  const supplierInvoice=form.purchaseEvidenceType==="SupplierElectronicInvoice";
  const supportDocument=form.purchaseEvidenceType==="BuyerElectronicSupportDocument";
  const visibleEvidenceTypes=options.purchaseEvidenceTypes.filter(item=>allowedPurchaseEvidenceTypes(supplierPolicy).includes(item.code));
  async function submit(event:React.FormEvent){event.preventDefault();setBusy(true);try{await expensesApi.confirm(form);toast.success("Gasto aceptado para procesamiento contable.");await onSaved()}catch(error){toast.error(error instanceof Error?error.message:"No fue posible registrar el gasto.")}finally{setBusy(false)}}
  return <form className="grid gap-4 sm:grid-cols-2" onSubmit={submit}>
    <Field label="Proveedor o beneficiario"><PartyRoleSelect role="Supplier" value={form.supplierId} placeholder="Buscar proveedor o beneficiario" onChange={(supplierId,party)=>{const policy=party?.supplierPurchaseEvidencePolicy??null;setSupplierPolicy(policy);const allowed=options.purchaseEvidenceTypes.filter(item=>allowedPurchaseEvidenceTypes(policy).includes(item.code));setForm(current=>({...current,supplierId,purchaseEvidenceType:allowed.some(item=>item.code===current.purchaseEvidenceType)?current.purchaseEvidenceType:allowed[0]?.code??current.purchaseEvidenceType}));}}/></Field>
    <Field label="Concepto"><Select value={form.conceptId} onValueChange={conceptId=>{const selected=options.concepts.find(item=>item.conceptId===conceptId);setForm({...form,conceptId,costCenterId:selected?.defaultCostCenterId??null})}}><SelectTrigger><SelectValue placeholder="Selecciona"/></SelectTrigger><SelectContent>{options.concepts.filter(item=>item.isActive).map(item=><SelectItem key={item.conceptId} value={item.conceptId}>{item.name}</SelectItem>)}</SelectContent></Select></Field>
    <Field label="Tipo de documento"><Select value={form.purchaseEvidenceType} onValueChange={(purchaseEvidenceType:PurchaseEvidenceType)=>setForm({...form,purchaseEvidenceType,supplierDocumentNumber:purchaseEvidenceType==="SupplierElectronicInvoice"?form.supplierDocumentNumber:""})}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent>{visibleEvidenceTypes.map(item=><SelectItem key={item.code} value={item.code}>{item.label}</SelectItem>)}</SelectContent></Select></Field>
    <Field label={supplierInvoice?"Número de factura electrónica":"Referencia (opcional)"}><Input required={supplierInvoice} value={form.supplierDocumentNumber??""} onChange={event=>setForm({...form,supplierDocumentNumber:event.target.value||null})}/></Field>
    <Field label="Fecha de emisión"><DatePicker value={form.issuedAt.slice(0,10)} onChange={date=>setForm({...form,issuedAt:localNoon(date)})}/></Field>
    <Field label="Fecha de vencimiento"><DatePicker value={form.dueDate.slice(0,10)} onChange={date=>setForm({...form,dueDate:localNoon(date)})}/></Field>
    <Field label="Base antes de IVA"><FormattedNumberInput kind="currency" value={form.taxExclusiveAmount} onValueChange={value=>setForm(current=>({...current,taxExclusiveAmount:value??0}))}/></Field>
    <Field label="IVA"><FormattedNumberInput kind="currency" value={form.vatAmount} onValueChange={value=>setForm(current=>({...current,vatAmount:value??0}))}/></Field>
    <div className="sm:col-span-2"><Field label="Descripción (opcional)"><Input value={form.description} onChange={event=>setForm({...form,description:event.target.value})} placeholder="Agrega detalle solo cuando haga falta"/></Field></div>
    <div className="sm:col-span-2 rounded-xl border bg-muted/30 p-3 text-sm"><b>Cuenta contable:</b> {concept?`${concept.expenseAccountCode} · ${concept.expenseAccountName}`:"se define con el concepto"}<p className="mt-1 text-muted-foreground"><b>Centro de costo:</b> {concept?.defaultCostCenterName??"Sin centro predeterminado"}. La retención se calcula con el perfil tributario del proveedor.</p>{supportDocument&&<p className="mt-1 text-amber-700">El documento soporte usa el código postal de la sede principal activa del proveedor. Debe tener seis dígitos. Si falta, ve a Terceros → Proveedores → Ubicación principal y consulta el código con la dirección completa en <a className="underline" href="https://www.4-72.com.co/codigo-postal/" target="_blank" rel="noreferrer">4-72</a>. El código postal del emisor en Configuración fiscal es un dato distinto.</p>}</div>
    <DialogFooter className="sm:col-span-2"><Button type="submit" disabled={busy||!form.supplierId||!form.conceptId||form.taxExclusiveAmount<=0||(supplierInvoice&&!form.supplierDocumentNumber?.trim())}>{busy&&<Loader2 className="mr-2 h-4 w-4 animate-spin"/>}Confirmar gasto</Button></DialogFooter>
  </form>;
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
