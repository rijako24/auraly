"use client";

import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import { inventoryApi, type WarehouseTransferDetail } from "@/services/api/inventory";

type Warehouse = { id:string;name:string };

export function WarehouseTransferReceiptPanel({ businessId, warehouses }:{ businessId:string;warehouses:Warehouse[] }) {
  const client = useQueryClient();
  const [warehouseId,setWarehouseId]=useState(warehouses.length===1?warehouses[0].id:"all");
  const [selectedId,setSelectedId]=useState<string|null>(null);
  const [quantities,setQuantities]=useState<Record<number,string>>({});
  const [differenceReason,setDifferenceReason]=useState("");
  const [notes,setNotes]=useState("");
  const pending=useQuery({queryKey:["pending-warehouse-transfers",businessId,warehouseId],queryFn:()=>inventoryApi.pendingTransfers({destinationWarehouseId:warehouseId==="all"?undefined:warehouseId,pageSize:100}),enabled:Boolean(businessId)});
  const detail=useQuery({queryKey:["warehouse-transfer",selectedId],queryFn:()=>inventoryApi.transfer(selectedId!),enabled:Boolean(selectedId)});
  const reasons=useQuery({queryKey:["inventory-reasons",businessId,"WarehouseTransfer"],queryFn:()=>inventoryApi.reasons({operationType:"WarehouseTransfer"}),enabled:Boolean(businessId)});
  useEffect(()=>{if(!detail.data)return;setQuantities(Object.fromEntries(detail.data.lines.map(line=>[line.lineNumber,String(line.pendingQuantity)])));setDifferenceReason("");setNotes("");},[detail.data]);
  const changed=useMemo(()=>detail.data?.lines.some(line=>Number(quantities[line.lineNumber]??0)!==line.pendingQuantity)??false,[detail.data,quantities]);
  const selectedDifferenceReason=(reasons.data??[]).find(item=>item.code===differenceReason);
  const valid=Boolean(detail.data)&&detail.data!.lines.every(line=>{const value=Number(quantities[line.lineNumber]);return Number.isFinite(value)&&value>=0&&value<=line.pendingQuantity;})&&(detail.data!.lines.some(line=>Number(quantities[line.lineNumber])>0)||(changed&&Boolean(differenceReason)))&&(!changed||(Boolean(differenceReason)&&(!selectedDifferenceReason?.requiresReference||Boolean(notes.trim()))));
  const receive=useMutation({mutationFn:async()=>{
    const transfer=detail.data as WarehouseTransferDetail;
    return inventoryApi.receiveTransfer(transfer.transferId,{receiptId:crypto.randomUUID(),businessId,occurredAt:new Date().toISOString(),differenceReasonCode:changed?differenceReason:null,notes:notes.trim()||null,rowVersion:transfer.rowVersion,lines:transfer.lines.map(line=>({lineNumber:line.lineNumber,productId:line.productId,receivedQuantity:Number(quantities[line.lineNumber])}))});
  },onSuccess:async result=>{toast.success(`${result.documentNumber}: entrada enviada al motor`);setSelectedId(null);await Promise.all([client.invalidateQueries({queryKey:["pending-warehouse-transfers"]}),client.invalidateQueries({queryKey:["inventory-operations"]}),client.invalidateQueries({queryKey:["inventory-balances"]}),client.invalidateQueries({queryKey:["inventory-movements"]})]);},onError:(error:{message?:string})=>toast.error(error.message??"No fue posible confirmar la entrada")});

  return <Card>
    <CardHeader><CardTitle>Traslados pendientes de entrada</CardTitle></CardHeader>
    <CardContent className="space-y-4">
      <div className="grid gap-3 md:grid-cols-[280px_1fr]">
        <div className="space-y-2"><Label>Bodega que recibe</Label><Select value={warehouseId} onValueChange={value=>{setWarehouseId(value);setSelectedId(null);}}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value="all">Todas</SelectItem>{warehouses.map(item=><SelectItem key={item.id} value={item.id}>{item.name}</SelectItem>)}</SelectContent></Select></div>
        <div className="space-y-2"><Label>Recuperar traslado</Label><Select value={selectedId??""} onValueChange={setSelectedId}><SelectTrigger><SelectValue placeholder={pending.isLoading?"Cargando…":"Selecciona un traslado pendiente"} /></SelectTrigger><SelectContent>{(pending.data?.items??[]).map(item=><SelectItem key={item.transferId} value={item.transferId}>{item.documentNumber} · {item.sourceWarehouseName} → {item.destinationWarehouseName} · {item.pendingQuantity}</SelectItem>)}</SelectContent></Select></div>
      </div>
      {detail.data&&<>
        <p className="text-sm text-muted-foreground">Salida confirmada desde <strong>{detail.data.sourceWarehouseName}</strong>. Verifica y ajusta únicamente lo que realmente llegó a <strong>{detail.data.destinationWarehouseName}</strong>.</p>
        <div className="overflow-x-auto rounded-xl border"><table className="w-full min-w-[700px] text-sm"><thead className="bg-muted/60"><tr><th className="px-3 py-3 text-left">Producto</th><th className="px-3 py-3 text-right">Salió</th><th className="px-3 py-3 text-right">Recibido antes</th><th className="px-3 py-3 text-left">Entra ahora</th></tr></thead><tbody>{detail.data.lines.map(line=><tr key={line.lineNumber} className="border-t"><td className="px-3 py-2"><strong>{line.productName}</strong><span className="block text-xs text-muted-foreground">{line.productCode}</span></td><td className="px-3 py-2 text-right tabular-nums">{line.dispatchedQuantity}</td><td className="px-3 py-2 text-right tabular-nums">{line.receivedQuantity}</td><td className="px-3 py-2"><Input className="w-36 text-right tabular-nums" inputMode="decimal" value={quantities[line.lineNumber]??""} onChange={event=>setQuantities(current=>({...current,[line.lineNumber]:event.target.value}))} aria-label={`Cantidad recibida de ${line.productName}`} /></td></tr>)}</tbody></table></div>
        {changed&&<div className="space-y-2"><Label>Motivo del faltante definitivo</Label><Select value={differenceReason} onValueChange={setDifferenceReason}><SelectTrigger><SelectValue placeholder="Selecciona el motivo contable" /></SelectTrigger><SelectContent>{(reasons.data??[]).filter(item=>item.counterpartAccountingCategory).map(item=><SelectItem key={item.inventoryReasonId} value={item.code}>{item.name}</SelectItem>)}</SelectContent></Select><p className="text-xs text-muted-foreground">La diferencia se cerrará como pérdida; no quedará cantidad pendiente en tránsito.</p></div>}
        <div className="space-y-2"><Label>Observaciones de recepción</Label><Textarea value={notes} onChange={event=>setNotes(event.target.value)} maxLength={1000} /></div>
        <div className="flex justify-end gap-2"><Button variant="outline" onClick={()=>setSelectedId(null)} disabled={receive.isPending}>Cancelar</Button><Button disabled={!valid||receive.isPending} onClick={()=>receive.mutate()}>{receive.isPending?"Procesando…":"Confirmar entrada"}</Button></div>
      </>}
      {!pending.isLoading&&(pending.data?.items.length??0)===0&&<p className="text-sm text-muted-foreground">No hay traslados pendientes para recuperar.</p>}
    </CardContent>
  </Card>;
}
