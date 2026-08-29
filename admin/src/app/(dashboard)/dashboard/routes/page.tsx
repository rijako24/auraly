"use client";

import { forwardRef, useEffect, useImperativeHandle, useMemo, useRef, useState } from "react";
import type { ColumnDef } from "@tanstack/react-table";
import { ArrowDown, ArrowUp, CalendarDays, Clock3, Download, GripVertical, MapPinned, Pencil, Plus, Printer, Route, Search, Sparkles, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { DataTable } from "@/components/tables/data-table";
import { PartyRoleSelect } from "@/components/parties/party-role-select";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Textarea } from "@/components/ui/textarea";
import { useCreateRoute, useCreateRouteZone, useRouteCandidates, useRouteDetail, useRouteOptions, useRoutes, useSetRouteStatus, useUpdateRoute } from "@/hooks/use-routes";
import type { RouteCandidateSite, RouteScheduleInput, SalesRouteListItem, SalesRouteStop } from "@/services/api/routes";
import { routesApi } from "@/services/api/routes";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";

const days=[{id:1,short:"Lun",name:"Lunes"},{id:2,short:"Mar",name:"Martes"},{id:3,short:"Mié",name:"Miércoles"},{id:4,short:"Jue",name:"Jueves"},{id:5,short:"Vie",name:"Viernes"},{id:6,short:"Sáb",name:"Sábado"},{id:7,short:"Dom",name:"Domingo"}];
const dayName=(value:number)=>days.find(day=>day.id===value)?.short??String(value);

export default function RoutesPage(){
  const permissions=useAuthStore(state=>new Set(state.user?.permissions??[]));
  const [page,setPage]=useState(1),[pageSize,setPageSize]=useState(25),[search,setSearch]=useState("");
  const [sellerId,setSellerId]=useState("all"),[zoneId,setZoneId]=useState("all"),[day,setDay]=useState("all"),[status,setStatus]=useState("active");
  const [selectedRoute,setSelectedRoute]=useState<string>(),[createOpen,setCreateOpen]=useState(false);
  const options=useRouteOptions();
  const query=useRoutes({page,pageSize,search:search.trim()||undefined,sellerId:sellerId==="all"?undefined:sellerId,zoneId:zoneId==="all"?undefined:zoneId,dayOfWeek:day==="all"?undefined:Number(day),isActive:status==="all"?undefined:status==="active"});
  const columns=useMemo<ColumnDef<SalesRouteListItem>[]>(()=>[
    {accessorKey:"name",header:"Ruta",cell:({row})=><div><p className="font-semibold">{row.original.name}</p><p className="text-xs text-muted-foreground">{row.original.code}</p></div>},
    {accessorKey:"sellerName",header:"Vendedor",cell:({row})=><div><p>{row.original.sellerName}</p><p className="text-xs text-muted-foreground">{row.original.zoneName??"Sin zona"}</p></div>},
    {id:"days",header:"Días",cell:({row})=><div className="flex flex-wrap gap-1">{row.original.days.map(value=><Badge key={value} variant="outline">{dayName(value)}</Badge>)}</div>},
    {accessorKey:"stopCount",header:"Establecimientos",cell:({row})=><span className="font-medium tabular-nums">{row.original.stopCount}</span>},
    {accessorKey:"preparationStatus",header:"Preparación",cell:({row})=><Badge variant={row.original.preparationStatus==="Ready"?"secondary":"outline"}>{row.original.preparationStatus==="Ready"?"Lista":"Borrador"}</Badge>},
    {accessorKey:"isActive",header:"Estado",cell:({row})=><Badge variant={row.original.isActive?"default":"outline"}>{row.original.isActive?"Activa":"Inactiva"}</Badge>},
    {id:"actions",header:"",cell:({row})=><Button size="sm" variant="ghost" onClick={(event)=>{event.stopPropagation();setSelectedRoute(row.original.routeId)}}><Pencil className="mr-2 h-4 w-4"/>Abrir</Button>}
  ],[]);
  return <div className="space-y-6">
    <header className="flex flex-col justify-between gap-4 xl:flex-row xl:items-end"><div><p className="text-sm font-medium text-primary">Operación comercial</p><h1 className="text-3xl font-semibold tracking-tight">Rutas comerciales</h1><p className="mt-1 max-w-3xl text-muted-foreground">Organiza recorridos por vendedor y día, asignando el establecimiento exacto de cada cliente.</p></div><Button disabled={!permissions.has("routes.create")} onClick={()=>setCreateOpen(true)}><Plus className="mr-2 h-4 w-4"/>Nueva ruta</Button></header>
    <section className="grid gap-3 md:grid-cols-3"><Summary icon={Route} label="Rutas encontradas" value={String(query.data?.totalCount??0)}/><Summary icon={MapPinned} label="Establecimientos visibles" value={String(query.data?.items.reduce((sum,item)=>sum+item.stopCount,0)??0)}/><Summary icon={CalendarDays} label="Programación" value="Por día y orden"/></section>
    <section className="grid gap-3 rounded-2xl border bg-card p-4 xl:grid-cols-[minmax(14rem,1fr)_14rem_14rem_10rem_10rem]"><label className="relative"><Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground"/><Input className="pl-9" value={search} onChange={event=>{setSearch(event.target.value);setPage(1)}} placeholder="Ruta, código, vendedor o zona"/></label><PartyRoleSelect role="Seller" value={sellerId} leadingOptions={[{value:"all",label:"Todos los vendedores"}]} placeholder="Buscar vendedor" onChange={value=>{setSellerId(value);setPage(1)}}/><Filter value={zoneId} onChange={value=>{setZoneId(value);setPage(1)}} placeholder="Zona" allLabel="Todas las zonas" items={(options.data?.zones.filter(item=>item.isActive)??[]).map(item=>({value:item.zoneId,label:item.name}))}/><Filter value={day} onChange={value=>{setDay(value);setPage(1)}} placeholder="Día" allLabel="Todos los días" items={days.map(item=>({value:String(item.id),label:item.name}))}/><Filter value={status} onChange={value=>{setStatus(value);setPage(1)}} placeholder="Estado" allLabel="Todos" items={[{value:"active",label:"Activas"},{value:"inactive",label:"Inactivas"}]}/></section>
    <DataTable columns={columns} data={query.data?.items??[]} isLoading={query.isLoading} page={query.data?.page} pageSize={query.data?.pageSize} pageCount={query.data?.totalPages} totalItems={query.data?.totalCount} enableRowSelection={false} onRowClick={item=>setSelectedRoute(item.routeId)} onPaginationChange={(newPage,newSize)=>{setPage(newPage);setPageSize(newSize)}}/>
    <RouteWorkspace open={createOpen||!!selectedRoute} routeId={selectedRoute} permissions={permissions} onCreated={routeId=>{setCreateOpen(false);setSelectedRoute(routeId)}} onClose={()=>{setCreateOpen(false);setSelectedRoute(undefined)}}/>
  </div>;
}

