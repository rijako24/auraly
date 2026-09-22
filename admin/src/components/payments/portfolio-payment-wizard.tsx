"use client";

import { useEffect,useMemo,useRef,useState } from "react";
import { useMutation,useQuery } from "@tanstack/react-query";
import { ArrowLeft,ArrowRight,CheckCircle2,Plus,Trash2 } from "lucide-react";
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
import { receivablesApi,type CustomerPaymentMethod,type CustomerPaymentTender } from "@/services/api/receivables";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { formatCurrency,formatDate } from "@/lib/utils";
import type { PosEdgeClient } from "@/services/pos/pos-edge-client";

type Direction="receivable"|"payable";
type Invoice={id:string;number:string;dueDate:string;outstanding:number;currency:string;overdue:boolean};
type Tender={methodCode:CustomerPaymentMethod;amount:string;bankAccountId:string;reference:string;cardFranchiseCode:string;approvalNumber:string};
const receivableMethods:[CustomerPaymentMethod,string][]=[["Cash","Efectivo"],["BankTransfer","Transferencia"],["DebitCard","Tarjeta débito"],["CreditCard","Tarjeta crédito"]];
const payableMethods:[CustomerPaymentMethod,string][]=[["Cash","Efectivo"],["BankTransfer","Transferencia"]];

