"use client";

import { useEffect,useMemo,useRef,useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { ArrowRight,CreditCard } from "lucide-react";
import { toast } from "sonner";
import { PartyRoleSelect,type PartyRoleSelection } from "@/components/parties/party-role-select";
import { SupplierSiteSelect } from "@/components/parties/supplier-site-select";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Dialog,DialogContent,DialogDescription,DialogFooter,DialogHeader,DialogTitle } from "@/components/ui/dialog";
import { FormattedNumberInput } from "@/components/ui/formatted-number-input";
import { DatePicker } from "@/components/ui/date-picker";
import { Label } from "@/components/ui/label";
import { Input } from "@/components/ui/input";
import { Select,SelectContent,SelectItem,SelectTrigger,SelectValue } from "@/components/ui/select";
import { payablesApi,type SupplierPaymentTender } from "@/services/api/payables";
import { accountingApi } from "@/services/api/accounting";
import { partiesApi } from "@/services/api/parties";
import { referenceOptionsApi } from "@/services/api/reference-options";
import { receivablesApi,type CustomerPaymentMethod,type CustomerPaymentTender } from "@/services/api/receivables";
import { PosPaymentDialog } from "@/app/(pos)/pos/pos-payment-dialog";
import { workSessionsApi } from "@/services/api/work-sessions";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { useBusinessDates } from "@/hooks/use-business-dates";
import { useTenantContextStore } from "@/stores/tenant-context-store";
import { useAuthStore } from "@/stores/auth-store";
import { formatCurrency, formatDate } from "@/lib/utils";
import { accountingDocumentTypeLabel } from "@/lib/accounting-labels";
import { portfolioAgingBandLabel, portfolioReportDate } from "@/lib/portfolio-report-export";
import { PosEdgeClient,PosEdgeError,loadBrowserPrinterConfiguration,readEdgeTokenFromLaunch,readEdgeUserSession,type PosPaymentInput,type PosPrintTemplateFormat } from "@/services/pos/pos-edge-client";
import { printPortfolioPayment,type PortfolioPaymentReceipt } from "@/services/pos/pos-portfolio-payment-print";

type Direction="receivable"|"payable";
type Invoice={id:string;number:string;dueDate:string;outstanding:number;currency:string;overdue:boolean;
  original?:number;paid?:number;adjustments?:number;overdueDays?:number;issuedAt?:string|null;agingBandCode?:string;sourceDocumentType?:string|null;dueOn?:string|null;issuedOn?:string|null;
  financialDiscountEligibleAmount?:number|null;financialDiscountAnticipated?:boolean|null;financialDiscountPendingReturn?:boolean};
type DiscountDecision="earned"|"lost";
type InvoiceTotals={currency:string;original?:number|null;paid?:number|null;adjustments?:number|null;outstanding:number;overdue:number};
type InvoicePage={totalCount:number;totalPages:number;items:Invoice[];totals?:InvoiceTotals[];referenceDate?:string|null};
type InvoiceLoad={key:string;status:"loading"|"success"|"error";data:InvoicePage|null};
type PaymentAttempt={fingerprint:string;paymentId:string;paidAt:string;sessionId:string|null;paidOn?:string};
type InvoiceFilters={search:string;from:string;to:string;overdue?:boolean};
const toPortfolioMethod=(code:string):CustomerPaymentMethod|null=>
  code==="Transfer"||code==="Cash"||code==="DebitCard"||code==="CreditCard"?code:null;