function RouteWorkspace({open,routeId,permissions,onCreated,onClose}:{open:boolean;routeId?:string;permissions:Set<string>;onCreated:(id:string)=>void;onClose:()=>void}){
  const businessId=useBusinessContextStore(state=>state.selectedBusinessId);
  const options=useRouteOptions(),detail=useRouteDetail(routeId),create=useCreateRoute(),update=useUpdateRoute(),status=useSetRouteStatus();
  const [tab,setTab]=useState("general"),[code,setCode]=useState(""),[name,setName]=useState(""),[seller,setSeller]=useState(""),[zone,setZone]=useState("none"),[notes,setNotes]=useState("");
  const [stopsSaving,setStopsSaving]=useState(false);
  const stopsRef=useRef<RouteStopsHandle>(null);
  const [schedule,setSchedule]=useState<Record<number,{selected:boolean;runOrder:string;time:string}>>(()=>Object.fromEntries(days.map(day=>[day.id,{selected:day.id<=5,runOrder:"1",time:""}])));
  const loaded=useRef<string | undefined>(undefined);
  useEffect(()=>{if(!open)setTab("general")},[open]);
  useEffect(()=>{
    const value=detail.data;if(!value||loaded.current===value.rowVersion)return;loaded.current=value.rowVersion;
    setCode(value.code);setName(value.name);setSeller(value.sellerId);setZone(value.zoneId??"none");setNotes(value.notes??"");
    setSchedule(Object.fromEntries(days.map(day=>{const found=value.schedules.find(item=>item.dayOfWeek===day.id);return [day.id,{selected:!!found,runOrder:String(found?.runOrder??1),time:found?.plannedStartTime?.slice(0,5)??""}]})));
  },[detail.data]);
  const schedules=():RouteScheduleInput[]=>days.filter(day=>schedule[day.id].selected).map(day=>({dayOfWeek:day.id,runOrder:Number(schedule[day.id].runOrder)||1,plannedStartTime:null}));
  const save=async()=>{
    if(!businessId||!code.trim()||!name.trim()||!seller)return toast.error("Completa código, nombre y vendedor.");
    if(!schedules().length)return toast.error("Selecciona al menos un día.");
    try{
      if(routeId&&detail.data){await update.mutateAsync({routeId,request:{code,name,sellerId:seller,zoneId:zone==="none"?null:zone,notes:notes||null,schedules:schedules(),rowVersion:detail.data.rowVersion}});toast.success("Ruta actualizada");await detail.refetch()}
      else{const result=await create.mutateAsync({businessId,code,name,sellerId:seller,zoneId:zone==="none"?null:zone,notes:notes||null,schedules:schedules()});toast.success("Ruta creada. Ya puedes construir el recorrido.");onCreated(result.routeId);setTab("stops")}
    }catch(error){toast.error(message(error,"No fue posible guardar la ruta."))}
  };
  const changeStatus=async()=>{
    if(!routeId||!detail.data)return;
    try{
      const refreshed=await detail.refetch();
      const current=refreshed.data??detail.data;
      await status.mutateAsync({routeId,isActive:!current.isActive,rowVersion:current.rowVersion});
      toast.success(current.isActive?"Ruta desactivada":"Ruta activada");
      await detail.refetch();
    }catch(error){toast.error(message(error,"No fue posible cambiar el estado."))}
  };
  const canSave=!routeId?permissions.has("routes.create"):permissions.has("routes.update");
  const saveCurrentTab=async()=>{if(tab==="general")return save();if(!stopsRef.current)return;setStopsSaving(true);try{await stopsRef.current.save()}finally{setStopsSaving(false)}};
  return <Dialog open={open} onOpenChange={value=>!value&&onClose()}><DialogContent className="flex max-h-[94vh] max-w-6xl flex-col overflow-hidden"><DialogHeader><DialogTitle>{routeId?(detail.data?.name??"Ruta comercial"):"Nueva ruta comercial"}</DialogTitle><DialogDescription>{routeId?"Configura el calendario y construye un recorrido ordenado por establecimientos.":"Define primero la identidad, el vendedor y los días de trabajo."}</DialogDescription></DialogHeader>
    <Tabs value={tab} onValueChange={setTab} className="min-h-0 flex-1 overflow-hidden"><TabsList><TabsTrigger value="general">Configuración</TabsTrigger><TabsTrigger value="stops" disabled={!routeId}>Recorrido {detail.data?`(${detail.data.stops.length})`:""}</TabsTrigger></TabsList>
      <TabsContent value="general" className="max-h-[66vh] space-y-5 overflow-y-auto pr-2"><div className="grid gap-4 md:grid-cols-2"><Field label="Código"><Input value={code} maxLength={32} onChange={event=>setCode(event.target.value.toUpperCase())} placeholder="RUTA-NORTE"/></Field><Field label="Nombre"><Input value={name} maxLength={160} onChange={event=>setName(event.target.value)} placeholder="Ruta norte"/></Field><Field label="Vendedor"><PartyRoleSelect role="Seller" value={seller} placeholder="Buscar vendedor" selectedOption={detail.data&&seller?{value:seller,label:detail.data.sellerName}:null} onChange={setSeller}/></Field><Field label="Zona comercial"><Select value={zone} onValueChange={setZone}><SelectTrigger><SelectValue placeholder="Sin zona"/></SelectTrigger><SelectContent><SelectItem value="none">Sin zona</SelectItem>{options.data?.zones.filter(item=>item.isActive).map(item=><SelectItem key={item.zoneId} value={item.zoneId}>{item.name}</SelectItem>)}</SelectContent></Select></Field></div>
        <ZoneCreator businessId={businessId} onCreated={setZone} canCreate={permissions.has("route-zones.manage")}/>
        <Field label="Notas operativas"><Textarea value={notes} maxLength={500} onChange={event=>setNotes(event.target.value)} placeholder="Indicaciones útiles para preparar el recorrido"/></Field>
        <section className="rounded-2xl border p-4"><div className="mb-4"><h3 className="font-semibold">Días de trabajo</h3><p className="text-sm text-muted-foreground">Selecciona los días. Si el vendedor tiene varias rutas el mismo día, indica cuál recorrido hace primero.</p></div><div className="grid gap-3 lg:grid-cols-2">{days.map(day=>{const value=schedule[day.id];return <div key={day.id} className="flex flex-wrap items-center gap-3 rounded-xl border p-3"><Checkbox checked={value.selected} onCheckedChange={checked=>setSchedule(current=>({...current,[day.id]:{...current[day.id],selected:checked===true,time:""}}))}/><Label className="min-w-28 flex-1">{day.name}</Label>{value.selected&&<Select value={value.runOrder} onValueChange={runOrder=>setSchedule(current=>({...current,[day.id]:{...current[day.id],runOrder}}))}><SelectTrigger className="w-44"><SelectValue/></SelectTrigger><SelectContent>{Array.from({length:10},(_,index)=>index+1).map(order=><SelectItem key={order} value={String(order)}>{order===1?"Primer recorrido":order===2?"Segundo recorrido":String(order)+".º recorrido"}</SelectItem>)}</SelectContent></Select>}</div>})}</div></section>
      </TabsContent>
      <TabsContent value="stops" className="min-h-0"><RouteStops ref={stopsRef} routeId={routeId} detail={detail} permissions={permissions}/></TabsContent>
    </Tabs>
    <DialogFooter className="border-t pt-4"><Button variant="outline" onClick={onClose}>Cerrar</Button>{routeId&&detail.data&&<Button variant="outline" disabled={!permissions.has(detail.data.isActive?"routes.deactivate":"routes.activate")||status.isPending||tab!=="general"} onClick={changeStatus}>{detail.data.isActive?"Desactivar":"Activar"}</Button>}<Button disabled={(tab==="general"?!canSave:!routeId||!permissions.has("routes.stops.manage"))||create.isPending||update.isPending||stopsSaving} onClick={saveCurrentTab}>{routeId?(stopsSaving?"Guardando…":"Guardar cambios"):"Crear ruta"}</Button></DialogFooter>
  </DialogContent></Dialog>;
}

