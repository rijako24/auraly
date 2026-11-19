"use client";

import { useMemo, useState } from "react";
import { ArrowRight, ClipboardCheck, Download, PackageCheck, Plus, RotateCcw, Truck, XCircle } from "lucide-react";
import { toast } from "sonner";
import { ReportViewer } from "@/components/reports/report-viewer";
import { DispatchSettlementDialog } from "@/components/dispatching/dispatch-settlement-dialog";
import { DispatchDriverSelect } from "@/components/dispatching/dispatch-driver-select";
import { DataTablePagination } from "@/components/tables/data-table-pagination";
import { ServerSearchInput } from "@/components/tables/server-search-input";
import { buildDispatchReportRows, dispatchReportColumns, type DispatchReportGroup } from "@/lib/dispatch-report";
import { shouldCloseDispatchAfterSettlement } from "@/lib/dispatch-settlement-navigation";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { DatePicker } from "@/components/ui/date-picker";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { useCreateDispatch, useDeclareDispatchShortage, useDispatchCandidates, useDispatchDetail, useDispatches, useDispatchOptions, useDispatchTransition, useVerifyDispatch } from "@/hooks/use-dispatches";
import { dispatchesApi, type DispatchStatus, type DispatchCandidate } from "@/services/api/dispatches";
import { useBusinessDates } from "@/hooks/use-business-dates";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";

const statusLabels:Record<DispatchStatus,string>={Draft:"Borrador",Prepared:"Preparado",InVerification:"En verificación",Verified:"Verificado",Released:"Listo para salir",InDelivery:"En ruta",PendingSettlement:"Por liquidar",SettlementProcessing:"Procesando liquidación",SettlementAttention:"Requiere atención",Closed:"Liquidado",Cancelled:"Cancelado"};
const money=new Intl.NumberFormat("es-CO",{style:"currency",currency:"COP",maximumFractionDigits:0});
const quantity=new Intl.NumberFormat("es-CO",{maximumFractionDigits:3});

