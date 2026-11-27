"use client";

import { DocumentNumbers } from "@/components/documents/document-numbers";

import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowDown, ArrowLeft, ArrowUp, Banknote, Camera, Check, ChevronRight, CircleDollarSign, CreditCard, Map, MapPin, PackageCheck, PackageX, Plus, Printer, ReceiptText, RefreshCw, Route, Search, Trash2, XCircle } from "lucide-react";
import { toast } from "sonner";
import { calculateSalesReturnSelection, estimateSalesReturnTotal } from "@/components/returns/sales-return-calculation";
import { PaymentMethodSelect } from "@/components/payments/payment-method-select";
import { CardPaymentDetailsDialog, TransferPaymentDetailsDialog, type CardPaymentDetails, type TransferPaymentDetails } from "@/components/payments/payment-details-dialogs";
import { useDispatchKey, useDispatchOutboxSynchronization, updateDispatchExecution } from "@/hooks/use-dispatches";
import { useReferenceOptions } from "@/hooks/use-reference-options";
import { useTenantContextStore } from "@/stores/tenant-context-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { paymentMethodsApi, type PaymentMethod } from "@/services/api/payment-methods";
import { DispatchPaymentGroups } from "@/components/dispatching/dispatch-payment-groups";
import { DispatchReturnLinks, type DispatchReturnSelections } from "@/components/dispatching/dispatch-return-links";
import { DispatchExpenseApprovalDialog } from "@/components/dispatching/dispatch-expense-approval-dialog";
import { PagedEntitySelect } from "@/components/forms/paged-entity-select";
import { expensesApi, type ExpenseAcceptance, type ExpenseConceptOption } from "@/services/api/expenses";
import { formatMoneyDraft, formatMoneyValue, parseMoneyDraft } from "@/lib/money-input";
import { printPosHtmlDocument } from "@/services/pos/pos-browser-print";
import { useBusinessDates } from "@/hooks/use-business-dates";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { RouteLocationMap } from "@/components/maps/route-location-map";
import { Card, CardContent } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import { dispatchesApi, type DeliveryDocument, type DeliveryResultInput, type DispatchExpense, type DispatchExecution, type DispatchListItem, type DispatchPaymentReviewResult, type DispatchReasonOption } from "@/services/api/dispatches";

const money=new Intl.NumberFormat("es-CO",{style:"currency",currency:"COP",maximumFractionDigits:0});


export function TransportDispatchApp({canExecute=false}:{canExecute?:boolean}){
  useDispatchOutboxSynchronization();
  const prefix=useDispatchKey();const [,tenant,business,user]=prefix;
  const deliveriesKey=[...prefix,"my-deliveries"] as const;
  const owner=prefix.join(":");
  const [selection,setSelection]=useState<{owner:string;dispatch:DispatchListItem}|null>(null);
  const selected=selection?.owner===owner?selection.dispatch:null;
  const query=useQuery({queryKey:deliveriesKey,queryFn:()=>dispatchesApi.page({page:1,pageSize:50,mine:true}),enabled:Boolean(tenant&&business&&user&&!selected),refetchOnWindowFocus:false,refetchOnReconnect:false});
  const rows=query.isError?undefined:query.data;
  if(selected)return <DispatchExecutionWorkspace dispatch={selected} canExecute={canExecute} onBack={()=>setSelection(null)}/>;
  return <div className="mx-auto max-w-5xl space-y-5 pb-24">
    <section className="overflow-hidden rounded-[2rem] bg-gradient-to-br from-slate-950 via-teal-950 to-teal-700 p-6 text-white shadow-xl md:p-8">
      <div className="flex items-start justify-between gap-4"><div><p className="text-sm font-semibold text-teal-200">Centro de entregas</p><h1 className="mt-1 text-3xl font-black tracking-tight">Mis despachos</h1><p className="mt-2 max-w-xl text-sm text-teal-50/80">Facturas, clientes, recaudos y novedades en un solo recorrido.</p></div><Button variant="ghost" size="icon" className="shrink-0 bg-white/10 text-white hover:bg-white/20 hover:text-white" aria-label="Actualizar mis despachos" disabled={query.isFetching} onClick={()=>void query.refetch()}><RefreshCw className={`h-5 w-5 ${query.isFetching?"animate-spin":""}`}/></Button></div>
      <div className="mt-6 grid grid-cols-3 gap-2"><HeroMetric label="Asignados" value={rows?.items.length??0}/><HeroMetric label="En ruta" value={rows?.items.filter(x=>x.status==="InDelivery").length??0}/><HeroMetric label="Por liquidar" value={rows?.items.filter(x=>x.status==="PendingSettlement").length??0}/></div>
    </section>
    {query.isLoading&&<div className="grid gap-3 md:grid-cols-2">{[0,1].map(item=><div key={item} className="h-48 animate-pulse rounded-3xl border bg-muted/50"/>)}</div>}
    {query.isError&&<div className="rounded-3xl border border-red-200 bg-red-50 p-6 text-center text-red-900"><strong className="block">No pudimos cargar tus despachos</strong><p className="mt-1 text-sm">Revisa la conexión y vuelve a intentarlo.</p><Button className="mt-4" variant="outline" onClick={()=>query.refetch()}>Reintentar</Button></div>}
    <div className="grid gap-3 md:grid-cols-2">{rows?.items.map(item=><button key={item.dispatchId} onClick={()=>setSelection({owner,dispatch:item})} className="group min-w-0 rounded-3xl border bg-card p-5 text-left shadow-sm transition hover:-translate-y-0.5 hover:border-teal-300 hover:shadow-lg"><div className="flex items-center justify-between gap-3"><span className="rounded-xl bg-teal-50 p-2 text-teal-700"><Route className="h-5 w-5"/></span><DispatchBadge status={item.status}/></div><h2 className="mt-4 break-all text-xl font-black">{item.dispatchNumber}</h2><p className="mt-1 text-sm text-muted-foreground">{item.scheduledDate} · {item.vehiclePlate??"Sin placa"}</p><div className="mt-5 flex items-center justify-between border-t pt-4"><span className="text-sm font-semibold">{item.documentCount} entregas</span><ChevronRight className="h-5 w-5 transition group-hover:translate-x-1"/></div></button>)}</div>
    {!query.isLoading&&!rows?.items.length&&<div className="rounded-3xl border border-dashed p-12 text-center"><PackageCheck className="mx-auto h-10 w-10 text-teal-600"/><h2 className="mt-3 font-bold">No tienes despachos asignados</h2><p className="mt-1 text-sm text-muted-foreground">Usa Actualizar para consultar tus despachos.</p></div>}
  </div>;
}