export function PortfolioPaymentWizard({direction,open,onOpenChange,initialParty,initialInvoice,initialPartySiteId,initialPartySiteName,workSessionId,onCompleted,edgeClient,receiptPrinter,businessId:businessIdOverride,printOnPos=false,businessName:posBusinessName}:{direction:Direction;open:boolean;onOpenChange:(open:boolean)=>void;initialParty?:PartyRoleSelection|null;initialInvoice?:Invoice|null;initialPartySiteId?:string;initialPartySiteName?:string;workSessionId?:string|null;onCompleted?:()=>void;edgeClient?:PosEdgeClient|null;receiptPrinter?:Pick<PosEdgeClient,"printPortfolioPayment"|"printerConfigurationForSale">|null;businessId?:string|null;printOnPos?:boolean;businessName?:string}){
  const initialPartyRef=useRef(initialParty);initialPartyRef.current=initialParty;
  const initialInvoiceRef=useRef(initialInvoice);initialInvoiceRef.current=initialInvoice;
  const initialPartySiteIdRef=useRef(initialPartySiteId);initialPartySiteIdRef.current=initialPartySiteId;
  const initialPartySiteNameRef=useRef(initialPartySiteName);initialPartySiteNameRef.current=initialPartySiteName;
  const selectedBusinessId=useBusinessContextStore(state=>state.selectedBusinessId);const businessId=businessIdOverride??selectedBusinessId;
  const { timeZone, today, formatDate: formatBusinessDate } = useBusinessDates(businessId);
  const localToday = open && !printOnPos ? today() : undefined;
  const user=useAuthStore(state=>state.user);const userId=user?.userId;
  const tenantId=useTenantContextStore(state=>state.selectedTenantId);
  const queryClient=useQueryClient();
  const attemptStorageKey=`portfolio-payment:${direction}:${businessId}:${workSessionId??userId??"anonymous"}`;
  const [paymentDate,setPaymentDate]=useState("");
  const [step,setStep]=useState<1|2>(1);const [party,setParty]=useState<PartyRoleSelection|null>(initialParty??null);
  const [partySiteId,setPartySiteId]=useState<string|undefined>(initialPartySiteId);
  const [partySiteName,setPartySiteName]=useState<string|undefined>(initialPartySiteName);
  const [invoicePage,setInvoicePage]=useState(1);
  const [invoiceIdFilter,setInvoiceIdFilter]=useState<string|null>(initialInvoice?.id??null);
  const [filterDraft,setFilterDraft]=useState<InvoiceFilters>({search:"",from:"",to:""});
  const [filters,setFilters]=useState<InvoiceFilters>({search:"",from:"",to:""});
  const filterTimer=useRef<ReturnType<typeof setTimeout>|null>(null);
  useEffect(()=>()=>{if(filterTimer.current)clearTimeout(filterTimer.current);},[]);
  const invalidFilterDates=!!(filterDraft.from&&filterDraft.to&&filterDraft.from>filterDraft.to);
  const filtersPending=JSON.stringify(filterDraft)!==JSON.stringify(filters);
  function changeFilters(next:InvoiceFilters,defer=false){
    if(filterTimer.current)clearTimeout(filterTimer.current);
    setFilterDraft(next);
    const apply=()=>{if(next.from&&next.to&&next.from>next.to)return;setInvoicePage(1);setFilters(current=>JSON.stringify(current)===JSON.stringify(next)?current:next);};
    if(defer)filterTimer.current=setTimeout(apply,250);else apply();
  }
  const initialSelectionApplied=useRef(false);
  const [selected,setSelected]=useState<Record<string,string>>({});
  const [selectedLimits,setSelectedLimits]=useState<Record<string,number>>({});
  const [selectedNumbers,setSelectedNumbers]=useState<Record<string,string>>({});
  const [discountDecisions,setDiscountDecisions]=useState<Record<string,DiscountDecision>>({});
  const [invoiceLoad,setInvoiceLoad]=useState<InvoiceLoad|null>(null);
  const invoiceRequestEpoch=useRef(0);
  const pendingAttempt=useRef<PaymentAttempt|null>(null);
  const role=direction==="receivable"?"Customer":"Supplier";
  const installedPrinter=useMemo(()=>{if(!open||receiptPrinter)return null;const token=readEdgeTokenFromLaunch();return token?new PosEdgeClient(token,readEdgeUserSession()):null;},[open,receiptPrinter]);
  const printClient=receiptPrinter??installedPrinter;
  const partyId=direction==="receivable"?party?.customerId:party?.supplierId;
  useEffect(()=>{if(filterTimer.current)clearTimeout(filterTimer.current);if(open){const party=initialPartyRef.current;setPaymentDate("");setStep(1);setParty(party??null);setPartySiteId(initialPartySiteIdRef.current);setPartySiteName(initialPartySiteNameRef.current);setInvoicePage(1);setSelected({});setSelectedLimits({});setSelectedNumbers({});setDiscountDecisions({});initialSelectionApplied.current=false;setInvoiceIdFilter(initialInvoiceRef.current?.id??null);setFilters({search:"",from:"",to:""});setFilterDraft({search:"",from:"",to:""});}},[open]);
  const requiresSupplierSite=direction==="payable"&&!edgeClient&&!printOnPos;
  const administrativeFilters=printOnPos?null:filters;
  const administrativeInvoiceId=printOnPos?null:invoiceIdFilter;
  const invoiceKey=open&&partyId&&(!requiresSupplierSite||partySiteId)?JSON.stringify([edgeClient?"edge":printOnPos?"web-pos":"web",direction,businessId,partyId,partySiteId,invoicePage,administrativeInvoiceId,administrativeFilters,printOnPos?null:timeZone]):null;
  const currentInvoices=invoiceLoad?.key===invoiceKey?invoiceLoad:null;
  const invoicesQuery={data:currentInvoices?.data??null,isLoading:!!invoiceKey&&(!currentInvoices||currentInvoices.status==="loading"),isError:currentInvoices?.status==="error"};
  useEffect(()=>{
    if(!invoiceKey||!partyId)return;
    let active=true;
    const controller=new AbortController();
    const epoch=++invoiceRequestEpoch.current;
    setInvoiceLoad({key:invoiceKey,status:"loading",data:null});
    const load=async():Promise<InvoicePage>=>{
      let page:InvoicePage;
      if(direction==="receivable"){const value=edgeClient?await edgeClient.portfolioReceivables(partyId,invoicePage,20):printOnPos?await receivablesApi.posList({page:invoicePage,pageSize:20,customerId:partyId,outstandingOnly:true}):await receivablesApi.list({page:invoicePage,pageSize:20,customerId:partyId,partySiteId,outstandingOnly:true,search:administrativeFilters?.search||undefined,from:administrativeFilters?.from||undefined,to:administrativeFilters?.to||undefined,overdue:administrativeFilters?.overdue,receivableId:administrativeInvoiceId??undefined},controller.signal);page={referenceDate:value.referenceDate,totals:[{currency:"COP",original:value.totalOriginal,paid:value.totalPaid,adjustments:value.totalAdjustments,outstanding:value.totalOutstanding,overdue:value.totalOverdue}],totalCount:value.totalCount,totalPages:value.totalPages,items:value.items.map<Invoice>(x=>({dueOn:x.dueOn,issuedOn:x.issuedOn,sourceDocumentType:x.sourceDocumentType,original:x.originalAmount,paid:x.paidAmount,adjustments:x.adjustmentAmount,overdueDays:x.overdueDays,issuedAt:x.issuedAt,agingBandCode:x.agingBandCode,id:x.receivableId,number:x.documentNumber,dueDate:x.dueDate,outstanding:x.outstandingAmount,currency:x.currencyCode,overdue:x.isOverdue}))};}
      else{const value=edgeClient?await edgeClient.portfolioPayables(partyId,invoicePage,20):printOnPos?await payablesApi.posList({page:invoicePage,pageSize:20,supplierId:partyId,outstandingOnly:true}):await payablesApi.list({page:invoicePage,pageSize:20,supplierId:partyId,partySiteId,outstandingOnly:true,search:administrativeFilters?.search||undefined,from:administrativeFilters?.from||undefined,to:administrativeFilters?.to||undefined,overdue:administrativeFilters?.overdue,payableId:administrativeInvoiceId??undefined},controller.signal);page={referenceDate:value.referenceDate,totals:value.currencyTotals.map(total=>({currency:total.currencyCode,original:total.originalAmount,paid:total.paidAmount,adjustments:total.adjustmentAmount,outstanding:total.outstandingAmount,overdue:total.overdueAmount})),totalCount:value.totalCount,totalPages:value.totalPages,items:value.items.map<Invoice>(x=>({dueOn:x.dueOn,issuedOn:x.issuedOn,sourceDocumentType:x.sourceDocumentType,original:x.originalAmount,paid:x.paidAmount,adjustments:x.adjustmentAmount,overdueDays:x.overdueDays,issuedAt:x.issuedAt,agingBandCode:x.agingBandCode,id:x.payableId,number:x.documentNumber,dueDate:x.dueDate,outstanding:x.outstandingAmount,currency:x.currencyCode,overdue:x.isOverdue,financialDiscountEligibleAmount:"financialDiscountEligibleAmount" in x?x.financialDiscountEligibleAmount:null,financialDiscountAnticipated:"financialDiscountAnticipated" in x?x.financialDiscountAnticipated:null,financialDiscountPendingReturn:"financialDiscountPendingReturn" in x?x.financialDiscountPendingReturn:false}))};}
      return page;
    };
    void load().then(data=>{if(active&&invoiceRequestEpoch.current===epoch){setInvoiceLoad({key:invoiceKey,status:"success",data});if(!printOnPos)setPaymentDate(current=>current||data.referenceDate||"");}}).catch(()=>{if(active&&invoiceRequestEpoch.current===epoch)setInvoiceLoad({key:invoiceKey,status:"error",data:null});});
    return()=>{active=false;controller.abort();};
  },[invoiceKey,partyId,partySiteId,invoicePage,direction,edgeClient,printOnPos,administrativeFilters,administrativeInvoiceId]);
  const invoiceItems=invoicesQuery.data?.items??[];
  useEffect(()=>{
    const page=invoicesQuery.data;
    if(!open||printOnPos||!page)return;
    setSelectedLimits(current=>{
      let updated=current;
      for(const invoice of page.items){
        if(current[invoice.id]===undefined)continue;
        const limit=invoicePaymentLimit(invoice,discountDecisions[invoice.id]);
        if(limit!==current[invoice.id]){if(updated===current)updated={...current};updated[invoice.id]=limit;}
      }
      return updated;
    });
  },[open,printOnPos,invoicesQuery.data,discountDecisions]);
  useEffect(()=>{
    if(!open||!invoicesQuery.data||!printOnPos&&initialSelectionApplied.current)return;
    if(!printOnPos)initialSelectionApplied.current=true;
    const invoice=initialInvoiceRef.current;
    const chosen=invoice?invoicesQuery.data.items.find(item=>item.id===invoice.id):invoicesQuery.data.totalCount===1?invoicesQuery.data.items[0]:null;
    if(!chosen||!printOnPos&&chosen.currency!=="COP")return;
    setSelected({[chosen.id]:String(chosen.outstanding)});
    setSelectedLimits({[chosen.id]:chosen.outstanding});
    setSelectedNumbers({[chosen.id]:chosen.number});
  },[open,invoicesQuery.data,printOnPos]);
  const allocations=useMemo(()=>Object.entries(selected).map(([id,value])=>({id,amount:Number(value)})).filter(x=>Number.isFinite(x.amount)&&x.amount>0),[selected]);
  const total=allocations.reduce((sum,x)=>sum+x.amount,0);
  const chooseDiscount=(invoice:Invoice,decision:DiscountDecision|null)=>{
    const eligible=invoice.financialDiscountEligibleAmount??0;
    if(decision&&(!eligible||invoice.financialDiscountPendingReturn))return;
    const limit=invoicePaymentLimit(invoice,decision??undefined);
    if(limit<=0){toast.error("El saldo restante no permite aplicar este descuento en el pago.");return;}
    setDiscountDecisions(current=>{const next={...current};if(decision)next[invoice.id]=decision;else delete next[invoice.id];return next;});
    setSelected(current=>{if(current[invoice.id]===undefined)return current;const previous=Number(current[invoice.id]);return {...current,[invoice.id]:String(previous===(selectedLimits[invoice.id]??invoice.outstanding)?limit:Math.min(previous,limit))};});
    setSelectedLimits(current=>({...current,[invoice.id]:limit}));
  };
  const paymentClient=useMemo(()=>({
    mode:edgeClient?"edge" as const:"online" as const,
    settlementScope:edgeClient||printOnPos?"identity" as const:"execution" as const,
    referenceOptions:(catalogCode:string)=>edgeClient?edgeClient.referenceOptions(catalogCode):referenceOptionsApi.list(catalogCode),
    settlementConfiguration:()=>edgeClient?edgeClient.portfolioSettlementConfiguration():printOnPos?receivablesApi.settlementConfiguration():accountingApi.settlementConfiguration(),
    printerConfigurationForSale:()=>printClient?printClient.printerConfigurationForSale():Promise.resolve({configuration:loadBrowserPrinterConfiguration(),direct:false}),
  }),[edgeClient,printClient,printOnPos]);
  const mutation=useMutation({mutationFn:async({tenders,printChoice,methodLabels}:{tenders:PosPaymentInput[];printChoice:PosPrintTemplateFormat|"none"|null;methodLabels:Readonly<Record<string,string>>})=>{
    if(!businessId||!partyId)throw new Error("Falta el tercero.");
    const previousFingerprint=JSON.stringify({direction,businessId,partyId,workSessionId,allocations,discountDecisions,tenders});
    const fingerprint=JSON.stringify({direction,businessId,partyId,workSessionId,allocations,discountDecisions,tenders,paidOn:printOnPos?undefined:paymentDate});
    let attempt=pendingAttempt.current;
    if(!attempt){const saved=sessionStorage.getItem(attemptStorageKey);if(saved){const parsed=JSON.parse(saved) as PaymentAttempt;if((parsed?.fingerprint===fingerprint||parsed?.fingerprint===previousFingerprint)&&parsed.paymentId&&parsed.paidAt)attempt=parsed;}}
    if(!attempt||(attempt.fingerprint!==fingerprint&&attempt.fingerprint!==previousFingerprint)){
      const sessionId=workSessionId===undefined?(await workSessionsApi.currentOrOpen(businessId)).workSessionId:workSessionId;
      attempt={fingerprint,paymentId:crypto.randomUUID(),paidAt:new Date().toISOString(),sessionId:sessionId??null,paidOn:printOnPos?undefined:paymentDate};
    }
    sessionStorage.setItem(attemptStorageKey,JSON.stringify(attempt));
    pendingAttempt.current=attempt;
    const {paymentId,paidAt,sessionId}=attempt;
    // An uncertain command created before date selection was introduced must replay
    // its original payload, including the absence of PaidOn, with the same identity.
    const paidOn=attempt.fingerprint===previousFingerprint?undefined:attempt.paidOn??paymentDate;
    const printedAllocations=allocations.map(x=>({documentNumber:selectedNumbers[x.id],amount:x.amount}));
    if(printOnPos&&printedAllocations.some(x=>!x.documentNumber))throw new Error("Falta el número de una factura seleccionada.");
    const printContext={paidAt,tenders,printedAllocations,partyName:party?.displayName??"",partyIdentification:party?.identification??"",total,printChoice,methodLabels,
      traceabilityContext:!edgeClient&&tenantId?`${tenantId}:${businessId}`:null};
    if(direction==="receivable"){
      const request={paymentId,businessId,customerId:partyId,workSessionId:sessionId,paidAt,paidOn,currencyCode:"COP",notes:null,allocations:allocations.map(x=>({receivableId:x.id,amount:x.amount})),payments:tenders.map(toCustomerTender)};
      const accepted=await(edgeClient?edgeClient.confirmPortfolioReceivable(request,`receivable-payment-${paymentId}`):printOnPos?receivablesApi.confirmPosPayment(request,`receivable-payment-${paymentId}`):receivablesApi.confirmPayment(request,`receivable-payment-${paymentId}`));
      return {accepted,printContext};
    }
    const request={paymentId,businessId,supplierId:partyId,workSessionId:sessionId,paidAt,paidOn,currencyCode:"COP",notes:null,allocations:allocations.map(x=>({payableId:x.id,amount:x.amount,financialDiscountEarned:discountDecisions[x.id]==="earned"?true:discountDecisions[x.id]==="lost"?false:null})),payments:tenders.map(toSupplierTender)};
    const accepted=await(edgeClient?edgeClient.confirmPortfolioPayable(request,`payable-payment-${paymentId}`):printOnPos?payablesApi.confirmPosPayment(request,`payable-payment-${paymentId}`):payablesApi.confirmPayment(request,`payable-payment-${paymentId}`));
    return {accepted,printContext};
  },onSuccess:({accepted,printContext})=>{
    if(printContext.traceabilityContext)void queryClient.invalidateQueries({queryKey:["financial-traceability",printContext.traceabilityContext],refetchType:"none"});
    sessionStorage.removeItem(attemptStorageKey);
    pendingAttempt.current=null;
    toast.success(`${direction==="receivable"?"Pago de cliente":"Pago a proveedor"} registrado: ${accepted.documentNumber}.`);
    changeOpen(false);
    onCompleted?.();
    if(accepted.idempotentReplay||!businessId||printContext.printChoice==="none")return;
    const receipt:PortfolioPaymentReceipt={
      paymentId:accepted.paymentId,
      direction:direction==="receivable"?"Receivable":"Payable",
      documentNumber:accepted.documentNumber,
      status:accepted.status,
      paidAt:accepted.paidAt??printContext.paidAt,
      companyName:posBusinessName??"",
      legalName:null,nit:null,verificationDigit:null,companyLogoSource:null,
      businessName:posBusinessName??"",businessAddress:null,businessPhone:null,
      partyName:printContext.partyName,partyIdentification:printContext.partyIdentification,
      responsibleName:[user?.firstName,user?.lastName].filter(Boolean).join(" ")||user?.username||"",
      totalAmount:printContext.total,
      allocations:printContext.printedAllocations,
      payments:printContext.tenders.map(value=>({
        methodName:value.paymentMethodName??printContext.methodLabels[value.methodCode]??value.methodCode,amount:value.amount,
        reference:value.reference?.trim()||null,
      })),
    };
    void printPortfolioPayment(receipt,businessId,printClient,printContext.printChoice??"Receipt").catch(error=>
      toast.error(`El pago quedó registrado, pero no se pudo imprimir: ${error instanceof Error?error.message:"revisa la impresora."}`));
  }});
  const validateStepOne=()=>{if(!partyId||allocations.length===0){toast.error("Selecciona un tercero y al menos una factura.");return false;}if(requiresSupplierSite&&!partySiteId){toast.error("Selecciona la sede del proveedor.");return false;}if(allocations.some(x=>x.amount>(selectedLimits[x.id]??0))){toast.error("Ningún abono puede superar el saldo de la factura.");return false;}return true;};
  if(open&&step===2)return <PosPaymentDialog
    client={paymentClient} total={total} grossTotal={total} withholdingTotal={0}
    busy={mutation.isPending} documentType="SalesReceipt" documentTypeLocked
    documentTypeReady customer={null} focusRequest={1}
    onChangeDocumentType={()=>{}} onCancel={()=>changeOpen(false)}
    onBack={()=>setStep(1)} portfolioDirection={direction}
    portfolioPaymentDate={printOnPos?undefined:{value:paymentDate,today:localToday,onChange:setPaymentDate}}
    submissionError={mutation.error?paymentSubmissionError(mutation.error):null}
    maxPaymentMethods={allocations.length>1?1:undefined}
    onConfirm={async (payments,_,printChoice,methodLabels)=>{if(allocations.length>1&&payments.length>1&&!payments.every(payment=>payment.paymentMethodId)){toast.error("Para varias facturas se usa un solo medio de pago.");return;}mutation.mutate({tenders:payments,printChoice,methodLabels});}}
  />;
  return <Dialog open={open} onOpenChange={next=>{if(!next&&mutation.isPending)return;changeOpen(next)}}><DialogContent className={`flex max-h-[92dvh] w-[96vw] ${printOnPos?"max-w-4xl":"max-w-7xl"} flex-col overflow-hidden p-0`}>
    <DialogHeader className={`border-b border-slate-200 text-left ${printOnPos?"px-6 py-5":"px-4 py-3"}`}><DialogTitle className="flex items-center gap-2 text-xl"><CreditCard className="h-5 w-5 text-teal-700"/>{direction==="receivable"?"Abono a cartera":"Pago a proveedores"}</DialogTitle><DialogDescription>Selecciona las facturas y el valor de cada una.</DialogDescription></DialogHeader>
    <div className={`min-h-0 overflow-y-auto ${printOnPos?"p-6":"p-4"}`}><div className={printOnPos?"space-y-5":"space-y-3"}><div className={printOnPos?"":"min-w-0"}><div className="space-y-2"><Label>{requiresSupplierSite?"Proveedor · sede":role==="Customer"?"Cliente":"Proveedor"}</Label>{requiresSupplierSite
      ? <SupplierSiteSelect value={partySiteId??""} preload={open}
          selectedOption={partySiteId&&partySiteName&&party?{value:partySiteId,label:`${party.displayName} · ${partySiteName}`,description:party.identification}:null}
          sourceKey="payment-supplier-sites" onChange={(siteId,supplier)=>{setParty(supplier??null);setPartySiteId(siteId||undefined);setPartySiteName(supplier?.siteName);resetInvoiceSelection();}}/>
          : <PartyRoleSelect role={role} value={partyId??""} sourceKey={edgeClient?"edge-portfolio":printOnPos?"web-pos-portfolio":"web-portfolio"} preload={open} loadPage={(search,page,pageSize)=>edgeClient?edgeClient.portfolioParties(role,search,page,pageSize):printOnPos?partiesApi.posPortfolioRoleOptions({role,search,page,pageSize}):partiesApi.portfolioRoleOptions({role,search,page,pageSize})} selectedOption={party?{value:partyId!,label:party.displayName,description:party.identification}:null} placeholder={`Buscar por nombre o identificación`} onChange={(_,value)=>{setParty(value??null);setPartySiteId(undefined);setPartySiteName(undefined);resetInvoiceSelection();}}/>}</div>
      </div>
      {!printOnPos && <div role="group" aria-label="Filtros de facturas para pago" className="grid gap-3 rounded-xl border p-3 sm:grid-cols-2 lg:grid-cols-4">
        <div className="space-y-1"><Label htmlFor="portfolio-invoice-search">Documento o referencia</Label><Input id="portfolio-invoice-search" maxLength={120} value={filterDraft.search} onChange={event=>changeFilters({...filterDraft,search:event.target.value},true)}/></div>
        <div className="space-y-1"><Label htmlFor="portfolio-invoice-from">Registro desde</Label><DatePicker today={localToday} id="portfolio-invoice-from" value={filterDraft.from} onChange={from=>changeFilters({...filterDraft,from})}/></div>
        <div className="space-y-1"><Label htmlFor="portfolio-invoice-to">Registro hasta</Label><DatePicker today={localToday} id="portfolio-invoice-to" value={filterDraft.to} onChange={to=>changeFilters({...filterDraft,to})}/></div>
        <div className="flex flex-wrap items-end gap-3"><Label className="flex items-center gap-2"><Checkbox checked={filterDraft.overdue===true} onCheckedChange={value=>changeFilters({...filterDraft,overdue:value===true?true:undefined})}/>Sólo vencidas</Label></div>
        {invoiceIdFilter&&<div className="flex flex-wrap items-center justify-between gap-2 text-sm sm:col-span-2 lg:col-span-4"><span>Documento seleccionado: {initialInvoiceRef.current?.number}</span><Button type="button" variant="outline" onClick={()=>{setInvoiceIdFilter(null);setInvoicePage(1);}}>Mostrar todas las facturas</Button></div>}
        <p className="text-xs text-muted-foreground sm:col-span-2 lg:col-span-4">Los totales corresponden al filtro completo. Las selecciones se conservan entre páginas y filtros.</p>
        {invalidFilterDates&&<p role="alert" className="text-sm text-destructive sm:col-span-2 lg:col-span-4">La fecha inicial no puede ser posterior a la final.</p>}
      </div>}
      {!printOnPos&&invoicesQuery.isError&&<Button type="button" variant="outline" onClick={()=>setFilters(current=>({...current}))}>Reintentar cartera</Button>}
      {!printOnPos && invoicesQuery.data?.totals?.map(summary=><div key={summary.currency} className="grid gap-2 rounded-xl border bg-muted/30 p-3 text-sm sm:grid-cols-5"><div><span className="block text-muted-foreground">Original · {summary.currency}</span><b>{summary.original==null?"—":formatCurrency(summary.original,summary.currency,4)}</b></div><div><span className="block text-muted-foreground">{direction==="receivable"?"Abonado":"Pagado"}</span><b>{summary.paid==null?"—":formatCurrency(summary.paid,summary.currency,4)}</b></div><div><span className="block text-muted-foreground">Ajustes netos</span><b>{summary.adjustments==null?"—":formatCurrency(summary.adjustments,summary.currency,4)}</b></div><div><span className="block text-muted-foreground">Pendiente</span><b>{formatCurrency(summary.outstanding,summary.currency,4)}</b></div><div><span className="block text-muted-foreground">Vencido</span><b>{formatCurrency(summary.overdue,summary.currency,4)}</b></div></div>)}
      {!printOnPos && invoicesQuery.data?.referenceDate && <p className="text-xs text-muted-foreground">Saldos del filtro completo · edades al {invoicesQuery.data.referenceDate}</p>}
      {!partyId?<Empty text={`Busca y selecciona un ${role==="Customer"?"cliente":"proveedor"}.`}/>:requiresSupplierSite&&!partySiteId?<Empty text="Selecciona la sede del proveedor para consultar sus facturas pendientes."/>:invoicesQuery.isLoading?<Empty text="Cargando cartera..."/>:invoicesQuery.isError?<Empty text="No fue posible consultar la cartera. Comprueba la conexión e inténtalo nuevamente."/>:invoiceItems.length===0?<Empty text={printOnPos?"Este tercero no tiene facturas pendientes.":"No hay facturas pendientes que coincidan con los filtros."}/>:<div className={printOnPos?"overflow-x-auto":"min-w-0"}><div className="space-y-2">{!printOnPos && <div className="hidden lg:grid-cols-[1.25rem_minmax(0,1.65fr)_minmax(0,.8fr)_repeat(4,minmax(0,1fr))_minmax(0,1.15fr)] gap-2 px-3 text-xs font-semibold text-muted-foreground lg:grid"><span/><span>Documento / fechas</span><span>Mora / edad</span><span className="text-right">Original</span><span className="text-right">{direction==="receivable"?"Abonado":"Pagado"}</span><span className="text-right">Ajustes</span><span className="text-right">Saldo</span><span>Aplicar</span></div>}{invoiceItems.map(invoice=>{const checked=selected[invoice.id]!==undefined;const supportedCurrency=printOnPos||invoice.currency==="COP";return <div key={invoice.id} role={printOnPos?undefined:"group"} aria-label={printOnPos?undefined:`Documento ${invoice.number}`} className={`grid ${printOnPos?"grid-cols-[auto_minmax(0,1fr)] lg:grid-cols-[auto_minmax(0,1fr)_auto_11rem]":"min-w-0 grid-cols-[1.25rem_minmax(0,1fr)] lg:grid-cols-[1.25rem_minmax(0,1.65fr)_minmax(0,.8fr)_repeat(4,minmax(0,1fr))_minmax(0,1.15fr)] [&>span]:min-w-0 [&>span]:break-words"} items-center ${printOnPos?"gap-3 p-4":"gap-2 p-3"} rounded-xl border ${checked?"border-primary/50 bg-primary/5":""}`}><Checkbox aria-label={`Seleccionar ${invoice.number}`} disabled={!supportedCurrency} checked={checked} onCheckedChange={value=>{setSelected(current=>{const next={...current};if(value)next[invoice.id]=String(selectedLimits[invoice.id]??invoice.outstanding);else delete next[invoice.id];return next;});setSelectedLimits(current=>{const next={...current};if(value)next[invoice.id]=selectedLimits[invoice.id]??invoice.outstanding;else delete next[invoice.id];return next;});setSelectedNumbers(current=>{const next={...current};if(value)next[invoice.id]=invoice.number;else delete next[invoice.id];return next;});}}/><div className="min-w-0"><div className="flex items-center gap-2"><b className={printOnPos?"truncate":"break-all text-sm"} title={invoice.number}>{invoice.number}</b>{printOnPos&&invoice.overdue&&<Badge variant="destructive">Vencida</Badge>}</div>{!printOnPos&&<div className="space-y-0.5 text-xs text-muted-foreground"><p>{accountingDocumentTypeLabel(invoice.sourceDocumentType)}</p><p>Emisión: <span>{invoice.issuedOn?portfolioReportDate(invoice.issuedOn):invoice.issuedAt?formatBusinessDate(invoice.issuedAt):"—"}</span></p><p>Vence: <span>{invoice.dueOn?portfolioReportDate(invoice.dueOn):formatBusinessDate(invoice.dueDate)}</span></p></div>}{!supportedCurrency&&<p className="text-xs text-muted-foreground">Pago no disponible en {invoice.currency} en este formulario.</p>}{printOnPos && <p className="text-sm text-muted-foreground">Vence {formatDate(invoice.dueDate)}</p>}{direction==="payable"&&!!invoice.financialDiscountEligibleAmount&&<div className="mt-2 space-y-1"><Label htmlFor={`discount-${invoice.id}`}>Descuento financiero disponible: {formatCurrency(invoice.financialDiscountEligibleAmount,"COP",4)}</Label><Select value={discountDecisions[invoice.id]??"pending"} disabled={invoice.financialDiscountPendingReturn} onValueChange={value=>chooseDiscount(invoice,value==="pending"?null:value as DiscountDecision)}><SelectTrigger id={`discount-${invoice.id}`} aria-label={`Descuento financiero para ${invoice.number}`} className="w-full"><SelectValue /></SelectTrigger><SelectContent><SelectItem value="pending">Decidir después</SelectItem><SelectItem value="earned">Sí lo obtuve</SelectItem><SelectItem value="lost">No lo obtuve</SelectItem></SelectContent></Select>{invoice.financialDiscountPendingReturn&&<p className="text-xs text-muted-foreground">Espera a que termine la devolución pendiente.</p>}</div>}</div>{!printOnPos && <><span className="col-span-2 text-xs lg:col-span-1"><span className="mr-2 font-medium lg:hidden">Mora / edad</span><b className={invoice.overdue?"text-red-700":""}>{invoice.overdueDays===undefined?"—":`${invoice.overdueDays} d`}</b><small className="ml-2 text-muted-foreground lg:ml-0 lg:block">{portfolioAgingBandLabel(invoice.agingBandCode)}</small></span><span className="col-span-2 flex justify-between gap-2 text-right text-xs tabular-nums lg:col-span-1 lg:block"><small className="lg:hidden">Original</small>{invoice.original===undefined?"—":formatCurrency(invoice.original,invoice.currency,4)}</span><span className="col-span-2 flex justify-between gap-2 text-right text-xs tabular-nums lg:col-span-1 lg:block"><small className="lg:hidden">{direction==="receivable"?"Abonado":"Pagado"}</small>{invoice.paid===undefined?"—":formatCurrency(invoice.paid,invoice.currency,4)}</span><span className="col-span-2 flex justify-between gap-2 text-right text-xs tabular-nums lg:col-span-1 lg:block"><small className="lg:hidden">Ajustes</small>{invoice.adjustments===undefined?"—":formatCurrency(invoice.adjustments,invoice.currency,4)}</span></>}<span className={`${printOnPos?"col-start-2 lg:col-auto text-sm":"col-span-2 flex justify-between gap-2 text-right text-xs tabular-nums lg:col-span-1 lg:block"} font-semibold`}>{printOnPos?"Saldo ":<small className="lg:hidden">Saldo</small>}{formatCurrency(invoice.outstanding,invoice.currency,4)}</span><div className={printOnPos?"col-start-2 lg:col-auto":"col-span-2 min-w-0 lg:col-span-1"}>{!printOnPos&&<Label className="text-xs lg:hidden" htmlFor={`apply-${invoice.id}`}>Aplicar</Label>}<FormattedNumberInput id={`apply-${invoice.id}`} kind="currency" ariaLabel={`Abono para ${invoice.number}`} invalid={!printOnPos&&Number(selected[invoice.id]??0)>(selectedLimits[invoice.id]??invoice.outstanding)} disabled={!supportedCurrency} value={selected[invoice.id]??""} onValueChange={value=>{setSelected(current=>{const next={...current};if(value&&value>0)next[invoice.id]=String(value);else delete next[invoice.id];return next;});setSelectedLimits(current=>({...current,[invoice.id]:current[invoice.id]??invoice.outstanding}));setSelectedNumbers(current=>({...current,[invoice.id]:invoice.number}));}}/></div></div>})}</div></div>}
      {partyId&&invoicesQuery.data&&invoicesQuery.data.totalPages>1&&<div className="flex items-center justify-between text-sm text-muted-foreground"><span>{invoicesQuery.data.totalCount} facturas · {allocations.length} seleccionadas</span><div className="flex items-center gap-2"><Button type="button" variant="outline" size="sm" disabled={invoicePage<=1} onClick={()=>setInvoicePage(page=>page-1)}>Anterior</Button><span>{invoicePage} de {invoicesQuery.data.totalPages}</span><Button type="button" variant="outline" size="sm" disabled={invoicePage>=invoicesQuery.data.totalPages} onClick={()=>setInvoicePage(page=>page+1)}>Siguiente</Button></div></div>}
      <div className="flex flex-wrap justify-between gap-3 rounded-xl bg-muted p-4"><div><span>Total seleccionado</span>{!printOnPos&&<p className="text-xs text-muted-foreground">{allocations.length} facturas · {allocations.filter(item=>!invoiceItems.some(invoice=>invoice.id===item.id)).length} fuera de esta página</p>}</div><b className="text-lg">{formatCurrency(total,"COP",4)}</b></div>
      {!printOnPos&&allocations.length>0&&<p className="text-sm">Saldo residual proyectado de las seleccionadas: <b>{formatCurrency(allocations.reduce((sum,item)=>sum+(selectedLimits[item.id]??0)-item.amount,0),"COP",4)}</b>. El saldo actual sólo cambia al aplicar el pago.</p>}
      </div></div>
    <DialogFooter className={`border-t ${printOnPos?"px-6 py-4":"px-4 py-3"}`}><Button type="button" variant="ghost" onClick={()=>changeOpen(false)}>Cancelar</Button><Button type="button" disabled={invoicesQuery.isError||!printOnPos&&(filtersPending||invalidFilterDates||invoicesQuery.isLoading)} onClick={()=>validateStepOne()&&setStep(2)}>Ir a pagar<ArrowRight className="ml-2 h-4 w-4"/></Button></DialogFooter>
  </DialogContent></Dialog>;
  function resetInvoiceSelection(){setSelected({});setSelectedLimits({});setSelectedNumbers({});setDiscountDecisions({});setInvoicePage(1);setInvoiceIdFilter(null);initialSelectionApplied.current=false;}
  function changeOpen(next:boolean){if(!next){if(filterTimer.current)clearTimeout(filterTimer.current);mutation.reset();invoiceRequestEpoch.current++;setParty(null);resetInvoiceSelection();setInvoiceLoad(null);setStep(1);}onOpenChange(next);}
}
function paymentSubmissionError(error:Error){
  const status=error instanceof PosEdgeError?error.status:"statusCode" in error&&typeof error.statusCode==="number"?error.statusCode:0;
  if(status>=400&&status<500&&status!==408)return {title:"El pago no se realizó",message:`${error.message} No se aplicó ningún abono de este intento. Revisa los datos antes de volver a confirmar.`};
  return {title:"No se pudo confirmar el pago",message:"No recibimos una confirmación del servidor y no podemos asegurar si quedó registrado. Reintenta con los mismos datos para verificarlo sin duplicarlo."};
}
function toCustomerTender(value:PosPaymentInput):CustomerPaymentTender{const methodCode=toPortfolioMethod(value.methodCode);if(!methodCode)throw new Error("Medio de pago no admitido para cartera.");return {methodCode,paymentMethodId:value.paymentMethodId,paymentMethodVersion:value.paymentMethodVersion,amount:value.amount,tenderedAmount:value.methodCode==="Cash"?value.tenderedAmount??value.amount:null,bankAccountId:value.methodCode==="Transfer"?value.bankAccountId??null:null,reference:value.reference?.trim()||null,notes:value.notes?.trim()||null,cardFranchiseCode:value.cardFranchiseCode?.trim()||null,approvalNumber:value.approvalNumber?.trim()||null};}
function toSupplierTender(value:PosPaymentInput):SupplierPaymentTender{const methodCode=toPortfolioMethod(value.methodCode);if(!methodCode)throw new Error("Medio de pago no admitido para proveedores.");return {methodCode,paymentMethodId:value.paymentMethodId,paymentMethodVersion:value.paymentMethodVersion,amount:value.amount,tenderedAmount:value.methodCode==="Cash"?value.tenderedAmount??value.amount:null,bankAccountId:value.methodCode==="Transfer"?value.bankAccountId??null:null,reference:value.reference?.trim()||null,notes:value.notes?.trim()||null,cardFranchiseCode:value.cardFranchiseCode?.trim()||null,approvalNumber:value.approvalNumber?.trim()||null};}
function Empty({text}:{text:string}){return <div className="rounded-xl border border-dashed py-12 text-center text-sm text-muted-foreground">{text}</div>}
function invoicePaymentLimit(invoice:Invoice,decision:DiscountDecision|undefined){
  const eligible=invoice.financialDiscountEligibleAmount??0;
  const adjustment=decision==="earned"?(invoice.financialDiscountAnticipated?0:eligible)
    :decision==="lost"&&invoice.financialDiscountAnticipated?-eligible:0;
  return invoice.outstanding-adjustment;
}