export default function DispatchesPage(){
  const businessId=useBusinessContextStore(state=>state.selectedBusinessId);
  const permissionValues=useAuthStore(state=>state.user?.permissions);
  const permissions=useMemo(()=>new Set(permissionValues??[]),[permissionValues]);
  const {timeZone}=useBusinessDates(businessId);
  const [page,setPage]=useState(1),[pageSize,setPageSize]=useState(25),[search,setSearch]=useState(""),[status,setStatus]=useState("all");
  const [selectedId,setSelectedId]=useState<string>(),[createOpen,setCreateOpen]=useState(false);
  const query=useDispatches({page,pageSize,search:search||undefined,status:status==="all"?undefined:status},!selectedId&&!createOpen);
  if(!businessId)return <p className="p-8 text-center text-muted-foreground">Selecciona una sede para administrar despachos.</p>;
  const openItem=(item:import("@/services/api/dispatches").DispatchListItem)=>setSelectedId(item.dispatchId);
  return <div className="space-y-4">
    <header className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between"><div><p className="text-sm font-medium text-emerald-600">Operación logística</p><h1 className="text-3xl font-bold tracking-tight">Despachos</h1><p className="text-muted-foreground">Prepara, verifica, libera y liquida los despachos desde un solo flujo.</p></div><div className="flex gap-2"><Button variant="outline" disabled={query.isFetching} onClick={()=>void query.refetch()} aria-label="Actualizar despachos"><RotateCcw className="mr-2 h-4 w-4"/>Actualizar</Button><Button className="shrink-0" disabled={!timeZone||!permissions.has("dispatches.create")} onClick={()=>setCreateOpen(true)}><Plus className="mr-2 h-4 w-4"/>Crear despacho</Button></div></header>
    <div className="grid gap-3 md:grid-cols-3"><Metric icon={Truck} label="Despachos encontrados" value={String(query.data?.totalCount??0)}/><Metric icon={PackageCheck} label="Documentos en esta página" value={String(query.data?.items.reduce((sum,item)=>sum+item.documentCount,0)??0)}/><Metric icon={ClipboardCheck} label="Faltantes en esta página" value={quantity.format(query.data?.items.reduce((sum,item)=>sum+item.shortageQuantity,0)??0)}/></div>
    <div className="grid gap-2 rounded-2xl border bg-card p-3 md:grid-cols-[1fr_14rem]"><ServerSearchInput value={search} onSearch={value=>{setSearch(value);setPage(1)}} isSearching={query.isFetching} placeholder="Buscar despacho, transportador o placa"/><Select value={status} onValueChange={value=>{setStatus(value);setPage(1)}}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent><SelectItem value="all">Todos los estados</SelectItem>{Object.entries(statusLabels).map(([value,label])=><SelectItem key={value} value={value}>{label}</SelectItem>)}</SelectContent></Select></div>
    <div className="grid gap-3 md:hidden">{query.data?.items.map(item=><button type="button" key={item.dispatchId} onClick={()=>openItem(item)} className="rounded-2xl border bg-card p-4 text-left shadow-sm"><div className="flex items-start justify-between gap-3"><div className="min-w-0"><strong className="block truncate">{item.dispatchNumber}</strong><p className="mt-1 truncate text-sm text-muted-foreground">{item.driverName}{item.vehiclePlate?` · ${item.vehiclePlate}`:""}</p></div><Status value={item.status}/></div><div className="mt-3 flex items-center justify-between border-t pt-3 text-sm"><span>{item.documentCount} documentos · {quantity.format(item.verifiedQuantity)}/{quantity.format(item.expectedQuantity)} verificado</span><ArrowRight className="h-4 w-4 text-muted-foreground"/></div></button>)}</div>
    <div className="hidden overflow-x-auto rounded-2xl border bg-card md:block"><table className="w-full text-sm"><thead className="border-b bg-muted/50 text-xs uppercase tracking-wide text-muted-foreground"><tr>{["Despacho","Programado","Transportador","Documentos","Verificación","Estado"].map(value=><th key={value} className="px-4 py-3 text-left font-semibold">{value}</th>)}</tr></thead><tbody>{query.isLoading?<tr><td colSpan={6} className="p-10 text-center">Cargando…</td></tr>:query.data?.items.map(item=><tr key={item.dispatchId} onClick={()=>openItem(item)} className="cursor-pointer border-b hover:bg-muted/40"><td className="px-4 py-3 font-semibold">{item.dispatchNumber}</td><td className="px-4 py-3">{item.scheduledDate}</td><td className="px-4 py-3">{item.driverName}<small className="block text-muted-foreground">{item.vehiclePlate??"Sin placa"}</small></td><td className="px-4 py-3">{item.documentCount} · {item.lineCount} líneas</td><td className="px-4 py-3">{quantity.format(item.verifiedQuantity)} / {quantity.format(item.expectedQuantity)}{item.shortageQuantity>0&&<small className="block text-red-700">{quantity.format(item.shortageQuantity)} faltante</small>}</td><td className="px-4 py-3"><Status value={item.status}/></td></tr>)}</tbody></table></div>
    <DataTablePagination pageIndex={Math.max(0,(query.data?.page??page)-1)} pageSize={query.data?.pageSize??pageSize} pageCount={query.data?.totalPages??0} totalItems={query.data?.totalCount??0} onPageChange={index=>setPage(index+1)} onPageSizeChange={size=>{setPageSize(size);setPage(1)}}/>
    {createOpen&&timeZone&&<CreateDispatch businessId={businessId} onClose={()=>setCreateOpen(false)} onCreated={id=>{setCreateOpen(false);setSelectedId(id)}}/>}
    <DispatchWorkspace id={selectedId} permissions={permissions} onClose={()=>setSelectedId(undefined)}/>
  </div>;
}

