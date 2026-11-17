"use client";

import { useState } from "react";
import { useQueryClient, type InfiniteData } from "@tanstack/react-query";
import { Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { PartySelect } from "@/components/parties/party-role-select";
import { AccountSelect } from "@/components/accounting/account-select";
import { PagedEntitySelect } from "@/components/forms/paged-entity-select";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Checkbox } from "@/components/ui/checkbox";
import { DatePicker } from "@/components/ui/date-picker";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { payrollApi, type PayrollOptions, type PayrollProfile, type PayrollProfileMapping, type PayrollProfilePage, type SavePayrollProfile } from "@/services/api/payroll";
import { useTenantContextStore } from "@/stores/tenant-context-store";
import { useBusinessDates } from "@/hooks/use-business-dates";

export function PayrollProfiles({ options, canConfigure, canReadAccounting }: {
  options:PayrollOptions;canConfigure:boolean;canReadAccounting:boolean;
}) {
  const tenant=useTenantContextStore(state=>state.selectedTenantId),client=useQueryClient();
  const today=useBusinessDates().today();
  const [form,setForm]=useState<SavePayrollProfile|null>(null),[operation,setOperation]=useState<"read"|"save"|null>(null);
  const busy=operation!==null;
  const key=["payroll-profiles",tenant];
  async function select(id:string) {
    setOperation("read");
    try {setForm(await payrollApi.profile(id));}
    catch(error){toast.error(error instanceof Error?error.message:"No fue posible consultar el perfil.");}
    finally{setOperation(null);}
  }
  async function save() {
    if(!form||busy)return;
    setOperation("save");
    try {
      const saved=await payrollApi.saveProfile(form);setForm(saved);
      client.setQueriesData<InfiniteData<PayrollProfilePage>>({queryKey:key},current=>current?{
        ...current,pages:current.pages.map(page=>({...page,items:page.items.map(row=>row.profileId===saved.profileId?{...saved,mappings:[]}:row)})),
      }:current);
      await client.invalidateQueries({queryKey:key,refetchType:"none"});
      toast.success("Perfil guardado.");
    } catch(error){toast.error(error instanceof Error?error.message:"No fue posible guardar el perfil.");}
    finally{setOperation(null);}
  }
  function mapping(id:string,patch:Partial<PayrollProfileMapping>) {
    setForm(current=>current?{...current,mappings:current.mappings.map(row=>row.mappingId===id?{...row,...patch}:row)}:current);
  }
  const referenceRules=options.ruleSets.find(row=>row.ruleSetId===form?.ruleSetId);
  return <Card className="rounded-3xl lg:col-span-2"><CardHeader><CardTitle>Perfiles contables</CardTitle>
    <CardDescription>Por ejemplo, Operativo y Administrativo. Asignan cuentas por concepto. Cada contrato utiliza los porcentajes y topes aprobados asociados aquí, sin seleccionarlos de nuevo al liquidar.</CardDescription>
  </CardHeader><CardContent className="space-y-4">
    <div className="flex flex-wrap gap-3"><div className="min-w-56 flex-1"><PagedEntitySelect<PayrollProfile>
      queryKey={key} value={form?.rowVersion?form.profileId:""} disabled={busy} ariaLabel="Consultar perfil de contrato"
      selectedOption={form?.rowVersion?{value:form.profileId,label:`${form.code} · ${form.name}`}:null}
      loadPage={(search,page,pageSize)=>payrollApi.profiles({search,page,pageSize})}
      getOption={row=>({value:row.profileId,label:`${row.code} · ${row.name}${row.isActive?"":" (inactivo)"}`})}
      onChange={id=>void select(id)}/></div>
      {canConfigure&&<Button type="button" variant="outline" disabled={busy} onClick={()=>setForm({profileId:crypto.randomUUID(),code:"",name:"",ruleSetId:"",isActive:true,rowVersion:null,mappings:[]})}><Plus className="mr-2 size-4"/>Nuevo perfil</Button>}
    </div>
    {operation==="read"&&<p role="status" className="text-sm text-muted-foreground">Consultando perfil…</p>}
    {form&&<fieldset disabled={busy||!canConfigure} className="space-y-4">
      <div className="grid gap-4 md:grid-cols-2">
        <label className="space-y-2"><span className="text-sm">Código</span><Input maxLength={32} value={form.code} onChange={event=>setForm({...form,code:event.target.value.toUpperCase()})}/></label>
        <label className="space-y-2"><span className="text-sm">Nombre</span><Input maxLength={160} value={form.name} onChange={event=>setForm({...form,name:event.target.value})}/></label>
        <label className="space-y-2"><span className="text-sm">Porcentajes y topes aprobados</span><Select disabled={busy||!canConfigure} value={form.ruleSetId} onValueChange={ruleSetId=>setForm({...form,ruleSetId})}>
          <SelectTrigger aria-label="Porcentajes y topes del parámetro"><SelectValue placeholder="Selecciona la configuración aprobada"/></SelectTrigger>
          <SelectContent>{options.ruleSets.filter(row=>row.status==="Approved"||row.ruleSetId===form.ruleSetId).map(row=><SelectItem key={row.ruleSetId} value={row.ruleSetId} disabled={row.status!=="Approved"}>{row.code} · {row.name} · {row.effectiveFrom} · {row.isGlobal?"Base general":"Empresa"}</SelectItem>)}</SelectContent>
        </Select></label>
        <label className="flex items-center gap-2"><Checkbox disabled={busy||!canConfigure} checked={form.isActive} onCheckedChange={checked=>setForm({...form,isActive:checked===true})}/>Perfil activo</label>
      </div>
      {referenceRules&&<details className="rounded-xl border p-3"><summary className="cursor-pointer text-sm font-medium">Porcentajes y topes de referencia · desde {referenceRules.effectiveFrom}</summary>
        <p className="mt-2 text-xs text-muted-foreground">Al liquidar se aplica la versión aprobada de esta configuración que corresponda al período. Para actualizarla usa Nueva vigencia en Porcentajes y topes.</p>
        <dl className="mt-3 grid gap-3 sm:grid-cols-2">{referenceRules.parameters.map(parameter=>{
          const definition=options.catalogs["payroll-rule-parameter"]?.find(row=>row.code===parameter.code);
          const unit=definition?.metadataCode??parameter.unitCode;
          return <div key={parameter.code} className="flex justify-between gap-3 rounded-lg bg-muted/40 p-2 text-sm"><dt>{definition?.label??parameter.code}</dt><dd className="font-medium tabular-nums">{unit==="Rate"?new Intl.NumberFormat("es-CO",{style:"percent",maximumFractionDigits:4}).format(parameter.numericValue):parameter.numericValue.toLocaleString("es-CO")}</dd></div>;
        })}</dl>
      </details>}
      <p className="text-sm text-muted-foreground">Las cuentas se aplican por vigencia al contabilizar. Un lado sin cuenta conserva la asignación general de ese concepto.</p>
      {form.mappings.map(row=>{
        const concept=options.concepts.find(item=>item.conceptId===row.conceptId),net=row.conceptId===null;
        const sides=net||concept?.natureCode==="Deduction"?["credit"] as const:concept?.natureCode==="Earning"?["debit"] as const:["debit","credit"] as const;
        return <div key={row.mappingId} className="grid gap-3 rounded-xl border p-3 md:grid-cols-2">
          <label className="space-y-2"><span className="text-sm">Concepto</span><Select disabled={busy||!canConfigure} value={row.conceptId??"_net"} onValueChange={id=>mapping(row.mappingId,{conceptId:id==="_net"?null:id,debitAccountId:null,creditAccountId:null,debitAccountLabel:null,creditAccountLabel:null,beneficiaryPartyId:null,beneficiaryName:null})}>
            <SelectTrigger aria-label="Concepto del perfil"><SelectValue/></SelectTrigger><SelectContent><SelectItem value="_net">Neto por pagar</SelectItem>{options.concepts.map(item=><SelectItem key={item.conceptId} value={item.conceptId}>{item.code} · {item.name}</SelectItem>)}</SelectContent></Select></label>
          {sides.map(side=>{
            const id=row[`${side}AccountId`],label=row[`${side}AccountLabel`];
            return <label key={side} className="space-y-2"><span className="text-sm">{side==="debit"?"Débito":"Crédito"}</span><div className="flex gap-2"><div className="min-w-0 flex-1"><AccountSelect value={id??""} resolveMissing={false} disabled={busy||!canConfigure||!canReadAccounting}
              accounts={id?[{accountId:id,code:"",name:label??"Cuenta seleccionada"}]:[]}
              onChange={(accountId,account)=>mapping(row.mappingId,{[`${side}AccountId`]:accountId||null,[`${side}AccountLabel`]:account?`${account.code} · ${account.name}`:null})}/></div>
              {id&&<Button type="button" variant="ghost" aria-label={`Quitar cuenta de ${side==="debit"?"débito":"crédito"}`} onClick={()=>mapping(row.mappingId,{[`${side}AccountId`]:null,[`${side}AccountLabel`]:null})}>×</Button>}</div></label>;
          })}
          {(concept?.systemRoleCode==="Sena"||concept?.systemRoleCode==="Icbf")&&<label className="space-y-2"><span className="text-sm">Entidad beneficiaria</span><PartySelect partyType="Organization" ariaLabel="Entidad beneficiaria del aporte" disabled={busy||!canConfigure}
            value={row.beneficiaryPartyId??""} selectedOption={row.beneficiaryPartyId?{value:row.beneficiaryPartyId,label:row.beneficiaryName??"Entidad seleccionada"}:null}
            onChange={(id,party)=>mapping(row.mappingId,{beneficiaryPartyId:id||null,beneficiaryName:party?.displayName??null})}/></label>}
          <label className="space-y-2"><span className="text-sm">Desde</span><DatePicker today={today} value={row.effectiveFrom} onChange={value=>mapping(row.mappingId,{effectiveFrom:value})}/></label>
          <label className="space-y-2"><span className="text-sm">Hasta (opcional)</span><DatePicker today={today} value={row.effectiveTo??""} onChange={value=>mapping(row.mappingId,{effectiveTo:value||null})}/></label>
          {canConfigure&&<Button type="button" variant="ghost" size="icon" aria-label="Retirar cuenta del perfil" onClick={()=>setForm({...form,mappings:form.mappings.filter(item=>item.mappingId!==row.mappingId)})}><Trash2 className="size-4"/></Button>}
        </div>;
      })}
      {!canReadAccounting&&<p className="text-sm text-muted-foreground">Se necesita permiso de consulta contable para seleccionar cuentas.</p>}
      {canConfigure&&<div className="flex flex-wrap gap-3"><Button type="button" variant="outline" disabled={busy||form.mappings.length>=500} onClick={()=>setForm({...form,mappings:[...form.mappings,{mappingId:crypto.randomUUID(),conceptId:null,debitAccountId:null,creditAccountId:null,effectiveFrom:today,effectiveTo:null}]})}>Agregar cuenta por concepto</Button>
        <Button type="button" disabled={busy||!form.code.trim()||!form.name.trim()||!form.ruleSetId} onClick={()=>void save()}>{operation==="save"?"Guardando perfil…":"Guardar perfil"}</Button></div>}
    </fieldset>}
  </CardContent></Card>;
}
