"use client";

import { useEffect,useMemo,useRef,useState } from "react";
import { useMutation,useQuery } from "@tanstack/react-query";
import { ArrowLeft,ArrowRight,CheckCircle2,CreditCard,Plus,Trash2 } from "lucide-react";
import { toast } from "sonner";
import { PartyRoleSelect,type PartyRoleSelection } from "@/components/parties/party-role-select";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Dialog,DialogContent,DialogDescription,DialogFooter,DialogHeader,DialogTitle } from "@/components/ui/dialog";
import { FormattedNumberInput } from "@/components/ui/formatted-number-input";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select,SelectContent,SelectItem,SelectTrigger,SelectValue } from "@/components/ui/select";
import { payablesApi,type SupplierPaymentTender } from "@/services/api/payables";
import { partiesApi } from "@/services/api/parties";
import { referenceOptionsApi } from "@/services/api/reference-options";
import { receivablesApi,type CustomerPaymentMethod,type CustomerPaymentTender } from "@/services/api/receivables";
import { workSessionsApi } from "@/services/api/work-sessions";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { useAuthStore } from "@/stores/auth-store";
import { formatCurrency,formatDate } from "@/lib/utils";
import type { PosEdgeClient } from "@/services/pos/pos-edge-client";

type Direction="receivable"|"payable";
type Invoice={id:string;number:string;dueDate:string;outstanding:number;currency:string;overdue:boolean};
type Tender={methodCode:CustomerPaymentMethod;amount:string;bankAccountId:string;reference:string;cardFranchiseCode:string;approvalNumber:string};
type PaymentAttempt={fingerprint:string;paymentId:string;paidAt:string;sessionId:string|null};
const toPortfolioMethod=(code:string):CustomerPaymentMethod|null=>
  code==="Transfer"?"BankTransfer":code==="Cash"||code==="DebitCard"||code==="CreditCard"?code:null;