type EditableRouteStop=SalesRouteStop&{isNew?:boolean};
type RouteStopsHandle={save:()=>Promise<void>};

const RouteStops=forwardRef<RouteStopsHandle,{routeId?:string;detail:ReturnType<typeof useRouteDetail>;permissions:Set<string>}>(function RouteStops({routeId,detail,permissions},ref){
  const [candidateOpen,setCandidateOpen]=useState(false),[removeId,setRemoveId]=useState<string>(),[ordered,setOrdered]=useState<EditableRouteStop[]>([]),[times,setTimes]=useState<Record<string,string>>({}),[optimizing,setOptimizing]=useState(false),[dirty,setDirty]=useState(false);
  const dragging=useRef<string|undefined>(undefined),dragOriginal=useRef<string[]>([]),orderedRef=useRef<EditableRouteStop[]>([]),hydratedVersion=useRef<string|undefined>(undefined);
  const data=detail.data;
  const hydrate=(stops:SalesRouteStop[],rowVersion:string)=>{const next=stops.map(stop=>({...stop}));orderedRef.current=next;setOrdered(next);setTimes(Object.fromEntries(next.map(stop=>[stop.routeStopId,stop.plannedVisitTime?.slice(0,5)??""])));hydratedVersion.current=rowVersion;setDirty(false)};
  useEffect(()=>{if(!data||dirty||hydratedVersion.current===data.rowVersion)return;hydrate(data.stops,data.rowVersion)},[data,dirty]);
  const applyLocal=(next:EditableRouteStop[])=>{orderedRef.current=next;setOrdered(next);setDirty(true)};
  const save=async()=>{
    if(!routeId||!data||!dirty){toast.info("No hay cambios pendientes en el recorrido.");return}
    let rowVersion=data.rowVersion;
    try{
      const desired=orderedRef.current;
      const desiredExistingIds=new Set(desired.filter(stop=>!stop.isNew).map(stop=>stop.routeStopId));
      for(const stop of data.stops.filter(stop=>!desiredExistingIds.has(stop.routeStopId))){const result=await routesApi.removeStop(routeId,stop.routeStopId,rowVersion);rowVersion=result.rowVersion}
      const additions=desired.filter(stop=>stop.isNew);
      if(additions.length){const result=await routesApi.addStops(routeId,additions.map(stop=>({customerId:stop.customerId,partySiteId:stop.partySiteId,plannedVisitTime:times[stop.routeStopId]||null,visitNote:stop.visitNote})),rowVersion);rowVersion=result.rowVersion}
      for(const stop of desired.filter(stop=>!stop.isNew)){
        const original=data.stops.find(item=>item.routeStopId===stop.routeStopId);
        const plannedVisitTime=times[stop.routeStopId]||null;
        if(original&&(original.plannedVisitTime?.slice(0,5)??null)!==plannedVisitTime){const result=await routesApi.updateStop(routeId,stop.routeStopId,{plannedVisitTime,visitNote:stop.visitNote,routeRowVersion:rowVersion});rowVersion=result.rowVersion}
      }
      let persisted=await routesApi.detail(routeId);
      const desiredIds=desired.map(stop=>stop.isNew?persisted.stops.find(item=>item.partySiteId===stop.partySiteId)?.routeStopId:stop.routeStopId).filter((value):value is string=>!!value);
      if(desiredIds.length!==desired.length)throw new Error("No fue posible identificar todos los establecimientos agregados.");
      if(desiredIds.join("|")!==persisted.stops.map(stop=>stop.routeStopId).join("|")){const result=await routesApi.reorder(routeId,desiredIds,rowVersion);rowVersion=result.rowVersion;persisted=await routesApi.detail(routeId)}
      hydrate(persisted.stops,persisted.rowVersion);
      await detail.refetch();
      toast.success("Cambios del recorrido guardados");
    }catch(error){
      const refreshed=await detail.refetch();
      if(refreshed.data)hydrate(refreshed.data.stops,refreshed.data.rowVersion);
      toast.error(message(error,"No fue posible guardar los cambios del recorrido."));
      return;
    }
  };
  useImperativeHandle(ref,()=>({save}));
  if(!routeId||!data)return <div className="py-16 text-center text-muted-foreground">Guarda la configuración para asignar clientes y ordenar el recorrido.</div>;
  const move=(index:number,direction:-1|1)=>{const target=index+direction;if(target<0||target>=ordered.length)return;const next=[...ordered];[next[index],next[target]]=[next[target],next[index]];applyLocal(next)};
  const pointerDown=(event:React.PointerEvent<HTMLButtonElement>,stopId:string)=>{dragging.current=stopId;dragOriginal.current=orderedRef.current.map(stop=>stop.routeStopId);event.currentTarget.setPointerCapture(event.pointerId);document.body.classList.add("select-none")};
  const pointerMove=(event:React.PointerEvent<HTMLButtonElement>)=>{if(!dragging.current)return;const element=document.elementFromPoint(event.clientX,event.clientY)?.closest<HTMLElement>("[data-route-stop-id]");const targetId=element?.dataset.routeStopId;if(!targetId||targetId===dragging.current)return;const current=[...orderedRef.current],from=current.findIndex(stop=>stop.routeStopId===dragging.current),to=current.findIndex(stop=>stop.routeStopId===targetId);if(from<0||to<0)return;const [item]=current.splice(from,1);current.splice(to,0,item);applyLocal(current)};
  const pointerUp=(event:React.PointerEvent<HTMLButtonElement>)=>{if(!dragging.current)return;try{event.currentTarget.releasePointerCapture(event.pointerId)}catch{}document.body.classList.remove("select-none");dragging.current=undefined;if(orderedRef.current.map(stop=>stop.routeStopId).join("|")!==dragOriginal.current.join("|"))setDirty(true)};
  const optimize=async()=>{const located=ordered.filter(stop=>stop.latitude!=null&&stop.longitude!=null);if(located.length<2)return toast.info("Se necesitan al menos dos establecimientos ubicados para ordenar por cercanía.");setOptimizing(true);try{const origin=await currentPosition().catch(()=>({latitude:located[0].latitude!,longitude:located[0].longitude!}));const pending=[...located],result:EditableRouteStop[]=[];let point=origin;while(pending.length){pending.sort((left,right)=>distance(point,left)-distance(point,right));const next=pending.shift()!;result.push(next);point={latitude:next.latitude!,longitude:next.longitude!}}const unlocated=ordered.filter(stop=>stop.latitude==null||stop.longitude==null);applyLocal([...result,...unlocated])}finally{setOptimizing(false)}};
  const removeConfirmed=()=>{if(!removeId)return;applyLocal(orderedRef.current.filter(stop=>stop.routeStopId!==removeId));setRemoveId(undefined)};
  const print=async()=>{try{await routesApi.export(routeId);window.print()}catch(error){toast.error(message(error,"No tienes acceso para imprimir este recorrido."))}};
  const download=async()=>{try{const exported=await routesApi.export(routeId);const rows=[["Orden","Hora sugerida","Cliente","Establecimiento","Dirección","Ciudad","Barrio","Teléfono"],...exported.stops.map(stop=>[String(stop.sequence),stop.plannedVisitTime?.slice(0,5)??"",stop.customerName,stop.siteName,stop.addressLine,stop.cityName,stop.neighborhood??"",stop.phone??""])];const csv=rows.map(row=>row.map(value=>`"${value.replaceAll('"','""')}"`).join(",")).join("\r\n");const link=document.createElement("a");link.href=URL.createObjectURL(new Blob(["\uFEFF"+csv],{type:"text/csv;charset=utf-8"}));link.download=`${exported.code}-recorrido.csv`;link.click();URL.revokeObjectURL(link.href)}catch(error){toast.error(message(error,"No fue posible exportar el recorrido."))}};
  return <div className="flex max-h-[68vh] min-h-[30rem] flex-col gap-4 overflow-hidden pt-2"><div className="flex flex-wrap justify-between gap-3"><div><h3 className="font-semibold">2. Clientes y orden de visita</h3><p className="text-sm text-muted-foreground">Arrastra, usa las flechas o calcula una propuesta. Los cambios se aplican al pulsar Guardar cambios.</p>{dirty&&<p className="mt-1 text-xs font-medium text-amber-700">Hay cambios pendientes por guardar.</p>}</div><div className="flex flex-wrap gap-2"><Button variant="outline" disabled={!permissions.has("routes.stops.manage")||ordered.length<2||optimizing} onClick={optimize}><Sparkles className="mr-2 h-4 w-4"/>{optimizing?"Calculando…":"Ordenar por cercanía"}</Button><Button variant="outline" disabled={!permissions.has("routes.export")||!ordered.length||dirty} onClick={print}><Printer className="mr-2 h-4 w-4"/>Imprimir</Button><Button variant="outline" disabled={!permissions.has("routes.export")||!ordered.length||dirty} onClick={download}><Download className="mr-2 h-4 w-4"/>CSV</Button><Button disabled={!permissions.has("routes.stops.manage")} onClick={()=>setCandidateOpen(true)}><Plus className="mr-2 h-4 w-4"/>Asignar clientes</Button></div></div>
    <div className="min-h-0 flex-1 space-y-2 overflow-y-auto rounded-2xl border bg-muted/20 p-2">{!ordered.length?<div className="grid min-h-72 place-items-center p-8 text-center"><div><MapPinned className="mx-auto h-10 w-10 text-primary"/><p className="mt-3 font-semibold">Asigna los clientes de esta ruta</p><p className="mt-1 text-sm text-muted-foreground">Seleccionarás la sede exacta y luego podrás organizar el recorrido.</p><Button className="mt-5" disabled={!permissions.has("routes.stops.manage")} onClick={()=>setCandidateOpen(true)}><Plus className="mr-2 h-4 w-4"/>Agregar clientes ahora</Button></div></div>:ordered.map((stop,index)=><div key={stop.routeStopId} data-route-stop-id={stop.routeStopId} className="grid items-center gap-3 rounded-2xl border bg-card p-3 shadow-sm sm:grid-cols-[auto_3rem_minmax(11rem,1fr)_minmax(12rem,1.25fr)_9rem_auto]"><button type="button" aria-label={`Arrastrar ${stop.customerName}`} className="grid h-10 w-10 touch-none place-items-center rounded-xl text-muted-foreground hover:bg-muted" onPointerDown={event=>pointerDown(event,stop.routeStopId)} onPointerMove={pointerMove} onPointerUp={pointerUp} onPointerCancel={pointerUp}><GripVertical className="h-5 w-5"/></button><span className="text-center text-lg font-black tabular-nums">{index+1}</span><span className="min-w-0"><strong className="block truncate">{stop.customerName}</strong><small className="text-muted-foreground">{stop.identification??"Sin identificación"}</small></span><span className="min-w-0"><strong className="block truncate">{stop.siteName}</strong><small className="block truncate text-muted-foreground">{stop.addressLine} · {stop.cityName}</small></span><label className="relative"><Clock3 className="pointer-events-none absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground"/><Input aria-label={`Hora sugerida para ${stop.customerName}`} type="time" className="pl-8" value={times[stop.routeStopId]??""} onChange={event=>{setTimes(current=>({...current,[stop.routeStopId]:event.target.value}));setDirty(true)}}/></label><span className="flex justify-end gap-1"><Button size="icon" variant="ghost" disabled={index===0} onClick={()=>move(index,-1)}><ArrowUp className="h-4 w-4"/></Button><Button size="icon" variant="ghost" disabled={index===ordered.length-1} onClick={()=>move(index,1)}><ArrowDown className="h-4 w-4"/></Button><Button size="icon" variant="ghost" onClick={()=>setRemoveId(stop.routeStopId)}><Trash2 className="h-4 w-4"/></Button></span></div>)}</div>
    <CandidateDialog open={candidateOpen} routeId={routeId} excludedSiteIds={new Set(ordered.map(stop=>stop.partySiteId))} onClose={()=>setCandidateOpen(false)} onAdd={(values)=>{const additions:EditableRouteStop[]=values.map(({candidate,time},index)=>({routeStopId:`draft:${candidate.partySiteId}`,customerId:candidate.customerId,partySiteId:candidate.partySiteId,sequence:orderedRef.current.length+index+1,customerName:candidate.customerName,identification:candidate.identification,siteName:candidate.siteName,addressLine:candidate.addressLine,neighborhood:candidate.neighborhood,cityName:candidate.cityName,phone:candidate.phone,googleMapsUrl:candidate.googleMapsUrl,latitude:candidate.latitude,longitude:candidate.longitude,plannedVisitTime:time||null,visitNote:null,rowVersion:"",isNew:true}));applyLocal([...orderedRef.current,...additions]);setTimes(current=>({...current,...Object.fromEntries(additions.map(stop=>[stop.routeStopId,stop.plannedVisitTime?.slice(0,5)??""]))}));setCandidateOpen(false)}}/>
    <Dialog open={!!removeId} onOpenChange={(value:boolean)=>!value&&setRemoveId(undefined)}><DialogContent className="max-w-md"><DialogHeader><DialogTitle>¿Retirar este establecimiento?</DialogTitle><DialogDescription>La parada saldrá del recorrido activo y el resto se renumerará automáticamente. El historial se conserva.</DialogDescription></DialogHeader><DialogFooter><Button variant="outline" onClick={()=>setRemoveId(undefined)}>Cancelar</Button><Button variant="destructive" onClick={removeConfirmed}>Retirar</Button></DialogFooter></DialogContent></Dialog>
  </div>;
});

