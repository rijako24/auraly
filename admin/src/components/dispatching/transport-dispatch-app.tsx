"use client";

import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowDown, ArrowLeft, ArrowUp, Banknote, Camera, Check, ChevronRight, CircleDollarSign, Map, MapPin, PackageCheck, PackageX, Plus, ReceiptText, Route, ShieldCheck, Trash2, Truck } from "lucide-react";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { RouteLocationMap } from "@/components/maps/route-location-map";
import { Card, CardContent } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import { dispatchesApi, type DeliveryDocument, type DeliveryResultInput, type DispatchExecution, type DispatchListItem } from "@/services/api/dispatches";

const money=new Intl.NumberFormat("es-CO",{style:"currency",currency:"COP",maximumFractionDigits:0});
const executionKey=(id:string)=>["dispatches","execution",id] as const;

export function TransportDispatchApp({canSettle=false}:{canSettle?:boolean}){
  const [selected,setSelected]=useState<DispatchListItem|null>(null);
  const query=useQuery({queryKey:["dispatches","my-deliveries"],queryFn:()=>dispatchesApi.page({page:1,pageSize:50})});
  if(selected)return <DispatchExecutionWorkspace dispatch={selected} canSettle={canSettle} onBack={()=>setSelected(null)}/>;
  return <div className="mx-auto max-w-5xl space-y-5 pb-24">
    <section className="overflow-hidden rounded-[2rem] bg-gradient-to-br from-slate-950 via-teal-950 to-teal-700 p-6 text-white shadow-xl md:p-8">
      <div className="flex items-start justify-between gap-4"><div><p className="text-sm font-semibold text-teal-200">Centro de entregas</p><h1 className="mt-1 text-3xl font-black tracking-tight">Mis despachos</h1><p className="mt-2 max-w-xl text-sm text-teal-50/80">Facturas, clientes, recaudos y novedades en un solo recorrido.</p></div><span className="rounded-2xl bg-white/10 p-3"><Truck className="h-7 w-7"/></span></div>
      <div className="mt-6 grid grid-cols-3 gap-2"><HeroMetric label="Asignados" value={query.data?.items.length??0}/><HeroMetric label="En ruta" value={query.data?.items.filter(x=>x.status==="InDelivery").length??0}/><HeroMetric label="Por liquidar" value={query.data?.items.filter(x=>x.status==="PendingSettlement").length??0}/></div>
    </section>
    <div className="grid gap-3 md:grid-cols-2">{query.data?.items.map(item=><button key={item.dispatchId} onClick={()=>setSelected(item)} className="group rounded-3xl border bg-card p-5 text-left shadow-sm transition hover:-translate-y-0.5 hover:border-teal-300 hover:shadow-lg"><div className="flex items-center justify-between gap-3"><span className="rounded-xl bg-teal-50 p-2 text-teal-700"><Route className="h-5 w-5"/></span><DispatchBadge status={item.status}/></div><h2 className="mt-4 text-xl font-black">{item.dispatchNumber}</h2><p className="mt-1 text-sm text-muted-foreground">{item.scheduledDate} · {item.vehiclePlate??"Sin placa"}</p><div className="mt-5 flex items-center justify-between border-t pt-4"><span className="text-sm font-semibold">{item.documentCount} entregas</span><ChevronRight className="h-5 w-5 transition group-hover:translate-x-1"/></div></button>)}</div>
    {!query.isLoading&&!query.data?.items.length&&<div className="rounded-3xl border border-dashed p-12 text-center"><PackageCheck className="mx-auto h-10 w-10 text-teal-600"/><h2 className="mt-3 font-bold">No tienes despachos asignados</h2><p className="mt-1 text-sm text-muted-foreground">Los nuevos cargues aparecerán automáticamente.</p></div>}
  </div>;
}

