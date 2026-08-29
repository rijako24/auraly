"use client";

import { useCallback, useDeferredValue, useEffect, useState } from "react";
import { FilePlus2, Loader2, ReceiptText, Search, Settings2 } from "lucide-react";
import { toast } from "sonner";
import { DataTablePagination } from "@/components/tables/data-table-pagination";
import { PartyRoleSelect } from "@/components/parties/party-role-select";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { DatePicker } from "@/components/ui/date-picker";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { expensesApi, type ConfirmExpense, type ExpenseOptions, type ExpensePage } from "@/services/api/expenses";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });
const localNoon = (date:string) => `${date}T12:00:00-05:00`;

export default function ExpensesPage() {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const permissions = useAuthStore((state) => new Set(state.user?.permissions ?? []));
  const [options,setOptions]=useState<ExpenseOptions|null>(null),[report,setReport]=useState<ExpensePage|null>(null);
  const [loading,setLoading]=useState(false),[creating,setCreating]=useState(false),[configuring,setConfiguring]=useState(false);
  const [page,setPage]=useState(1),[pageSize,setPageSize]=useState(25),[search,setSearch]=useState("");
  const deferredSearch=useDeferredValue(search.trim());
  const load=useCallback(async()=>{setLoading(true);try{const [nextOptions,nextReport]=await Promise.all([expensesApi.options(),expensesApi.list({page,pageSize,search:deferredSearch||undefined})]);setOptions(nextOptions);setReport(nextReport)}catch(error){toast.error(error instanceof Error?error.message:"No fue posible cargar los gastos.")}finally{setLoading(false)}},[page,pageSize,deferredSearch]);
  useEffect(()=>{if(businessId)void load()},[businessId,load]);
  if(!businessId)return <Card><CardContent className="p-8 text-center text-muted-foreground">Selecciona una sede para consultar sus gastos.</CardContent></Card>;
  return <div className="space-y-6">
    <header className="flex flex-col gap-4 md:flex-row md:items-end md:justify-between"><div><p className="text-sm font-medium text-emerald-600">Operación y contabilidad</p><h1 className="text-3xl font-bold tracking-tight">Gastos</h1><p className="mt-1 text-muted-foreground">Registra el soporte; Auraly calcula retenciones, la cuenta por pagar y la contabilización.</p></div><div className="flex flex-wrap gap-2">{permissions.has("expenses.configure")&&<Button variant="outline" onClick={()=>setConfiguring(true)}><Settings2 className="mr-2 h-4 w-4"/>Conceptos</Button>}{permissions.has("expenses.create")&&<Button onClick={()=>setCreating(true)}><FilePlus2 className="mr-2 h-4 w-4"/>Nuevo gasto</Button>}</div></header>
    <div className="grid gap-4 sm:grid-cols-3"><Metric label="Gasto bruto" value={report?.grossTotal??0}/><Metric label="Retenciones" value={report?.withholdingTotal??0}/><Metric label="Por pagar" value={report?.netPayableTotal??0}/></div>
    <Card><CardHeader><CardTitle className="flex items-center gap-2"><ReceiptText className="h-5 w-5 text-primary"/>Documentos registrados</CardTitle><CardDescription>La búsqueda y la paginación consultan siempre al servidor.</CardDescription></CardHeader><CardContent>
      <label className="relative mb-4 block"><Search className="absolute left-3 top-2.5 h-4 w-4 text-muted-foreground"/><Input className="pl-9" value={search} onChange={event=>{setSearch(event.target.value);setPage(1)}} placeholder="Documento, proveedor o concepto"/></label>
      {loading?<p className="flex items-center gap-2 py-8 text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin"/>Cargando…</p>:<div className="overflow-x-auto rounded-xl border"><table className="w-full text-sm"><thead className="bg-muted/50 text-xs uppercase tracking-wide text-muted-foreground"><tr>{["Documento","Proveedor","Concepto","Bruto","Retención","Por pagar","Estado"].map(label=><th key={label} className={`p-3 font-semibold ${["Bruto","Retención","Por pagar"].includes(label)?"text-right":"text-left"}`}>{label}</th>)}</tr></thead><tbody>{report?.items.map(item=><tr key={item.expenseId} className="border-t"><td className="p-3"><b>{item.documentNumber}</b><small className="block text-muted-foreground">Ref. {item.supplierDocumentNumber}</small></td><td className="p-3">{item.supplierName}</td><td className="p-3">{item.conceptName}</td><td className="p-3 text-right">{money.format(item.grossAmount)}</td><td className="p-3 text-right">{money.format(item.withholdingAmount)}</td><td className="p-3 text-right font-bold">{money.format(item.netPayable)}</td><td className="p-3">{item.status}</td></tr>)}</tbody></table>{!report?.items.length&&<p className="p-8 text-center text-muted-foreground">Todavía no hay gastos registrados.</p>}</div>}
      <DataTablePagination pageIndex={Math.max(0,(report?.page??page)-1)} pageSize={report?.pageSize??pageSize} pageCount={report?.totalPages??0} totalItems={report?.totalCount??0} onPageChange={index=>setPage(index+1)} onPageSizeChange={size=>{setPageSize(size);setPage(1)}}/>
    </CardContent></Card>
    <Dialog open={creating} onOpenChange={setCreating}><DialogContent className="max-h-[92dvh] max-w-3xl overflow-y-auto"><DialogHeader><DialogTitle>Registrar gasto</DialogTitle><DialogDescription>Busca el proveedor o beneficiario; el concepto define la cuenta contable.</DialogDescription></DialogHeader>{options&&<ExpenseForm businessId={businessId} options={options} onSaved={async()=>{setCreating(false);await load()}}/>}</DialogContent></Dialog>
    <Dialog open={configuring} onOpenChange={setConfiguring}><DialogContent className="max-h-[92dvh] max-w-2xl overflow-y-auto"><DialogHeader><DialogTitle>Conceptos de gasto</DialogTitle><DialogDescription>Clasifican el gasto y determinan su cuenta contable.</DialogDescription></DialogHeader>{options&&<ConceptForm businessId={businessId} options={options} onSaved={load}/>}</DialogContent></Dialog>
  </div>;
}