export function DispatchExecutionWorkspace({dispatch,canExecute,onBack}:{dispatch:DispatchListItem;canExecute:boolean;onBack:()=>void}){
  const prefix=useDispatchKey();const [,tenant,business,user]=prefix;
  const executionKey=[...prefix,"execution",dispatch.dispatchId] as const;
  const cache=useQueryClient();const query=useQuery({queryKey:executionKey,queryFn:()=>dispatchesApi.execution(dispatch.dispatchId),enabled:Boolean(tenant&&business&&user),networkMode:"always",refetchOnWindowFocus:false,refetchOnReconnect:false});
  const [reordering,setReordering]=useState(false);
  const [tab,setTab]=useState<"list"|"map"|"expenses">("list"),[active,setActive]=useState<DeliveryDocument|null>(null),[expenseOpen,setExpenseOpen]=useState(false),[closing,setClosing]=useState(false),[printing,setPrinting]=useState(false);
  const dates=useBusinessDates();
  const businessName=useBusinessContextStore(state=>state.businesses.find(item=>item.businessId===state.selectedBusinessId)?.name);
  const printClosure=async()=>{setPrinting(true);try{const receipt=await dispatchesApi.closureReceipt(dispatch.dispatchId,businessName);await printPosHtmlDocument(receipt.html,"No fue posible abrir la impresión del cierre de despacho.")}catch(error){toast.error(errorMessage(error,"El cierre está guardado. Reintenta su impresión."))}finally{setPrinting(false)}};
  const data=query.isError?undefined:query.data;const completed=data?.documents.filter(x=>x.deliveryStatus!=="Pending").length??0;const total=data?.documents.length??0;
  const deliveryEnabled=canExecute&&!data?.settlement&&(data?.status==="Released"||data?.status==="InDelivery"||data?.status==="PendingSettlement");
  const awaitingRelease=!!data&&["Draft","Prepared","InVerification","Verified"].includes(data.status);
  const applyUpdate=(updated:DispatchExecution)=>updateDispatchExecution(cache,prefix,updated);
  const move=async(documentId:string,direction:-1|1)=>{if(!data||reordering)return;const ids=data.documents.map(x=>x.dispatchSourceDocumentId),index=ids.indexOf(documentId),target=index+direction;if(target<0||target>=ids.length)return;[ids[index],ids[target]]=[ids[target],ids[index]];setReordering(true);try{applyUpdate(await dispatchesApi.reorder(data.dispatchId,ids,data.rowVersion??dispatch.rowVersion))}catch(error){toast.error(errorMessage(error,"No fue posible cambiar el orden."))}finally{setReordering(false)}};
  return <div className="mx-auto max-w-6xl space-y-4 pb-28">
    <section className="sticky top-0 z-20 -mx-4 -mt-4 border-b bg-background px-4 pb-4 pt-4 shadow-sm md:static md:mx-0 md:mt-0 md:rounded-3xl md:border md:p-5 md:shadow-none">
      <div className="flex items-center gap-3"><Button size="icon" variant="ghost" aria-label="Volver a mis despachos" onClick={onBack}><ArrowLeft className="h-5 w-5"/></Button><div className="min-w-0 flex-1"><div className="flex items-center gap-2"><h1 className="truncate text-xl font-black">{data?.dispatchNumber??dispatch.dispatchNumber}</h1><DispatchBadge status={data?.status??dispatch.status}/></div><p className="truncate text-xs text-muted-foreground">{data?.driverName??dispatch.driverName} · {data?.vehiclePlate??"Sin placa"}</p></div><Button size="icon" variant="outline" aria-label="Actualizar despacho" disabled={query.isFetching||reordering} onClick={()=>void query.refetch()}><RefreshCw className="h-4 w-4"/></Button><span className="text-right"><strong className="block text-lg">{completed}/{total}</strong><small className="text-muted-foreground">entregas</small></span></div>
      <div className="mt-4 h-2 overflow-hidden rounded-full bg-muted"><div className="h-full rounded-full bg-teal-500 transition-all" style={{width:`${total?completed/total*100:0}%`}}/></div>
      <div className="mt-4 grid grid-cols-3 gap-1 rounded-2xl bg-muted p-1"><Tab active={tab==="list"} onClick={()=>setTab("list")} icon={ReceiptText}>Lista</Tab><Tab active={tab==="map"} onClick={()=>setTab("map")} icon={Map}>Mapa</Tab><Tab active={tab==="expenses"} onClick={()=>setTab("expenses")} icon={CircleDollarSign}>Gastos</Tab></div>
    </section>
    {query.isLoading&&<div className="space-y-3"><div className="h-36 animate-pulse rounded-3xl bg-muted/60"/><div className="h-36 animate-pulse rounded-3xl bg-muted/60"/></div>}
    {query.isError&&<div className="rounded-3xl border border-red-200 bg-red-50 p-6 text-center text-red-900"><strong className="block">No pudimos abrir este despacho</strong><p className="mt-1 text-sm">La información guardada no está disponible todavía.</p><Button className="mt-4" variant="outline" onClick={()=>query.refetch()}>Reintentar</Button></div>}
    {awaitingRelease&&<div className="rounded-2xl border border-amber-200 bg-amber-50 p-4 text-sm text-amber-900"><strong className="block">Este despacho todavía no está liberado</strong>Termina la verificación y pulsa “Liberar despacho”. Las entregas se habilitarán cuando quede listo para salir.</div>}
    {data&&tab==="list"&&<div className="space-y-3">{data.documents.map((document,index)=><DeliveryCard key={document.dispatchSourceDocumentId} document={document} index={index} total={data.documents.length} enabled={deliveryEnabled&&!reordering} onOpen={()=>deliveryEnabled&&setActive(document)} onMove={direction=>move(document.dispatchSourceDocumentId,direction)}/>)}</div>}
    {data&&tab==="map"&&<DispatchMap data={data} onOpen={document=>deliveryEnabled&&setActive(document)}/>}
    {data&&tab==="expenses"&&<DispatchExpensesPanel data={data} onExpense={deliveryEnabled?()=>setExpenseOpen(true):undefined}/>}
    {data&&completed===total&&total>0&&!data.settlement&&<div className="space-y-3 rounded-2xl border border-emerald-200 bg-emerald-50 p-4 text-emerald-950"><strong className="block">Recorrido completo</strong><p className="text-sm">El transportador asignado registra los valores que entrega. El administrativo verificará el cierre y sus soportes.</p>{canExecute&&data.isAssignedDriver&&<Button className="min-h-11 w-full" onClick={()=>setClosing(true)}>Cerrar despacho</Button>}</div>}
    {data?.settlement&&<section aria-label="Declaración guardada" className="space-y-3 rounded-2xl border bg-card p-4"><h2 className="font-bold">Declaración guardada</h2><p className="text-sm text-muted-foreground">{data.settlement.transporterClosedAt?dates.formatDateTime(data.settlement.transporterClosedAt):"Cierre registrado"}</p><div className="grid gap-2 sm:grid-cols-3"><MiniMoney label="Efectivo declarado" value={data.settlement.declaredCash}/>{data.settlement.declaredCard!=null&&<MiniMoney label="Tarjetas declaradas" value={data.settlement.declaredCard}/>} {data.settlement.declaredTransfer!=null&&<MiniMoney label="Transferencias declaradas" value={data.settlement.declaredTransfer}/>}</div><Button variant="outline" className="min-h-11" disabled={printing} onClick={()=>void printClosure()}><Printer className="mr-2 h-4 w-4"/>{printing?"Preparando impresión…":"Imprimir cierre"}</Button></section>}
    {active&&data&&<DeliveryDialog dispatchId={data.dispatchId} document={active} onClose={()=>setActive(null)} onSaved={updated=>{setActive(null);applyUpdate(updated)}}/>}
    {expenseOpen&&data&&<ExpenseDialog dispatchId={data.dispatchId} onClose={()=>setExpenseOpen(false)} onSaved={updated=>{setExpenseOpen(false);applyUpdate(updated)}}/>}
    {closing&&data&&<DispatchBlindCloseDialog dispatchId={data.dispatchId} onClose={()=>setClosing(false)} onSaved={updated=>{setClosing(false);applyUpdate(updated);void printClosure()}}/>}
  </div>;
}

function DispatchBlindCloseDialog({dispatchId,onClose,onSaved}:{dispatchId:string;onClose:()=>void;onSaved:(updated:DispatchExecution)=>void}){
  const [cash,setCash]=useState(""),[cards,setCards]=useState(""),[transfers,setTransfers]=useState("");
  const [key]=useState(()=>crypto.randomUUID());
  const [saving,setSaving]=useState(false),[error,setError]=useState<string|null>(null);
  const values=[{id:"dispatch-declared-cash",label:"Efectivo contado",value:cash,set:setCash},{id:"dispatch-declared-cards",label:"Total de tarjetas",value:cards,set:setCards},{id:"dispatch-declared-transfers",label:"Total de transferencias",value:transfers,set:setTransfers}];
  const save=async()=>{if(values.some(item=>!item.value.trim())||saving)return;setSaving(true);setError(null);try{const updated=await dispatchesApi.closeRoute(dispatchId,{declaredCash:parseMoneyDraft(cash),declaredCard:parseMoneyDraft(cards),declaredTransfer:parseMoneyDraft(transfers),idempotencyKey:key});toast.success("Declaración del despacho guardada");onSaved(updated)}catch(error){setError(errorMessage(error,"No fue posible guardar el cierre. Reintenta sin cambiar los valores."))}finally{setSaving(false)}};
  return <Dialog open onOpenChange={open=>!open&&!saving&&onClose()}><DialogContent showClose={!saving} className="max-h-[94dvh] w-[calc(100%-1rem)] overflow-y-auto rounded-3xl"><DialogHeader><DialogTitle>Cerrar despacho</DialogTitle><DialogDescription>Escribe los valores reales que entregas. Si no recibiste un medio, escribe cero. Al confirmar no podrás modificar las entregas.</DialogDescription></DialogHeader><div className="space-y-4">{values.map(item=><div key={item.id} className="space-y-2"><Label htmlFor={item.id}>{item.label}</Label><Input id={item.id} inputMode="decimal" className="h-12 text-xl tabular-nums" autoComplete="off" disabled={saving} value={item.value} onChange={event=>item.set(formatMoneyDraft(event.target.value,4))}/></div>)}{error&&<p role="alert" className="text-sm text-destructive">{error}</p>}</div><DialogFooter><Button className="min-h-11" variant="outline" disabled={saving} onClick={onClose}>Cancelar</Button><Button className="min-h-11" disabled={saving||values.some(item=>!item.value.trim())} onClick={()=>void save()}>{saving?"Guardando declaración…":"Confirmar cierre"}</Button></DialogFooter></DialogContent></Dialog>;
}