export function DispatchExecutionWorkspace({dispatch,canSettle,onBack}:{dispatch:DispatchListItem;canSettle:boolean;onBack:()=>void}){
  const cache=useQueryClient();const query=useQuery({queryKey:executionKey(dispatch.dispatchId),queryFn:()=>dispatchesApi.execution(dispatch.dispatchId)});
  const [tab,setTab]=useState<"list"|"map"|"settlement">("list"),[active,setActive]=useState<DeliveryDocument|null>(null),[expenseOpen,setExpenseOpen]=useState(false);
  const data=query.data;const completed=data?.documents.filter(x=>x.deliveryStatus!=="Pending").length??0;const total=data?.documents.length??0;
  const deliveryEnabled=data?.status==="Released"||data?.status==="InDelivery";
  const move=async(documentId:string,direction:-1|1)=>{if(!data)return;const ids=data.documents.map(x=>x.dispatchSourceDocumentId),index=ids.indexOf(documentId),target=index+direction;if(target<0||target>=ids.length)return;[ids[index],ids[target]]=[ids[target],ids[index]];try{await dispatchesApi.reorder(data.dispatchId,ids,dispatch.rowVersion);await query.refetch()}catch(error){toast.error(errorMessage(error,"No fue posible cambiar el orden."))}};
  return <div className="mx-auto max-w-6xl space-y-4 pb-28">
    <section className="sticky top-0 z-20 -mx-4 border-b bg-background/95 px-4 pb-4 pt-2 backdrop-blur md:static md:mx-0 md:rounded-3xl md:border md:p-5">
      <div className="flex items-center gap-3"><Button size="icon" variant="ghost" onClick={onBack}><ArrowLeft className="h-5 w-5"/></Button><div className="min-w-0 flex-1"><div className="flex items-center gap-2"><h1 className="truncate text-xl font-black">{data?.dispatchNumber??dispatch.dispatchNumber}</h1><DispatchBadge status={data?.status??dispatch.status}/></div><p className="truncate text-xs text-muted-foreground">{data?.driverName??dispatch.driverName} · {data?.vehiclePlate??"Sin placa"}</p></div><span className="text-right"><strong className="block text-lg">{completed}/{total}</strong><small className="text-muted-foreground">entregas</small></span></div>
      <div className="mt-4 h-2 overflow-hidden rounded-full bg-muted"><div className="h-full rounded-full bg-teal-500 transition-all" style={{width:`${total?completed/total*100:0}%`}}/></div>
      <div className="mt-4 grid grid-cols-3 gap-2 rounded-2xl bg-muted p-1"><Tab active={tab==="list"} onClick={()=>setTab("list")} icon={ReceiptText}>Lista</Tab><Tab active={tab==="map"} onClick={()=>setTab("map")} icon={Map}>Mapa</Tab><Tab active={tab==="settlement"} onClick={()=>setTab("settlement")} icon={CircleDollarSign}>Cierre</Tab></div>
    </section>
    {query.isLoading&&<p className="p-10 text-center text-muted-foreground">Cargando despacho…</p>}
    {data&&!deliveryEnabled&&<div className="rounded-2xl border border-amber-200 bg-amber-50 p-4 text-sm text-amber-900"><strong className="block">Este despacho todavía no está liberado</strong>Termina la verificación y pulsa “Liberar despacho”. Las entregas se habilitarán cuando quede listo para salir.</div>}
    {data&&tab==="list"&&<div className="space-y-3">{data.documents.map((document,index)=><DeliveryCard key={document.dispatchSourceDocumentId} document={document} index={index} total={data.documents.length} enabled={deliveryEnabled} onOpen={()=>deliveryEnabled&&setActive(document)} onMove={direction=>move(document.dispatchSourceDocumentId,direction)}/>)}</div>}
    {data&&tab==="map"&&<DispatchMap data={data} onOpen={document=>deliveryEnabled&&setActive(document)}/>}
    {data&&tab==="settlement"&&<SettlementPanel data={data} canSettle={canSettle} onChanged={()=>query.refetch()} onExpense={()=>setExpenseOpen(true)}/>}
    {active&&data&&<DeliveryDialog dispatchId={data.dispatchId} document={active} onClose={()=>setActive(null)} onSaved={async()=>{setActive(null);await cache.invalidateQueries({queryKey:executionKey(data.dispatchId)});await query.refetch()}}/>}
    {expenseOpen&&data&&<ExpenseDialog dispatchId={data.dispatchId} onClose={()=>setExpenseOpen(false)} onSaved={async()=>{setExpenseOpen(false);await query.refetch()}}/>}
  </div>;
}

