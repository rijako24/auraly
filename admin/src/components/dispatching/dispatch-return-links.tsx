"use client";

import { useState } from "react";
import { PagedEntitySelect, type PagedEntityOption } from "@/components/forms/paged-entity-select";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { salesReturnsApi, type SalesReturnListItem } from "@/services/api/sales-returns";
import type { DispatchExecution } from "@/services/api/dispatches";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { useTenantContextStore } from "@/stores/tenant-context-store";

export type DispatchReturnSelections = Record<string, PagedEntityOption[]>;
const money=new Intl.NumberFormat("es-CO",{style:"currency",currency:"COP",maximumFractionDigits:0});

export function DispatchReturnLinks({data,selections,onChange,disabled}:{
  data:DispatchExecution; selections:DispatchReturnSelections; onChange:(value:DispatchReturnSelections)=>void; disabled:boolean;
}) {
  const tenant=useTenantContextStore(state=>state.selectedTenantId);
  const business=useBusinessContextStore(state=>state.selectedBusinessId);
  const user=useAuthStore(state=>state.user?.userId);
  const canRead=useAuthStore(state=>state.user?.permissions.includes("sales.returns.read")??false);
  const canCreate=useAuthStore(state=>state.user?.permissions.includes("sales.returns.create")??false);
  const documents=data.documents.filter(document=>(document.collectionMode!=="Dispatch"||document.payments.some(payment=>payment.applicationType==="KeepCurrent"))&&
    ((document.deliveryStatus==="NotDelivered"?document.documentTotal:document.returnTotal??0)>document.creditAmount || selections[document.sourceDocumentId]?.length));
  const [selected,setSelected]=useState("");
  const document=documents.find(item=>item.sourceDocumentId===selected)??documents[0];
  if(!document)return null;
  const options=selections[document.sourceDocumentId]??[];
  return <section aria-label="Devoluciones administrativas vinculadas" className="space-y-3 rounded-2xl border bg-muted/20 p-4">
    <div><h3 className="font-bold">Reembolso de ventas ya pagadas</h3>
      <p className="text-sm text-muted-foreground">Registra el reembolso real desde Devoluciones y vincúlalo a la entrega. Vincularlo no vuelve a pagar ni devuelve mercancía otra vez.</p></div>
    <div className="grid min-w-0 gap-3 sm:grid-cols-2">
      <div className="min-w-0 space-y-2"><p className="text-sm font-medium">Venta devuelta</p>
        <Select value={document.sourceDocumentId} onValueChange={setSelected}><SelectTrigger aria-label="Venta devuelta"><SelectValue/></SelectTrigger>
          <SelectContent>{documents.map(item=><SelectItem key={item.sourceDocumentId} value={item.sourceDocumentId}>{item.documentNumber} · {item.customerName}</SelectItem>)}</SelectContent></Select></div>
      <div className="min-w-0 space-y-2"><p className="text-sm font-medium">Devoluciones registradas</p>
        <PagedEntitySelect<SalesReturnListItem> queryKey={["dispatch-return-links",tenant,business,user,document.sourceDocumentId]}
          value="" values={options.map(item=>item.value)} selectedOptions={options}
          onChange={()=>{}} onValuesChange={(_,next)=>onChange({...selections,[document.sourceDocumentId]:next})}
          disabled={disabled||!canRead||!business} ariaLabel="Devoluciones registradas" placeholder="Vincular devoluciones" searchPlaceholder="Número de devolución…" wrapLabels
          loadPage={(search,page,pageSize)=>salesReturnsApi.listReturns({businessId:business!,originalDocumentId:document.sourceDocumentId,status:"Processed",search,page,pageSize})}
          getOption={item=>item.economicResolution==="Refund"||item.economicResolution==="CustomerCredit"
            ? {value:item.returnId,label:item.documentNumber,description:`${item.economicResolution==="Refund"?"Reembolso":"Aplicación a cartera"} · ${money.format(item.totalAmount)}`} : null}/></div>
    </div>
    {!disabled&&canCreate&&<a href={`/dashboard/sales-returns?document=${encodeURIComponent(document.documentNumber)}`} target="_blank" rel="noreferrer" className="inline-block text-sm font-semibold text-teal-700 underline">Registrar devolución de {document.documentNumber}</a>}
    {!disabled&&!canRead&&<p className="text-sm text-amber-800">Necesitas permiso para consultar devoluciones y seleccionar el reembolso registrado.</p>}
    {Object.entries(selections).flatMap(([source,items])=>items.map(item=><p key={item.value} className="break-words text-sm"><strong>{item.label}</strong> · {data.documents.find(value=>value.sourceDocumentId===source)?.documentNumber}{item.description&&` · ${item.description}`}</p>))}
  </section>;
}