function DeliveryCard({document,index,total,enabled,onOpen,onMove}:{document:DeliveryDocument;index:number;total:number;enabled:boolean;onOpen:()=>void;onMove:(direction:-1|1)=>void}){
  const done=document.deliveryStatus!=="Pending";
  return <Card className={`overflow-hidden rounded-3xl transition ${done?"border-emerald-200 bg-emerald-50/30":enabled?"hover:border-teal-300 hover:shadow-md":"opacity-75"}`}><CardContent className="p-0"><div className="flex gap-3 p-4"><span className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-2xl font-black ${done?"bg-emerald-500 text-white":"bg-slate-900 text-white"}`}>{done?<Check className="h-5 w-5"/>:index+1}</span><button disabled={!enabled} onClick={onOpen} className="min-w-0 flex-1 text-left disabled:cursor-not-allowed"><div className="flex items-start justify-between gap-2"><span><strong className="block truncate text-base">{document.customerName}</strong><DocumentNumbers internalNumber={document.documentNumber} fiscalNumber={document.fiscalNumber}/><small className="text-muted-foreground">{money.format(document.documentTotal)}</small></span><DeliveryBadge status={document.deliveryStatus}/></div><p className="mt-2 flex items-start gap-1 text-sm text-muted-foreground"><MapPin className="mt-0.5 h-4 w-4 shrink-0"/>{document.deliveryAddress??"Ubicación pendiente"}</p></button></div><div className="flex border-t bg-background/70"><Button variant="ghost" className="flex-1 rounded-none" disabled={!enabled||index===0||done} onClick={()=>onMove(-1)}><ArrowUp className="mr-1 h-4 w-4"/>Subir</Button><Button variant="ghost" className="flex-1 rounded-none" disabled={!enabled||index===total-1||done} onClick={()=>onMove(1)}><ArrowDown className="mr-1 h-4 w-4"/>Bajar</Button><Button variant="ghost" disabled={!enabled} className="flex-1 rounded-none text-teal-700" onClick={onOpen}>{done?"Ver":enabled?"Entregar":"Pendiente"}<ChevronRight className="ml-1 h-4 w-4"/></Button></div></CardContent></Card>;
}

function DispatchMap({data,onOpen}:{data:DispatchExecution;onOpen:(document:DeliveryDocument)=>void}){
  const stops=data.documents.map((document,index)=>({routeStopId:document.dispatchSourceDocumentId,sequence:index+1,customerName:document.customerName,siteName:document.documentNumber,addressLine:document.deliveryAddress??"Dirección pendiente",cityName:"",googleMapsUrl:null,latitude:document.destinationLatitude,longitude:document.destinationLongitude,document}));
  return <RouteLocationMap stops={stops} onOpen={stop=>onOpen(stop.document)} statusOf={stop=>stop.document.deliveryStatus==="NotDelivered"?"skipped":stop.document.deliveryStatus==="Pending"?"pending":"visited"}/>;
}
function estimateDeliveryReturn(document:DeliveryDocument,quantities:Record<number,string>):number {
  const selection=calculateSalesReturnSelection(document.lines.map(line=>({originalLineNumber:line.originalLineNumber,
    soldQuantity:line.quantity,availableQuantity:line.quantity-(line.returnedQuantity??0),lineTotal:line.lineTotal,
    returnedLineTotal:line.returnedLineTotal})),Object.fromEntries(Object.entries(quantities).map(([key,value])=>[key,Number(value)||0])));
  const calculation=document.returnCalculation;
  return calculation?estimateSalesReturnTotal(calculation.originalUnrounded,calculation.originalRounding,
    calculation.unroundedOutstanding,calculation.remainingRounding,selection.estimatedTotal):selection.estimatedTotal;
}
function DeliveryDialog({dispatchId,document,onClose,onSaved}:{dispatchId:string;document:DeliveryDocument;onClose:()=>void;onSaved:(updated:DispatchExecution)=>void}){
  const hasCredit=document.creditAmount>0;
  const previouslyCollected=document.collectionMode==="Session";
  const recordedPayments=document.payments.filter(payment=>payment.applicationType==="InvoicePayment"||payment.applicationType==="CreditAdvance");
  const [keepCurrent,setKeepCurrent]=useState(document.payments.some(payment=>payment.applicationType==="KeepCurrent"));
  const multiplePaymentForms=new Set(recordedPayments.map(payment=>JSON.stringify([payment.paymentMethodId,payment.paymentMethod,payment.reference,payment.cardFranchiseCode,payment.approvalNumber]))).size>1;
  const recordedPayment=multiplePaymentForms?null:recordedPayments[0]??null;
  const [collectCredit,setCollectCredit]=useState(recordedPayments.some(payment=>payment.applicationType==="CreditAdvance"));
  const isCredit=!collectCredit&&(document.creditAmount>=document.documentTotal||previouslyCollected&&hasCredit);
  const canCaptureMoney=!previouslyCollected||hasCredit&&collectCredit;
  const recordedReturns=new globalThis.Map(document.returns.map(line=>[line.originalLineNumber,line]));
  const initialMethod=keepCurrent?null:recordedPayment?.paymentMethod==="Deposit"?"Transfer":recordedPayment?.paymentMethod??(isCredit?null:"Cash");
  const [status,setStatus]=useState<"Delivered"|"NotDelivered">(document.deliveryStatus==="NotDelivered"?"NotDelivered":"Delivered");
  const [paymentMethod,setPaymentMethod]=useState<"Cash"|"Transfer"|"DebitCard"|"CreditCard"|null>(initialMethod);
  const tenant=useTenantContextStore(state=>state.selectedTenantId);
  const business=useBusinessContextStore(state=>state.selectedBusinessId);
  const [selectedForm,setForm]=useState<PaymentMethod|null|undefined>(undefined);
  const originalForm=useQuery({queryKey:["dispatches","recorded-payment-form",tenant,business,recordedPayment?.paymentMethodId],
    queryFn:()=>paymentMethodsApi.list({page:1,pageSize:1,usage:"Receipts",paymentMethodId:recordedPayment!.paymentMethodId!}),
    enabled:Boolean(!isCredit&&canCaptureMoney&&recordedPayment?.paymentMethodId&&tenant&&business),staleTime:5*60*1000});
  const form=selectedForm===undefined?originalForm.data?.items[0]??null:selectedForm;
  const resolvingOriginal=Boolean(recordedPayment?.paymentMethodId)&&selectedForm===undefined&&originalForm.isPending;
  const [paymentReference,setPaymentReference]=useState(recordedPayment?.reference??"");
  const [franchise,setFranchise]=useState(recordedPayment?.cardFranchiseCode??"");
  const [approval,setApproval]=useState(recordedPayment?.approvalNumber??"");
  const [depositEvidenceUrl,setDepositEvidenceUrl]=useState(recordedPayment?.evidenceUrl??null);
  const signedEvidenceUrl=document.payments.find(payment=>payment.applicationType==="CreditDocument")?.evidenceUrl??null;
  const depositReadUrl=depositEvidenceUrl===recordedPayment?.evidenceUrl?recordedPayment?.evidenceReadUrl??depositEvidenceUrl:depositEvidenceUrl;
  const signedReadUrl=document.payments.find(payment=>payment.applicationType==="CreditDocument")?.evidenceReadUrl??signedEvidenceUrl;
  const [capture,setCapture]=useState<{card?:CardPaymentDetails;transfer?:TransferPaymentDetails;form?:PaymentMethod|null}|null>(null);
  const captureForm=capture?.form===undefined?form:capture.form;
  const card=paymentMethod==="DebitCard"||paymentMethod==="CreditCard";
  const paymentTypes=useReferenceOptions("payment-method",!isCredit&&canCaptureMoney);
  const franchises=useReferenceOptions("card-franchise",Boolean(capture?.card));
  const selectForm=(selected:PaymentMethod)=>{
    if(selected.paymentMethodId===form?.paymentMethodId)return;
    setForm(selected);setPaymentReference("");setFranchise("");setApproval("");setDepositProof(null);setDepositEvidenceUrl(null);
  };
  const selectType=(code:"Cash"|"Transfer"|"DebitCard"|"CreditCard")=>{
    setKeepCurrent(false);
    if(code===paymentMethod){if(code==="DebitCard"||code==="CreditCard")setCapture({card:{franchiseCode:franchise,approvalNumber:approval}});else if(code==="Transfer")setCapture({transfer:{reference:paymentReference,notes:""}});return;}
    setPaymentMethod(code);setForm(null);setCapture(code==="Transfer"?{form:null,transfer:{reference:"",notes:""}}:code!=="Cash"?{form:null,card:{franchiseCode:"",approvalNumber:""}}:null);setPaymentReference("");setFranchise("");setApproval("");setDepositProof(null);setDepositEvidenceUrl(null);
  };
  const savePaymentDetails=()=>{
    if(capture?.card){
      if(!captureForm||captureForm.methodCode!==paymentMethod||!franchises.data?.some(item=>item.code===capture.card?.franchiseCode)||!capture.card.approvalNumber.trim())return;
      if(captureForm.paymentMethodId!==form?.paymentMethodId){setDepositProof(null);setDepositEvidenceUrl(null);}
      setForm(captureForm);
      setFranchise(capture.card.franchiseCode);setApproval(capture.card.approvalNumber.trim());
    }else if(capture?.transfer){
      if(!capture.transfer.reference.trim()||captureForm?.methodCode!=="Transfer"||!captureForm.bankAccountId)return;
      if(captureForm.paymentMethodId!==form?.paymentMethodId){setDepositProof(null);setDepositEvidenceUrl(null);}
      setForm(captureForm);
      setPaymentReference(capture.transfer.reference.trim());
    }else return;
    setCapture(null);
  };
  const [reason,setReason]=useState(document.reason??""),[notes,setNotes]=useState(document.notes??"");
  const [depositProof,setDepositProof]=useState<File|null>(null),[signedProof,setSignedProof]=useState<File|null>(null),[returns,setReturns]=useState<Record<number,string>>(()=>Object.fromEntries(document.returns.map(line=>[line.originalLineNumber,String(line.quantity)])));
  const [returnsOpen,setReturnsOpen]=useState(false);
  const [returnReason,setReturnReason]=useState<DispatchReasonOption|null>(null);
  const returnReasons=useQuery({queryKey:["dispatches",tenant,business,"reasons","DeliveryReturn"],
    queryFn:()=>dispatchesApi.reasons("DeliveryReturn"),enabled:returnsOpen,staleTime:5*60*1000,refetchOnWindowFocus:false,refetchOnReconnect:false});
  const reasons=useQuery({queryKey:["dispatches",tenant,business,"reasons","NotDelivered"],queryFn:()=>dispatchesApi.reasons("NotDelivered")});
  const returnedTotal=estimateDeliveryReturn(document,returns);
  const net=Math.max(0,Math.round((document.documentTotal-returnedTotal)*10000)/10000);
  const creditAvailable=Math.max(0,document.creditCollectibleAmount??0);
  const creditToCollect=Math.max(0,creditAvailable-returnedTotal);
  const invoiceToCollect=hasCredit?Math.max(0,(document.uncollectedPaymentAmount??0)-Math.max(0,returnedTotal-creditAvailable)):
    previouslyCollected?0:Math.min(net,Math.max(0,document.documentTotal-document.creditAmount));
  const immediateAmount=invoiceToCollect+(collectCredit?creditToCollect:0);
  const selectedReturns=Object.values(returns).filter(value=>Number(value)>0).length;
  const deliveryStatus=status==="NotDelivered"?"NotDelivered":selectedReturns>0?"PartiallyDelivered":"Delivered";
  useEffect(()=>{if(status==="NotDelivered"&&!reason&&reasons.data?.[0])setReason(reasons.data[0].name)},[status,reason,reasons.data]);
  const mutation=useMutation({networkMode:"always",mutationFn:async()=>{
    const returnInputs=deliveryStatus==="NotDelivered"?[]:document.lines.flatMap(line=>{
      const quantity=Number(returns[line.originalLineNumber]||0);
      if(quantity<=0)return [];
      const saved=recordedReturns.get(line.originalLineNumber);
      const code=returnReason?.code??saved?.reasonCode;
      const description=returnReason?.name??saved?.reasonDescription;
      if(!code||!description)throw new Error("Selecciona el motivo de devolución antes de confirmar la entrega.");
      return [{originalLineNumber:line.originalLineNumber,quantity,inventoryDisposition:saved?.inventoryDisposition??"Sellable" as const,
        reasonCode:code,reasonDescription:description}];
    });
    let depositUrl:string|null=depositEvidenceUrl,signedUrl:string|null=signedEvidenceUrl;
    if(deliveryStatus!=="NotDelivered"&&depositProof)depositUrl=(await dispatchesApi.uploadEvidence(dispatchId,depositProof)).url;
    if(deliveryStatus!=="NotDelivered"&&signedProof)signedUrl=(await dispatchesApi.uploadEvidence(dispatchId,signedProof)).url;
    const payments:DeliveryResultInput["payments"]=[];
    if(keepCurrent)payments.push({applicationType:"KeepCurrent",paymentMethod:null,amount:0,reference:null,evidenceUrl:null});
    if(deliveryStatus!=="NotDelivered"){
      if(!keepCurrent&&paymentMethod&&immediateAmount>0){
        const details={paymentMethod,reference:paymentMethod==="Transfer"?paymentReference.trim():null,evidenceUrl:paymentMethod!=="Cash"?depositUrl:null,paymentMethodId:form?.paymentMethodId,paymentMethodVersion:form?.version,cardFranchiseCode:card?franchise:null,approvalNumber:card?approval.trim():null};
        if(invoiceToCollect>0)payments.push({...details,applicationType:"InvoicePayment",amount:invoiceToCollect});
        if(collectCredit&&creditToCollect>0)payments.push({...details,applicationType:"CreditAdvance",amount:creditToCollect});
      }
      if(document.creditAmount>0)payments.push({applicationType:"CreditDocument",paymentMethod:null,amount:0,reference:null,evidenceUrl:signedUrl});
    }
    const position=await currentPosition();
    return dispatchesApi.recordDelivery(dispatchId,{dispatchSourceDocumentId:document.dispatchSourceDocumentId,deliveryStatus,reason:deliveryStatus==="PartiallyDelivered"?"Devolución registrada durante la entrega":reason||null,notes:notes||null,latitude:position?.latitude??null,longitude:position?.longitude??null,occurredAt:new Date().toISOString(),idempotencyKey:crypto.randomUUID(),payments,returns:returnInputs});
  },onSuccess:updated=>{toast.success("Resultado de entrega guardado");onSaved(updated)},onError:error=>toast.error(errorMessage(error,"No fue posible guardar la entrega."))});
  const paymentRequired=!keepCurrent&&deliveryStatus!=="NotDelivered"&&immediateAmount>0;
  const invalid=multiplePaymentForms||status==="NotDelivered"&&!reason.trim()||paymentRequired&&(!paymentMethod||!form||card&&(!franchise||!approval.trim())||paymentMethod==="Transfer"&&!paymentReference.trim())||hasCredit&&deliveryStatus!=="NotDelivered"&&!signedProof&&!signedEvidenceUrl;
  const paymentMethodField=<div className="space-y-2"><Label>{card?"Tarjeta o datáfono":"Forma de transferencia"}</Label>{resolvingOriginal&&capture?.form===undefined?<p role="status" className="text-sm text-muted-foreground">Cargando la forma registrada…</p>:<PaymentMethodSelect tenantId={tenant} businessId={business??""} usage="Receipts" methodCode={paymentMethod??undefined}
    className="min-h-11" wrapLabels autoSelectSingle={!recordedPayment?.paymentMethodId||selectedForm!==undefined||capture?.form!==undefined}
    value={captureForm?.paymentMethodId??""} name={captureForm?.name} ariaLabel="Forma de pago de la entrega"
    onChange={selected=>{if(selected.paymentMethodId!==captureForm?.paymentMethodId)setCapture(current=>current?{...current,form:selected,...(current.card?{card:{franchiseCode:"",approvalNumber:""}}:{transfer:{reference:"",notes:""}})}:null)}}/>}</div>;
  return <>
    <Dialog open onOpenChange={open=>!open&&onClose()}><DialogContent className="max-h-[96dvh] max-w-3xl grid-cols-[minmax(0,1fr)] overflow-y-auto rounded-3xl">
      <DialogHeader><DialogTitle>Confirmar entrega</DialogTitle><DialogDescription>{document.customerName} · <DocumentNumbers internalNumber={document.documentNumber} fiscalNumber={document.fiscalNumber}/></DialogDescription></DialogHeader>
      <div className="space-y-5">
        <section className="grid grid-cols-3 gap-2 rounded-2xl bg-slate-950 p-4 text-center text-white"><MiniMoney label="Factura" value={document.documentTotal}/><MiniMoney label="Devolución" value={returnedTotal}/><MiniMoney label={keepCurrent?"A recibir":isCredit?"Venta a crédito":"A recibir"} value={keepCurrent?0:isCredit?Math.max(0,document.creditAmount-returnedTotal):immediateAmount}/></section>
        <Field label="¿Cómo terminó la visita?"><div className="grid grid-cols-2 gap-3"><DeliveryResultButton active={status==="Delivered"} icon={PackageCheck} title="Entregado" onClick={()=>{setStatus("Delivered");setReason("")}}/><DeliveryResultButton active={status==="NotDelivered"} icon={XCircle} title="No se entregó" onClick={()=>setStatus("NotDelivered")}/></div></Field>
        {status==="NotDelivered"&&<Field label="Motivo"><Select value={reason} onValueChange={setReason}><SelectTrigger className="h-12 rounded-xl"><SelectValue placeholder={reasons.isLoading?"Cargando motivos…":"Selecciona el motivo"}/></SelectTrigger><SelectContent>{reasons.data?.map(item=><SelectItem key={item.dispatchReasonId} value={item.name}>{item.name}</SelectItem>)}</SelectContent></Select>{reasons.isError&&<p className="text-xs text-red-700">No fue posible cargar los motivos de no entrega.</p>}</Field>}
        {deliveryStatus!=="NotDelivered"&&<>
          <section className="flex flex-col items-stretch gap-3 rounded-2xl border p-4 sm:flex-row sm:items-center sm:justify-between"><span className="min-w-0"><strong className="block">Devolución de productos</strong><small className="text-muted-foreground">{selectedReturns?`${selectedReturns} producto(s) · ${money.format(returnedTotal)}`:"La entrega no tiene devoluciones"}</small></span><Button type="button" className="min-h-11 shrink-0" variant={selectedReturns?"secondary":"outline"} onClick={()=>setReturnsOpen(true)}>{selectedReturns?<><PackageX className="mr-2 h-4 w-4"/>Editar</>:<><Plus className="mr-2 h-4 w-4"/>Agregar devolución</>}</Button></section>
          {hasCredit&&creditAvailable>0&&<Label className="flex items-start gap-3 rounded-xl border p-4"><Checkbox checked={collectCredit} onCheckedChange={value=>{setKeepCurrent(false);setCollectCredit(value===true);setPaymentMethod(value===true?"Cash":invoiceToCollect>0?"Cash":null);setForm(null);setCapture(null);}}/><span>Recaudar saldo de crédito<strong className="mt-1 block">{money.format(creditToCollect)}</strong><small className="mt-1 block font-normal text-muted-foreground">Se aplica al saldo de cartera al validar el cierre. Los abonos anteriores se conservan.</small></span></Label>}
          {immediateAmount>0?<>
            <Field label={keepCurrent?"Registrar un recaudo":`Pago recibido · ${money.format(immediateAmount)}`}><div className="grid grid-cols-2 gap-3">{paymentTypes.data?.filter(option=>["Cash","Transfer","DebitCard","CreditCard"].includes(option.code)).map(option=><PaymentButton key={option.code} active={paymentMethod===option.code} icon={option.code==="Cash"?Banknote:option.code==="Transfer"?ReceiptText:CreditCard} title={option.label} onClick={()=>selectType(option.code as "Cash"|"Transfer"|"DebitCard"|"CreditCard")}/>)}</div></Field>
            {paymentMethod==="Cash"&&(resolvingOriginal?<p role="status" className="text-sm text-muted-foreground">Cargando la forma de pago registrada…</p>:<PaymentMethodSelect key={paymentMethod} tenantId={tenant} businessId={business??""} usage="Receipts" methodCode={paymentMethod}
              className="min-h-11" wrapLabels autoSelectSingle={!recordedPayment?.paymentMethodId||selectedForm!==undefined} value={form?.paymentMethodId??""} name={form?.name} ariaLabel="Forma de pago de la entrega" onChange={selectForm}/>)}
            {originalForm.isError&&selectedForm===undefined&&<p role="alert" className="text-sm text-destructive">No fue posible consultar la forma registrada. <button type="button" className="underline" onClick={()=>void originalForm.refetch()}>Reintentar</button></p>}
            {multiplePaymentForms&&<p role="alert" className="text-sm text-destructive">Esta entrega tiene varias formas de pago. Consérvalas y revísalas desde el cierre; este formulario captura una sola forma.</p>}
            {form&&paymentMethod==="Cash"&&<p className="rounded-xl bg-muted/40 p-3 text-sm">Cuenta configurada: {form.accountCode} · {form.accountName}</p>}
            {paymentMethod&&paymentMethod!=="Cash"&&<div className="flex flex-wrap items-center justify-between gap-2 rounded-xl border p-3"><span className="min-w-0 flex-1 break-words text-sm [overflow-wrap:anywhere]">{form&&<strong className="mb-1 block">{form.name}</strong>}{card?(approval?`Aprobación: ${approval}`:"Faltan los datos de la tarjeta"):(paymentReference?`Referencia: ${paymentReference}`:"Faltan los datos de la transferencia")}</span><Button type="button" variant="outline" size="sm" onClick={()=>selectType(paymentMethod)}>Datos del pago</Button></div>}
            {paymentMethod&&paymentMethod!=="Cash"&&<PhotoInput label={card?"Foto del comprobante de tarjeta (opcional)":"Foto de la transferencia (opcional)"} file={depositProof} onChange={setDepositProof}/>}
            {depositEvidenceUrl&&!depositProof&&<a className="block text-sm font-semibold text-teal-700" href={depositReadUrl??undefined} target="_blank" rel="noreferrer">Ver soporte guardado</a>}
          </>:isCredit?<div className="space-y-2 rounded-2xl border bg-muted/30 p-4 text-sm"><Button type="button" variant="secondary" className="min-h-11 w-full" disabled><CreditCard className="mr-2 h-4 w-4"/>Crédito</Button><p>Se conserva el saldo de cartera hasta confirmar su recaudo.</p></div>:<p className="rounded-2xl border p-4 text-sm">{previouslyCollected?"Pago ya registrado en la sesión original. Confirma únicamente la entrega.":"Sin importe por recaudar después de la devolución."}</p>}
          {document.creditAmount>0&&<><PhotoInput label="Factura firmada por el cliente" file={signedProof} onChange={setSignedProof}/>{signedEvidenceUrl&&!signedProof&&<a className="block text-sm font-semibold text-teal-700" href={signedReadUrl??undefined} target="_blank" rel="noreferrer">Ver factura firmada guardada</a>}</>}
        </>}
        <Button type="button" aria-pressed={keepCurrent} variant={keepCurrent?"secondary":"outline"} className="min-h-14 w-full whitespace-normal rounded-xl px-4 py-3 text-left" onClick={()=>{setKeepCurrent(true);setCollectCredit(false);setPaymentMethod(null);setForm(null);setCapture(null);}}>Mantener forma de pago actual</Button>
        {keepCurrent&&<p className="rounded-xl bg-muted/30 p-3 text-sm">Se conserva el pago registrado. No se recauda dinero, no se cambia la cartera y no se mueve tu sesión de dinero.</p>}
        <Field label="Observaciones"><Textarea rows={2} className="min-h-20 resize-y" value={notes} onChange={event=>setNotes(event.target.value)} placeholder="Novedades de la entrega (opcional)" maxLength={500}/></Field>
      </div>
      <DialogFooter className="sticky -bottom-6 -mx-6 -mb-6 border-t bg-background px-6 py-4"><Button className="min-h-11" variant="outline" onClick={onClose}>Cancelar</Button><Button className="min-h-11 bg-teal-600 hover:bg-teal-700" disabled={mutation.isPending||invalid} onClick={()=>mutation.mutate()}>{mutation.isPending?"Guardando…":"Confirmar entrega"}</Button></DialogFooter>
    </DialogContent></Dialog>
    {capture?.card&&<CardPaymentDetailsDialog value={capture.card} franchises={franchises.data??[]} catalogFailed={franchises.isError}
      canSave={Boolean(captureForm&&captureForm.methodCode===paymentMethod)}
      paymentMethodField={paymentMethodField}
      useDialogHost approvalMaxLength={80} returnHint="Estos datos se guardan al confirmar la entrega."
      onChange={value=>setCapture(current=>current?{...current,card:value}:null)} onSave={savePaymentDetails} onClose={()=>setCapture(null)}/>}
    {capture?.transfer&&<TransferPaymentDetailsDialog value={capture.transfer} paymentMethodField={paymentMethodField}
      accountLabel={captureForm?`${captureForm.name} · ${captureForm.accountCode} · ${captureForm.accountName}`:"Selecciona una forma de transferencia"} canSave={Boolean(captureForm?.methodCode==="Transfer"&&captureForm.bankAccountId)} useDialogHost referenceMaxLength={120} showNotes={false}
      onChange={value=>setCapture(current=>current?{...current,transfer:value}:null)} onSave={savePaymentDetails} onClose={()=>setCapture(null)}/>}
    {returnsOpen&&<ReturnsDialog document={document} open values={returns} reasons={returnReasons.data??[]} loading={returnReasons.isPending}
      reasonCode={returnReason?.code??document.returns[0]?.reasonCode} failed={returnReasons.isError}
      onClose={()=>setReturnsOpen(false)} onSave={(values,reason)=>{setReturns(values);setReturnReason(reason);setReturnsOpen(false)}}/>}
  </>;
}

function ReturnsDialog({document,open,values,reasons,loading,failed,reasonCode,onClose,onSave}:{document:DeliveryDocument;open:boolean;values:Record<number,string>;reasons:DispatchReasonOption[];loading:boolean;failed:boolean;reasonCode?:string;onClose:()=>void;onSave:(values:Record<number,string>,reason:DispatchReasonOption|null)=>void}){
  const [draft,setDraft]=useState(values);
  const [search,setSearch]=useState("");
  const [selectedReason,setSelectedReason]=useState(reasonCode??"");
  const activeReason=reasons.find(reason=>reason.code===selectedReason)??reasons[0]??null;
  useEffect(()=>{if(open){setDraft(values);setSearch("")}},[open,values]);
  const selected=Object.values(draft).filter(value=>Number(value)>0).length;
  const amount=estimateDeliveryReturn(document,draft);
  const normalizedSearch=search.trim().toLocaleLowerCase("es");
  const visibleLines=document.lines.filter(line=>Number(draft[line.originalLineNumber]||0)>0||!normalizedSearch||`${line.productCode} ${line.description}`.toLocaleLowerCase("es").includes(normalizedSearch));
  return <Dialog open={open} onOpenChange={value=>!value&&onClose()}><DialogContent className="max-h-[92dvh] max-w-2xl overflow-y-auto rounded-3xl"><DialogHeader><DialogTitle>Agregar productos devueltos</DialogTitle><DialogDescription>Busca y registra únicamente lo que el cliente devuelve.</DialogDescription></DialogHeader><Field label="Motivo de devolución"><Select value={activeReason?.code??""} onValueChange={setSelectedReason} disabled={loading||failed}><SelectTrigger aria-label="Motivo de devolución"><SelectValue placeholder={loading?"Cargando motivos…":"Selecciona el motivo"}/></SelectTrigger><SelectContent>{reasons.map(reason=><SelectItem key={reason.code} value={reason.code}>{reason.name}</SelectItem>)}</SelectContent></Select>{failed&&<p role="alert" className="text-sm text-destructive">No fue posible cargar los motivos de devolución.</p>}{!loading&&!failed&&!reasons.length&&<p role="alert" className="text-sm text-destructive">Configura un motivo activo para devoluciones de entrega.</p>}</Field><label className="relative"><Search className="absolute left-3 top-3 h-4 w-4 text-muted-foreground"/><Input className="pl-9" value={search} onChange={event=>setSearch(event.target.value)} placeholder="Buscar por código o nombre"/></label><div className="space-y-2">{visibleLines.map(line=><div key={line.originalLineNumber} className="grid grid-cols-[minmax(0,1fr)_6rem] items-center gap-3 rounded-2xl border p-3"><span className="min-w-0"><strong className="block truncate text-sm">{line.productCode} · {line.description}</strong><small className="text-muted-foreground">Disponible: {line.quantity-(line.returnedQuantity??0)} · {money.format(line.lineTotal-(line.returnedLineTotal??0))}</small></span><Input aria-label={`Cantidad devuelta de ${line.description}`} inputMode="decimal" type="number" min="0" max={line.quantity-(line.returnedQuantity??0)} step="any" value={draft[line.originalLineNumber]??""} onChange={event=>setDraft(current=>({...current,[line.originalLineNumber]:event.target.value}))} placeholder="0"/></div>)}{visibleLines.length===0&&<p className="rounded-2xl border border-dashed p-8 text-center text-sm text-muted-foreground">No hay productos que coincidan con la búsqueda.</p>}</div><div className="flex items-center justify-between rounded-2xl bg-muted p-3"><span><small className="block text-muted-foreground">Devolución seleccionada</small><strong>{selected} producto(s) · {money.format(amount)}</strong></span>{selected>0&&<Button variant="ghost" size="sm" onClick={()=>setDraft({})}><Trash2 className="mr-1 h-4 w-4"/>Limpiar</Button>}</div><DialogFooter><Button variant="outline" onClick={onClose}>Cancelar</Button><Button disabled={selected>0&&!activeReason} onClick={()=>onSave(draft,activeReason)}>Aplicar devolución</Button></DialogFooter></DialogContent></Dialog>;
}

function PaymentButton({active,icon:Icon,title,onClick}:{active:boolean;icon:typeof Banknote;title:string;onClick:()=>void}){return <button type="button" onClick={onClick} className={`flex h-16 min-w-0 items-center justify-center gap-1.5 rounded-2xl border-2 px-2 text-sm font-bold transition sm:gap-2 sm:text-base ${active?"border-teal-500 bg-teal-50 text-teal-800":"border-border bg-background hover:border-teal-300"}`}><Icon className="h-5 w-5 shrink-0"/><span className="min-w-0 break-words leading-tight">{title}</span>{active&&<Check className="h-4 w-4 shrink-0"/>}</button>}
function DeliveryResultButton({active,icon:Icon,title,onClick}:{active:boolean;icon:typeof PackageCheck;title:string;onClick:()=>void}){return <button type="button" onClick={onClick} className={`flex min-h-20 flex-col items-center justify-center gap-2 rounded-2xl border-2 p-3 text-center text-sm font-bold transition ${active?"border-teal-500 bg-teal-50 text-teal-800":"border-border bg-background hover:border-teal-300"}`}><Icon className="h-6 w-6"/><span>{title}</span></button>}

function DispatchExpensesPanel({data,onExpense}:{data:DispatchExecution;onExpense?:()=>void}){
  return <div className="space-y-3"><div className="flex items-center justify-between gap-3"><span><h2 className="font-black">Gastos del recorrido</h2><p className="text-xs text-muted-foreground">{onExpense?"Regístralos en el momento en que ocurren.":"Consulta los gastos reportados por el transportador."}</p></span>{onExpense&&<Button onClick={onExpense}><Plus className="mr-2 h-4 w-4"/>Registrar gasto</Button>}</div>{data.expenses.length===0?<div className="rounded-3xl border border-dashed p-8 text-center"><ReceiptText className="mx-auto h-9 w-9 text-teal-600"/><strong className="mt-3 block">Sin gastos registrados</strong><p className="mt-1 text-sm text-muted-foreground">Peajes, parqueaderos y otros gastos aparecerán aquí.</p></div>:data.expenses.map(expense=><div key={expense.expenseId} className="flex items-center justify-between gap-3 rounded-2xl border bg-card p-4"><span><strong className="block">{expense.category} · {money.format(expense.amount)}</strong>{expense.description&&<small className="text-muted-foreground">{expense.description}</small>}</span><Badge variant={expense.approvalStatus==="Approved"?"default":expense.approvalStatus==="Rejected"?"destructive":"secondary"}>{expense.approvalStatus==="Pending"?"Pendiente":expense.approvalStatus==="Approved"?"Aprobado":"Rechazado"}</Badge></div>)}</div>;
}

export function DispatchSettlementPanel({data,onChanged,onPaymentReview,readOnly=false}:{data:DispatchExecution;onChanged:(updated:DispatchExecution)=>void;onPaymentReview:(updated:DispatchPaymentReviewResult)=>void;readOnly?:boolean}){
  const summary=data.settlement;
  const cashCollected=data.documents.flatMap(item=>item.payments).filter(item=>item.paymentMethod==="Cash").reduce((sum,item)=>sum+item.amount,0);
  const returnsTotal=data.documents.reduce((sum,document)=>sum+(document.returnTotal??0),0);
  const expectedCash=summary?.expectedCash??cashCollected;
  const [cash,setCash]=useState(()=>formatMoneyValue(summary?.cashReceived??summary?.declaredCash??expectedCash,4));
  const [notes,setNotes]=useState("");
  const [differenceForm,setDifferenceForm]=useState<PaymentMethod|null>(null);
  const [returnSelections,setReturnSelections]=useState<DispatchReturnSelections>(()=>{
    const selected:DispatchReturnSelections={};
    for(const item of data.linkedReturns??[])(selected[item.originalDocumentId]??=[]).push({value:item.returnId,label:item.documentNumber,
      description:`${item.economicResolution==="Refund"?"Reembolso":"Aplicación a cartera"} · ${money.format(item.totalAmount)}`});
    return selected;
  });
  const tenantId=useTenantContextStore(state=>state.selectedTenantId);
  const businessId=useBusinessContextStore(state=>state.selectedBusinessId);
  const cashFormIds=new Set(data.documents.flatMap(item=>item.payments).filter(item=>item.paymentMethod==="Cash"&&item.paymentMethodId).map(item=>item.paymentMethodId));
  const [approving,setApproving]=useState<DispatchExpense|null>(null),[rejecting,setRejecting]=useState<DispatchExpense|null>(null);
  const [action,setAction]=useState<"settle"|null>(null);
  const [settleKey]=useState(()=>crypto.randomUUID());
  const [actionError,setActionError]=useState<string|null>(null);
  const difference=parseMoneyDraft(cash)-expectedCash;
  const settle=async()=>{if(action)return;setAction("settle");setActionError(null);try{const updated=await dispatchesApi.settle(data.dispatchId,parseMoneyDraft(cash),difference===0?notes||null:notes,settleKey,difference!==0&&cashFormIds.size!==1?differenceForm?.paymentMethodId:undefined,Object.values(returnSelections).flatMap(items=>items.map(item=>item.value)));toast.success("Liquidación enviada. Usa Actualizar para consultar el resultado.");onChanged(updated)}catch(error){const message=errorMessage(error,"No fue posible aceptar la liquidación.");setActionError(message);toast.error(message)}finally{setAction(null)}};
  const approve=(accepted:ExpenseAcceptance)=>{
    const result=accepted.dispatchApproval;
    if(!result||result.dispatchId!==data.dispatchId||result.dispatchExpenseId!==approving?.expenseId)throw new Error("El gasto fue aceptado, pero falta el resultado de aprobación del despacho. Actualiza para comprobarlo.");
    const expenses=data.expenses.map(item=>item.expenseId===result.dispatchExpenseId?{...item,approvalStatus:"Approved" as const,approvedAmount:result.approvedCash,acceptedExpenseId:accepted.expenseId,acceptedExpenseStatus:accepted.status}:item);
    onChanged({...data,expenses,settlement:data.settlement?{...data.settlement,expectedCash:result.expectedCash,approvedCashExpenses:expenses.reduce((sum,item)=>sum+(item.approvalStatus==="Approved"?item.approvedAmount??0:0),0),difference:(data.settlement.cashReceived??data.settlement.declaredCash)-result.expectedCash}:null});
    setApproving(null);
  };
  const pending=data.documents.filter(item=>item.deliveryStatus==="Pending").length;
  const processing=summary?.status==="Processing"||data.status==="SettlementProcessing";
  const cashLocked=readOnly||processing||data.status==="SettlementAttention"||data.status==="Closed";
  const cashContent=(
    <Card className="overflow-hidden rounded-3xl"><CardContent className="space-y-5 p-5">
      {summary&&summary.balanceDifference!==0&&<p role="alert" className="rounded-xl bg-amber-50 p-3 text-sm text-amber-900">El despacho tiene una diferencia comercial de {money.format(summary.balanceDifference)}. Revisa entregas, devoluciones y recaudos antes de aceptar.</p>}
      <div className="grid items-end gap-4 sm:grid-cols-2">
        <div className="min-w-0 space-y-2"><Label htmlFor={`dispatch-received-${data.dispatchId}`} className="block text-sm leading-5">Efectivo físico recibido</Label>
          <div className="relative"><span aria-hidden="true" className="pointer-events-none absolute inset-y-0 left-4 flex items-center text-xl font-semibold text-muted-foreground">$</span>
            <Input id={`dispatch-received-${data.dispatchId}`} inputMode="decimal" value={cash} disabled={cashLocked}
              onChange={event=>setCash(formatMoneyDraft(event.target.value,4))}
              className="h-16 rounded-2xl pl-10 pr-4 text-right text-2xl font-bold tabular-nums md:text-2xl"/>
          </div>
        </div>
        <div className="min-w-0 space-y-2"><span className="block text-sm font-medium leading-5">Resultado del efectivo</span>
          <div role="status" aria-live="polite" aria-label="Resultado del efectivo" className={`flex h-16 items-center justify-between gap-3 rounded-2xl border px-4 ${difference>0?"border-emerald-200 bg-emerald-50 text-emerald-900":difference<0?"border-red-200 bg-red-50 text-red-900":"border-slate-200 bg-slate-50 text-slate-700"}`}>
            <span className="text-sm font-medium">{difference>0?"Sobrante":difference<0?"Faltante":"Cuadra"}</span><strong className="text-right text-2xl font-bold tabular-nums">{money.format(Math.abs(difference))}</strong>
          </div>
        </div>
      </div>
      {difference!==0&&cashFormIds.size!==1&&<Field label="Efectivo que asume la diferencia"><PaymentMethodSelect tenantId={tenantId} businessId={businessId??""} usage="Receipts" methodCode="Cash" value={differenceForm?.paymentMethodId??""} name={differenceForm?.name} disabled={cashLocked} ariaLabel="Forma de efectivo para la diferencia" onChange={setDifferenceForm} autoSelectSingle wrapLabels/></Field>}
      {difference!==0&&<Field label="Explicación obligatoria"><Input disabled={cashLocked} value={notes} onChange={event=>setNotes(event.target.value)} placeholder={difference>0?"¿Por qué sobró efectivo?":"¿Por qué faltó efectivo?"}/></Field>}

      {actionError&&<p role="alert" className="rounded-2xl border border-red-200 bg-red-50 p-3 text-sm font-medium text-red-800">{actionError}</p>}
      {pending>0&&<p className="text-sm text-amber-700">Faltan {pending} entregas por registrar.</p>}
      {data.status==="SettlementAttention"&&<p className="rounded-2xl bg-red-50 p-3 text-sm font-medium text-red-800">La liquidación requiere atención. Revisa y reprocesa su conciliación en Trazabilidad financiera; después usa Actualizar para comprobar el resultado.</p>}
    </CardContent></Card>
  );
  return <div className="space-y-4">
    <div className="grid gap-3 sm:grid-cols-2">
      <MoneyCard icon={PackageCheck} label="Devoluciones" value={summary?.returnTotal??returnsTotal}/>
      <MoneyCard icon={CircleDollarSign} label="Gastos aprobados" value={summary?.approvedCashExpenses??0}/>
    </div>
    {(summary?.previouslyCollectedTotal??0)>0&&<p className="rounded-2xl border bg-muted/30 p-4 text-sm">Pagos conservados o recaudados fuera del despacho: <strong>{money.format(summary!.previouslyCollectedTotal!)}</strong>. Este valor no se vuelve a recaudar en el cierre.</p>}
    <DispatchPaymentGroups data={data} editable={!readOnly&&summary?.status==="PendingReview"} cashContent={cashContent} onChanged={onPaymentReview}/>
    <DispatchReturnLinks data={data} selections={returnSelections} onChange={setReturnSelections} disabled={readOnly||summary?.status!=="PendingReview"||action!==null}/>
    <section className="space-y-2"><h3 className="font-bold">Gastos presentados</h3>{data.expenses.length===0&&<div className="flex items-center gap-3 rounded-2xl border border-dashed bg-muted/20 p-4"><span className="rounded-2xl bg-teal-50 p-3 text-teal-700"><ReceiptText className="h-5 w-5"/></span><span><strong className="block text-sm">Sin gastos registrados</strong><small className="text-muted-foreground">El transportador no reportó gastos para este recorrido.</small></span></div>}{data.expenses.map(expense=><div key={expense.expenseId} className="flex flex-col gap-3 rounded-2xl border bg-card p-3 sm:flex-row sm:items-center sm:justify-between"><span><strong>{expense.category} · {money.format(expense.amount)}</strong>{expense.description&&<small className="block text-muted-foreground">{expense.description}</small>}{expense.evidenceUrl&&<a href={expense.evidenceReadUrl??expense.evidenceUrl} target="_blank" rel="noreferrer" className="text-xs font-semibold text-teal-700">Ver soporte</a>}{expense.approvalStatus==="Approved"&&<small className="block text-muted-foreground">{!expense.acceptedExpenseId?"Falta conciliar su documento de gasto":expense.acceptedExpenseStatus==="Processed"?"Gasto registrado":"Gasto aceptado, pendiente de procesamiento. Usa Actualizar para comprobarlo."}</small>}</span><div className="flex flex-wrap items-center gap-2"><Badge variant={expense.approvalStatus==="Approved"?"default":expense.approvalStatus==="Rejected"?"destructive":"secondary"}>{expense.approvalStatus==="Pending"?"Pendiente":expense.approvalStatus==="Approved"?"Aprobado":"Rechazado"}</Badge>{!readOnly&&summary?.status==="PendingReview"&&expense.approvalStatus==="Pending"&&<><Button size="sm" variant="outline" onClick={()=>setRejecting(expense)}>Rechazar</Button><Button size="sm" onClick={()=>setApproving(expense)}>Aprobar</Button></>}</div></div>)}</section>
    {approving&&<DispatchExpenseApprovalDialog dispatchId={data.dispatchId} expense={approving} onClose={()=>setApproving(null)} onSaved={approve}/>}
    {rejecting&&<RejectDispatchExpenseDialog dispatchId={data.dispatchId} expense={rejecting} onClose={()=>setRejecting(null)} onSaved={updated=>{setRejecting(null);onChanged(updated)}}/>}
    {!readOnly&&summary?.status==="PendingReview"&&<Button className="h-12 w-full bg-teal-600 hover:bg-teal-700" disabled={action!==null||summary.balanceDifference!==0||data.expenses.some(item=>item.approvalStatus==="Pending"||item.approvalStatus==="Approved"&&(!item.acceptedExpenseId||item.acceptedExpenseStatus!=="Processed"))||data.documents.some(document=>document.payments.some(payment=>(payment.applicationType==="InvoicePayment"||payment.applicationType==="CreditAdvance")&&payment.paymentMethod!=="Cash"&&!payment.verifiedAt))||difference!==0&&(!notes.trim()||cashFormIds.size!==1&&!differenceForm)} onClick={settle}>{action==="settle"?"Procesando…":"Aceptar liquidación"}</Button>}

  </div>;
}

function ExpenseDialog({dispatchId,onClose,onSaved}:{dispatchId:string;onClose:()=>void;onSaved:(updated:DispatchExecution)=>void}){
  const tenant=useTenantContextStore(state=>state.selectedTenantId),business=useBusinessContextStore(state=>state.selectedBusinessId);
  const [concept,setConcept]=useState<ExpenseConceptOption|null>(null),[amount,setAmount]=useState(""),[description,setDescription]=useState("");
  const [proof,setProof]=useState<File|null>(null),[evidence,setEvidence]=useState<string|null>(null),[saving,setSaving]=useState(false);
  const [key]=useState(()=>crypto.randomUUID()),[occurredAt]=useState(()=>new Date().toISOString());
  const save=async()=>{if((!concept&&!description.trim())||saving)return;setSaving(true);try{let url=evidence;if(proof&&!url){url=(await dispatchesApi.uploadEvidence(dispatchId,proof)).url;setEvidence(url)}const updated=await dispatchesApi.addExpense(dispatchId,{category:concept?.name??description.trim(),conceptId:concept?.conceptId??null,amount:parseMoneyDraft(amount),description:description.trim()||null,evidenceUrl:url,idempotencyKey:key,occurredAt});toast.success("Gasto enviado para aprobación");onSaved(updated)}catch(error){toast.error(errorMessage(error,"No fue posible guardar el gasto."))}finally{setSaving(false)}};
  return <Dialog open onOpenChange={open=>!open&&!saving&&onClose()}><DialogContent showClose={!saving} className="max-h-[92dvh] w-[calc(100%-1rem)] overflow-y-auto rounded-3xl"><DialogHeader><DialogTitle>Otro gasto del despacho</DialogTitle><DialogDescription>El reporte no crea todavía un gasto financiero. El administrativo debe aprobarlo y completar su soporte.</DialogDescription></DialogHeader><fieldset disabled={saving} className="space-y-4">
    <Field label="Concepto de gasto"><PagedEntitySelect<ExpenseConceptOption> queryKey={["expense-concept-options",tenant,business]} value={concept?.conceptId??""}
      disabled={typeof navigator!=="undefined"&&!navigator.onLine} ariaLabel="Concepto de gasto del despacho" placeholder="Selecciona un concepto (opcional)" searchPlaceholder="Buscar concepto…" className="min-h-11" wrapLabels
      selectedOption={concept?{value:concept.conceptId,label:concept.name,description:concept.code}:null}
      getOption={item=>({value:item.conceptId,label:item.name,description:item.code})}
      loadPage={(search,page,pageSize)=>expensesApi.conceptOptions({search,page,pageSize})} onChange={(_value,_option,item)=>{if(item)setConcept(item)}}/></Field>
    <div className="space-y-2"><Label htmlFor="dispatch-expense-amount">Valor</Label><Input id="dispatch-expense-amount" inputMode="decimal" className="h-12 text-lg" value={amount} onChange={event=>setAmount(formatMoneyDraft(event.target.value,4))}/></div>
    <div className="space-y-2"><Label htmlFor="dispatch-expense-description">{concept?"Descripción (opcional)":"Describe el gasto"}</Label><Input id="dispatch-expense-description" maxLength={concept?300:120} value={description} onChange={event=>setDescription(event.target.value)}/></div>
    {!concept&&<p className="text-xs text-muted-foreground">Puedes describir el gasto si no encuentras un concepto o estás sin conexión. El administrativo lo clasificará al aprobarlo.</p>}<PhotoInput label="Foto del soporte (opcional)" file={proof} onChange={file=>{setProof(file);setEvidence(null)}}/>
  </fieldset><DialogFooter><Button className="min-h-11" variant="outline" disabled={saving} onClick={onClose}>Cancelar</Button><Button className="min-h-11" disabled={saving||(!concept&&!description.trim())||parseMoneyDraft(amount)<=0||parseMoneyDraft(amount)>999999999999} onClick={()=>void save()}>{saving?"Guardando reporte…":"Guardar gasto"}</Button></DialogFooter></DialogContent></Dialog>;
}

function RejectDispatchExpenseDialog({dispatchId,expense,onClose,onSaved}:{dispatchId:string;expense:DispatchExpense;onClose:()=>void;onSaved:(updated:DispatchExecution)=>void}){
  const [notes,setNotes]=useState(""),[saving,setSaving]=useState(false),[error,setError]=useState<string|null>(null);
  const [key]=useState(()=>crypto.randomUUID());
  const save=async()=>{if(!notes.trim()||saving)return;setSaving(true);setError(null);try{const updated=await dispatchesApi.reviewExpense(dispatchId,expense.expenseId,{decision:"Rejected",approvedAmount:null,notes:notes.trim(),idempotencyKey:key});toast.success("Gasto rechazado; no se descuenta del efectivo");onSaved(updated)}catch(error){setError(errorMessage(error,"No fue posible rechazar el gasto."))}finally{setSaving(false)}};
  return <Dialog open onOpenChange={open=>!open&&!saving&&onClose()}><DialogContent showClose={!saving} className="max-h-[92dvh] w-[calc(100%-1rem)] overflow-y-auto rounded-3xl"><DialogHeader><DialogTitle>Rechazar gasto del despacho</DialogTitle><DialogDescription>{expense.category} · {money.format(expense.amount)}. El rechazo no reduce el efectivo esperado.</DialogDescription></DialogHeader><div className="space-y-2"><Label htmlFor="dispatch-rejection-reason">Motivo del rechazo</Label><Textarea id="dispatch-rejection-reason" disabled={saving} maxLength={500} value={notes} onChange={event=>setNotes(event.target.value)}/></div>{error&&<p role="alert" className="text-sm text-destructive">{error}</p>}<DialogFooter><Button variant="outline" disabled={saving} onClick={onClose}>Cancelar</Button><Button disabled={saving||!notes.trim()} onClick={()=>void save()}>{saving?"Guardando rechazo…":"Confirmar rechazo"}</Button></DialogFooter></DialogContent></Dialog>;
}
function PhotoInput({label,file,onChange}:{label:string;file:File|null;onChange:(file:File|null)=>void}){return <label className="block cursor-pointer rounded-2xl border-2 border-dashed p-4 text-center transition hover:border-teal-400 hover:bg-teal-50/40"><Camera className="mx-auto h-6 w-6 text-teal-600"/><strong className="mt-1 block text-sm">{file?file.name:label}</strong><small className="text-muted-foreground">Tomar foto o elegir imagen</small><input className="sr-only" type="file" accept="image/*" capture="environment" onChange={event=>onChange(event.target.files?.[0]??null)}/></label>}
function HeroMetric({label,value}:{label:string;value:number}){return <div className="rounded-2xl bg-white/10 p-3 text-center backdrop-blur"><strong className="block text-2xl">{value}</strong><small className="text-teal-100">{label}</small></div>}
function Tab({active,onClick,icon:Icon,children}:{active:boolean;onClick:()=>void;icon:typeof Map;children:React.ReactNode}){return <button onClick={onClick} className={`flex items-center justify-center rounded-xl px-2 py-2 text-sm font-semibold transition ${active?"bg-background text-foreground shadow":"text-muted-foreground"}`}><Icon className="mr-1 h-4 w-4"/>{children}</button>}
function Field({label,children}:{label:string;children:React.ReactNode}){return <div className="space-y-2"><Label>{label}</Label>{children}</div>}
function MiniMoney({label,value}:{label:string;value:number}){return <div><small className="text-slate-400">{label}</small><strong className="block text-sm sm:text-base">{money.format(value)}</strong></div>}
function MoneyCard({icon:Icon,label,value}:{icon:typeof Banknote;label:string;value:number}){return <Card className="rounded-3xl"><CardContent className="flex items-center gap-3 p-4"><span className="rounded-2xl bg-teal-50 p-3 text-teal-700"><Icon className="h-5 w-5"/></span><span><small className="text-muted-foreground">{label}</small><strong className="block text-lg">{money.format(value)}</strong></span></CardContent></Card>}
function DispatchBadge({status}:{status:string}){const labels:Record<string,string>={Released:"Listo para salir",InDelivery:"En ruta",PendingSettlement:"Por liquidar",SettlementProcessing:"Procesando",SettlementAttention:"Requiere atención",Closed:"Liquidado",Draft:"Borrador",Prepared:"Preparado",InVerification:"Verificando",Verified:"Verificado",Cancelled:"Cancelado"};return <Badge variant={status==="Closed"?"default":status==="SettlementAttention"||status==="Cancelled"?"destructive":"secondary"}>{labels[status]??status}</Badge>}
function DeliveryBadge({status}:{status:string}){const labels:Record<string,string>={Pending:"Pendiente",Delivered:"Entregado",PartiallyDelivered:"Parcial",NotDelivered:"No entregado"};return <Badge variant={status==="Delivered"?"default":status==="NotDelivered"?"destructive":"secondary"}>{labels[status]??status}</Badge>}
async function currentPosition(){if(typeof navigator==="undefined"||!navigator.geolocation)return null;return new Promise<{latitude:number;longitude:number}|null>(resolve=>navigator.geolocation.getCurrentPosition(value=>resolve({latitude:value.coords.latitude,longitude:value.coords.longitude}),()=>resolve(null),{enableHighAccuracy:true,timeout:7000,maximumAge:60000}))}
function errorMessage(error:unknown,fallback:string){return error&&typeof error==="object"&&"message" in error&&typeof error.message==="string"?error.message:fallback}
