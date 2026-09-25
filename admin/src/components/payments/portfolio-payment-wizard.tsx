"use client";

import { useEffect,useMemo,useRef,useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { ArrowRight,CreditCard } from "lucide-react";
import { toast } from "sonner";
import { PartyRoleSelect,type PartyRoleSelection } from "@/components/parties/party-role-select";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Dialog,DialogContent,DialogDescription,DialogFooter,DialogHeader,DialogTitle } from "@/components/ui/dialog";
import { FormattedNumberInput } from "@/components/ui/formatted-number-input";
import { Label } from "@/components/ui/label";
import { payablesApi,type SupplierPaymentTender } from "@/services/api/payables";
import { partiesApi } from "@/services/api/parties";
import { referenceOptionsApi } from "@/services/api/reference-options";
import { receivablesApi,type CustomerPaymentMethod,type CustomerPaymentTender } from "@/services/api/receivables";
import { PosPaymentDialog } from "@/app/(pos)/pos/pos-payment-dialog";
import { workSessionsApi } from "@/services/api/work-sessions";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { useAuthStore } from "@/stores/auth-store";
import { formatCurrency,formatDate } from "@/lib/utils";
import type { PosEdgeClient,PosPaymentInput } from "@/services/pos/pos-edge-client";
import { printPortfolioPayment,type PortfolioPaymentReceipt } from "@/services/pos/pos-portfolio-payment-print";

type Direction="receivable"|"payable";
type Invoice={id:string;number:string;dueDate:string;outstanding:number;currency:string;overdue:boolean};
type InvoicePage={totalCount:number;totalPages:number;items:Invoice[]};
type InvoiceLoad={key:string;status:"loading"|"success"|"error";data:InvoicePage|null};
type PaymentAttempt={fingerprint:string;paymentId:string;paidAt:string;sessionId:string|null};
const toPortfolioMethod=(code:string):CustomerPaymentMethod|null=>
  code==="Transfer"?"BankTransfer":code==="Cash"||code==="DebitCard"||code==="CreditCard"?code:null;