function CreateDispatch({businessId,onClose,onCreated}:{businessId:string;onClose:()=>void;onCreated:(id:string)=>void}) {
  const {today,formatDateTime}=useBusinessDates(businessId);
  const options=useDispatchOptions(),create=useCreateDispatch();
  const [routeId,setRouteId]=useState("none"),[date,setDate]=useState(today);
  const [driverUserId,setDriverUserId]=useState("none"),[driver,setDriver]=useState("");
  const [plate,setPlate]=useState(""),[notes,setNotes]=useState("");
  const [search,setSearch]=useState(""),[type,setType]=useState("all"),[page,setPage]=useState(1);
  const [from,setFrom]=useState(today),[to,setTo]=useState(today);
  const [selected,setSelected]=useState<Map<string,DispatchCandidate>>(new Map());
  const rangeValid=Boolean(from&&to&&from<=to);
  const candidates=useDispatchCandidates({page,pageSize:20,
    search:search||undefined,documentType:type==="all"?undefined:type,from,to,dateRangeVersion:2},rangeValid);
  const changeRange=(value:string,end:boolean)=>{
    if(end)setTo(value);else setFrom(value);
    setPage(1);setSelected(new Map());
  };
  const toggle=(item:DispatchCandidate)=>{
    setSelected(current=>{
      const next=new Map(current);
      if(next.has(item.documentId))next.delete(item.documentId);else next.set(item.documentId,item);
      return next;
    });
  };
  const submit=async()=>{
    if(selected.size===0||driverUserId==="none"||!driver.trim()||!rangeValid||!date){toast.error("Selecciona el transportador, las fechas y al menos un documento.");return;}
    try{
      const result=await create.mutateAsync({businessId,scheduledDate:date,
        driverName:driver,vehiclePlate:plate||null,driverUserId,routeId:routeId==="none"?null:routeId,
        notes:notes||null,sourceDocumentIds:[...selected.keys()]});
      toast.success(`${result.dispatchNumber} creado`);onCreated(result.dispatchId);
    }catch(error){toast.error(message(error,"No fue posible crear el despacho."));}
  };
  return <Dialog open onOpenChange={value=>!value&&onClose()}>
    <DialogContent className="flex max-h-[94dvh] w-[calc(100%-1rem)] max-w-5xl flex-col overflow-hidden">
      <DialogHeader><DialogTitle>Nuevo despacho de mercancía</DialogTitle><DialogDescription>Agrupa ventas procesadas y asigna el transportador responsable del despacho.</DialogDescription></DialogHeader>
      <div className="grid gap-3 md:grid-cols-3">
        <Field label="Transportador"><DispatchDriverSelect value={driverUserId} name={driver} onChange={(value,name)=>{setDriverUserId(value);setDriver(name)}}/></Field>
        <div className="space-y-1"><Label htmlFor="dispatch-date">Fecha del despacho</Label><DatePicker id="dispatch-date" value={date} today={today()} onChange={setDate}/></div>
        <Field label="Ruta sugerida (opcional)"><Select value={routeId} onValueChange={setRouteId}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent><SelectItem value="none">Sin ruta sugerida</SelectItem>{options.data?.routes.map(item=><SelectItem key={item.routeId} value={item.routeId}>{item.name} · {item.sellerName}</SelectItem>)}</SelectContent></Select></Field>
        <Field label="Placa"><Input value={plate} onChange={event=>setPlate(event.target.value.toUpperCase())}/></Field>
        <div className="md:col-span-2"><Field label="Notas"><Input value={notes} onChange={event=>setNotes(event.target.value)}/></Field></div>
      </div>
      {options.isError&&<p role="alert" className="text-sm text-red-700">No fue posible cargar las rutas. <button type="button" className="underline" onClick={()=>void options.refetch()}>Reintentar</button></p>}
      <section className="space-y-3 rounded-xl border bg-muted/20 p-3" aria-label="Filtros de documentos del despacho">
        <div className="grid gap-3 sm:grid-cols-2">
          <div className="space-y-1"><Label htmlFor="dispatch-documents-from">Documentos desde</Label><DatePicker id="dispatch-documents-from" value={from} today={today()} onChange={value=>changeRange(value,false)}/></div>
          <div className="space-y-1"><Label htmlFor="dispatch-documents-to">Documentos hasta</Label><DatePicker id="dispatch-documents-to" value={to} today={today()} onChange={value=>changeRange(value,true)}/></div>
        </div>
        <div className="grid gap-2 md:grid-cols-[1fr_13rem]"><ServerSearchInput value={search} onSearch={value=>{setSearch(value);setPage(1)}} isSearching={candidates.isFetching} placeholder="Buscar factura o cliente"/><Select value={type} onValueChange={value=>{setType(value);setPage(1)}}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent><SelectItem value="all">Facturas y comprobantes</SelectItem><SelectItem value="SalesInvoice">Facturas</SelectItem><SelectItem value="SalesReceipt">Comprobantes de venta</SelectItem></SelectContent></Select></div>
        <p className="text-xs text-muted-foreground">Documentos de la sede seleccionada. Cada uno conserva su bodega de origen.</p>
        {!rangeValid&&<p role="alert" className="text-sm text-red-700">Selecciona un rango válido: la fecha desde no puede ser posterior a la fecha hasta.</p>}
      </section>
      <div className="min-h-0 flex-1 overflow-y-auto rounded-xl border" aria-label="Documentos disponibles para despacho">
        {candidates.isLoading&&<p className="p-6 text-center text-muted-foreground">Cargando documentos…</p>}
        {candidates.isError&&<p role="alert" className="p-4 text-sm text-red-700">No fue posible consultar los documentos. <button type="button" className="underline" onClick={()=>void candidates.refetch()}>Reintentar</button></p>}
        {candidates.data?.items.map(item=><label key={item.documentId} className="flex cursor-pointer gap-3 border-b p-3 hover:bg-muted/40"><Checkbox checked={selected.has(item.documentId)} disabled={create.isPending} onCheckedChange={()=>toggle(item)}/><span className="min-w-0 flex-1"><strong>{item.documentNumber} · {item.customerName}</strong><small className="block text-muted-foreground">{item.documentType==="SalesInvoice"?"Factura":"Comprobante de venta"} · {formatDateTime(item.issuedAt)} · {item.warehouseName} · {item.sellerName} · {item.lineCount} líneas</small></span><span className="font-semibold">{money.format(item.documentTotal)}</span></label>)}
        {rangeValid&&!candidates.isLoading&&!candidates.isError&&!candidates.data?.items.length&&<p className="p-8 text-center text-muted-foreground">No hay ventas pendientes de asignar en este rango.</p>}
      </div>
      <div className="flex items-center justify-between gap-2 text-sm"><span>{candidates.data?.totalCount??0} documentos · página {page} de {Math.max(1,candidates.data?.totalPages??0)}</span><div className="flex gap-1"><Button type="button" size="sm" variant="outline" disabled={page<=1||candidates.isFetching} onClick={()=>setPage(value=>value-1)}>Anterior</Button><Button type="button" size="sm" variant="outline" disabled={page>=(candidates.data?.totalPages??0)||candidates.isFetching} onClick={()=>setPage(value=>value+1)}>Siguiente</Button></div></div>
      <DialogFooter><span className="mr-auto self-center text-sm text-muted-foreground">{selected.size} seleccionados {selected.size>0&&<button type="button" className="ml-2 underline" onClick={()=>{setSelected(new Map());setPage(1)}}>Limpiar selección</button>}</span><Button variant="outline" disabled={create.isPending} onClick={onClose}>Cancelar</Button><Button disabled={create.isPending||selected.size===0||driverUserId==="none"||!rangeValid} onClick={submit}>Crear despacho</Button></DialogFooter>
    </DialogContent>
  </Dialog>;
}

