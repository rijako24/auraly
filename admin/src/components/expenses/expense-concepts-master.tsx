"use client";

import { useState } from "react";
import { Loader2, Pencil } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { expensesApi, type ExpenseOptions } from "@/services/api/expenses";

export function ExpenseConceptEditor({businessId,options,onSaved}:{businessId:string;options:ExpenseOptions;onSaved:()=>Promise<void>}) {
  const [editingId,setEditingId]=useState<string|null>(null),[name,setName]=useState(""),[accountId,setAccountId]=useState(""),[costCenterId,setCostCenterId]=useState("none"),[busy,setBusy]=useState(false);
  function reset(){setEditingId(null);setName("");setAccountId("");setCostCenterId("none")}
  function edit(item:ExpenseOptions["concepts"][number]){setEditingId(item.conceptId);setName(item.name);setAccountId(item.expenseAccountId);setCostCenterId(item.defaultCostCenterId??"none")}
  async function save(){const current=editingId?options.concepts.find(item=>item.conceptId===editingId):null;setBusy(true);try{await expensesApi.saveConcept({conceptId:editingId??crypto.randomUUID(),businessId,name,expenseAccountId:accountId,defaultCostCenterId:costCenterId==="none"?null:costCenterId,withholdingConceptCode:current?.withholdingConceptCode??null,isActive:current?.isActive??true});toast.success(editingId?"Concepto actualizado.":"Concepto creado.");reset();await onSaved()}catch(error){toast.error(error instanceof Error?error.message:"No fue posible guardar el concepto.")}finally{setBusy(false)}}
  return <div className="space-y-4"><div className="grid gap-4 sm:grid-cols-2"><Field label="Nombre del concepto"><Input value={name} onChange={event=>setName(event.target.value)}/></Field><Field label="Cuenta de gasto"><Select value={accountId} onValueChange={setAccountId}><SelectTrigger><SelectValue placeholder="Selecciona"/></SelectTrigger><SelectContent>{options.expenseAccounts.map(item=><SelectItem key={item.accountId} value={item.accountId}>{item.code} · {item.name}</SelectItem>)}</SelectContent></Select></Field><Field label="Centro de costo predeterminado"><Select value={costCenterId} onValueChange={setCostCenterId}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent><SelectItem value="none">Sin predeterminado</SelectItem>{options.costCenters.map(item=><SelectItem key={item.costCenterId} value={item.costCenterId}>{item.code} · {item.name}</SelectItem>)}</SelectContent></Select></Field><div className="flex items-end gap-2"><Button className="flex-1" disabled={busy||!name.trim()||!accountId} onClick={()=>void save()}>{busy&&<Loader2 className="mr-2 h-4 w-4 animate-spin"/>}{editingId?"Guardar cambios":"Crear concepto"}</Button>{editingId&&<Button variant="outline" onClick={reset}>Cancelar</Button>}</div></div><div className="space-y-2 border-t pt-4">{options.concepts.map(item=><div key={item.conceptId} className="flex items-center justify-between gap-3 rounded-xl border p-3 text-sm"><span><b>{item.name}</b><span className="block text-muted-foreground">{item.expenseAccountCode} · {item.expenseAccountName}{item.defaultCostCenterName?` · ${item.defaultCostCenterName}`:""}</span></span><Button size="sm" variant="outline" onClick={()=>edit(item)}><Pencil className="mr-2 h-4 w-4"/>Editar</Button></div>)}</div></div>;
}

function Field({label,children}:{label:string;children:React.ReactNode}){return <div className="space-y-2"><Label>{label}</Label>{children}</div>}