function currentPosition(){return new Promise<{latitude:number;longitude:number}>((resolve,reject)=>navigator.geolocation.getCurrentPosition(value=>resolve({latitude:value.coords.latitude,longitude:value.coords.longitude}),reject,{enableHighAccuracy:true,timeout:8000,maximumAge:60000}))}
function distance(origin:{latitude:number;longitude:number},stop:SalesRouteStop){const radians=(value:number)=>value*Math.PI/180,dLat=radians(stop.latitude!-origin.latitude),dLon=radians(stop.longitude!-origin.longitude),a=Math.sin(dLat/2)**2+Math.cos(radians(origin.latitude))*Math.cos(radians(stop.latitude!))*Math.sin(dLon/2)**2;return 2*6371*Math.asin(Math.sqrt(a))}

function CandidateDialog({open,routeId,excludedSiteIds,onClose,onAdd}:{open:boolean;routeId:string;excludedSiteIds:Set<string>;onClose:()=>void;onAdd:(values:Array<{candidate:RouteCandidateSite;time:string}>)=>void}){
  const [search,setSearch]=useState(""),[page,setPage]=useState(1),[selected,setSelected]=useState<Record<string,RouteCandidateSite>>({}),[times,setTimes]=useState<Record<string,string>>({});const query=useRouteCandidates(open?routeId:undefined,search,page);
  const submit=()=>{const values=Object.values(selected);if(!values.length)return;onAdd(values.map(candidate=>({candidate,time:times[candidate.partySiteId]??""})));setSelected({});setTimes({})};
  return <Dialog open={open} onOpenChange={value=>!value&&onClose()}><DialogContent className="flex max-h-[88vh] max-w-4xl flex-col overflow-hidden"><DialogHeader><DialogTitle>Asignar clientes a la ruta</DialogTitle><DialogDescription>Selecciona la sede exacta. Quedará pendiente hasta pulsar Guardar cambios.</DialogDescription></DialogHeader><label className="relative"><Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground"/><Input autoFocus className="pl-9" value={search} onChange={event=>{setSearch(event.target.value);setPage(1)}} placeholder="Cliente, documento, sede, dirección o teléfono"/></label><div className="min-h-0 flex-1 space-y-2 overflow-y-auto">{query.isLoading?<p className="py-12 text-center text-muted-foreground">Buscando...</p>:query.data?.items.map(item=>{const locallyIncluded=excludedSiteIds.has(item.partySiteId),disabled=item.isAlreadyInRoute||item.hasScheduleConflict||locallyIncluded,checked=!!selected[item.partySiteId];return <div key={item.partySiteId} className={`grid gap-3 rounded-xl border p-3 sm:grid-cols-[auto_1fr_9rem] ${disabled?"bg-muted/40 opacity-70":"hover:border-primary/50"}`}><Checkbox checked={checked} disabled={disabled} onCheckedChange={value=>setSelected(current=>{const next={...current};if(value===true)next[item.partySiteId]=item;else delete next[item.partySiteId];return next})}/><button type="button" disabled={disabled} onClick={()=>!disabled&&setSelected(current=>{const next={...current};if(next[item.partySiteId])delete next[item.partySiteId];else next[item.partySiteId]=item;return next})} className="min-w-0 text-left"><strong className="block">{item.customerName} · {item.siteName}</strong><span className="block text-sm text-muted-foreground">{item.addressLine} · {item.cityName}{item.neighborhood?` · ${item.neighborhood}`:""}</span>{(item.isAlreadyInRoute||locallyIncluded)&&<span className="text-xs text-primary">Ya pertenece a esta ruta.</span>}{item.conflictDescription&&<span className="block text-xs text-destructive">{item.conflictDescription}</span>}</button><Input aria-label={`Hora sugerida para ${item.customerName}`} type="time" disabled={!checked} value={times[item.partySiteId]??""} onChange={event=>setTimes(current=>({...current,[item.partySiteId]:event.target.value}))}/></div>})}{!query.isLoading&&!query.data?.items.length&&<p className="py-12 text-center text-muted-foreground">No se encontraron establecimientos.</p>}</div><div className="flex items-center justify-between text-sm text-muted-foreground"><span>{query.data?.totalCount??0} resultados · {Object.keys(selected).length} seleccionados</span><div className="flex gap-2"><Button size="sm" variant="outline" disabled={page<=1} onClick={()=>setPage(value=>value-1)}>Anterior</Button><Button size="sm" variant="outline" disabled={!query.data||page>=query.data.totalPages} onClick={()=>setPage(value=>value+1)}>Siguiente</Button></div></div><DialogFooter><Button variant="outline" onClick={onClose}>Cancelar</Button><Button disabled={!Object.keys(selected).length} onClick={submit}>Agregar a la ruta</Button></DialogFooter></DialogContent></Dialog>;
}

