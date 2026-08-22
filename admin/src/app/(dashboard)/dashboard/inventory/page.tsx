"use client";

import { useDeferredValue, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, Boxes, History, RefreshCw, Search } from "lucide-react";
import { toast } from "sonner";
import { InventoryOperationWorkspace } from "@/components/inventory/inventory-operation-workspace";
import { DataTablePagination } from "@/components/tables/data-table-pagination";
import { inventoryApi, type InventoryBalanceItem, type InventoryOperationDetail } from "@/services/api/inventory";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { useAuthStore } from "@/stores/auth-store";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { formatCurrency, formatDateTime } from "@/lib/utils";

const fmt=(value:number)=>new Intl.NumberFormat("es-CO",{maximumFractionDigits:6}).format(value);
const documentLabels:Record<string,string>={StockCount:"Conteo físico",InventoryAdjustment:"Ajuste",WarehouseTransfer:"Traslado",ProductConversion:"Conversión",Damage:"Avería",SalesInvoice:"Venta",GoodsReceipt:"Recepción"};
const movementLabels:Record<string,string>={GoodsReceipt:"Recepci\u00f3n de mercanc\u00eda",InventoryAdjustment:"Ajuste de inventario",StockCount:"Ajuste por conteo f\u00edsico",StockCountAdjustment:"Ajuste por conteo f\u00edsico",WarehouseTransfer:"Traslado entre bodegas",TransferOut:"Salida por traslado",TransferIn:"Entrada por traslado",ProductConversion:"Conversi\u00f3n de producto",ConversionInput:"Consumo por conversi\u00f3n",ConversionOutput:"Producci\u00f3n por conversi\u00f3n",Damage:"Salida por aver\u00eda",InventoryDamage:"Salida por aver\u00eda",SalesInvoice:"Venta",Sale:"Venta",SalesReturn:"Devoluci\u00f3n de venta"};
const statusLabels:Record<string,string>={Draft:"Borrador",Accepted:"En proceso",Processed:"Procesado",Confirmed:"Confirmado",Failed:"Con error"};
const reasonLabels:Record<string,string>={GOODS_RECEIPT:"Recepción de mercancía",PHYSICAL_COUNT:"Conteo físico",INVENTORY_VERIFICATION:"Verificación de existencias",INITIAL_BALANCE:"Saldo inicial",FOUND_SURPLUS:"Sobrante encontrado",WAREHOUSE_TRANSFER:"Traslado entre bodegas",DAMAGE:"Avería"};
const humanLabel=(value:string|null|undefined,labels:Record<string,string>)=>value ? labels[value]??value.replace(/([a-z])([A-Z])/g,"$1 $2") : "\u2014";