function DispatchWorkspace({id,permissions,onClose}:{id?:string;permissions:Set<string>;onClose:()=>void}){
  const [settlementOpen,setSettlementOpen]=useState(false);
  const detail=useDispatchDetail(id,!settlementOpen),transition=useDispatchTransition(),verify=useVerifyDispatch(),shortage=useDeclareDispatchShortage();
  const value=detail.data;
  const [viewer,setViewer]=useState<{title:string;description:string;fileName:string;rows:ReturnType<typeof buildDispatchReportRows>;columns:ReturnType<typeof dispatchReportColumns>}|null>(null);
  const [reportOpen,setReportOpen]=useState(false);
  const act=async(action:string)=>{if(!id||!value)return;try{await transition.mutateAsync({id,action,rowVersion:value.rowVersion});toast.success("Estado actualizado")}catch(error){toast.error(message(error,"No fue posible cambiar el estado."))}};
  const closeAndRelease=async()=>{if(!id||!value)return;try{const verified=await transition.mutateAsync({id,action:"complete-verification",rowVersion:value.rowVersion});await transition.mutateAsync({id,action:"release",rowVersion:verified.rowVersion});toast.success("Despacho verificado y liberado para el transportador")}catch(error){await detail.refetch();toast.error(message(error,"No fue posible cerrar y liberar el despacho."))}};
  const report=async(group:DispatchReportGroup)=>{if(!id||!permissions.has("dispatches.reports.view"))return;try{const prices=permissions.has("dispatches.view-prices");const data=await dispatchesApi.report(id,prices);setReportOpen(false);setViewer({title:data.title,description:`${data.rows.length} líneas · ${group==="detail"?"Detalle agrupado por factura":group==="product"?"Consolidado por producto":group==="customer"?"Consolidado por cliente":"Consolidado por vendedor"}`,fileName:`${value?.dispatchNumber??"despacho"}-${group}`,rows:buildDispatchReportRows(data.rows,group),columns:dispatchReportColumns(prices)})}catch(error){toast.error(message(error,"No fue posible generar el reporte."))}};
  const allAccountedFor=value?.lines.every(line=>line.verifiedQuantity+line.shortageQuantity>=line.assignedQuantity)??false;
  const busy=transition.isPending;
  const canSettle=!!value&&permissions.has("dispatches.settle")&&(["Released","InDelivery","PendingSettlement","SettlementProcessing","SettlementAttention","Closed"] as DispatchStatus[]).includes(value.status);
  return <>
    <Dialog open={!!id&&!settlementOpen&&!reportOpen&&!viewer} onOpenChange={open=>!open&&!settlementOpen&&!reportOpen&&!viewer&&onClose()}><DialogContent className="flex max-h-[95dvh] w-[calc(100%-1rem)] max-w-6xl flex-col overflow-hidden rounded-3xl p-0 sm:p-6">
      <div className="px-4 pt-5 sm:px-0 sm:pt-0"><DialogHeader><DialogTitle>{value?.dispatchNumber??"Cargando despacho…"}</DialogTitle><DialogDescription>{value?`${value.driverName}${value.vehiclePlate?` · ${value.vehiclePlate}`:""}`:"Consulta el manifiesto completo."}</DialogDescription></DialogHeader></div>
      {value&&<><div className="flex flex-wrap items-center gap-2 px-4 sm:px-0"><Status value={value.status}/><Button size="sm" variant="outline" disabled={detail.isFetching||busy} onClick={()=>void detail.refetch()} aria-label="Actualizar despacho"><RotateCcw className="mr-2 h-4 w-4"/>Actualizar</Button><Badge variant="outline">{value.documents.length} documentos</Badge><Badge variant="outline">{value.lines.length} líneas</Badge><Badge variant="outline">{quantity.format(value.lines.reduce((sum,line)=>sum+line.verifiedQuantity,0))} verificado</Badge>{value.shortages.length>0&&<Badge variant="destructive">{value.shortages.length} faltantes</Badge>}{canSettle&&<Button size="sm" onClick={()=>setSettlementOpen(true)}>Revisar y liquidar</Button>}</div>
      <div className="px-4 pb-1 sm:px-0">{permissions.has("dispatches.reports.view")&&<Button size="sm" variant="outline" onClick={()=>setReportOpen(true)}><Download className="mr-2 h-4 w-4"/>Reportes</Button>}</div>
      <div className="min-h-0 flex-1 space-y-4 overflow-y-auto px-4 sm:px-0"><section className="rounded-xl border"><h3 className="border-b bg-muted/40 px-4 py-3 font-semibold">Documentos</h3>{value.documents.map(document=><div key={document.dispatchSourceDocumentId} className="flex justify-between gap-3 border-b px-4 py-3 text-sm"><span><strong>{document.documentNumber} · {document.customerName}</strong><small className="block text-muted-foreground">{document.deliveryAddress??"Sin dirección"} · {document.sellerName}</small></span><span>{permissions.has("dispatches.view-prices")?money.format(document.documentTotal):"Valor restringido"}</span></div>)}</section><section className="rounded-xl border"><h3 className="border-b bg-muted/40 px-4 py-3 font-semibold">Verificación de mercancía</h3><div className="overflow-x-auto"><table className="w-full min-w-[720px] text-sm"><thead className="bg-muted/50 text-xs uppercase tracking-wide text-muted-foreground"><tr>{["Producto","Asignado","Verificado","Faltante","Acciones"].map(text=><th key={text} className="px-4 py-3 text-left font-semibold">{text}</th>)}</tr></thead><tbody>{value.lines.map(line=><tr key={line.dispatchLineId} className="border-t"><td className="px-4 py-3"><strong>{line.productCode}</strong><small className="block text-muted-foreground">{line.description}</small></td><td className="px-4 py-3">{quantity.format(line.assignedQuantity)}</td><td className="px-4 py-3">{quantity.format(line.verifiedQuantity)}</td><td className="px-4 py-3">{quantity.format(line.shortageQuantity)}</td><td className="px-4 py-3"><div className="flex gap-2"><Button size="sm" disabled={value.status!=="InVerification"||line.verifiedQuantity+line.shortageQuantity>=line.assignedQuantity||verify.isPending} onClick={async()=>{await verify.mutateAsync({id:value.dispatchId,lineId:line.dispatchLineId,quantity:1})}}>+1 verificada</Button><Button size="sm" variant="outline" disabled={value.status!=="InVerification"||line.verifiedQuantity+line.shortageQuantity>=line.assignedQuantity||shortage.isPending} onClick={async()=>{const remaining=line.assignedQuantity-line.verifiedQuantity-line.shortageQuantity;await shortage.mutateAsync({id:value.dispatchId,dispatchLineId:line.dispatchLineId,quantity:remaining,reason:"Sin existencia física",notes:null,rowVersion:value.rowVersion})}}><XCircle className="mr-1 h-4 w-4"/>Marcar faltante</Button></div></td></tr>)}</tbody></table></div></section></div>
      <div className="border-t bg-background px-4 py-4 shadow-[0_-12px_30px_rgba(15,23,42,.06)] sm:px-0 sm:pb-0"><div className="grid gap-2 sm:grid-cols-[auto_1fr_auto]">{!(["Released","InDelivery","PendingSettlement","SettlementProcessing","SettlementAttention","Closed","Cancelled"] as DispatchStatus[]).includes(value.status)&&<Button className="order-2 sm:order-1" variant="outline" disabled={!permissions.has("dispatches.cancel")||busy} onClick={()=>act("cancel")}><XCircle className="mr-2 h-4 w-4"/>Cancelar despacho</Button>}<Button className="order-3 sm:order-2" variant="ghost" onClick={onClose}>Cerrar ventana</Button><div className="order-1 flex gap-2 sm:order-3">{value.status==="Draft"&&<Button className="w-full" disabled={!permissions.has("dispatches.prepare")||busy} onClick={()=>act("prepare")}>Preparar despacho</Button>}{value.status==="Prepared"&&<><Button variant="outline" disabled={!permissions.has("dispatches.verify")||busy} onClick={()=>act("start-verification")}>Verificar mercancía</Button><Button disabled={!permissions.has("dispatches.release")||busy} onClick={()=>act("release")}>Liberar sin verificar</Button></>}{value.status==="InVerification"&&<Button className="w-full" disabled={!permissions.has("dispatches.verify")||!permissions.has("dispatches.release")||!allAccountedFor||busy} onClick={closeAndRelease}>{busy?"Liberando…":"Cerrar verificación y liberar"}</Button>}{value.status==="Verified"&&<Button className="w-full" disabled={!permissions.has("dispatches.release")||busy} onClick={()=>act("release")}>Liberar despacho</Button>}{value.status==="Cancelled"&&<Button className="w-full" disabled={!permissions.has("dispatches.reopen")||busy} onClick={()=>act("reopen")}>Reabrir verificación</Button>}</div></div>{value.status==="InVerification"&&!allAccountedFor&&<p className="mt-2 text-center text-xs text-amber-700">Verifica o registra como faltante toda la mercancía antes de liberar.</p>}</div></>}
    </DialogContent></Dialog>
    <Dialog open={reportOpen} onOpenChange={setReportOpen}><DialogContent className="max-w-2xl"><DialogHeader><DialogTitle>Reportes del despacho</DialogTitle><DialogDescription>Elige una presentación. Se abrirá en el visor con impresión y exportación.</DialogDescription></DialogHeader><div className="grid gap-3 sm:grid-cols-2"><ReportChoice title="Detalle por factura" description="Cada factura conserva sus productos y totales." onClick={()=>void report("detail")}/><ReportChoice title="Consolidado por producto" description="Agrupa cantidades del mismo producto." onClick={()=>void report("product")}/><ReportChoice title="Por cliente" description="Agrupa el despacho por cliente y factura." onClick={()=>void report("customer")}/><ReportChoice title="Por vendedor" description="Resume la operación de cada vendedor." onClick={()=>void report("seller")}/></div><DialogFooter><Button variant="outline" onClick={()=>setReportOpen(false)}>Cancelar</Button></DialogFooter></DialogContent></Dialog>
    <Dialog open={Boolean(viewer)} onOpenChange={open=>!open&&setViewer(null)}><DialogContent showClose={false} className="h-[96dvh] max-h-[96dvh] w-[98vw] max-w-[1500px] overflow-hidden p-2 sm:p-4">{viewer&&<ReportViewer onClose={()=>setViewer(null)} title={viewer.title} description={viewer.description} fileName={viewer.fileName} rows={viewer.rows} columns={viewer.columns}/>}</DialogContent></Dialog>
    {settlementOpen&&id&&<DispatchSettlementDialog id={id} mode="progress" onClose={()=>setSettlementOpen(false)} onChanged={updated=>{if(shouldCloseDispatchAfterSettlement(updated.status)){setSettlementOpen(false);onClose()}}}/>}
  </>;
}

function Field({label,children}:{label:string;children:React.ReactNode}){return <div className="space-y-2"><Label>{label}</Label>{children}</div>}
function ReportChoice({title,description,onClick}:{title:string;description:string;onClick:()=>void}){return <button type="button" onClick={onClick} className="rounded-2xl border bg-card p-5 text-left transition hover:border-primary hover:bg-primary/5"><strong className="block">{title}</strong><span className="mt-1 block text-sm text-muted-foreground">{description}</span></button>}
function Metric({icon:Icon,label,value}:{icon:typeof Truck;label:string;value:string}){return <div className="flex items-center gap-3 rounded-2xl border bg-card p-5"><span className="rounded-xl bg-emerald-50 p-3 text-emerald-700"><Icon className="h-5 w-5"/></span><span><p className="text-sm text-muted-foreground">{label}</p><p className="text-2xl font-bold">{value}</p></span></div>}
function Status({value}:{value:DispatchStatus}){return <Badge variant={value==="Closed"||value==="Released"?"default":value==="Cancelled"||value==="SettlementAttention"?"destructive":"secondary"}>{statusLabels[value]}</Badge>}
function message(error:unknown,fallback:string){return error&&typeof error==="object"&&"message" in error&&typeof error.message==="string"?error.message:fallback}