export function PortfolioPaymentWizard({direction,open,onOpenChange,initialParty,initialInvoice,workSessionId,onCompleted,edgeClient,businessId:businessIdOverride,printOnPos=false,businessName:posBusinessName}:{direction:Direction;open:boolean;onOpenChange:(open:boolean)=>void;initialParty?:PartyRoleSelection|null;initialInvoice?:Invoice|null;workSessionId?:string|null;onCompleted?:()=>void;edgeClient?:PosEdgeClient|null;businessId?:string|null;printOnPos?:boolean;businessName?:string}){
  const initialPartyRef=useRef(initialParty);initialPartyRef.current=initialParty;
  const initialInvoiceRef=useRef(initialInvoice);initialInvoiceRef.current=initialInvoice;
  const selectedBusinessId=useBusinessContextStore(state=>state.selectedBusinessId);const businessId=businessIdOverride??selectedBusinessId;
  const user=useAuthStore(state=>state.user);const userId=user?.userId;
  const attemptStorageKey=`portfolio-payment:${direction}:${businessId}:${workSessionId??userId??"anonymous"}`;
  const [step,setStep]=useState<1|2>(1);const [party,setParty]=useState<PartyRoleSelection|null>(initialParty??null);
  const [invoicePage,setInvoicePage]=useState(1);
  const [selected,setSelected]=useState<Record<string,string>>({});
  const [selectedLimits,setSelectedLimits]=useState<Record<string,number>>({});
  const [selectedNumbers,setSelectedNumbers]=useState<Record<string,string>>({});
  const [invoiceLoad,setInvoiceLoad]=useState<InvoiceLoad|null>(null);
  const invoiceRequestEpoch=useRef(0);
  const pendingAttempt=useRef<PaymentAttempt|null>(null);
  const role=direction==="receivable"?"Customer":"Supplier";
  const partyId=direction==="receivable"?party?.customerId:party?.supplierId;
  useEffect(()=>{if(open){const party=initialPartyRef.current;setStep(1);setParty(party??null);setInvoicePage(1);setSelected({});setSelectedLimits({});setSelectedNumbers({});}},[open]);
  const invoiceKey=open&&partyId?JSON.stringify([edgeClient?"edge":"web",direction,businessId,partyId,invoicePage]):null;
  const currentInvoices=invoiceLoad?.key===invoiceKey?invoiceLoad:null;
  const invoicesQuery={data:currentInvoices?.data??null,isLoading:!!invoiceKey&&(!currentInvoices||currentInvoices.status==="loading"),isError:currentInvoices?.status==="error"};
  useEffect(()=>{
    if(!invoiceKey||!partyId)return;
    let active=true;
    const epoch=++invoiceRequestEpoch.current;
    const requested=initialInvoiceRef.current;
    const initialPartyId=direction==="receivable"?initialPartyRef.current?.customerId:initialPartyRef.current?.supplierId;
    setInvoiceLoad({key:invoiceKey,status:"loading",data:null});
    const load=async():Promise<InvoicePage>=>{
      let page:InvoicePage;
      if(direction==="receivable"){const value=edgeClient?await edgeClient.portfolioReceivables(partyId,invoicePage,20):await receivablesApi.list({page:invoicePage,pageSize:20,customerId:partyId,outstandingOnly:true});page={totalCount:value.totalCount,totalPages:value.totalPages,items:value.items.map<Invoice>(x=>({id:x.receivableId,number:x.documentNumber,dueDate:x.dueDate,outstanding:x.outstandingAmount,currency:x.currencyCode,overdue:x.isOverdue}))};}
      else{const value=edgeClient?await edgeClient.portfolioPayables(partyId,invoicePage,20):await payablesApi.list({page:invoicePage,pageSize:20,supplierId:partyId,outstandingOnly:true});page={totalCount:value.totalCount,totalPages:value.totalPages,items:value.items.map<Invoice>(x=>({id:x.payableId,number:x.documentNumber,dueDate:x.dueDate,outstanding:x.outstandingAmount,currency:x.currencyCode,overdue:x.isOverdue}))};}
      if(!edgeClient&&invoicePage===1&&requested&&initialPartyId===partyId&&!page.items.some(item=>item.id===requested.id)){
        if(direction==="receivable"){
          const detail=await receivablesApi.get(requested.id);
          if(detail.customerId===partyId&&detail.outstandingAmount>0&&detail.status!=="Cancelled")page.items.unshift({id:detail.receivableId,number:detail.documentNumber,dueDate:detail.dueDate,outstanding:detail.outstandingAmount,currency:detail.currencyCode,overdue:false});
        }else{
          const detail=await payablesApi.get(requested.id);
          if(detail.supplierId===partyId&&detail.outstandingAmount>0&&detail.status!=="Cancelled")page.items.unshift({id:detail.payableId,number:detail.documentNumber,dueDate:detail.dueDate,outstanding:detail.outstandingAmount,currency:detail.currencyCode,overdue:false});
        }
      }
      return page;
    };
    void load().then(data=>{if(active&&invoiceRequestEpoch.current===epoch)setInvoiceLoad({key:invoiceKey,status:"success",data});}).catch(()=>{if(active&&invoiceRequestEpoch.current===epoch)setInvoiceLoad({key:invoiceKey,status:"error",data:null});});
    return()=>{active=false};
  },[invoiceKey,partyId,invoicePage,direction,edgeClient]);
  const invoiceItems=invoicesQuery.data?.items??[];
  useEffect(()=>{
    if(!open||!invoicesQuery.data)return;
    const invoice=initialInvoiceRef.current;
    const chosen=invoice?invoicesQuery.data.items.find(item=>item.id===invoice.id):invoicesQuery.data.totalCount===1?invoicesQuery.data.items[0]:null;
    if(!chosen)return;
    setSelected({[chosen.id]:String(chosen.outstanding)});
    setSelectedLimits({[chosen.id]:chosen.outstanding});
    setSelectedNumbers({[chosen.id]:chosen.number});
  },[open,invoicesQuery.data]);
  const allocations=useMemo(()=>Object.entries(selected).map(([id,value])=>({id,amount:Number(value)})).filter(x=>Number.isFinite(x.amount)&&x.amount>0),[selected]);
  const total=allocations.reduce((sum,x)=>sum+x.amount,0);
  const paymentClient=useMemo(()=>({
    mode:edgeClient?"edge" as const:"online" as const,
    referenceOptions:(catalogCode:string)=>edgeClient?edgeClient.referenceOptions(catalogCode):referenceOptionsApi.list(catalogCode),
    settlementConfiguration:()=>edgeClient?edgeClient.portfolioSettlementConfiguration():receivablesApi.settlementConfiguration(),
  }),[edgeClient]);
  const mutation=useMutation({mutationFn:async(tenders:PosPaymentInput[])=>{
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
    const printedAllocations=allocations.map(x=>({documentNumber:selectedNumbers[x.id],amount:x.amount}));
    if(printOnPos&&printedAllocations.some(x=>!x.documentNumber))throw new Error("Falta el número de una factura seleccionada.");
    const printContext={paidAt,tenders,printedAllocations,partyName:party?.displayName??"",partyIdentification:party?.identification??"",total};
    if(direction==="receivable"){
      const request={paymentId,businessId,customerId:partyId,workSessionId:sessionId,paidAt,currencyCode:"COP",notes:null,allocations:allocations.map(x=>({receivableId:x.id,amount:x.amount})),payments:tenders.map(toCustomerTender)};
      const accepted=await(edgeClient?edgeClient.confirmPortfolioReceivable(request,`receivable-payment-${paymentId}`):receivablesApi.confirmPayment(request,`receivable-payment-${paymentId}`));
      return {accepted,printContext};
    }
    const request={paymentId,businessId,supplierId:partyId,workSessionId:sessionId,paidAt,currencyCode:"COP",notes:null,allocations:allocations.map(x=>({payableId:x.id,amount:x.amount})),payments:tenders.map(toSupplierTender)};
    const accepted=await(edgeClient?edgeClient.confirmPortfolioPayable(request,`payable-payment-${paymentId}`):payablesApi.confirmPayment(request,`payable-payment-${paymentId}`));
    return {accepted,printContext};
  },onSuccess:({accepted,printContext})=>{
    sessionStorage.removeItem(attemptStorageKey);
    pendingAttempt.current=null;
    toast.success(`${accepted.documentNumber} quedó registrado. El saldo se actualizará al aplicar el movimiento.`);
    changeOpen(false);
    onCompleted?.();
    if(!printOnPos||accepted.idempotentReplay||!businessId)return;
    const receipt:PortfolioPaymentReceipt={
      paymentId:accepted.paymentId,
      direction:direction==="receivable"?"Receivable":"Payable",
      documentNumber:accepted.documentNumber,
      paidAt:printContext.paidAt,
      companyName:posBusinessName??"",
      legalName:null,nit:null,verificationDigit:null,companyLogoSource:null,
      businessName:posBusinessName??"",businessAddress:null,businessPhone:null,
      partyName:printContext.partyName,partyIdentification:printContext.partyIdentification,
      responsibleName:[user?.firstName,user?.lastName].filter(Boolean).join(" ")||user?.username||"",
      totalAmount:printContext.total,
      allocations:printContext.printedAllocations,
      payments:printContext.tenders.map(value=>({
        methodName:paymentMethodName(value.methodCode),amount:value.amount,
        reference:value.reference?.trim()||null,
      })),
    };
    void printPortfolioPayment(receipt,businessId,edgeClient??null).catch(error=>
      toast.error(`El pago quedó registrado, pero no se pudo imprimir: ${error instanceof Error?error.message:"revisa la impresora."}`));
  },onError:error=>toast.error(error instanceof Error?error.message:"No fue posible registrar el movimiento. Si no recibiste confirmación, reintenta sin cambiar los valores.")});
  const validateStepOne=()=>{if(!partyId||allocations.length===0){toast.error("Selecciona un tercero y al menos una factura.");return false;}if(allocations.some(x=>x.amount>(selectedLimits[x.id]??0))){toast.error("Ningún abono puede superar el saldo de la factura.");return false;}return true;};
  if(open&&step===2)return <PosPaymentDialog
    client={paymentClient} total={total} grossTotal={total} withholdingTotal={0}
    busy={mutation.isPending} documentType="SalesReceipt" documentTypeLocked
    documentTypeReady customer={null} focusRequest={1}
    onChangeDocumentType={()=>{}} onCancel={()=>changeOpen(false)}
    onBack={()=>setStep(1)} portfolioDirection={direction}
    maxPaymentMethods={allocations.length>1?1:undefined}
    onConfirm={async payments=>{if(allocations.length>1&&payments.length>1){toast.error("Para varias facturas se usa un solo medio de pago.");return;}mutation.mutate(payments);}}
  />;
  return <Dialog open={open} onOpenChange={next=>{if(!next&&mutation.isPending)return;changeOpen(next)}}><DialogContent className="flex max-h-[92dvh] w-[96vw] max-w-4xl flex-col overflow-hidden p-0">
    <DialogHeader className="border-b border-slate-200 px-6 py-5 text-left"><DialogTitle className="flex items-center gap-2 text-xl"><CreditCard className="h-5 w-5 text-teal-700"/>{direction==="receivable"?"Abono a cartera":"Pago a proveedores"}</DialogTitle><DialogDescription>Selecciona las facturas y el valor de cada una.</DialogDescription></DialogHeader>
    <div className="min-h-0 overflow-y-auto p-6"><div className="space-y-5"><div className="space-y-2"><Label>{role==="Customer"?"Cliente":"Proveedor"}</Label><PartyRoleSelect role={role} value={partyId??""} sourceKey={edgeClient?"edge-portfolio":"web-portfolio"} preload={open} loadPage={(search,page,pageSize)=>edgeClient?edgeClient.portfolioParties(role,search,page,pageSize):partiesApi.portfolioRoleOptions({role,search,page,pageSize})} selectedOption={party?{value:partyId!,label:party.displayName,description:party.identification}:null} placeholder={`Buscar por nombre o identificación`} onChange={(_,value)=>{setParty(value??null);setSelected({});setSelectedLimits({});setSelectedNumbers({});setInvoicePage(1);}}/></div>
      {!partyId?<Empty text={`Busca y selecciona un ${role==="Customer"?"cliente":"proveedor"}.`}/>:invoicesQuery.isLoading?<Empty text="Cargando cartera..."/>:invoicesQuery.isError?<Empty text="No fue posible consultar la cartera. Comprueba la conexión e inténtalo nuevamente."/>:invoiceItems.length===0?<Empty text="Este tercero no tiene facturas pendientes."/>:<div className="space-y-2">{invoiceItems.map(invoice=>{const checked=selected[invoice.id]!==undefined;return <div key={invoice.id} className={`grid grid-cols-[auto_minmax(0,1fr)] items-center gap-3 rounded-xl border p-4 lg:grid-cols-[auto_minmax(0,1fr)_auto_11rem] ${checked?"border-primary/50 bg-primary/5":""}`}><Checkbox checked={checked} onCheckedChange={value=>{setSelected(current=>{const next={...current};if(value)next[invoice.id]=String(invoice.outstanding);else delete next[invoice.id];return next;});setSelectedLimits(current=>{const next={...current};if(value)next[invoice.id]=invoice.outstanding;else delete next[invoice.id];return next;});setSelectedNumbers(current=>{const next={...current};if(value)next[invoice.id]=invoice.number;else delete next[invoice.id];return next;});}}/><div className="min-w-0"><div className="flex items-center gap-2"><b className="truncate">{invoice.number}</b>{invoice.overdue&&<Badge variant="destructive">Vencida</Badge>}</div><p className="text-sm text-muted-foreground">Vence {formatDate(invoice.dueDate)}</p></div><span className="col-start-2 text-sm font-semibold lg:col-auto">Saldo {formatCurrency(invoice.outstanding,invoice.currency)}</span><div className="col-start-2 lg:col-auto"><FormattedNumberInput kind="currency" ariaLabel={`Abono para ${invoice.number}`} value={selected[invoice.id]??""} onValueChange={value=>{setSelected(current=>{const next={...current};if(value&&value>0)next[invoice.id]=String(value);else delete next[invoice.id];return next;});setSelectedLimits(current=>({...current,[invoice.id]:invoice.outstanding}));setSelectedNumbers(current=>({...current,[invoice.id]:invoice.number}));}}/></div></div>})}</div>}
      {partyId&&invoicesQuery.data&&invoicesQuery.data.totalPages>1&&<div className="flex items-center justify-between text-sm text-muted-foreground"><span>{invoicesQuery.data.totalCount} facturas · {allocations.length} seleccionadas</span><div className="flex items-center gap-2"><Button type="button" variant="outline" size="sm" disabled={invoicePage<=1} onClick={()=>setInvoicePage(page=>page-1)}>Anterior</Button><span>{invoicePage} de {invoicesQuery.data.totalPages}</span><Button type="button" variant="outline" size="sm" disabled={invoicePage>=invoicesQuery.data.totalPages} onClick={()=>setInvoicePage(page=>page+1)}>Siguiente</Button></div></div>}
      <div className="flex justify-between rounded-xl bg-muted p-4"><span>Total seleccionado</span><b className="text-lg">{formatCurrency(total)}</b></div></div></div>
    <DialogFooter className="border-t px-6 py-4"><Button type="button" variant="ghost" onClick={()=>changeOpen(false)}>Cancelar</Button><Button type="button" disabled={invoicesQuery.isError} onClick={()=>validateStepOne()&&setStep(2)}>Ir a pagar<ArrowRight className="ml-2 h-4 w-4"/></Button></DialogFooter>
  </DialogContent></Dialog>;
  function changeOpen(next:boolean){if(!next){invoiceRequestEpoch.current++;setParty(null);setSelected({});setSelectedLimits({});setSelectedNumbers({});setInvoiceLoad(null);setInvoicePage(1);setStep(1);}onOpenChange(next);}
}
function paymentMethodName(code:string){return code==="Cash"?"Efectivo":code==="Transfer"?"Transferencia":code==="DebitCard"?"Tarjeta débito":code==="CreditCard"?"Tarjeta crédito":code;}
function toCustomerTender(value:PosPaymentInput):CustomerPaymentTender{const methodCode=toPortfolioMethod(value.methodCode);if(!methodCode)throw new Error("Medio de pago no admitido para cartera.");return {methodCode,amount:value.amount,tenderedAmount:value.methodCode==="Cash"?value.tenderedAmount??value.amount:null,bankAccountId:value.methodCode==="Transfer"?value.bankAccountId??null:null,reference:value.reference?.trim()||null,notes:value.notes?.trim()||null,cardFranchiseCode:value.cardFranchiseCode?.trim()||null,approvalNumber:value.approvalNumber?.trim()||null};}
function toSupplierTender(value:PosPaymentInput):SupplierPaymentTender{const methodCode=toPortfolioMethod(value.methodCode);if(!methodCode)throw new Error("Medio de pago no admitido para proveedores.");return {methodCode,amount:value.amount,tenderedAmount:value.methodCode==="Cash"?value.tenderedAmount??value.amount:null,bankAccountId:value.methodCode==="Transfer"?value.bankAccountId??null:null,reference:value.reference?.trim()||null,notes:value.notes?.trim()||null,cardFranchiseCode:value.cardFranchiseCode?.trim()||null,approvalNumber:value.approvalNumber?.trim()||null};}
function Empty({text}:{text:string}){return <div className="rounded-xl border border-dashed py-12 text-center text-sm text-muted-foreground">{text}</div>}