function ZoneCreator({businessId,onCreated,canCreate}:{businessId:string|null;onCreated:(zoneId:string)=>void;canCreate:boolean}){
  const [open,setOpen]=useState(false),[code,setCode]=useState(""),[name,setName]=useState("");const create=useCreateRouteZone();
  const submit=async()=>{if(!businessId||!code.trim()||!name.trim())return toast.error("Completa código y nombre de la zona.");try{const result=await create.mutateAsync({businessId,code,name});onCreated(result.zoneId);setOpen(false);setCode("");setName("");toast.success("Zona comercial creada")}catch(error){toast.error(message(error,"No fue posible crear la zona."))}};
  return <div className="flex justify-end"><Button type="button" size="sm" variant="ghost" disabled={!canCreate} onClick={()=>setOpen(true)}><Plus className="mr-2 h-4 w-4"/>Crear zona comercial</Button>
    <Dialog open={open} onOpenChange={setOpen}><DialogContent className="max-w-md"><DialogHeader><DialogTitle>Nueva zona comercial</DialogTitle><DialogDescription>Agrupa recorridos con una clasificación propia del negocio.</DialogDescription></DialogHeader>
      <Field label="Código"><Input value={code} maxLength={32} onChange={event=>setCode(event.target.value.toUpperCase())} placeholder="NORTE"/></Field>
      <Field label="Nombre"><Input value={name} maxLength={160} onChange={event=>setName(event.target.value)} placeholder="Zona norte"/></Field>
      <DialogFooter><Button variant="outline" onClick={()=>setOpen(false)}>Cancelar</Button><Button disabled={create.isPending} onClick={submit}>Crear zona</Button></DialogFooter>
    </DialogContent></Dialog>
  </div>;
}

function Filter({value,onChange,placeholder,allLabel,items}:{value:string;onChange:(value:string)=>void;placeholder:string;allLabel:string;items:Array<{value:string;label:string}>}){return <Select value={value} onValueChange={onChange}><SelectTrigger><SelectValue placeholder={placeholder}/></SelectTrigger><SelectContent><SelectItem value="all">{allLabel}</SelectItem>{items.map(item=><SelectItem key={item.value} value={item.value}>{item.label}</SelectItem>)}</SelectContent></Select>}
function Field({label,children}:{label:string;children:React.ReactNode}){return <div className="space-y-2"><Label>{label}</Label>{children}</div>}
function Summary({icon:Icon,label,value}:{icon:typeof Route;label:string;value:string}){return <Card><CardContent className="flex items-center gap-3 p-4"><span className="rounded-xl bg-primary/10 p-2 text-primary"><Icon className="h-5 w-5"/></span><div><p className="text-xs text-muted-foreground">{label}</p><p className="font-semibold">{value}</p></div></CardContent></Card>}
function message(error:unknown,fallback:string){if(error&&typeof error==="object"&&"message" in error&&typeof error.message==="string")return error.message;return fallback}