function ExpenseForm({businessId,options,onSaved}:{businessId:string;options:ExpenseOptions;onSaved:()=>Promise<void>}){
  const today=new Date().toISOString().slice(0,10),[busy,setBusy]=useState(false);
  const [form,setForm]=useState<ConfirmExpense>({expenseId:crypto.randomUUID(),businessId,supplierId:"",conceptId:"",costCenterId:null,supplierDocumentNumber:"",issuedAt:localNoon(today),dueDate:localNoon(today),currencyCode:"COP",description:"",taxExclusiveAmount:0,vatAmount:0,withholdingJurisdictionCode:"CO",evidenceUrl:null});
  const concept=options.concepts.find(item=>item.conceptId===form.conceptId);
  async function submit(event:React.FormEvent){event.preventDefault();setBusy(true);try{await expensesApi.confirm(form);toast.success("Gasto aceptado para procesamiento contable.");await onSaved()}catch(error){toast.error(error instanceof Error?error.message:"No fue posible registrar el gasto.")}finally{setBusy(false)}}
  return <form className="grid gap-4 sm:grid-cols-2" onSubmit={submit}>
    <Field label="Proveedor o beneficiario"><PartyRoleSelect role="Supplier" value={form.supplierId} placeholder="Buscar proveedor o beneficiario" onChange={supplierId=>setForm({...form,supplierId})}/></Field>
    <Field label="Concepto"><Select value={form.conceptId} onValueChange={conceptId=>{const selected=options.concepts.find(item=>item.conceptId===conceptId);setForm({...form,conceptId,costCenterId:selected?.defaultCostCenterId??null})}}><SelectTrigger><SelectValue placeholder="Selecciona"/></SelectTrigger><SelectContent>{options.concepts.filter(item=>item.isActive).map(item=><SelectItem key={item.conceptId} value={item.conceptId}>{item.code} · {item.name}</SelectItem>)}</SelectContent></Select></Field>
    <Field label="Centro de costo"><Select value={form.costCenterId??"none"} onValueChange={value=>setForm({...form,costCenterId:value==="none"?null:value})}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent><SelectItem value="none">Sin centro de costo</SelectItem>{options.costCenters.map(item=><SelectItem key={item.costCenterId} value={item.costCenterId}>{item.code} · {item.name}</SelectItem>)}</SelectContent></Select></Field>
    <Field label="Factura o soporte"><Input required value={form.supplierDocumentNumber} onChange={event=>setForm({...form,supplierDocumentNumber:event.target.value})}/></Field>
    <Field label="Fecha de emisión"><DatePicker value={form.issuedAt.slice(0,10)} onChange={date=>setForm({...form,issuedAt:localNoon(date)})}/></Field>
    <Field label="Fecha de vencimiento"><DatePicker value={form.dueDate.slice(0,10)} onChange={date=>setForm({...form,dueDate:localNoon(date)})}/></Field>
    <Field label="Base antes de IVA"><Input required min={0} type="number" value={form.taxExclusiveAmount} onChange={event=>setForm({...form,taxExclusiveAmount:event.currentTarget.valueAsNumber||0})}/></Field>
    <Field label="IVA"><Input required min={0} type="number" value={form.vatAmount} onChange={event=>setForm({...form,vatAmount:event.currentTarget.valueAsNumber||0})}/></Field>
    <div className="sm:col-span-2"><Field label="Descripción (opcional)"><Input value={form.description} onChange={event=>setForm({...form,description:event.target.value})} placeholder="Agrega detalle solo cuando haga falta"/></Field></div>
    <div className="sm:col-span-2 rounded-xl border bg-muted/30 p-3 text-sm"><b>Cuenta contable:</b> {concept?`${concept.expenseAccountCode} · ${concept.expenseAccountName}`:"se define con el concepto"}<p className="mt-1 text-muted-foreground">La retención se calcula con el perfil tributario del proveedor.</p></div>
    <DialogFooter className="sm:col-span-2"><Button type="submit" disabled={busy||!form.supplierId||!form.conceptId||!form.supplierDocumentNumber.trim()}>{busy&&<Loader2 className="mr-2 h-4 w-4 animate-spin"/>}Confirmar gasto</Button></DialogFooter>
  </form>;
}