export default function InventoryPage(){
 const businessId=useBusinessContextStore(s=>s.selectedBusinessId);
 const queryClient=useQueryClient();
 const permissions=new Set(useAuthStore(s=>s.user?.permissions??[]));
 const [search,setSearch]=useState(""); const deferredSearch=useDeferredValue(search.trim());
 const [warehouseId,setWarehouseId]=useState("all"); const [activeTab,setActiveTab]=useState("balances");
 const [pageSize,setPageSize]=useState(20);
 const [pages,setPages]=useState({balances:1,movements:1,operations:1,conversions:1}); const [detail,setDetail]=useState<InventoryOperationDetail>(); const [detailLoading,setDetailLoading]=useState(false);
 const resetPages=()=>setPages({balances:1,movements:1,operations:1,conversions:1});
 const warehouseOptions=useQuery({queryKey:["inventory-warehouses",businessId],queryFn:()=>inventoryApi.warehouses(),enabled:!!businessId});
 const balances=useQuery({queryKey:["inventory-balances",businessId,warehouseId,deferredSearch,pages.balances,pageSize],queryFn:()=>inventoryApi.balances({warehouseId:warehouseId==="all"?undefined:warehouseId,search:deferredSearch||undefined,page:pages.balances,pageSize}),enabled:!!businessId});
 const movements=useQuery({queryKey:["inventory-movements",businessId,warehouseId,deferredSearch,pages.movements,pageSize],queryFn:()=>inventoryApi.movements({warehouseId:warehouseId==="all"?undefined:warehouseId,search:deferredSearch||undefined,page:pages.movements,pageSize}),enabled:!!businessId});
 const operations=useQuery({queryKey:["inventory-operations",businessId,warehouseId,deferredSearch,pages.operations,pageSize],queryFn:()=>inventoryApi.operations({warehouseId:warehouseId==="all"?undefined:warehouseId,search:deferredSearch||undefined,page:pages.operations,pageSize}),enabled:!!businessId,refetchInterval:query=>query.state.data?.items.some(item=>item.status==="Accepted")?2_000:false});
 const conversions=useQuery({queryKey:["inventory-conversions",businessId,warehouseId,deferredSearch,pages.conversions,pageSize],queryFn:()=>inventoryApi.operations({warehouseId:warehouseId==="all"?undefined:warehouseId,search:deferredSearch||undefined,documentType:"ProductConversion",page:pages.conversions,pageSize}),enabled:!!businessId,refetchInterval:query=>query.state.data?.items.some(item=>item.status==="Accepted")?2_000:false});
 const warehouses=(warehouseOptions.data??[]).map(x=>({id:x.warehouseId,name:x.name}));
 async function openOperationDetail(item?:{documentId:string}){
  if(!item)return;
  setDetail(undefined);
  setDetailLoading(true);
  try{setDetail(await queryClient.fetchQuery({queryKey:["inventory-operation-detail",businessId,item.documentId],queryFn:()=>inventoryApi.operationDetail(item.documentId),staleTime:60_000}));}
  catch{toast.error("No fue posible consultar el detalle del documento.");}
  finally{setDetailLoading(false)}
 }
 const changeTab=(value:string)=>{
  setActiveTab(value);
  if(value==="balances")void balances.refetch();
  else if(value==="movements")void movements.refetch();
  else if(value==="operations")void operations.refetch();
  else if(value==="conversions")void conversions.refetch();
  else if(value==="capture")void Promise.all([warehouseOptions.refetch(),queryClient.refetchQueries({queryKey:["inventory-reasons"]}),queryClient.refetchQueries({queryKey:["product-picker"]})]);
 };
 if(!businessId)return <Empty text="Selecciona una sede para consultar su inventario."/>;
 return <div className="space-y-5 p-6"><header><p className="text-sm font-medium text-emerald-600">Operación y trazabilidad</p><h1 className="text-3xl font-bold tracking-tight">Inventario</h1><p className="text-muted-foreground">Existencias, kárdex y documentos procesados por el motor ordenado de Auraly.</p></header>
 <div className="grid gap-3 md:grid-cols-3"><Metric icon={Boxes} label="Referencias con saldo" value={String(balances.data?.totalCount??0)}/><Metric icon={History} label="Movimientos filtrados" value={String(movements.data?.totalCount??0)}/><Metric icon={AlertTriangle} label="Operaciones" value={String(operations.data?.totalCount??0)}/></div>
 <Card><CardContent className="flex flex-col gap-3 pt-6 md:flex-row"><div className="relative flex-1"><Search className="absolute left-3 top-2.5 h-4 w-4 text-muted-foreground"/><Input className="pl-9" value={search} onChange={e=>{setSearch(e.target.value);resetPages()}} placeholder="Producto, referencia, código, documento o motivo"/></div><Select value={warehouseId} onValueChange={v=>{setWarehouseId(v);resetPages()}}><SelectTrigger className="md:w-72"><SelectValue placeholder="Todas las bodegas"/></SelectTrigger><SelectContent><SelectItem value="all">Todas las bodegas</SelectItem>{warehouses.map(w=><SelectItem key={w.id} value={w.id}>{w.name}</SelectItem>)}</SelectContent></Select><Button variant="outline" onClick={()=>void Promise.all([balances.refetch(),movements.refetch(),operations.refetch(),conversions.refetch()])} disabled={balances.isFetching||movements.isFetching||operations.isFetching||conversions.isFetching}><RefreshCw className={`mr-2 h-4 w-4 ${balances.isFetching||movements.isFetching||operations.isFetching||conversions.isFetching?"animate-spin":""}`}/>Actualizar</Button></CardContent></Card>
 <Tabs value={activeTab} onValueChange={changeTab}><TabsList className="grid h-auto w-full grid-cols-2 sm:grid-cols-5"><TabsTrigger value="balances">Existencias</TabsTrigger><TabsTrigger value="movements">Kárdex</TabsTrigger><TabsTrigger value="operations">Historial</TabsTrigger><TabsTrigger value="conversions"><RefreshCw className="mr-2 h-4 w-4"/>Conversiones</TabsTrigger><TabsTrigger value="capture">Nueva operación</TabsTrigger></TabsList>
 <TabsContent value="balances" className="space-y-4"><BalanceTable items={balances.data?.items??[]} loading={balances.isLoading}/><Pager page={pages.balances} pageSize={pageSize} total={balances.data?.totalPages??0} totalItems={balances.data?.totalCount??0} setPage={page=>setPages(current=>({...current,balances:page}))} setPageSize={setPageSize}/></TabsContent>
 <TabsContent value="movements" className="space-y-4"><SimpleTable headers={["Fecha","Producto","Bodega","Movimiento","Cantidad","Saldo","Documento"]} rows={(movements.data?.items??[]).map(x=>[formatDateTime(x.occurredAt),`${x.productCode} · ${x.productName}`,x.warehouseName,humanLabel(x.movementType,movementLabels),<Signed key={x.inventoryMovementId} value={x.quantityChange}/>,fmt(x.quantityAfter),x.documentNumber??humanLabel(x.documentType,documentLabels)])} loading={movements.isLoading}/><Pager page={pages.movements} pageSize={pageSize} total={movements.data?.totalPages??0} totalItems={movements.data?.totalCount??0} setPage={page=>setPages(current=>({...current,movements:page}))} setPageSize={setPageSize}/></TabsContent>
 <TabsContent value="operations" className="space-y-4"><SimpleTable headers={["Fecha","Documento","Tipo","Bodega","Motivo","Estado","Líneas","Valor"]} rows={(operations.data?.items??[]).map(x=>[formatDateTime(x.occurredAt),x.documentNumber??"Borrador",documentLabels[x.documentType]??x.documentType,x.destinationWarehouseName?`${x.warehouseName} → ${x.destinationWarehouseName}`:x.warehouseName,humanLabel(x.reasonCode,reasonLabels),humanLabel(x.status,statusLabels),String(x.lineCount),x.totalValueChange==null?"Restringido":formatCurrency(x.totalValueChange)])} loading={operations.isLoading} onRowClick={index=>void openOperationDetail(operations.data?.items[index])}/><Pager page={pages.operations} pageSize={pageSize} total={operations.data?.totalPages??0} totalItems={operations.data?.totalCount??0} setPage={page=>setPages(current=>({...current,operations:page}))} setPageSize={setPageSize}/></TabsContent>
 <TabsContent value="conversions" className="space-y-4"><SimpleTable headers={["Fecha","Documento","Bodega","Entrada equivalente","Salida equivalente","Merma","Tolerancia","Estado"]} rows={(conversions.data?.items??[]).map(x=>[formatDateTime(x.occurredAt),x.documentNumber??"Borrador",x.warehouseName,x.conversionInputEquivalent==null?"—":fmt(x.conversionInputEquivalent),x.conversionOutputEquivalent==null?"—":fmt(x.conversionOutputEquivalent),x.conversionLossPercent==null?"—":`${fmt(x.conversionLossQuantity??0)} · ${fmt(x.conversionLossPercent)} %`,x.conversionMaximumLossPercent==null?"—":`${fmt(x.conversionMaximumLossPercent)} %`,humanLabel(x.status,statusLabels)])} loading={conversions.isLoading} onRowClick={index=>void openOperationDetail(conversions.data?.items[index])}/><Pager page={pages.conversions} pageSize={pageSize} total={conversions.data?.totalPages??0} totalItems={conversions.data?.totalCount??0} setPage={page=>setPages(current=>({...current,conversions:page}))} setPageSize={setPageSize}/></TabsContent>
 <TabsContent value="capture"><InventoryOperationWorkspace businessId={businessId} warehouses={warehouses} permissions={permissions}/></TabsContent></Tabs>
 {detailLoading&&<Dialog open onOpenChange={()=>setDetailLoading(false)}><DialogContent><div className="flex items-center justify-center gap-2 p-10 text-muted-foreground"><RefreshCw className="h-5 w-5 animate-spin"/>Cargando detalle…</div></DialogContent></Dialog>}<InventoryDetailDialog detail={detail} onClose={()=>setDetail(undefined)}/></div>
}
function BalanceTable({items,loading}:{items:InventoryBalanceItem[];loading:boolean}){return <SimpleTable headers={["Producto","Bodega","Cantidad","Costo promedio","Valorización","Actualizado"]} rows={items.map(x=>[`${x.productCode} · ${x.productName}`,x.warehouseName,fmt(x.quantityOnHand),x.averageUnitCost==null?"Restringido":formatCurrency(x.averageUnitCost),x.inventoryValue==null?"Restringido":formatCurrency(x.inventoryValue),x.updatedAt?formatDateTime(x.updatedAt):"—"])} loading={loading}/>}
function SimpleTable({headers,rows,loading,onRowClick}:{headers:string[];rows:React.ReactNode[][];loading:boolean;onRowClick?:(index:number)=>void}){return <Card><CardContent className="overflow-x-auto p-0"><table className="w-full text-sm"><thead className="border-b bg-muted/50"><tr>{headers.map(h=><th key={h} className="px-4 py-3 text-left text-xs font-medium uppercase tracking-wide text-muted-foreground">{h}</th>)}</tr></thead><tbody>{loading?<tr><td colSpan={headers.length} className="p-10 text-center">Cargando…</td></tr>:rows.length===0?<tr><td colSpan={headers.length} className="p-10 text-center text-muted-foreground">No hay información para los filtros seleccionados.</td></tr>:rows.map((row,i)=><tr key={i} onClick={()=>onRowClick?.(i)} className={`border-b last:border-0 ${onRowClick?"cursor-pointer transition-colors hover:bg-muted/40":""}`}>{row.map((cell,j)=><td key={j} className="px-4 py-3 align-middle">{cell}</td>)}</tr>)}</tbody></table></CardContent></Card>}
function Signed({value}:{value:number}){return <span className={value<0?"font-semibold text-red-600":"font-semibold text-emerald-600"}>{value>0?"+":""}{fmt(value)}</span>}
function InventoryDetailDialog({detail,onClose}:{detail?:InventoryOperationDetail;onClose:()=>void}){
 if(!detail)return null;
 const isCount=detail.documentType==="StockCount";
 const isConversion=detail.documentType==="ProductConversion";
 const countValue=isCount?detail.lines.reduce((total,line)=>total+(line.processedUnitCost??0)*Math.abs(line.quantity??0),0):null;
 return <Dialog open onOpenChange={value=>!value&&onClose()}><DialogContent className="flex max-h-[92dvh] max-w-5xl flex-col overflow-hidden p-0">
  <DialogHeader className="border-b px-6 py-5"><DialogTitle>{detail.documentNumber??"Documento en preparación"}</DialogTitle><DialogDescription>{documentLabels[detail.documentType]??detail.documentType} · documento inmutable y trazable</DialogDescription></DialogHeader>
  <div className="space-y-5 overflow-y-auto px-6 py-5">
   <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
    <DetailCard label="Tipo" value={documentLabels[detail.documentType]??detail.documentType}/>
    <DetailCard label="Estado" value={humanLabel(detail.status,statusLabels)}/>
    <DetailCard label="Bodega" value={detail.destinationWarehouseName?`${detail.warehouseName} → ${detail.destinationWarehouseName}`:detail.warehouseName}/>
    <DetailCard label="Fecha" value={formatDateTime(detail.occurredAt)}/>
    <DetailCard label="Motivo" value={detail.reasonDescription||humanLabel(detail.reasonCode,reasonLabels)}/>
    <DetailCard label="Líneas" value={String(detail.lines.length)}/>
    <DetailCard label={isCount?"Valor del conteo":"Valor"} value={isCount?formatCurrency(countValue??0):detail.totalValueChange==null?"Restringido":formatCurrency(detail.totalValueChange)}/>
    <DetailCard label="Procesado" value={detail.processedAt?formatDateTime(detail.processedAt):"Pendiente"}/>
   </div>
   {isConversion&&<div className="grid gap-3 rounded-2xl border border-emerald-200 bg-emerald-50/50 p-4 sm:grid-cols-5">
    <DetailCard label="Entrada equivalente" value={detail.conversionInputEquivalent==null?"—":fmt(detail.conversionInputEquivalent)}/>
    <DetailCard label="Salida equivalente" value={detail.conversionOutputEquivalent==null?"—":fmt(detail.conversionOutputEquivalent)}/>
    <DetailCard label="Merma" value={detail.conversionLossQuantity==null?"—":fmt(detail.conversionLossQuantity)}/>
    <DetailCard label="Merma %" value={detail.conversionLossPercent==null?"—":`${fmt(detail.conversionLossPercent)} %`}/>
    <DetailCard label="Máximo" value={detail.conversionMaximumLossPercent==null?"—":`${fmt(detail.conversionMaximumLossPercent)} %`}/>
   </div>}
   <div className="overflow-x-auto rounded-2xl border"><table className="w-full min-w-[850px] text-sm">
    <thead className="bg-muted/60 text-xs uppercase tracking-wide text-muted-foreground"><tr><th className="px-4 py-3 text-left">Producto</th><th className="px-4 py-3 text-left">Movimiento</th>{isCount&&<th className="px-4 py-3 text-right">Existencia sistema</th>}<th className="px-4 py-3 text-right">{isCount?"Preconteo":"Saldo base"}</th><th className="px-4 py-3 text-right">{isCount?"Cantidad contada":"Cantidad"}</th>{isCount&&<th className="px-4 py-3 text-right">Diferencia</th>}<th className="px-4 py-3 text-right">Valor unitario</th><th className="px-4 py-3 text-right">Valor total</th></tr></thead>
    <tbody className="divide-y">{detail.lines.map(line=><tr key={line.lineNumber}>
     <td className="px-4 py-3"><p>{line.productName}</p><p className="text-xs text-muted-foreground">{line.productCode} · Línea {line.lineNumber}{isConversion&&line.conversionFactor!=null?` · factor ${fmt(line.conversionFactor)} · equivalente ${fmt(line.conversionEquivalentQuantity??0)}`:""}</p></td>
     <td className="px-4 py-3">{directionLabel(line.direction)}</td>
     {isCount&&<td className="px-4 py-3 text-right tabular-nums">{line.systemQuantityAtBase==null?"—":fmt(line.systemQuantityAtBase)}</td>}
     <td className="px-4 py-3 text-right tabular-nums">{isCount?(line.preCountQuantity==null?"—":fmt(line.preCountQuantity)):(line.systemQuantityAtBase==null?"—":fmt(line.systemQuantityAtBase))}</td>
     <td className="px-4 py-3 text-right tabular-nums">{line.quantity==null?"—":fmt(line.quantity)}</td>
     {isCount&&<td className={`px-4 py-3 text-right font-semibold tabular-nums ${countDifference(line)>0?"text-emerald-700":countDifference(line)<0?"text-red-700":"text-muted-foreground"}`}>{countDifference(line)>0?"+":""}{fmt(countDifference(line))}</td>}
     <td className="px-4 py-3 text-right tabular-nums">{line.processedUnitCost==null?"Restringido":formatCurrency(line.processedUnitCost)}</td>
     <td className="px-4 py-3 text-right font-medium tabular-nums">{(isCount?line.processedUnitCost:line.processedValue)==null?"Restringido":formatCurrency(isCount?Math.abs(line.quantity??0)*(line.processedUnitCost??0):line.processedValue??0)}</td>
    </tr>)}</tbody>
   </table></div>
   {detail.notes&&<div className="rounded-2xl border bg-muted/30 p-4"><p className="text-xs font-medium uppercase text-muted-foreground">Observaciones</p><p className="mt-1">{detail.notes}</p></div>}
  </div>
  <DialogFooter className="border-t px-6 py-4"><Button onClick={onClose}>Cerrar</Button></DialogFooter>
 </DialogContent></Dialog>;
}
function DetailCard({label,value}:{label:string;value:string}){return <div className="rounded-2xl border bg-muted/20 p-4"><p className="text-xs font-medium uppercase text-muted-foreground">{label}</p><p className="mt-1 text-sm">{value}</p></div>}
function countDifference(line:InventoryOperationDetail["lines"][number]){return (line.quantity??0)-(line.preCountQuantity??0);}
function directionLabel(value:string){return ({COUNT:"Conteo",ADJUSTMENT:"Ajuste",TRANSFER:"Traslado",INPUT:"Consumo",OUTPUT:"Producción",DAMAGE:"Avería",RECEIPT:"Recepción de mercancía"} as Record<string,string>)[value]??value;}

function Metric({icon:Icon,label,value}:{icon:typeof Boxes;label:string;value:string}){return <Card><CardContent className="flex items-center gap-3 pt-6"><div className="rounded-xl bg-emerald-50 p-3 text-emerald-700"><Icon className="h-5 w-5"/></div><div><p className="text-sm text-muted-foreground">{label}</p><p className="text-2xl font-bold">{value}</p></div></CardContent></Card>}
function Pager({page,pageSize,total,totalItems,setPage,setPageSize}:{page:number;pageSize:number;total:number;totalItems:number;setPage:(n:number)=>void;setPageSize:(n:number)=>void}){return <DataTablePagination pageIndex={Math.max(0,page-1)} pageSize={pageSize} pageCount={total} totalItems={totalItems} onPageChange={index=>setPage(index+1)} onPageSizeChange={size=>{setPageSize(size);setPage(1)}}/>}
function Empty({text}:{text:string}){return <div className="p-10 text-center text-muted-foreground">{text}</div>}
