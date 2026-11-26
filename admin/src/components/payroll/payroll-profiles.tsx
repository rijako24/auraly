"use client";

import { useEffect, useRef, useState } from "react";
import { useQuery, useQueryClient, type InfiniteData } from "@tanstack/react-query";
import { Plus, Trash2, Pencil } from "lucide-react";
import { toast } from "sonner";
import { PartySelect } from "@/components/parties/party-role-select";
import { AccountSelect } from "@/components/accounting/account-select";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Checkbox } from "@/components/ui/checkbox";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { DatePicker } from "@/components/ui/date-picker";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { payrollApi, type PayrollOptions, type PayrollProfileMapping, type PayrollProfilePage, type PayrollRuleSet, type SavePayrollProfile } from "@/services/api/payroll";
import { PayrollRuleParameterFields } from "@/components/payroll/payroll-rule-parameter-fields";
import { useTenantContextStore } from "@/stores/tenant-context-store";
import { useBusinessDates } from "@/hooks/use-business-dates";

export function PayrollProfiles({ options, canConfigure, canReadAccounting }: {
  options:PayrollOptions;canConfigure:boolean;canReadAccounting:boolean;
}) {
  const tenant=useTenantContextStore(state=>state.selectedTenantId),client=useQueryClient();
  const mounted=useRef(true);
  useEffect(()=>{mounted.current=true;return()=>{mounted.current=false;};},[]);
  const today=useBusinessDates().today();
  const [form,setForm]=useState<SavePayrollProfile|null>(null),[operation,setOperation]=useState<"read"|"save"|"refresh"|null>(null);
  const [page,setPage]=useState(1),[search,setSearch]=useState(""),[draftSearch,setDraftSearch]=useState("");
  const [active,setActive]=useState("all");
  const [baseConfiguration,setBaseConfiguration]=useState<PayrollRuleSet|null>(null),[configurationChanged,setConfigurationChanged]=useState(false);
  const [versionPage,setVersionPage]=useState(1);
  const versions=useQuery({queryKey:["payroll-profile-versions",tenant,form?.profileId,versionPage],
    queryFn:()=>payrollApi.profileRuleSets(form!.profileId,versionPage),enabled:Boolean(form?.rowVersion)&&operation===null,
    staleTime:Infinity,refetchOnWindowFocus:false,refetchOnReconnect:false});
  const [parameters,setParameters]=useState<Record<string,string>>({}),[source,setSource]=useState(""),[effectiveFrom,setEffectiveFrom]=useState(today);
  const definitions=options.catalogs["payroll-rule-parameter"]??[];
  const initialComplete=definitions.length>0&&source.trim().length>0&&effectiveFrom&&definitions.every(item=>parameters[item.code]!==undefined&&parameters[item.code]!==""&&Number.isFinite(Number(parameters[item.code])));
  const [sort,setSort]=useState<{by:"name"|"status";direction:"asc"|"desc"}>({by:"name",direction:"asc"});
  const busy=operation!==null;
  const key=["payroll-profiles",tenant];
  const list=useQuery({queryKey:["payroll-profile-page",tenant,page,search,active,sort],
    queryFn:()=>payrollApi.profiles({page,pageSize:20,search:search||undefined,isActive:active==="all"?undefined:active==="active",sortBy:sort.by,sortDirection:sort.direction}),
    staleTime:Infinity,refetchOnWindowFocus:false,refetchOnReconnect:false});
  const currentTenant=()=>mounted.current&&useTenantContextStore.getState().selectedTenantId===tenant;
  async function select(id:string) {
    if(busy)return;
    setOperation("read");
    try {
      const result=await payrollApi.profile(id);
      const data=await client.fetchQuery({queryKey:["payroll-profile-versions",tenant,id,1],queryFn:()=>payrollApi.profileRuleSets(id,1),staleTime:Infinity});
      const configuration=data.items.find(item=>item.status==="Approved"||item.status==="Retired");
      if(!configuration)throw new Error("El perfil no tiene una configuración publicada disponible.");
      if(currentTenant()){
        setVersionPage(1);setBaseConfiguration(configuration);setConfigurationChanged(false);
        setParameters(Object.fromEntries(configuration.parameters.map(item=>[item.code,String(item.numericValue)])));
        setSource(configuration.sourceReference);setEffectiveFrom(configuration.effectiveFrom);setForm(result);
      }
    }
    catch(error){if(currentTenant())toast.error(error instanceof Error?error.message:"No fue posible consultar el perfil.");}
    finally{if(currentTenant())setOperation(null);}
  }
  async function save() {
    if(!form||busy)return;
    setOperation("save");
    try {
      const saved=await payrollApi.saveProfile({...form,configuration:form.rowVersion&&!configurationChanged?null:{
        ruleSetId:form.rowVersion?crypto.randomUUID():form.ruleSetId,countryCode:baseConfiguration?.countryCode??"CO",code:baseConfiguration?.code??"",name:form.name,effectiveFrom,effectiveTo:null,sourceReference:source,
        parameters:definitions.map(item=>({code:item.code,numericValue:Number(parameters[item.code]),unitCode:item.metadataCode??"Value",description:item.description})),rowVersion:null,
      }});
      if(saved.createdRuleSet)await client.invalidateQueries({queryKey:["payroll-profile-versions",tenant,form.profileId],refetchType:"none"});
      if(currentTenant())setForm(saved);
      client.setQueriesData<InfiniteData<PayrollProfilePage>>({queryKey:key},current=>current?{
        ...current,pages:current.pages.map(page=>({...page,items:page.items.map(row=>row.profileId===saved.profileId?{...saved,mappings:[]}:row)})),
      }:current);
      await client.invalidateQueries({queryKey:key,refetchType:"none"});
      // The detail response cannot reproduce server ordering and filtered totals.
      if(currentTenant()){setOperation("refresh");await list.refetch();}
      if(currentTenant()){setForm(null);toast.success("Perfil guardado.");}
    } catch(error){if(currentTenant())toast.error(error instanceof Error?error.message:"No fue posible guardar el perfil.");}
    finally{if(currentTenant())setOperation(null);}
  }
  function mapping(id:string,patch:Partial<PayrollProfileMapping>) {
    setForm(current=>current?{...current,mappings:current.mappings.map(row=>row.mappingId===id?{...row,...patch}:row)}:current);
  }
  const heading=(by:typeof sort.by,label:string)=><th aria-sort={sort.by===by?(sort.direction==="asc"?"ascending":"descending"):undefined} className="text-left"><Button variant="ghost" size="sm" disabled={busy||list.isFetching} onClick={()=>{setPage(1);setSort({by,direction:sort.by===by&&sort.direction==="asc"?"desc":"asc"});}}>{label}{sort.by===by?(sort.direction==="asc"?" ↑":" ↓"):""}</Button></th>;
  return <Card role="region" aria-label="Perfiles de nómina" className="min-w-0 rounded-3xl lg:col-span-2"><CardHeader><CardTitle>Perfiles de nómina</CardTitle>
    <CardDescription>Cuentas y porcentajes por perfil, con sus vigencias. Cada contrato conserva su perfil al liquidar.</CardDescription>
  </CardHeader><CardContent className="space-y-4">
    <div className="flex flex-wrap items-center gap-3"><form className="flex w-full min-w-0 gap-2 sm:w-auto sm:flex-1" onSubmit={event=>{event.preventDefault();setSearch(draftSearch.trim());setPage(1);}}>
      <Input className="min-w-0 flex-1" aria-label="Buscar perfil de nómina" maxLength={160} placeholder="Nombre del perfil" value={draftSearch} disabled={busy} onChange={event=>setDraftSearch(event.target.value)}/>
      <Button type="submit" variant="outline" disabled={busy||list.isFetching}>Buscar</Button></form>
      <Select value={active} disabled={busy} onValueChange={value=>{setActive(value);setPage(1);}}><SelectTrigger aria-label="Estado del perfil" className="w-40"><SelectValue/></SelectTrigger><SelectContent><SelectItem value="all">Todos</SelectItem><SelectItem value="active">Activos</SelectItem><SelectItem value="inactive">Inactivos</SelectItem></SelectContent></Select>
      {canConfigure&&<Button type="button" variant="outline" disabled={busy} onClick={()=>{setBaseConfiguration(null);setConfigurationChanged(false);setParameters({});setSource("");setEffectiveFrom(today);setForm({profileId:crypto.randomUUID(),code:"",name:"",ruleSetId:crypto.randomUUID(),isActive:true,rowVersion:null,mappings:[]});}}><Plus className="mr-2 size-4"/>Nuevo perfil</Button>}
    </div>
    {list.isPending?<p role="status">Cargando perfiles…</p>:list.isError?<div role="alert"><p>No fue posible consultar los perfiles.</p><Button variant="outline" onClick={()=>void list.refetch()}>Reintentar perfiles</Button></div>:<>
      <div className="max-w-full overflow-x-auto"><table className="w-full text-sm"><thead><tr>{heading("name","Nombre")}{heading("status","Estado")}<th><span className="sr-only">Abrir perfil</span></th></tr></thead><tbody>{list.data?.items.map(row=><tr key={row.profileId} className="border-t"><td className="break-words p-2">{row.name}</td><td className="p-2">{row.isActive?"Activo":"Inactivo"}</td><td className="p-2"><Button variant="ghost" size="icon" aria-label={`Abrir perfil ${row.name}`} disabled={busy} onClick={()=>void select(row.profileId)}><Pencil className="size-4"/></Button></td></tr>)}</tbody></table></div>
      {list.data?.items.length===0&&<p className="text-sm text-muted-foreground">No hay perfiles con estos filtros.</p>}
      <div className="flex flex-wrap items-center justify-between gap-2 text-sm"><span>{list.data?.totalCount??0} perfiles · Página {page}</span><div className="flex gap-2"><Button variant="outline" size="sm" disabled={busy||list.isFetching||page===1} onClick={()=>setPage(page-1)}>Anterior perfiles</Button><Button variant="outline" size="sm" disabled={busy||list.isFetching||page>=(list.data?.totalPages??0)} onClick={()=>setPage(page+1)}>Siguiente perfiles</Button></div></div>
    </>}
    {operation==="read"&&<p role="status" className="text-sm text-muted-foreground">Consultando perfil…</p>}
    {operation==="refresh"&&<p role="status" className="text-sm text-muted-foreground">Actualizando lista de perfiles…</p>}
    <Dialog open={form!==null} onOpenChange={open=>{if(!open&&!busy)setForm(null);}}>
    <DialogContent showClose={!busy} className="flex h-[min(92dvh,52rem)] max-h-[92dvh] w-[calc(100%_-_1rem)] max-w-4xl flex-col gap-0 overflow-visible p-0">
      <DialogHeader className="shrink-0 border-b p-5 text-left"><DialogTitle>{form?.rowVersion?"Editar perfil de nómina":"Nuevo perfil de nómina"}</DialogTitle><DialogDescription>Configura las cuentas por concepto y los porcentajes con sus vigencias.</DialogDescription></DialogHeader>
      <div className="min-h-0 flex-1 space-y-4 overflow-y-auto p-5">
    {form&&<fieldset disabled={busy||!canConfigure} className="min-w-0 space-y-4">
      <div className="grid min-w-0 grid-cols-1 gap-4 md:grid-cols-2">
        <label className="min-w-0 space-y-2 md:col-span-2"><span className="text-sm">Nombre</span><Input maxLength={160} value={form.name} onChange={event=>setForm({...form,name:event.target.value})}/></label>
        <label className="flex items-center gap-2"><Checkbox disabled={busy||!canConfigure} checked={form.isActive} onCheckedChange={checked=>setForm({...form,isActive:checked===true})}/>Perfil activo</label>
      </div>
      <section className="space-y-4 rounded-xl border p-4"><h3 className="font-semibold">Configuración del perfil</h3>
        {baseConfiguration?.status==="Retired"&&<p role="status" className="text-sm text-amber-800">Configuración retirada. Para volver a aplicarla, cambia la vigencia o los valores y guarda el perfil.</p>}
        <div className="grid min-w-0 grid-cols-1 gap-4 sm:grid-cols-2"><label className="min-w-0 space-y-2"><span className="text-sm">Vigente desde</span><DatePicker today={today} value={effectiveFrom} onChange={value=>{setEffectiveFrom(value);setConfigurationChanged(true);}}/></label>
          <label className="min-w-0 space-y-2"><span className="text-sm">Fuente normativa</span><Input maxLength={500} value={source} onChange={event=>{setSource(event.target.value);setConfigurationChanged(true);}}/></label></div>
        <PayrollRuleParameterFields definitions={definitions} values={parameters} onChange={(key,value)=>{setParameters(current=>({...current,[key]:value}));setConfigurationChanged(true);}} disabled={busy||!canConfigure}/>
      </section>
      <p className="text-sm text-muted-foreground">Las cuentas se aplican por vigencia al contabilizar. Un lado sin cuenta conserva la asignación general de ese concepto.</p>
      {form.mappings.map(row=>{
        const concept=options.concepts.find(item=>item.conceptId===row.conceptId),net=row.conceptId===null;
        const sides=net||concept?.natureCode==="Deduction"?["credit"] as const:concept?.natureCode==="Earning"?["debit"] as const:["debit","credit"] as const;
        return <div key={row.mappingId} className="grid min-w-0 grid-cols-1 gap-3 rounded-xl border p-3 md:grid-cols-2">
          <label className="min-w-0 space-y-2"><span className="text-sm">Concepto</span><Select disabled={busy||!canConfigure} value={row.conceptId??"_net"} onValueChange={id=>mapping(row.mappingId,{conceptId:id==="_net"?null:id,debitAccountId:null,creditAccountId:null,debitAccountLabel:null,creditAccountLabel:null,beneficiaryPartyId:null,beneficiaryName:null})}>
            <SelectTrigger aria-label="Concepto del perfil"><SelectValue/></SelectTrigger><SelectContent><SelectItem value="_net">Neto por pagar</SelectItem>{options.concepts.map(item=><SelectItem key={item.conceptId} value={item.conceptId}>{item.code} · {item.name}</SelectItem>)}</SelectContent></Select></label>
          {sides.map(side=>{
            const id=row[`${side}AccountId`],label=row[`${side}AccountLabel`];
            return <label key={side} className="min-w-0 space-y-2"><span className="text-sm">{side==="debit"?"Débito":"Crédito"}</span><div className="flex gap-2"><div className="min-w-0 flex-1"><AccountSelect value={id??""} resolveMissing={false} disabled={busy||!canConfigure||!canReadAccounting}
              accounts={id?[{accountId:id,code:"",name:label??"Cuenta seleccionada"}]:[]}
              onChange={(accountId,account)=>mapping(row.mappingId,{[`${side}AccountId`]:accountId||null,[`${side}AccountLabel`]:account?`${account.code} · ${account.name}`:null})}/></div>
              {id&&<Button type="button" variant="ghost" aria-label={`Quitar cuenta de ${side==="debit"?"débito":"crédito"}`} onClick={()=>mapping(row.mappingId,{[`${side}AccountId`]:null,[`${side}AccountLabel`]:null})}>×</Button>}</div></label>;
          })}
          {(concept?.systemRoleCode==="Sena"||concept?.systemRoleCode==="Icbf")&&<label className="min-w-0 space-y-2"><span className="text-sm">Entidad beneficiaria</span><PartySelect partyType="Organization" ariaLabel="Entidad beneficiaria del aporte" disabled={busy||!canConfigure}
            value={row.beneficiaryPartyId??""} selectedOption={row.beneficiaryPartyId?{value:row.beneficiaryPartyId,label:row.beneficiaryName??"Entidad seleccionada"}:null}
            onChange={(id,party)=>mapping(row.mappingId,{beneficiaryPartyId:id||null,beneficiaryName:party?.displayName??null})}/></label>}
          <label className="min-w-0 space-y-2"><span className="text-sm">Desde</span><DatePicker today={today} value={row.effectiveFrom} onChange={value=>mapping(row.mappingId,{effectiveFrom:value})}/></label>
          <label className="min-w-0 space-y-2"><span className="text-sm">Hasta (opcional)</span><DatePicker today={today} value={row.effectiveTo??""} onChange={value=>mapping(row.mappingId,{effectiveTo:value||null})}/></label>
          {canConfigure&&<Button type="button" variant="ghost" size="icon" aria-label="Retirar cuenta del perfil" onClick={()=>setForm({...form,mappings:form.mappings.filter(item=>item.mappingId!==row.mappingId)})}><Trash2 className="size-4"/></Button>}
        </div>;
      })}
      {!canReadAccounting&&<p className="text-sm text-muted-foreground">Se necesita permiso de consulta contable para seleccionar cuentas.</p>}
      {canConfigure&&<Button type="button" variant="outline" disabled={busy||form.mappings.length>=500} onClick={()=>setForm({...form,mappings:[...form.mappings,{mappingId:crypto.randomUUID(),conceptId:null,debitAccountId:null,creditAccountId:null,effectiveFrom:today,effectiveTo:null}]})}>Agregar cuenta por concepto</Button>}
    </fieldset>}
    {form?.rowVersion&&<details className="rounded-xl border p-3"><summary className="cursor-pointer font-medium">Historial del perfil</summary>{versions.isPending?<p role="status">Cargando vigencias…</p>:versions.isError?<div role="alert"><p>No fue posible consultar las vigencias del perfil.</p><Button variant="outline" onClick={()=>void versions.refetch()}>Reintentar vigencias</Button></div>:<>
      {versions.data.items.map(item=><div key={item.ruleSetId} className="border-b py-3 text-sm"><b>{item.name}</b><p>{item.isGlobal?"Base general":"Empresa"} · Desde {item.effectiveFrom} · {item.parameters.length} parámetros</p></div>)}
      <div className="flex flex-wrap items-center justify-between gap-2 text-sm"><span>{versions.data.totalCount} vigencias · Página {versionPage}</span><div className="flex gap-2"><Button variant="outline" size="sm" disabled={versions.isFetching||versionPage===1} onClick={()=>setVersionPage(value=>value-1)}>Anterior vigencias</Button><Button variant="outline" size="sm" disabled={versions.isFetching||versionPage*20>=versions.data.totalCount} onClick={()=>setVersionPage(value=>value+1)}>Siguiente vigencias</Button></div></div>
    </>}</details>}
    </div>
    <DialogFooter className="shrink-0 border-t bg-muted/20 p-4"><Button variant="outline" disabled={busy} onClick={()=>setForm(null)}>Cerrar perfil</Button>
      {canConfigure&&form&&<Button type="button" disabled={busy||!form.name.trim()||!form.ruleSetId||(!form.rowVersion||configurationChanged)&&!initialComplete} onClick={()=>void save()}>{operation==="save"?"Guardando perfil…":operation==="refresh"?"Actualizando lista…":"Guardar perfil"}</Button>}
    </DialogFooter></DialogContent></Dialog>
  </CardContent></Card>;
}