function ConceptForm({businessId,options,onSaved}:{businessId:string;options:ExpenseOptions;onSaved:()=>Promise<void>}){
  const [code,setCode]=useState(""),[name,setName]=useState(""),[accountId,setAccountId]=useState(""),[costCenterId,setCostCenterId]=useState("none"),[busy,setBusy]=useState(false);
  async function save(){setBusy(true);try{await expensesApi.saveConcept({conceptId:crypto.randomUUID(),businessId,code,name,expenseAccountId:accountId,defaultCostCenterId:costCenterId==="none"?null:costCenterId,withholdingConceptCode:null,isActive:true});toast.success("Concepto creado.");setCode("");setName("");await onSaved()}catch(error){toast.error(error instanceof Error?error.message:"No fue posible crear el concepto.")}finally{setBusy(false)}}
  return <div className="space-y-4"><div className="grid gap-4 sm:grid-cols-2"><Field label="Código"><Input value={code} onChange={event=>setCode(event.target.value.toUpperCase())}/></Field><Field label="Nombre"><Input value={name} onChange={event=>setName(event.target.value)}/></Field><Field label="Cuenta de gasto"><Select value={accountId} onValueChange={setAccountId}><SelectTrigger><SelectValue placeholder="Selecciona"/></SelectTrigger><SelectContent>{options.expenseAccounts.map(item=><SelectItem key={item.accountId} value={item.accountId}>{item.code} · {item.name}</SelectItem>)}</SelectContent></Select></Field><Field label="Centro de costo predeterminado"><Select value={costCenterId} onValueChange={setCostCenterId}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent><SelectItem value="none">Sin predeterminado</SelectItem>{options.costCenters.map(item=><SelectItem key={item.costCenterId} value={item.costCenterId}>{item.code} · {item.name}</SelectItem>)}</SelectContent></Select></Field></div><Button disabled={busy||!code.trim()||!name.trim()||!accountId} onClick={()=>void save()}>Crear concepto</Button><div className="space-y-2 border-t pt-4">{options.concepts.map(item=><div key={item.conceptId} className="rounded-xl border p-3 text-sm"><b>{item.code} · {item.name}</b><span className="block text-muted-foreground">{item.expenseAccountCode} · {item.expenseAccountName}</span></div>)}</div></div>;
}

function Metric({label,value}:{label:string;value:number}){return <Card><CardContent className="p-5"><p className="text-sm text-muted-foreground">{label}</p><p className="mt-1 text-2xl font-bold">{money.format(value)}</p></CardContent></Card>}
function Field({label,children}:{label:string;children:React.ReactNode}){return <label className="space-y-2"><Label>{label}</Label>{children}</label>}