function DeliveryCard({document,index,total,enabled,onOpen,onMove}:{document:DeliveryDocument;index:number;total:number;enabled:boolean;onOpen:()=>void;onMove:(direction:-1|1)=>void}){
  const done=document.deliveryStatus!=="Pending";
  return <Card className={`overflow-hidden rounded-3xl transition ${done?"border-emerald-200 bg-emerald-50/30":enabled?"hover:border-teal-300 hover:shadow-md":"opacity-75"}`}><CardContent className="p-0"><div className="flex gap-3 p-4"><span className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-2xl font-black ${done?"bg-emerald-500 text-white":"bg-slate-900 text-white"}`}>{done?<Check className="h-5 w-5"/>:index+1}</span><button disabled={!enabled} onClick={onOpen} className="min-w-0 flex-1 text-left disabled:cursor-not-allowed"><div className="flex items-start justify-between gap-2"><span><strong className="block truncate text-base">{document.customerName}</strong><small className="text-muted-foreground">{document.documentNumber} · {money.format(document.documentTotal)}</small></span><DeliveryBadge status={document.deliveryStatus}/></div><p className="mt-2 flex items-start gap-1 text-sm text-muted-foreground"><MapPin className="mt-0.5 h-4 w-4 shrink-0"/>{document.deliveryAddress??"Ubicación pendiente"}</p></button></div><div className="flex border-t bg-background/70"><Button variant="ghost" className="flex-1 rounded-none" disabled={!enabled||index===0||done} onClick={()=>onMove(-1)}><ArrowUp className="mr-1 h-4 w-4"/>Subir</Button><Button variant="ghost" className="flex-1 rounded-none" disabled={!enabled||index===total-1||done} onClick={()=>onMove(1)}><ArrowDown className="mr-1 h-4 w-4"/>Bajar</Button><Button variant="ghost" disabled={!enabled} className="flex-1 rounded-none text-teal-700" onClick={onOpen}>{done?"Ver":enabled?"Entregar":"Pendiente"}<ChevronRight className="ml-1 h-4 w-4"/></Button></div></CardContent></Card>;
}

function DispatchMap({data,onOpen}:{data:DispatchExecution;onOpen:(document:DeliveryDocument)=>void}){
  const stops=data.documents.map((document,index)=>({routeStopId:document.dispatchSourceDocumentId,sequence:index+1,customerName:document.customerName,siteName:document.documentNumber,addressLine:document.deliveryAddress??"Dirección pendiente",cityName:"",googleMapsUrl:null,latitude:document.destinationLatitude,longitude:document.destinationLongitude,document}));
  return <RouteLocationMap stops={stops} onOpen={stop=>onOpen(stop.document)} statusOf={stop=>stop.document.deliveryStatus==="NotDelivered"?"skipped":stop.document.deliveryStatus==="Pending"?"pending":"visited"}/>;
}
function DeliveryDialog({dispatchId,document,onClose,onSaved}:{dispatchId:string;document:DeliveryDocument;onClose:()=>void;onSaved:()=>void}){
  const [status,setStatus]=useState<"Delivered"|"PartiallyDelivered"|"NotDelivered">(document.deliveryStatus==="Pending"?"Delivered":document.deliveryStatus as "Delivered"|"PartiallyDelivered"|"NotDelivered");
  const [paymentMethod,setPaymentMethod]=useState<"Cash"|"Deposit"|null>(document.creditAmount>0?null:"Cash");
  const [reason,setReason]=useState(document.reason??""),[notes,setNotes]=useState(document.notes??"");
  const [depositProof,setDepositProof]=useState<File|null>(null),[signedProof,setSignedProof]=useState<File|null>(null),[returns,setReturns]=useState<Record<number,string>>({});
  const [returnsOpen,setReturnsOpen]=useState(false);
  const returnedTotal=document.lines.reduce((sum,line)=>sum+line.lineTotal/line.quantity*Number(returns[line.originalLineNumber]||0),0);
  const net=Math.max(0,document.documentTotal-returnedTotal);
  const immediateAmount=document.creditAmount>0?Math.max(0,net-document.creditAmount):net;
  const selectedReturns=Object.values(returns).filter(value=>Number(value)>0).length;
  const mutation=useMutation({mutationFn:async()=>{
    const returnInputs=document.lines.flatMap(line=>{const quantity=Number(returns[line.originalLineNumber]||0);return quantity>0?[{originalLineNumber:line.originalLineNumber,quantity,inventoryDisposition:"Sellable" as const,reasonCode:"CustomerReturn",reasonDescription:"Devuelto durante la entrega"}]:[]});
    let depositUrl:string|null=null,signedUrl:string|null=null;
    if(depositProof)depositUrl=(await dispatchesApi.uploadEvidence(dispatchId,depositProof)).url;
    if(signedProof)signedUrl=(await dispatchesApi.uploadEvidence(dispatchId,signedProof)).url;
    const payments:DeliveryResultInput["payments"]=[];
    if(status!=="NotDelivered"){
      const application=document.creditAmount>0?"CreditAdvance":"InvoicePayment";
      if(paymentMethod&&immediateAmount>0)payments.push({applicationType:application,paymentMethod,amount:immediateAmount,reference:null,evidenceUrl:paymentMethod==="Deposit"?depositUrl:null});
      if(document.creditAmount>0)payments.push({applicationType:"CreditDocument",paymentMethod:null,amount:0,reference:null,evidenceUrl:signedUrl});
    }
    const position=await currentPosition();
    return dispatchesApi.recordDelivery(dispatchId,{dispatchSourceDocumentId:document.dispatchSourceDocumentId,deliveryStatus:status,reason:reason||null,notes:notes||null,latitude:position?.latitude??null,longitude:position?.longitude??null,occurredAt:new Date().toISOString(),idempotencyKey:crypto.randomUUID(),payments,returns:returnInputs});
  },onSuccess:()=>{toast.success("Resultado de entrega guardado");onSaved()},onError:error=>toast.error(errorMessage(error,"No fue posible guardar la entrega."))});
  const paymentRequired=status!=="NotDelivered"&&immediateAmount>0;
  const invalid=status!=="Delivered"&&!reason.trim()||paymentRequired&&!paymentMethod||paymentMethod==="Deposit"&&!depositProof||document.creditAmount>0&&status!=="NotDelivered"&&!signedProof;
  return <>
    <Dialog open onOpenChange={open=>!open&&onClose()}><DialogContent className="max-h-[96dvh] max-w-3xl overflow-y-auto rounded-3xl">
      <DialogHeader><DialogTitle>Confirmar entrega</DialogTitle><DialogDescription>{document.customerName} · {document.documentNumber}</DialogDescription></DialogHeader>
      <div className="space-y-5">
        <section className="grid grid-cols-3 gap-2 rounded-2xl bg-slate-950 p-4 text-center text-white"><MiniMoney label="Factura" value={document.documentTotal}/><MiniMoney label="Devolución" value={returnedTotal}/><MiniMoney label="A recibir" value={net}/></section>
        <Field label="¿Cómo terminó la visita?"><Select value={status} onValueChange={value=>setStatus(value as typeof status)}><SelectTrigger className="h-12 rounded-xl"><SelectValue/></SelectTrigger><SelectContent><SelectItem value="Delivered">Entrega completa</SelectItem><SelectItem value="PartiallyDelivered">Entrega con devolución</SelectItem><SelectItem value="NotDelivered">No se pudo entregar</SelectItem></SelectContent></Select></Field>
        {status!=="Delivered"&&<Field label="Motivo"><Input value={reason} onChange={event=>setReason(event.target.value)} placeholder="Describe qué ocurrió"/></Field>}
        {status!=="NotDelivered"&&<>
          <section className="flex items-center justify-between gap-3 rounded-2xl border p-4"><span><strong className="block">Devolución de productos</strong><small className="text-muted-foreground">{selectedReturns?`${selectedReturns} producto(s) · ${money.format(returnedTotal)}`:"La entrega no tiene devoluciones"}</small></span><Button type="button" variant={selectedReturns?"secondary":"outline"} onClick={()=>setReturnsOpen(true)}>{selectedReturns?<><PackageX className="mr-2 h-4 w-4"/>Editar</>:<><Plus className="mr-2 h-4 w-4"/>Agregar devolución</>}</Button></section>
          {immediateAmount>0?<Field label={`Pago recibido · ${money.format(immediateAmount)}`}><div className="grid grid-cols-2 gap-3"><PaymentButton active={paymentMethod==="Cash"} icon={Banknote} title="Efectivo" onClick={()=>{setPaymentMethod("Cash");setDepositProof(null)}}/><PaymentButton active={paymentMethod==="Deposit"} icon={ReceiptText} title="Consignación" onClick={()=>setPaymentMethod("Deposit")}/></div></Field>:<div className="rounded-2xl border bg-muted/30 p-4 text-sm"><strong className="block">Venta a crédito</strong>No se registra recaudo en esta entrega.</div>}
          {paymentMethod==="Deposit"&&immediateAmount>0&&<PhotoInput label="Foto de la consignación" file={depositProof} onChange={setDepositProof}/>}
          {document.creditAmount>0&&<PhotoInput label="Factura firmada por el cliente" file={signedProof} onChange={setSignedProof}/>}
        </>}
        <Field label="Observaciones"><Textarea rows={2} className="min-h-20 resize-y" value={notes} onChange={event=>setNotes(event.target.value)} placeholder="Novedades de la entrega (opcional)" maxLength={500}/></Field>
      </div>
      <DialogFooter><Button variant="outline" onClick={onClose}>Cancelar</Button><Button className="bg-teal-600 hover:bg-teal-700" disabled={mutation.isPending||invalid} onClick={()=>mutation.mutate()}>{mutation.isPending?"Guardando…":"Confirmar entrega"}</Button></DialogFooter>
    </DialogContent></Dialog>
    <ReturnsDialog document={document} open={returnsOpen} values={returns} onClose={()=>setReturnsOpen(false)} onSave={values=>{setReturns(values);setReturnsOpen(false);if(Object.values(values).some(value=>Number(value)>0))setStatus("PartiallyDelivered");else if(status==="PartiallyDelivered")setStatus("Delivered")}}/>
  </>;
}

function ReturnsDialog({document,open,values,onClose,onSave}:{document:DeliveryDocument;open:boolean;values:Record<number,string>;onClose:()=>void;onSave:(values:Record<number,string>)=>void}){
  const [draft,setDraft]=useState(values);
  useEffect(()=>{if(open)setDraft(values)},[open,values]);
  const selected=Object.values(draft).filter(value=>Number(value)>0).length;
  const amount=document.lines.reduce((sum,line)=>sum+line.lineTotal/line.quantity*Number(draft[line.originalLineNumber]||0),0);
  return <Dialog open={open} onOpenChange={value=>!value&&onClose()}><DialogContent className="max-h-[92dvh] max-w-2xl overflow-y-auto rounded-3xl"><DialogHeader><DialogTitle>Agregar productos devueltos</DialogTitle><DialogDescription>Busca únicamente las líneas que el cliente realmente devuelve.</DialogDescription></DialogHeader><div className="space-y-2">{document.lines.map(line=><div key={line.originalLineNumber} className="grid grid-cols-[minmax(0,1fr)_6rem] items-center gap-3 rounded-2xl border p-3"><span className="min-w-0"><strong className="block truncate text-sm">{line.productCode} · {line.description}</strong><small className="text-muted-foreground">Facturado: {line.quantity} · {money.format(line.lineTotal)}</small></span><Input aria-label={`Cantidad devuelta de ${line.description}`} type="number" min="0" max={line.quantity} step="any" value={draft[line.originalLineNumber]??""} onChange={event=>setDraft(current=>({...current,[line.originalLineNumber]:event.target.value}))} placeholder="0"/></div>)}</div><div className="flex items-center justify-between rounded-2xl bg-muted p-3"><span><small className="block text-muted-foreground">Devolución seleccionada</small><strong>{selected} producto(s) · {money.format(amount)}</strong></span>{selected>0&&<Button variant="ghost" size="sm" onClick={()=>setDraft({})}><Trash2 className="mr-1 h-4 w-4"/>Limpiar</Button>}</div><DialogFooter><Button variant="outline" onClick={onClose}>Cancelar</Button><Button onClick={()=>onSave(draft)}>Aplicar devolución</Button></DialogFooter></DialogContent></Dialog>;
}

function PaymentButton({active,icon:Icon,title,onClick}:{active:boolean;icon:typeof Banknote;title:string;onClick:()=>void}){return <button type="button" onClick={onClick} className={`flex h-16 items-center justify-center gap-2 rounded-2xl border-2 font-bold transition ${active?"border-teal-500 bg-teal-50 text-teal-800":"border-border bg-background hover:border-teal-300"}`}><Icon className="h-5 w-5"/>{title}{active&&<Check className="h-4 w-4"/>}</button>}

function SettlementPanel({data,canSettle,onChanged,onExpense}:{data:DispatchExecution;canSettle:boolean;onChanged:()=>void;onExpense:()=>void}){
  const summary=data.settlement;
  const cashCollected=data.documents.flatMap(item=>item.payments).filter(item=>item.paymentMethod==="Cash").reduce((sum,item)=>sum+item.amount,0);
  const depositsCollected=data.documents.flatMap(item=>item.payments).filter(item=>item.paymentMethod==="Deposit").reduce((sum,item)=>sum+item.amount,0);
  const returnsTotal=data.documents.reduce((documentSum,document)=>documentSum+document.returns.reduce((returnSum,item)=>{const line=document.lines.find(line=>line.originalLineNumber===item.originalLineNumber);return returnSum+(line?line.lineTotal/line.quantity*item.quantity:0)},0),0);
  const expectedCash=summary?.expectedCash??cashCollected;
  const [cash,setCash]=useState(String(expectedCash));
  const [notes,setNotes]=useState("");
  const [reviewing,setReviewing]=useState<string|null>(null);
  const difference=Number(cash||0)-expectedCash;
  useEffect(()=>setCash(String(expectedCash)),[expectedCash]);
  const close=async()=>{try{await dispatchesApi.closeRoute(data.dispatchId,Number(cash||0),difference===0?null:notes);toast.success("Recorrido cerrado y enviado a liquidación");onChanged()}catch(error){toast.error(errorMessage(error,"No fue posible cerrar el recorrido."))}};
  const settle=async()=>{try{await dispatchesApi.settle(data.dispatchId,Number(cash||0),difference===0?notes||null:notes);toast.success("Liquidación aceptada; el motor está procesando los documentos");onChanged()}catch(error){toast.error(errorMessage(error,"No fue posible aceptar la liquidación."))}};
  const review=async(expenseId:string,decision:"Approved"|"Rejected",amount:number)=>{setReviewing(expenseId);try{await dispatchesApi.reviewExpense(data.dispatchId,expenseId,{decision,approvedAmount:decision==="Approved"?amount:null,notes:null,idempotencyKey:crypto.randomUUID()});toast.success(decision==="Approved"?"Gasto aprobado":"Gasto rechazado");onChanged()}catch(error){toast.error(errorMessage(error,"No fue posible revisar el gasto."))}finally{setReviewing(null)}};
  const pending=data.documents.filter(item=>item.deliveryStatus==="Pending").length;
  const processing=summary?.status==="Processing"||data.status==="SettlementProcessing";
  return <div className="space-y-4">
    <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
      <MoneyCard icon={Banknote} label="Efectivo recaudado" value={summary?.grossCash??cashCollected}/>
      <MoneyCard icon={ReceiptText} label="Consignaciones" value={summary?.depositTotal??depositsCollected}/>
      <MoneyCard icon={PackageCheck} label="Devoluciones" value={summary?.returnTotal??returnsTotal}/>
      <MoneyCard icon={CircleDollarSign} label="Gastos aprobados" value={summary?.approvedCashExpenses??0}/>
    </div>
    <Card className="overflow-hidden rounded-3xl"><CardContent className="space-y-5 p-5">
      <div className="flex items-center justify-between"><div><p className="text-sm text-muted-foreground">Cuadre comercial obligatorio</p><p className={`text-3xl font-black ${(summary?.balanceDifference??0)===0?"text-emerald-600":"text-red-600"}`}>{money.format(summary?.balanceDifference??0)}</p></div><span className={`rounded-2xl p-3 ${(summary?.balanceDifference??0)===0?"bg-emerald-50 text-emerald-600":"bg-red-50 text-red-600"}`}><ShieldCheck className="h-7 w-7"/></span></div>
      <div className="grid gap-3 sm:grid-cols-2"><Field label="Efectivo físico recibido"><Input inputMode="numeric" value={cash} onChange={event=>setCash(event.target.value)} className="h-12 text-lg font-bold"/></Field><div className={`rounded-2xl border p-3 ${difference>0?"border-emerald-200 bg-emerald-50":difference<0?"border-red-200 bg-red-50":"bg-muted/50"}`}><small>{difference>0?"Sobrante":difference<0?"Faltante":"Diferencia"}</small><strong className="block text-xl">{money.format(Math.abs(difference))}</strong></div></div>
      {difference!==0&&<Field label="Explicación obligatoria"><Input value={notes} onChange={event=>setNotes(event.target.value)} placeholder={difference>0?"¿Por qué sobró efectivo?":"¿Por qué faltó efectivo?"}/></Field>}
      <div className="flex flex-wrap gap-2">{!summary&&<Button variant="outline" onClick={onExpense}>Agregar gasto</Button>}{!summary&&<Button disabled={pending>0||difference!==0&&!notes.trim()} onClick={close}>Cerrar recorrido</Button>}{summary&&canSettle&&summary.status==="PendingReview"&&<Button className="bg-teal-600 hover:bg-teal-700" disabled={summary.balanceDifference!==0||data.expenses.some(item=>item.approvalStatus==="Pending")||difference!==0&&!notes.trim()} onClick={settle}>Aceptar liquidación</Button>}</div>
      {pending>0&&<p className="text-sm text-amber-700">Faltan {pending} entregas por registrar.</p>}
      {processing&&<p className="rounded-2xl bg-blue-50 p-3 text-sm font-medium text-blue-800">El motor está procesando recaudos, devoluciones, inventario, cartera y DIAN. Este despacho se cerrará automáticamente cuando todos terminen.</p>}
      {data.status==="SettlementAttention"&&<p className="rounded-2xl bg-red-50 p-3 text-sm font-medium text-red-800">La liquidación requiere atención. El motor conserva las mismas claves y seguirá reintentando sin duplicar documentos.</p>}
    </CardContent></Card>
    <section className="space-y-2"><h3 className="font-bold">Gastos del despacho</h3>{data.expenses.map(expense=><div key={expense.expenseId} className="flex flex-col gap-3 rounded-2xl border bg-card p-3 sm:flex-row sm:items-center sm:justify-between"><span><strong>{expense.category} · {money.format(expense.amount)}</strong><small className="block text-muted-foreground">{expense.description}</small>{expense.evidenceUrl&&<a href={expense.evidenceUrl} target="_blank" rel="noreferrer" className="text-xs font-semibold text-teal-700">Ver soporte</a>}</span><div className="flex items-center gap-2"><Badge variant={expense.approvalStatus==="Approved"?"default":expense.approvalStatus==="Rejected"?"destructive":"secondary"}>{expense.approvalStatus==="Pending"?"Pendiente":expense.approvalStatus==="Approved"?"Aprobado":"Rechazado"}</Badge>{canSettle&&summary?.status==="PendingReview"&&expense.approvalStatus==="Pending"&&<><Button size="sm" variant="outline" disabled={reviewing===expense.expenseId} onClick={()=>review(expense.expenseId,"Rejected",expense.amount)}>Rechazar</Button><Button size="sm" disabled={reviewing===expense.expenseId} onClick={()=>review(expense.expenseId,"Approved",expense.amount)}>Aprobar</Button></>}</div></div>)}</section>
  </div>;
}

function ExpenseDialog({dispatchId,onClose,onSaved}:{dispatchId:string;onClose:()=>void;onSaved:()=>void}){const [category,setCategory]=useState("Peaje"),[amount,setAmount]=useState(""),[description,setDescription]=useState(""),[proof,setProof]=useState<File|null>(null),[saving,setSaving]=useState(false);const save=async()=>{if(!proof)return;setSaving(true);try{const evidenceUrl=(await dispatchesApi.uploadEvidence(dispatchId,proof)).url;await dispatchesApi.addExpense(dispatchId,{category,amount:Number(amount),description,evidenceUrl,idempotencyKey:crypto.randomUUID(),occurredAt:new Date().toISOString()});toast.success("Gasto enviado para aprobación");onSaved()}catch(error){toast.error(errorMessage(error,"No fue posible guardar el gasto."))}finally{setSaving(false)}};return <Dialog open onOpenChange={open=>!open&&onClose()}><DialogContent className="rounded-3xl"><DialogHeader><DialogTitle>Otro gasto del despacho</DialogTitle><DialogDescription>El supervisor debe aprobarlo antes de liquidar.</DialogDescription></DialogHeader><div className="space-y-4"><Field label="Categoría"><Select value={category} onValueChange={setCategory}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent>{["Peaje","Parqueadero","Combustible","Cargue y descargue","Otro"].map(x=><SelectItem key={x} value={x}>{x}</SelectItem>)}</SelectContent></Select></Field><Field label="Valor"><Input inputMode="numeric" value={amount} onChange={event=>setAmount(event.target.value)}/></Field><Field label="Descripción"><Input value={description} onChange={event=>setDescription(event.target.value)}/></Field><PhotoInput label="Foto del soporte" file={proof} onChange={setProof}/></div><DialogFooter><Button variant="outline" onClick={onClose}>Cancelar</Button><Button disabled={saving||!proof||Number(amount)<=0||!description.trim()} onClick={save}>Guardar gasto</Button></DialogFooter></DialogContent></Dialog>}

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