export function PortfolioPaymentWizard({direction,open,onOpenChange,initialParty,initialInvoice,workSessionId,onCompleted,edgeClient,businessId:businessIdOverride}:{direction:Direction;open:boolean;onOpenChange:(open:boolean)=>void;initialParty?:PartyRoleSelection|null;initialInvoice?:Invoice|null;workSessionId?:string|null;onCompleted?:()=>void;edgeClient?:PosEdgeClient|null;businessId?:string|null}){
  const initialPartyRef=useRef(initialParty);initialPartyRef.current=initialParty;
  const initialInvoiceRef=useRef(initialInvoice);initialInvoiceRef.current=initialInvoice;
  const selectedBusinessId=useBusinessContextStore(state=>state.selectedBusinessId);const businessId=businessIdOverride??selectedBusinessId;
  const [step,setStep]=useState<1|2>(1);const [party,setParty]=useState<PartyRoleSelection|null>(initialParty??null);
  const [invoicePage,setInvoicePage]=useState(1);
  const [selected,setSelected]=useState<Record<string,string>>({});
  const [selectedLimits,setSelectedLimits]=useState<Record<string,number>>({});
  const [tenders,setTenders]=useState<Tender[]>([{methodCode:"Cash",amount:"",bankAccountId:"",reference:"",cardFranchiseCode:"",approvalNumber:""}]);
  const pendingAttempt=useRef<{fingerprint:string;paymentId:string;paidAt:string;sessionId:string|null}|null>(null);
  const role=direction==="receivable"?"Customer":"Supplier";
  const partyId=direction==="receivable"?party?.customerId:party?.supplierId;
  useEffect(()=>{if(open){const party=initialPartyRef.current;const invoice=initialInvoiceRef.current;setStep(1);setParty(party??null);setInvoicePage(1);setSelected(invoice?{[invoice.id]:String(invoice.outstanding)}:{});setSelectedLimits(invoice?{[invoice.id]:invoice.outstanding}:{});setTenders([{methodCode:"Cash",amount:"",bankAccountId:"",reference:"",cardFranchiseCode:"",approvalNumber:""}]);}},[open]);
  const invoicesQuery=useQuery({queryKey:["portfolio-payment-invoices",direction,businessId,partyId,invoicePage],enabled:open&&!!partyId,queryFn:async()=>{
    if(direction==="receivable"){const value=edgeClient?await edgeClient.portfolioReceivables(partyId!,invoicePage,20):await receivablesApi.list({page:invoicePage,pageSize:20,customerId:partyId!,outstandingOnly:true});return {totalCount:value.totalCount,totalPages:value.totalPages,items:value.items.map<Invoice>(x=>({id:x.receivableId,number:x.documentNumber,dueDate:x.dueDate,outstanding:x.outstandingAmount,currency:x.currencyCode,overdue:x.isOverdue}))};}
    const value=edgeClient?await edgeClient.portfolioPayables(partyId!,invoicePage,20):await payablesApi.list({page:invoicePage,pageSize:20,supplierId:partyId!,outstandingOnly:true});return {totalCount:value.totalCount,totalPages:value.totalPages,items:value.items.map<Invoice>(x=>({id:x.payableId,number:x.documentNumber,dueDate:x.dueDate,outstanding:x.outstandingAmount,currency:x.currencyCode,overdue:x.isOverdue}))};
  }});
  const invoiceItems=useMemo(()=>{const items=invoicesQuery.data?.items??[];return initialInvoice&&partyId===(direction==="receivable"?initialParty?.customerId:initialParty?.supplierId)&&!items.some(item=>item.id===initialInvoice.id)?[initialInvoice,...items]:items;},[invoicesQuery.data,initialInvoice,initialParty,partyId,direction]);
  const configuration=useQuery({queryKey:["payment-settlement-configuration",edgeClient?"edge":"web",businessId],queryFn:()=>edgeClient?edgeClient.portfolioSettlementConfiguration():receivablesApi.settlementConfiguration(),enabled:open&&step===2&&!!businessId});
  const allocations=useMemo(()=>Object.entries(selected).map(([id,value])=>({id,amount:Number(value)})).filter(x=>Number.isFinite(x.amount)&&x.amount>0),[selected]);
  const total=allocations.reduce((sum,x)=>sum+x.amount,0);const tenderTotal=tenders.reduce((sum,x)=>sum+(Number(x.amount)||0),0);
  useEffect(()=>{if(step!==2)return;setTenders(current=>{if(allocations.length>1)return [{...current[0],amount:String(total)}];if(current.length===1&&!current[0].amount)return [{...current[0],amount:String(total)}];return current;});},[step,allocations.length,total]);
  const mutation=useMutation({mutationFn:async()=>{
    if(!businessId||!partyId)throw new Error("Falta el tercero.");
    const fingerprint=JSON.stringify({direction,businessId,partyId,workSessionId,allocations,tenders});
    let attempt=pendingAttempt.current;
    if(!attempt||attempt.fingerprint!==fingerprint){
      const sessionId=workSessionId===undefined?(direction==="receivable"?await receivablesApi.currentWorkSession(businessId):await payablesApi.currentWorkSession(businessId)).workSessionId:workSessionId;
      attempt={fingerprint,paymentId:crypto.randomUUID(),paidAt:new Date().toISOString(),sessionId:sessionId??null};
      pendingAttempt.current=attempt;
    }
    const {paymentId,paidAt,sessionId}=attempt;
    if(direction==="receivable"){const request={paymentId,businessId,customerId:partyId,workSessionId:sessionId,paidAt,currencyCode:"COP",notes:null,allocations:allocations.map(x=>({receivableId:x.id,amount:x.amount})),payments:tenders.map(toCustomerTender)};return edgeClient?edgeClient.confirmPortfolioReceivable(request,`receivable-payment-${paymentId}`):receivablesApi.confirmPayment(request,`receivable-payment-${paymentId}`);}
    const request={paymentId,businessId,supplierId:partyId,workSessionId:sessionId,paidAt,currencyCode:"COP",notes:null,allocations:allocations.map(x=>({payableId:x.id,amount:x.amount})),payments:tenders.map(toSupplierTender)};return edgeClient?edgeClient.confirmPortfolioPayable(request,`payable-payment-${paymentId}`):payablesApi.confirmPayment(request,`payable-payment-${paymentId}`);
  },onSuccess:accepted=>{pendingAttempt.current=null;toast.success(`${accepted.documentNumber} quedó registrado.`);onOpenChange(false);onCompleted?.();},onError:error=>toast.error(error instanceof Error?error.message:"No fue posible registrar el movimiento.")});
  const validateStepOne=()=>{if(!partyId||allocations.length===0){toast.error("Selecciona un tercero y al menos una factura.");return false;}if(allocations.some(x=>x.amount>(selectedLimits[x.id]??0))){toast.error("Ningún abono puede superar el saldo de la factura.");return false;}return true;};
  const canPay=total>0&&Number(tenderTotal.toFixed(4))===Number(total.toFixed(4))&&tenders.every(validTender)&&!(allocations.length>1&&tenders.length>1);
  const methods=direction==="receivable"?receivableMethods:payableMethods;
  return <Dialog open={open} onOpenChange={onOpenChange}><DialogContent className="flex max-h-[92dvh] w-[96vw] max-w-4xl flex-col overflow-hidden p-0">
    <DialogHeader className="border-b px-6 py-5"><DialogTitle>{direction==="receivable"?"Abono a cartera":"Pago a proveedores"}</DialogTitle><DialogDescription>Paso {step} de 2 · {step===1?"Selecciona las facturas y el valor de cada una.":"Confirma cómo se realiza el pago."}</DialogDescription></DialogHeader>
    <div className="overflow-y-auto p-6">{step===1?<div className="space-y-5"><div className="space-y-2"><Label>{role==="Customer"?"Cliente":"Proveedor"}</Label><PartyRoleSelect role={role} value={partyId??""} loadPage={edgeClient?(search,page,pageSize)=>edgeClient.portfolioParties(role,search,page,pageSize):undefined} selectedOption={party?{value:partyId!,label:party.displayName,description:party.identification}:null} placeholder={`Buscar por nombre o identificación`} onChange={(_,value)=>{setParty(value??null);setSelected({});setSelectedLimits({});setInvoicePage(1);}}/></div>
      {!partyId?<Empty text={`Busca y selecciona un ${role==="Customer"?"cliente":"proveedor"}.`}/>:invoicesQuery.isLoading?<Empty text="Cargando cartera..."/>:invoiceItems.length===0?<Empty text="Este tercero no tiene facturas pendientes."/>:<div className="space-y-2">{invoiceItems.map(invoice=>{const checked=selected[invoice.id]!==undefined;return <div key={invoice.id} className={`grid items-center gap-3 rounded-xl border p-4 sm:grid-cols-[auto_1fr_11rem] ${checked?"border-primary/50 bg-primary/5":""}`}><Checkbox checked={checked} onCheckedChange={value=>{setSelected(current=>{const next={...current};if(value)next[invoice.id]=String(invoice.outstanding);else delete next[invoice.id];return next;});setSelectedLimits(current=>{const next={...current};if(value)next[invoice.id]=invoice.outstanding;else delete next[invoice.id];return next;});}}/><div><div className="flex items-center gap-2"><b>{invoice.number}</b>{invoice.overdue&&<Badge variant="destructive">Vencida</Badge>}</div><p className="text-sm text-muted-foreground">Vence {formatDate(invoice.dueDate)} · Debe {formatCurrency(invoice.outstanding,invoice.currency)}</p></div><FormattedNumberInput kind="currency" disabled={!checked} value={selected[invoice.id]??""} onValueChange={value=>setSelected(current=>({...current,[invoice.id]:value?.toString()??""}))}/></div>})}</div>}
      {partyId&&invoicesQuery.data&&invoicesQuery.data.totalPages>1&&<div className="flex items-center justify-between text-sm text-muted-foreground"><span>{invoicesQuery.data.totalCount} facturas · {allocations.length} seleccionadas</span><div className="flex items-center gap-2"><Button type="button" variant="outline" size="sm" disabled={invoicePage<=1} onClick={()=>setInvoicePage(page=>page-1)}>Anterior</Button><span>{invoicePage} de {invoicesQuery.data.totalPages}</span><Button type="button" variant="outline" size="sm" disabled={invoicePage>=invoicesQuery.data.totalPages} onClick={()=>setInvoicePage(page=>page+1)}>Siguiente</Button></div></div>}
      <div className="flex justify-between rounded-xl bg-muted p-4"><span>Total seleccionado</span><b className="text-lg">{formatCurrency(total)}</b></div></div>:<div className="space-y-5"><div className="rounded-xl border bg-muted/30 p-4"><p className="text-sm text-muted-foreground">{party?.displayName} · {allocations.length} factura{allocations.length===1?"":"s"}</p><p className="text-2xl font-semibold">{formatCurrency(total)}</p>{allocations.length>1&&<p className="mt-1 text-xs text-muted-foreground">Para varias facturas se usa un solo medio de pago.</p>}</div>
      <div className="space-y-3">{tenders.map((tender,index)=><div key={index} className="grid gap-3 rounded-xl border p-4 md:grid-cols-2"><div className="space-y-2"><Label>Medio de pago</Label><Select value={tender.methodCode} onValueChange={value=>updateTender(index,{methodCode:value as CustomerPaymentMethod,bankAccountId:"",reference:"",cardFranchiseCode:"",approvalNumber:""})}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent>{methods.filter(([code])=>!tenders.some((x,i)=>i!==index&&x.methodCode===code)).map(([code,label])=><SelectItem key={code} value={code}>{label}</SelectItem>)}</SelectContent></Select></div><div className="space-y-2"><Label>Valor</Label><FormattedNumberInput kind="currency" value={tender.amount} onValueChange={value=>updateTender(index,{amount:value?.toString()??""})}/></div>
        {tender.methodCode==="BankTransfer"&&<><div className="space-y-2"><Label>Cuenta bancaria</Label><Select value={tender.bankAccountId} onValueChange={value=>updateTender(index,{bankAccountId:value})}><SelectTrigger><SelectValue placeholder="Selecciona una cuenta"/></SelectTrigger><SelectContent>{(configuration.data?.bankAccounts??[]).map(x=><SelectItem key={x.bankAccountId} value={x.bankAccountId}>{x.displayName}</SelectItem>)}</SelectContent></Select></div><Field label="Referencia" value={tender.reference} set={value=>updateTender(index,{reference:value})}/></>}
        {(tender.methodCode==="DebitCard"||tender.methodCode==="CreditCard")&&<><Field label="Franquicia" value={tender.cardFranchiseCode} set={value=>updateTender(index,{cardFranchiseCode:value})}/><Field label="Número de aprobación" value={tender.approvalNumber} set={value=>updateTender(index,{approvalNumber:value})}/></>}
        {tenders.length>1&&<Button className="md:col-span-2" type="button" variant="ghost" onClick={()=>setTenders(current=>current.filter((_,i)=>i!==index))}><Trash2 className="mr-2 h-4 w-4"/>Quitar medio</Button>}</div>)}</div>
      {allocations.length===1&&tenders.length<methods.length&&<Button type="button" variant="outline" onClick={()=>setTenders(current=>[...current,{methodCode:methods.find(([code])=>!current.some(x=>x.methodCode===code))![0],amount:"",bankAccountId:"",reference:"",cardFranchiseCode:"",approvalNumber:""}])}><Plus className="mr-2 h-4 w-4"/>Agregar otro medio de pago</Button>}
      <div className={`flex justify-between rounded-xl p-4 ${Math.abs(tenderTotal-total)<0.005?"bg-emerald-50 text-emerald-900":"bg-amber-50 text-amber-900"}`}><span>Total en medios</span><b>{formatCurrency(tenderTotal)} / {formatCurrency(total)}</b></div></div>}</div>
    <DialogFooter className="border-t px-6 py-4">{step===2&&<Button type="button" variant="outline" onClick={()=>setStep(1)}><ArrowLeft className="mr-2 h-4 w-4"/>Atrás</Button>}<Button type="button" variant="ghost" onClick={()=>onOpenChange(false)}>Cancelar</Button>{step===1?<Button type="button" onClick={()=>validateStepOne()&&setStep(2)}>Ir a pagar<ArrowRight className="ml-2 h-4 w-4"/></Button>:<Button type="button" disabled={!canPay||mutation.isPending} onClick={()=>mutation.mutate()}><CheckCircle2 className="mr-2 h-4 w-4"/>{mutation.isPending?"Registrando...":"Confirmar pago"}</Button>}</DialogFooter>
  </DialogContent></Dialog>;
  function updateTender(index:number,patch:Partial<Tender>){setTenders(current=>current.map((value,i)=>i===index?{...value,...patch}:value));}
}
function validTender(value:Tender){const amount=Number(value.amount);if(!Number.isFinite(amount)||amount<=0)return false;if(value.methodCode==="BankTransfer")return !!value.bankAccountId&&!!value.reference.trim();if(value.methodCode==="DebitCard"||value.methodCode==="CreditCard")return !!value.cardFranchiseCode.trim()&&!!value.approvalNumber.trim();return true;}
function toCustomerTender(value:Tender):CustomerPaymentTender{return {methodCode:value.methodCode,amount:Number(value.amount),tenderedAmount:value.methodCode==="Cash"?Number(value.amount):null,bankAccountId:value.methodCode==="BankTransfer"?value.bankAccountId:null,reference:value.reference.trim()||null,notes:null,cardFranchiseCode:value.cardFranchiseCode.trim()||null,approvalNumber:value.approvalNumber.trim()||null};}
function toSupplierTender(value:Tender):SupplierPaymentTender{return {methodCode:value.methodCode as "Cash"|"BankTransfer",amount:Number(value.amount),tenderedAmount:value.methodCode==="Cash"?Number(value.amount):null,bankAccountId:value.methodCode==="BankTransfer"?value.bankAccountId:null,reference:value.reference.trim()||null,notes:null};}
function Field({label,value,set}:{label:string;value:string;set:(value:string)=>void}){return <div className="space-y-2"><Label>{label}</Label><Input value={value} onChange={event=>set(event.target.value)}/></div>}
function Empty({text}:{text:string}){return <div className="rounded-xl border border-dashed py-12 text-center text-sm text-muted-foreground">{text}</div>}
