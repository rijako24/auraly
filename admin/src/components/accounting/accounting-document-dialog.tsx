"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { AlertTriangle, BookOpenCheck, FileText, Loader2, RotateCcw } from "lucide-react";
import { accountingApi } from "@/services/api/accounting";
import { fiscalDocumentsApi } from "@/services/api/fiscal-documents";
import { ReportViewer } from "@/components/reports/report-viewer";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import {accountingDocumentTypeLabel,accountingStatusLabel,fiscalStatusLabel} from "@/lib/accounting-labels";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP" });
export function AccountingDocumentDialog({documentId,sourceLabel,onClose}:{documentId?:string;sourceLabel?:string;onClose:()=>void}){
  const [reportOpen,setReportOpen]=useState(false);
  const queryClient=useQueryClient();
  const posting=useQuery({queryKey:["accounting-posting",documentId],queryFn:()=>accountingApi.posting(documentId!),enabled:Boolean(documentId),retry:false});
  const fiscal=useQuery({queryKey:["fiscal-document",documentId],queryFn:()=>fiscalDocumentsApi.get(documentId!),enabled:Boolean(documentId),retry:false});
  const entry=useQuery({queryKey:["accounting-entry",documentId],queryFn:()=>accountingApi.entry(documentId!),enabled:Boolean(documentId&&posting.data?.status==="Posted"),retry:false});
  const retry=useMutation({mutationFn:()=>accountingApi.retryPosting(documentId!),onSuccess:async()=>{await queryClient.invalidateQueries({queryKey:["accounting-posting",documentId]});await queryClient.invalidateQueries({queryKey:["accounting-entry",documentId]})}});
  if(!documentId)return null;
  const loading=posting.isLoading||(posting.data?.status==="Posted"&&entry.isLoading);
  const value=entry.data;
  const difference=value?value.debitTotal-value.creditTotal:0;
  if(reportOpen&&value)return <Dialog open onOpenChange={open=>!open&&setReportOpen(false)}><DialogContent showClose={false} className="h-[96dvh] max-h-[96dvh] w-[98vw] max-w-[1500px] overflow-hidden p-2 sm:p-4"><ReportViewer
    onClose={()=>setReportOpen(false)}
    title={`Comprobante contable ${value.entryNumber}`}
    description={`${accountingDocumentTypeLabel(value.sourceDocumentType)} · ${value.description} · ${new Date(value.occurredAt).toLocaleDateString("es-CO")}`}
    fileName={`comprobante-${value.entryNumber}`}
    rows={value.lines.map(line=>({id:line.lineNumber,cuenta:line.accountCode,nombreCuenta:line.accountName,tercero:line.partyName??"",identificacion:line.partyIdentification??"",centroCosto:line.costCenterCode?`${line.costCenterCode} · ${line.costCenterName}`:"",detalle:line.description,debito:line.debit,credito:line.credit}))}
    columns={[{key:"cuenta",label:"Cuenta"},{key:"nombreCuenta",label:"Nombre de la cuenta"},{key:"tercero",label:"Tercero"},{key:"identificacion",label:"Identificación"},{key:"centroCosto",label:"Centro de costo"},{key:"detalle",label:"Detalle"},{key:"debito",label:"Débito",align:"right",format:value=>money.format(Number(value??0))},{key:"credito",label:"Crédito",align:"right",format:value=>money.format(Number(value??0))}]}
  /></DialogContent></Dialog>;
  return <Dialog open onOpenChange={open=>!open&&onClose()}><DialogContent className="accounting-print-root flex max-h-[94dvh] max-w-6xl flex-col overflow-hidden p-0">
    <DialogHeader className="border-b px-6 py-5"><DialogTitle className="flex items-center gap-2"><BookOpenCheck className="h-5 w-5 text-primary"/>Comprobante contable {value?.entryNumber??""}</DialogTitle><DialogDescription>{sourceLabel??(posting.data?accountingDocumentTypeLabel(posting.data.sourceDocumentType):"Documento origen")} · trazabilidad del asiento persistido</DialogDescription></DialogHeader>
    <div className="overflow-y-auto p-6">
      {loading&&<div className="flex items-center justify-center gap-2 p-12 text-muted-foreground"><Loader2 className="h-5 w-5 animate-spin"/>Consultando contabilización…</div>}
      {!loading&&posting.isError&&<Notice text="Este documento no tiene un trabajo contable registrado. Si produce efectos económicos, debe revisarse como una ausencia contable."/>}
      {!loading&&posting.data&&posting.data.status!=="Posted"&&<Notice text={posting.data.errorMessage??postingNotice(posting.data.status)} status={postingStatus(posting.data.status)}/>}
      {!loading&&posting.data?.status==="Posted"&&entry.isError&&<Notice text="El trabajo figura contabilizado, pero no fue posible recuperar su comprobante. Esta inconsistencia requiere revisión."/>}
      {value&&<div className="space-y-5">
        {fiscal.data&&<div className="grid gap-3 rounded-2xl border border-cyan-200 bg-cyan-50 p-4 sm:grid-cols-3"><Datum label="Documento fiscal" value={accountingDocumentTypeLabel(fiscal.data.fiscalDocumentType)}/><Datum label="Número DIAN" value={fiscal.data.dianNumber}/><Datum label="Estado DIAN" value={fiscalStatusLabel(fiscal.data.status)}/>{fiscal.data.uniqueCode&&<div className="sm:col-span-3 break-all font-mono text-[10px]"><b>{fiscal.data.uniqueCodeType}</b><br/>{fiscal.data.uniqueCode}</div>}</div>}
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4"><Datum label="Comprobante" value={value.entryNumber}/><Datum label="Documento origen" value={accountingDocumentTypeLabel(value.sourceDocumentType)}/><Datum label="Fecha del documento" value={new Date(value.occurredAt).toLocaleString("es-CO")}/><Datum label="Contabilizado" value={new Date(value.postedAt).toLocaleString("es-CO")}/></div>
        <div className="rounded-2xl border bg-muted/20 p-4"><p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Concepto</p><p className="mt-1 font-medium">{value.description}</p><p className="mt-1 font-mono text-xs text-muted-foreground">Origen: {value.sourceDocumentId}</p></div>
        <div className="overflow-x-auto rounded-2xl border"><table className="w-full min-w-[980px] text-sm"><thead className="bg-muted/50"><tr><th className="p-3 text-left">Cuenta</th><th className="text-left">Tercero</th><th className="text-left">Centro de costo</th><th className="text-left">Detalle</th><th className="text-right">Débito</th><th className="pr-3 text-right">Crédito</th></tr></thead><tbody>{value.lines.map(line=><tr className="border-t align-top" key={line.lineNumber}><td className="p-3"><b className="font-mono">{line.accountCode}</b><small className="block text-muted-foreground">{line.accountName}</small></td><td>{line.partyName??"—"}{line.partyIdentification&&<small className="block text-muted-foreground">{line.partyIdentification}</small>}</td><td>{line.costCenterCode?`${line.costCenterCode} · ${line.costCenterName}`:"Sin centro"}</td><td>{line.description}</td><td className="text-right">{line.debit?money.format(line.debit):"—"}</td><td className="pr-3 text-right">{line.credit?money.format(line.credit):"—"}</td></tr>)}</tbody><tfoot className="border-t-2 font-bold"><tr><td className="p-3" colSpan={4}>Totales</td><td className="text-right">{money.format(value.debitTotal)}</td><td className="pr-3 text-right">{money.format(value.creditTotal)}</td></tr></tfoot></table></div>
        <div className="flex justify-end"><Badge variant={difference===0?"secondary":"destructive"}>{difference===0?"Comprobante balanceado":`Diferencia ${money.format(difference)}`}</Badge></div>
      </div>}
    </div>
    <DialogFooter className="border-t px-6 py-4">{!value&&<Button variant="outline" disabled={retry.isPending} onClick={()=>retry.mutate()}>{retry.isPending?<Loader2 className="mr-2 h-4 w-4 animate-spin"/>:<RotateCcw className="mr-2 h-4 w-4"/>}Reintentar contabilización</Button>}<Button variant="outline" disabled={!value} onClick={()=>setReportOpen(true)}><FileText className="mr-2 h-4 w-4"/>Abrir reporte</Button><Button onClick={onClose}>Cerrar</Button></DialogFooter>
  </DialogContent></Dialog>;
}

function Datum({label,value}:{label:string;value:string}){return <div className="rounded-2xl border p-3"><p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{label}</p><p className="mt-1 font-semibold">{value}</p></div>}
function Notice({text,status}:{text:string;status?:string}){return <div className="flex gap-3 rounded-2xl border border-amber-300 bg-amber-50 p-5 text-amber-950"><AlertTriangle className="mt-0.5 h-5 w-5 shrink-0"/><div><b>{status??"Ausencia contable"}</b><p className="mt-1 text-sm">{text}</p></div></div>}
function postingStatus(status:string){return accountingStatusLabel(status);}
function postingNotice(status:string){return status==="CommercialEffectsApplied"?"El documento aplicó sus efectos comerciales mientras la contabilidad no estaba activa; por diseño no generó comprobante contable.":"El documento está pendiente de contabilización.";}
