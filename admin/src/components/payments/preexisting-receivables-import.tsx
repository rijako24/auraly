"use client";

import { useRef,useState } from "react";
import { useMutation,useQuery } from "@tanstack/react-query";
import { Download,FileUp,Loader2 } from "lucide-react";
import { toast } from "sonner";
import { accountingApi } from "@/services/api/accounting";
import { receivablesApi } from "@/services/api/receivables";
import { Button } from "@/components/ui/button";
import { Dialog,DialogContent,DialogDescription,DialogFooter,DialogHeader,DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select,SelectContent,SelectItem,SelectTrigger,SelectValue } from "@/components/ui/select";
import { buildPreexistingReceivablesImport,parsePreexistingReceivablesCsv,preexistingReceivablesTemplate,type ParsedPreexistingReceivable } from "./preexisting-receivables-template";

export function PreexistingReceivablesImport({businessId,open,onOpenChange,onCompleted}:{businessId:string;open:boolean;onOpenChange:(value:boolean)=>void;onCompleted:()=>void}){
  const accounts=useQuery({queryKey:["accounting-accounts",businessId],queryFn:accountingApi.accounts,enabled:open&&!!businessId});
  const [accountId,setAccountId]=useState("");
  const [rows,setRows]=useState<ParsedPreexistingReceivable[]>([]);
  const [fileName,setFileName]=useState("");
  const pendingImport=useRef<{fingerprint:string;request:ReturnType<typeof buildPreexistingReceivablesImport>}|null>(null);
  const mutation=useMutation({
    mutationFn:()=>{
      const fingerprint=JSON.stringify({businessId,accountId,rows});
      if(pendingImport.current?.fingerprint!==fingerprint)
        pendingImport.current={fingerprint,request:buildPreexistingReceivablesImport(businessId,accountId,rows)};
      return receivablesApi.importPreexisting(pendingImport.current.request);
    },
    onSuccess:value=>{pendingImport.current=null;toast.success(`${value.acceptedCount} facturas quedaron aceptadas por el motor contable.`);onOpenChange(false);setRows([]);setFileName("");onCompleted();},
    onError:error=>toast.error(error instanceof Error?error.message:"No fue posible importar la cartera.")
  });
  async function read(file:File|null){
    setRows([]);setFileName(file?.name??"");if(!file)return;
    try{setRows(parsePreexistingReceivablesCsv(await file.text()));}
    catch(error){toast.error(error instanceof Error?error.message:"La plantilla no es válida.");}
  }
  return <Dialog open={open} onOpenChange={onOpenChange}><DialogContent className="sm:max-w-2xl">
    <DialogHeader><DialogTitle>Importar cartera preexistente</DialogTitle><DialogDescription>La factura se crea directamente en cartera y el motor contable registra el saldo. No genera venta, inventario ni documento DIAN.</DialogDescription></DialogHeader>
    <div className="space-y-4"><Button type="button" variant="outline" onClick={downloadPreexistingReceivablesTemplate}><Download className="mr-2 h-4 w-4"/>Descargar plantilla vacía</Button>
      <div className="space-y-2"><Label>Cuenta contrapartida</Label><Select value={accountId} onValueChange={setAccountId}><SelectTrigger><SelectValue placeholder="Selecciona la contrapartida contable"/></SelectTrigger><SelectContent>{(accounts.data??[]).filter(value=>value.isActive&&value.allowsPosting).map(value=><SelectItem key={value.accountId} value={value.accountId}>{value.code} · {value.name}</SelectItem>)}</SelectContent></Select><p className="text-xs text-muted-foreground">Cartera se debita automáticamente; esta cuenta recibe el crédito.</p></div>
      <div className="space-y-2"><Label>Plantilla CSV diligenciada</Label><Input type="file" accept=".csv,text/csv" onChange={event=>void read(event.target.files?.[0]??null)}/><p className="text-xs text-muted-foreground">Una factura por fila; fechas AAAA-MM-DD y saldo numérico sin separadores de miles. El cliente debe existir con la identificación indicada.</p></div>
      {fileName&&<div className="rounded-xl border bg-muted/30 p-4 text-sm"><b>{fileName}</b><p>{rows.length} factura{rows.length===1?"":"s"} lista{rows.length===1?"":"s"} para importar.</p></div>}
    </div><DialogFooter><Button variant="outline" onClick={()=>onOpenChange(false)}>Cancelar</Button><Button disabled={!accountId||rows.length===0||mutation.isPending} onClick={()=>mutation.mutate()}>{mutation.isPending?<Loader2 className="mr-2 h-4 w-4 animate-spin"/>:<FileUp className="mr-2 h-4 w-4"/>}Importar cartera</Button></DialogFooter>
  </DialogContent></Dialog>;
}

export function downloadPreexistingReceivablesTemplate(){
  const url=URL.createObjectURL(new Blob([preexistingReceivablesTemplate],{type:"text/csv;charset=utf-8"}));
  const link=document.createElement("a");link.href=url;link.download="plantilla-cartera-preexistente.csv";link.click();URL.revokeObjectURL(url);
}
