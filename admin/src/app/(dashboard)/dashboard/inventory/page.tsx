"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { AlertTriangle, Boxes, History, Search } from "lucide-react";
import { InventoryOperationWorkspace } from "@/components/inventory/inventory-operation-workspace";
import { inventoryApi, type InventoryBalanceItem } from "@/services/api/inventory";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { useAuthStore } from "@/stores/auth-store";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { formatCurrency, formatDateTime } from "@/lib/utils";

const fmt=(value:number)=>new Intl.NumberFormat("es-CO",{maximumFractionDigits:6}).format(value);
const documentLabels:Record<string,string>={StockCount:"Conteo físico",InventoryAdjustment:"Ajuste",WarehouseTransfer:"Traslado",ProductConversion:"Conversión",Damage:"Avería",SalesInvoice:"Venta",GoodsReceipt:"Recepción"};

export default function InventoryPage(){
 const businessId=useBusinessContextStore(s=>s.selectedBusinessId);
 const permissions=new Set(useAuthStore(s=>s.user?.permissions??[]));
 const [search,setSearch]=useState(""); const [warehouseId,setWarehouseId]=useState("all"); const [page,setPage]=useState(1);
 const warehouseOptions=useQuery({queryKey:["inventory-warehouses",businessId],queryFn:()=>inventoryApi.warehouses(),enabled:!!businessId});
 const balances=useQuery({queryKey:["inventory-balances",businessId,warehouseId,search,page],queryFn:()=>inventoryApi.balances({warehouseId:warehouseId==="all"?undefined:warehouseId,search:search||undefined,page,pageSize:50}),enabled:!!businessId});
 const movements=useQuery({queryKey:["inventory-movements",businessId,warehouseId,search,page],queryFn:()=>inventoryApi.movements({warehouseId:warehouseId==="all"?undefined:warehouseId,search:search||undefined,page,pageSize:50}),enabled:!!businessId});
 const operations=useQuery({queryKey:["inventory-operations",businessId,warehouseId,search,page],queryFn:()=>inventoryApi.operations({warehouseId:warehouseId==="all"?undefined:warehouseId,search:search||undefined,page,pageSize:50}),enabled:!!businessId});
 const warehouses=(warehouseOptions.data??[]).map(x=>({id:x.warehouseId,name:x.name}));
 if(!businessId)return <Empty text="Selecciona una sede para consultar su inventario."/>;
 return <div className="space-y-5 p-6"><header><p className="text-sm font-medium text-emerald-600">Operación y trazabilidad</p><h1 className="text-3xl font-bold tracking-tight">Inventario</h1><p className="text-muted-foreground">Existencias, kárdex y documentos procesados por el motor ordenado de Auraly.</p></header>
 <div className="grid gap-3 md:grid-cols-3"><Metric icon={Boxes} label="Referencias con saldo" value={String(balances.data?.totalCount??0)}/><Metric icon={History} label="Movimientos filtrados" value={String(movements.data?.totalCount??0)}/><Metric icon={AlertTriangle} label="Operaciones" value={String(operations.data?.totalCount??0)}/></div>
 <Card><CardContent className="flex flex-col gap-3 pt-6 md:flex-row"><div className="relative flex-1"><Search className="absolute left-3 top-2.5 h-4 w-4 text-muted-foreground"/><Input className="pl-9" value={search} onChange={e=>{setSearch(e.target.value);setPage(1)}} placeholder="Producto, código, documento o motivo"/></div><Select value={warehouseId} onValueChange={v=>{setWarehouseId(v);setPage(1)}}><SelectTrigger className="md:w-72"><SelectValue placeholder="Todas las bodegas"/></SelectTrigger><SelectContent><SelectItem value="all">Todas las bodegas</SelectItem>{warehouses.map(w=><SelectItem key={w.id} value={w.id}>{w.name}</SelectItem>)}</SelectContent></Select></CardContent></Card>
 <Tabs defaultValue="balances"><TabsList className="grid h-auto w-full grid-cols-4"><TabsTrigger value="balances">Existencias</TabsTrigger><TabsTrigger value="movements">Kárdex</TabsTrigger><TabsTrigger value="operations">Historial</TabsTrigger><TabsTrigger value="capture">Nueva operación</TabsTrigger></TabsList>
 <TabsContent value="balances"><BalanceTable items={balances.data?.items??[]} loading={balances.isLoading}/></TabsContent>
 <TabsContent value="movements"><SimpleTable headers={["Fecha","Producto","Bodega","Movimiento","Cantidad","Saldo","Documento"]} rows={(movements.data?.items??[]).map(x=>[formatDateTime(x.occurredAt),`${x.productCode} · ${x.productName}`,x.warehouseName,x.movementType,<Signed key={x.inventoryMovementId} value={x.quantityChange}/>,fmt(x.quantityAfter),x.documentNumber??documentLabels[x.documentType]??x.documentType])} loading={movements.isLoading}/></TabsContent>
 <TabsContent value="operations"><SimpleTable headers={["Fecha","Documento","Tipo","Bodega","Motivo","Estado","Líneas","Valor"]} rows={(operations.data?.items??[]).map(x=>[formatDateTime(x.occurredAt),x.documentNumber??"Borrador",documentLabels[x.documentType]??x.documentType,x.destinationWarehouseName?`${x.warehouseName} → ${x.destinationWarehouseName}`:x.warehouseName,x.reasonCode,x.status,String(x.lineCount),x.totalValueChange==null?"Restringido":formatCurrency(x.totalValueChange)])} loading={operations.isLoading}/></TabsContent>
 <TabsContent value="capture"><InventoryOperationWorkspace businessId={businessId} warehouses={warehouses} permissions={permissions}/></TabsContent></Tabs>
 <Pager page={page} total={Math.max(balances.data?.totalPages??0,movements.data?.totalPages??0,operations.data?.totalPages??0)} setPage={setPage}/></div>
}
function BalanceTable({items,loading}:{items:InventoryBalanceItem[];loading:boolean}){return <SimpleTable headers={["Producto","Bodega","Cantidad","Costo promedio","Valorización","Actualizado"]} rows={items.map(x=>[`${x.productCode} · ${x.productName}`,x.warehouseName,fmt(x.quantityOnHand),x.averageUnitCost==null?"Restringido":formatCurrency(x.averageUnitCost),x.inventoryValue==null?"Restringido":formatCurrency(x.inventoryValue),x.updatedAt?formatDateTime(x.updatedAt):"—"])} loading={loading}/>}
function SimpleTable({headers,rows,loading}:{headers:string[];rows:React.ReactNode[][];loading:boolean}){return <Card><CardContent className="overflow-x-auto p-0"><table className="w-full text-sm"><thead className="border-b bg-muted/50"><tr>{headers.map(h=><th key={h} className="px-4 py-3 text-left font-medium">{h}</th>)}</tr></thead><tbody>{loading?<tr><td colSpan={headers.length} className="p-10 text-center">Cargando…</td></tr>:rows.length===0?<tr><td colSpan={headers.length} className="p-10 text-center text-muted-foreground">No hay información para los filtros seleccionados.</td></tr>:rows.map((row,i)=><tr key={i} className="border-b last:border-0">{row.map((cell,j)=><td key={j} className="px-4 py-3">{cell}</td>)}</tr>)}</tbody></table></CardContent></Card>}
function Signed({value}:{value:number}){return <span className={value<0?"font-semibold text-red-600":"font-semibold text-emerald-600"}>{value>0?"+":""}{fmt(value)}</span>}
function Metric({icon:Icon,label,value}:{icon:typeof Boxes;label:string;value:string}){return <Card><CardContent className="flex items-center gap-3 pt-6"><div className="rounded-xl bg-emerald-50 p-3 text-emerald-700"><Icon className="h-5 w-5"/></div><div><p className="text-sm text-muted-foreground">{label}</p><p className="text-2xl font-bold">{value}</p></div></CardContent></Card>}
function Pager({page,total,setPage}:{page:number;total:number;setPage:(n:number)=>void}){if(total<=1)return null;return <div className="flex justify-end gap-2"><Button variant="outline" disabled={page<=1} onClick={()=>setPage(page-1)}>Anterior</Button><span className="self-center text-sm">Página {page} de {total}</span><Button variant="outline" disabled={page>=total} onClick={()=>setPage(page+1)}>Siguiente</Button></div>}
function Empty({text}:{text:string}){return <div className="p-10 text-center text-muted-foreground">{text}</div>}