export function PortfolioPaymentWizard({direction,open,onOpenChange,initialParty,initialInvoice,workSessionId,onCompleted,edgeClient,businessId:businessIdOverride}:{direction:Direction;open:boolean;onOpenChange:(open:boolean)=>void;initialParty?:PartyRoleSelection|null;initialInvoice?:Invoice|null;workSessionId?:string|null;onCompleted?:()=>void;edgeClient?:PosEdgeClient|null;businessId?:string|null}){
  const initialPartyRef=useRef(initialParty);initialPartyRef.current=initialParty;
  const initialInvoiceRef=useRef(initialInvoice);initialInvoiceRef.current=initialInvoice;
  const selectedBusinessId=useBusinessContextStore(state=>state.selectedBusinessId);const businessId=businessIdOverride??selectedBusinessId;
  const userId=useAuthStore(state=>state.user?.userId);
  const attemptStorageKey=`portfolio-payment:${direction}:${businessId}:${workSessionId??userId??"anonymous"}`;
  const [step,setStep]=useState<1|2>(1);const [party,setParty]=useState<PartyRoleSelection|null>(initialParty??null);
  const [invoicePage,setInvoicePage]=useState(1);
  const [selected,setSelected]=useState<Record<string,string>>({});
  const [selectedLimits,setSelectedLimits]=useState<Record<string,number>>({});
  const [tenders,setTenders]=useState<Tender[]>([{methodCode:"Cash",amount:"",bankAccountId:"",reference:"",cardFranchiseCode:"",approvalNumber:""}]);
  const pendingAttempt=useRef<PaymentAttempt|null>(null);
  const role=direction==="receivable"?"Customer":"Supplier";
  const partyId=direction==="receivable"?party?.customerId:party?.supplierId;
  useEffect(()=>{if(open){const party=initialPartyRef.current;const invoice=initialInvoiceRef.current;setStep(1);setParty(party??null);setInvoicePage(1);setSelected(invoice?{[invoice.id]:String(invoice.outstanding)}:{});setSelectedLimits(invoice?{[invoice.id]:invoice.outstanding}:{});setTenders([{methodCode:"Cash",amount:"",bankAccountId:"",reference:"",cardFranchiseCode:"",approvalNumber:""}]);}},[open]);
  const invoicesQuery=useQuery({queryKey:["portfolio-payment-invoices",edgeClient?"edge":"web",direction,businessId,partyId,invoicePage],enabled:open&&!!partyId,queryFn:async()=>{
    if(direction==="receivable"){const value=edgeClient?await edgeClient.portfolioReceivables(partyId!,invoicePage,20):await receivablesApi.list({page:invoicePage,pageSize:20,customerId:partyId!,outstandingOnly:true});return {totalCount:value.totalCount,totalPages:value.totalPages,items:value.items.map<Invoice>(x=>({id:x.receivableId,number:x.documentNumber,dueDate:x.dueDate,outstanding:x.outstandingAmount,currency:x.currencyCode,overdue:x.isOverdue}))};}
    const value=edgeClient?await edgeClient.portfolioPayables(partyId!,invoicePage,20):await payablesApi.list({page:invoicePage,pageSize:20,supplierId:partyId!,outstandingOnly:true});return {totalCount:value.totalCount,totalPages:value.totalPages,items:value.items.map<Invoice>(x=>({id:x.payableId,number:x.documentNumber,dueDate:x.dueDate,outstanding:x.outstandingAmount,currency:x.currencyCode,overdue:x.isOverdue}))};
  }});
  const invoiceItems=useMemo(()=>{const items=invoicesQuery.data?.items??[];return initialInvoice&&partyId===(direction==="receivable"?initialParty?.customerId:initialParty?.supplierId)&&!items.some(item=>item.id===initialInvoice.id)?[initialInvoice,...items]:items;},[invoicesQuery.data,initialInvoice,initialParty,partyId,direction]);
  useEffect(()=>{
    if(!open||initialInvoice||invoicesQuery.data?.totalCount!==1)return;
    const only=invoicesQuery.data.items[0];
    if(!only)return;
    setSelected({[only.id]:String(only.outstanding)});
    setSelectedLimits({[only.id]:only.outstanding});
  },[open,initialInvoice,invoicesQuery.data]);
  const configuration=useQuery({queryKey:["payment-settlement-configuration",edgeClient?"edge":"web",businessId],queryFn:()=>edgeClient?edgeClient.portfolioSettlementConfiguration():receivablesApi.settlementConfiguration(),enabled:open&&step===2&&!!businessId});
  const paymentMethods=useQuery({queryKey:["portfolio-payment-methods",edgeClient?"edge":"web"],queryFn:()=>edgeClient?edgeClient.referenceOptions("payment-method"):referenceOptionsApi.list("payment-method"),enabled:open&&step===2});
  const cardFranchises=useQuery({queryKey:["portfolio-card-franchises",edgeClient?"edge":"web"],queryFn:()=>edgeClient?edgeClient.referenceOptions("card-franchise"):referenceOptionsApi.list("card-franchise"),enabled:open&&step===2&&direction==="receivable"});
  const allocations=useMemo(()=>Object.entries(selected).map(([id,value])=>({id,amount:Number(value)})).filter(x=>Number.isFinite(x.amount)&&x.amount>0),[selected]);
  const total=allocations.reduce((sum,x)=>sum+x.amount,0);const tenderTotal=tenders.reduce((sum,x)=>sum+(Number(x.amount)||0),0);
  useEffect(()=>{if(step!==2)return;setTenders(current=>{if(allocations.length>1)return [{...current[0],amount:String(total)}];if(current.length===1&&!current[0].amount)return [{...current[0],amount:String(total)}];return current;});},[step,allocations.length,total]);
  const mutation=useMutation({mutationFn:async()=>{
    if(!businessId||!partyId)throw new Error("Falta el tercero.");
    const fingerprint=JSON.stringify({direction,businessId,partyId,workSessionId,allocations,tenders});
    let attempt=pendingAttempt.current;
    if(!attempt){const saved=sessionStorage.getItem(attemptStorageKey);if(saved){const parsed=JSON.parse(saved) as PaymentAttempt;if(parsed?.fingerprint===fingerprint&&parsed.paymentId&&parsed.paidAt)attempt=parsed;}}
    if(!attempt||attempt.fingerprint!==fingerprint){
      const sessionId=workSessionId===undefined?(await workSessionsApi.currentOrOpen(businessId)).workSessionId:workSessionId;
      attempt={fingerprint,paymentId:crypto.randomUUID(),paidAt:new Date().toISOString(),sessionId:sessionId??null};
    }
    sessionStorage.setItem(attemptStorageKey,JSON.stringify(attempt));
    pendingAttempt.current=attempt;
    const {paymentId,paidAt,sessionId}=attempt;
    if(direction==="receivable"){const request={paymentId,businessId,customerId:partyId,workSessionId:sessionId,paidAt,currencyCode:"COP",notes:null,allocations:allocations.map(x=>({receivableId:x.id,amount:x.amount})),payments:tenders.map(toCustomerTender)};return edgeClient?edgeClient.confirmPortfolioReceivable(request,`receivable-payment-${paymentId}`):receivablesApi.confirmPayment(request,`receivable-payment-${paymentId}`);}
    const request={paymentId,businessId,supplierId:partyId,workSessionId:sessionId,paidAt,currencyCode:"COP",notes:null,allocations:allocations.map(x=>({payableId:x.id,amount:x.amount})),payments:tenders.map(toSupplierTender)};return edgeClient?edgeClient.confirmPortfolioPayable(request,`payable-payment-${paymentId}`):payablesApi.confirmPayment(request,`payable-payment-${paymentId}`);
  },onSuccess:accepted=>{sessionStorage.removeItem(attemptStorageKey);pendingAttempt.current=null;toast.success(`${accepted.documentNumber} quedó registrado. El saldo se actualizará al aplicar el movimiento.`);changeOpen(false);onCompleted?.();},onError:error=>toast.error(error instanceof Error?error.message:"No fue posible registrar el movimiento. Si no recibiste confirmación, reintenta sin cambiar los valores.")});
  const validateStepOne=()=>{if(!partyId||allocations.length===0){toast.error("Selecciona un tercero y al menos una factura.");return false;}if(allocations.some(x=>x.amount>(selectedLimits[x.id]??0))){toast.error("Ningún abono puede superar el saldo de la factura.");return false;}return true;};
  const methods=useMemo(()=>(paymentMethods.data??[]).map(option=>[toPortfolioMethod(option.code),option.label] as const).filter((option):option is readonly [CustomerPaymentMethod,string]=>option[0]!==null&&(direction==="receivable"||option[0]==="Cash"||option[0]==="BankTransfer")),[paymentMethods.data,direction]);
  const paymentBlockReason=!configuration.isSuccess||!paymentMethods.isSuccess?"Cargando configuración y medios de pago. Si falla, reintenta la consulta.":
    total<=0?"Selecciona un valor de abono.":
    Number(tenderTotal.toFixed(4))!==Number(total.toFixed(4))?"El total en medios debe ser igual al valor seleccionado.":
    allocations.length>1&&tenders.length>1?"Varias facturas requieren un solo medio de pago.":
    tenders.some(value=>!validTender(value,configuration.data.isAccountingEnabled,methods.map(([code])=>code),cardFranchises.data?.map(option=>option.code)??[]))?"Completa el medio de pago, sus datos y un valor válido.":null;
  const canPay=paymentBlockReason===null;
  useEffect(()=>{
    if(!open||step!==2)return;
    const onKeyDown=(event:KeyboardEvent)=>{
      if(event.altKey||event.ctrlKey||event.metaKey||event.shiftKey)return;
      const index=/^F[1-4]$/.test(event.key)?Number(event.key.slice(1))-1:-1;
      if(index<0||index>=methods.length)return;
      event.preventDefault();
      updateTender(0,{methodCode:methods[index][0],bankAccountId:"",reference:"",cardFranchiseCode:"",approvalNumber:""});
    };
    window.addEventListener("keydown",onKeyDown);
    return()=>window.removeEventListener("keydown",onKeyDown);
  },[open,step,methods]);
  return <Dialog open={open} onOpenChange={next=>{if(!next&&mutation.isPending)return;changeOpen(next)}}><DialogContent className="flex max-h-[92dvh] w-[96vw] max-w-4xl flex-col overflow-hidden p-0">
    <DialogHeader className="flex flex-row items-start justify-between gap-4 border-b border-slate-200 px-6 py-5 text-left"><div><DialogTitle className="flex items-center gap-2 text-xl"><CreditCard className="h-5 w-5 text-teal-700"/>{direction==="receivable"?"Abono a cartera":"Pago a proveedores"}</DialogTitle><DialogDescription className="mt-1">{step===1?"Selecciona las facturas y el valor de cada una.":`Escribe el valor recibido. F1-F${direction==="receivable"?4:2} seleccionan el medio de pago.`}</DialogDescription></div>{step===2&&<div className="shrink-0 text-right"><span className="block text-xs uppercase text-slate-500">Total a pagar</span><strong className="text-2xl text-teal-800">{formatCurrency(total)}</strong></div>}</DialogHeader>
    <div className="overflow-y-auto p-6" inert={mutation.isPending}>{step===1?<div className="space-y-5"><div className="space-y-2"><Label>{role==="Customer"?"Cliente":"Proveedor"}</Label><PartyRoleSelect role={role} value={partyId??""} sourceKey={edgeClient?"edge-portfolio":"web-portfolio"} preload={open} loadPage={(search,page,pageSize)=>edgeClient?edgeClient.portfolioParties(role,search,page,pageSize):partiesApi.portfolioRoleOptions({role,search,page,pageSize})} selectedOption={party?{value:partyId!,label:party.displayName,description:party.identification}:null} placeholder={`Buscar por nombre o identificación`} onChange={(_,value)=>{setParty(value??null);setSelected({});setSelectedLimits({});setInvoicePage(1);}}/></div>
      {!partyId?<Empty text={`Busca y selecciona un ${role==="Customer"?"cliente":"proveedor"}.`}/>:invoicesQuery.isLoading?<Empty text="Cargando cartera..."/>:invoicesQuery.isError?<Empty text="No fue posible consultar la cartera. Comprueba la conexión e inténtalo nuevamente."/>:invoiceItems.length===0?<Empty text="Este tercero no tiene facturas pendientes."/>:<div className="space-y-2">{invoiceItems.map(invoice=>{const checked=selected[invoice.id]!==undefined;return <div key={invoice.id} className={`grid grid-cols-[auto_minmax(0,1fr)] items-center gap-3 rounded-xl border p-4 lg:grid-cols-[auto_minmax(0,1fr)_auto_11rem] ${checked?"border-primary/50 bg-primary/5":""}`}><Checkbox checked={checked} onCheckedChange={value=>{setSelected(current=>{const next={...current};if(value)next[invoice.id]=String(invoice.outstanding);else delete next[invoice.id];return next;});setSelectedLimits(current=>{const next={...current};if(value)next[invoice.id]=invoice.outstanding;else delete next[invoice.id];return next;});}}/><div className="min-w-0"><div className="flex items-center gap-2"><b className="truncate">{invoice.number}</b>{invoice.overdue&&<Badge variant="destructive">Vencida</Badge>}</div><p className="text-sm text-muted-foreground">Vence {formatDate(invoice.dueDate)}</p></div><span className="col-start-2 text-sm font-semibold lg:col-auto">Saldo {formatCurrency(invoice.outstanding,invoice.currency)}</span><div className="col-start-2 lg:col-auto"><FormattedNumberInput kind="currency" ariaLabel={`Abono para ${invoice.number}`} value={selected[invoice.id]??""} onValueChange={value=>{setSelected(current=>{const next={...current};if(value&&value>0)next[invoice.id]=String(value);else delete next[invoice.id];return next;});setSelectedLimits(current=>({...current,[invoice.id]:invoice.outstanding}));}}/></div></div>})}</div>}
      {partyId&&invoicesQuery.data&&invoicesQuery.data.totalPages>1&&<div className="flex items-center justify-between text-sm text-muted-foreground"><span>{invoicesQuery.data.totalCount} facturas · {allocations.length} seleccionadas</span><div className="flex items-center gap-2"><Button type="button" variant="outline" size="sm" disabled={invoicePage<=1} onClick={()=>setInvoicePage(page=>page-1)}>Anterior</Button><span>{invoicePage} de {invoicesQuery.data.totalPages}</span><Button type="button" variant="outline" size="sm" disabled={invoicePage>=invoicesQuery.data.totalPages} onClick={()=>setInvoicePage(page=>page+1)}>Siguiente</Button></div></div>}
      <div className="flex justify-between rounded-xl bg-muted p-4"><span>Total seleccionado</span><b className="text-lg">{formatCurrency(total)}</b></div></div>:<div className="space-y-5"><div className="rounded-xl border bg-muted/30 p-4"><p className="text-sm text-muted-foreground">{party?.displayName} · {allocations.length} factura{allocations.length===1?"":"s"}</p><p className="text-2xl font-semibold">{formatCurrency(total)}</p>{allocations.length>1&&<p className="mt-1 text-xs text-muted-foreground">Para varias facturas se usa un solo medio de pago.</p>}</div>
      {(configuration.isError||paymentMethods.isError||cardFranchises.isError)&&<p role="alert" className="text-sm text-destructive">No fue posible cargar la configuración de pagos. Comprueba la conexión e inténtalo nuevamente.</p>}
      <div className="grid grid-cols-2 gap-2 md:grid-cols-4">{methods.map(([code,label],index)=><button key={code} type="button" onClick={()=>updateTender(0,{methodCode:code,bankAccountId:"",reference:"",cardFranchiseCode:"",approvalNumber:""})} className="flex min-h-12 items-center justify-between gap-2 rounded-lg border border-slate-300 px-3 text-left text-sm font-semibold hover:border-teal-500 hover:bg-teal-50"><span>{label}</span><kbd className="rounded bg-slate-100 px-1.5 py-0.5 text-xs text-slate-600">F{index+1}</kbd></button>)}</div>
      <div className="space-y-3">{tenders.map((tender,index)=><div key={index} className="grid gap-3 rounded-xl border border-slate-200 p-4 md:grid-cols-[1fr_11rem_1fr_auto]"><div className="space-y-2"><Label>Medio de pago</Label><Select value={tender.methodCode} onValueChange={value=>updateTender(index,{methodCode:value as CustomerPaymentMethod,bankAccountId:"",reference:"",cardFranchiseCode:"",approvalNumber:""})}><SelectTrigger className="h-11 border-slate-300"><SelectValue/></SelectTrigger><SelectContent>{methods.filter(([code])=>!tenders.some((x,i)=>i!==index&&x.methodCode===code)).map(([code,label])=><SelectItem key={code} value={code}>{label}</SelectItem>)}</SelectContent></Select></div><div className="space-y-2"><Label>Valor recibido</Label><FormattedNumberInput kind="currency" className="h-11 text-right text-lg font-semibold" value={tender.amount} onValueChange={value=>updateTender(index,{amount:value?.toString()??""})}/></div><div className="flex items-end"><div className="flex h-11 w-full items-center rounded-lg border border-emerald-200 bg-emerald-50 px-3 text-sm font-medium text-emerald-900">{tender.methodCode==="Cash"?"Pago en efectivo":tender.methodCode==="BankTransfer"?"Referencia de transferencia":"Datos de la tarjeta"}</div></div>
        {tender.methodCode==="BankTransfer"&&<>{configuration.data?.isAccountingEnabled&&<div className="space-y-2"><Label>Cuenta bancaria</Label><Select value={tender.bankAccountId} onValueChange={value=>updateTender(index,{bankAccountId:value})}><SelectTrigger><SelectValue placeholder="Selecciona una cuenta"/></SelectTrigger><SelectContent>{configuration.data.bankAccounts.map(x=><SelectItem key={x.bankAccountId} value={x.bankAccountId}>{x.displayName}</SelectItem>)}</SelectContent></Select></div>}<Field label="Referencia" value={tender.reference} set={value=>updateTender(index,{reference:value})}/></>}
        {(tender.methodCode==="DebitCard"||tender.methodCode==="CreditCard")&&<><div className="space-y-2"><Label>Franquicia</Label><Select value={tender.cardFranchiseCode} onValueChange={value=>updateTender(index,{cardFranchiseCode:value})}><SelectTrigger><SelectValue placeholder="Selecciona una franquicia"/></SelectTrigger><SelectContent>{(cardFranchises.data??[]).map(option=><SelectItem key={option.code} value={option.code}>{option.label}</SelectItem>)}</SelectContent></Select></div><Field label="Número de aprobación" value={tender.approvalNumber} set={value=>updateTender(index,{approvalNumber:value})}/></>}
        {tenders.length>1&&<Button className="md:col-start-4 md:row-start-1 md:self-end" type="button" variant="ghost" aria-label="Quitar medio" onClick={()=>setTenders(current=>current.filter((_,i)=>i!==index))}><Trash2 className="h-4 w-4"/></Button>}</div>)}</div>
      {allocations.length===1&&tenders.length<methods.length&&<Button type="button" variant="outline" onClick={()=>setTenders(current=>[...current,{methodCode:methods.find(([code])=>!current.some(x=>x.methodCode===code))![0],amount:"",bankAccountId:"",reference:"",cardFranchiseCode:"",approvalNumber:""}])}><Plus className="mr-2 h-4 w-4"/>Agregar otro medio de pago</Button>}
      <div className={`flex justify-between rounded-xl p-4 ${Math.abs(tenderTotal-total)<0.005?"bg-emerald-50 text-emerald-900":"bg-amber-50 text-amber-900"}`}><span>Total en medios</span><b>{formatCurrency(tenderTotal)} / {formatCurrency(total)}</b></div></div>}</div>
    {step===2&&paymentBlockReason&&<p role="status" className="border-t px-6 py-2 text-sm text-amber-800">{paymentBlockReason} {(configuration.isError||paymentMethods.isError||cardFranchises.isError)&&<Button variant="link" size="sm" onClick={()=>{void configuration.refetch();void paymentMethods.refetch();if(cardFranchises.isError)void cardFranchises.refetch()}}>Reintentar</Button>}</p>}
    <DialogFooter className="border-t px-6 py-4">{step===2&&<Button type="button" variant="outline" disabled={mutation.isPending} onClick={()=>setStep(1)}><ArrowLeft className="mr-2 h-4 w-4"/>Atrás</Button>}<Button type="button" variant="ghost" disabled={mutation.isPending} onClick={()=>changeOpen(false)}>Cancelar</Button>{step===1?<Button type="button" disabled={invoicesQuery.isError} onClick={()=>validateStepOne()&&setStep(2)}>Ir a pagar<ArrowRight className="ml-2 h-4 w-4"/></Button>:<Button type="button" disabled={!canPay||mutation.isPending} onClick={()=>mutation.mutate()}><CheckCircle2 className="mr-2 h-4 w-4"/>{mutation.isPending?"Registrando...":"Confirmar pago"}</Button>}</DialogFooter>
  </DialogContent></Dialog>;
  function changeOpen(next:boolean){if(!next){setParty(null);setSelected({});setSelectedLimits({});setInvoicePage(1);setStep(1);}onOpenChange(next);}
  function updateTender(index:number,patch:Partial<Tender>){setTenders(current=>current.map((value,i)=>i===index?{...value,...patch}:value));}
}
function validTender(value:Tender,accountingEnabled:boolean,methods:CustomerPaymentMethod[],franchises:string[]){const amount=Number(value.amount);if(!methods.includes(value.methodCode)||!Number.isFinite(amount)||amount<=0)return false;if(value.methodCode==="BankTransfer")return (!accountingEnabled||!!value.bankAccountId)&&!!value.reference.trim();if(value.methodCode==="DebitCard"||value.methodCode==="CreditCard")return franchises.includes(value.cardFranchiseCode)&&!!value.approvalNumber.trim();return true;}
function toCustomerTender(value:Tender):CustomerPaymentTender{return {methodCode:value.methodCode,amount:Number(value.amount),tenderedAmount:value.methodCode==="Cash"?Number(value.amount):null,bankAccountId:value.methodCode==="BankTransfer"?value.bankAccountId:null,reference:value.reference.trim()||null,notes:null,cardFranchiseCode:value.cardFranchiseCode.trim()||null,approvalNumber:value.approvalNumber.trim()||null};}
function toSupplierTender(value:Tender):SupplierPaymentTender{return {methodCode:value.methodCode as "Cash"|"BankTransfer",amount:Number(value.amount),tenderedAmount:value.methodCode==="Cash"?Number(value.amount):null,bankAccountId:value.methodCode==="BankTransfer"?value.bankAccountId:null,reference:value.reference.trim()||null,notes:null};}
function Field({label,value,set}:{label:string;value:string;set:(value:string)=>void}){return <div className="space-y-2"><Label>{label}</Label><Input value={value} onChange={event=>set(event.target.value)}/></div>}
function Empty({text}:{text:string}){return <div className="rounded-xl border border-dashed py-12 text-center text-sm text-muted-foreground">{text}</div>}
