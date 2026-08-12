"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import type { ColumnDef } from "@tanstack/react-table";
import { ArrowRight, BriefcaseBusiness, MapPin, Pencil, Plus, Search, Truck, UserRound, UsersRound } from "lucide-react";
import { toast } from "sonner";
import { DataTable } from "@/components/tables/data-table";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { useCities, useCountries, useCreateThirdParty, useCustomerPricingOptions, useDivisions, useParties, usePartyDetail, usePartyIdentity, useSetPartyStatus, useUpdateParty } from "@/hooks/use-parties";
import type { PartyRole, PartyWorkspaceDetail, PartyWorkspaceItem } from "@/services/api/parties";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";

const roleLabels: Record<PartyRole,string>={Customer:"Cliente",Supplier:"Proveedor",Seller:"Vendedor",Carrier:"Transportador"};
const roleDescriptions: Record<PartyRole,string>={Customer:"Ventas, precios y cartera",Supplier:"Compras, costos y cuentas por pagar",Seller:"Comisiones, rutas y atención comercial",Carrier:"Despachos y transporte de mercancía"};
const rolePermissions: Record<PartyRole,string>={Customer:"customers.create",Supplier:"suppliers.create",Seller:"sellers.create",Carrier:"carriers.create"};
const emptyThirdPartyForm={identificationTypeCode:"CC",identification:"",displayName:"",legalName:"",firstName:"",lastName:"",email:"",phone:"",siteName:"Principal",addressLine:"",neighborhood:"",code:"",commission:"",commissionBasis:"SaleAfterTax",commissionTrigger:"Sale",transportationMode:"Road"};

export default function PartiesPage(){
  const permissions=useAuthStore((s)=>new Set(s.user?.permissions??[]));
  const [page,setPage]=useState(1),[pageSize,setPageSize]=useState(25),[search,setSearch]=useState("");
  const [role,setRole]=useState("all"),[status,setStatus]=useState("all"),[createOpen,setCreateOpen]=useState(false),[detailTarget,setDetailTarget]=useState<{partyId:string;edit:boolean}>(),[createSeed,setCreateSeed]=useState<PartyWorkspaceDetail>();
  const query=useParties({page,pageSize,search:search.trim()||undefined,role:role==="all"?undefined:role,isActive:status==="all"?undefined:status==="active"});
  const statusMutation=useSetPartyStatus();
  const columns=useMemo<ColumnDef<PartyWorkspaceItem>[]>(()=>[
    {accessorKey:"displayName",header:"Tercero",cell:({row})=><div><p className="font-semibold">{row.original.displayName}</p><p className="text-xs text-muted-foreground">{row.original.identificationTypeCode??"Sin documento"} {row.original.identification??""}</p></div>},
    {accessorKey:"roles",header:"Roles",cell:({row})=><div className="flex flex-wrap gap-1">{row.original.roles.map((x)=><Badge key={x} variant={x==="Customer"?"default":"secondary"}>{roleLabels[x]}</Badge>)}</div>},
    {accessorKey:"primarySiteName",header:"Sede principal",cell:({row})=><div><p>{row.original.primarySiteName??"Sin sede"}</p><p className="text-xs text-muted-foreground">{row.original.cityName??"Ubicación pendiente"}</p></div>},
    {accessorKey:"email",header:"Contacto",cell:({row})=><div><p>{row.original.phone??"Sin teléfono"}</p><p className="text-xs text-muted-foreground">{row.original.email??"Sin correo"}</p></div>},
    {accessorKey:"isActive",header:"Estado",cell:({row})=><Badge variant={row.original.isActive?"secondary":"outline"}>{row.original.isActive?"Activo":"Inactivo"}</Badge>},
    {id:"actions",header:"",cell:({row})=><div className="flex justify-end gap-2"><Button size="sm" variant="ghost" disabled={!permissions.has("parties.update")} onClick={(e)=>{e.stopPropagation();setDetailTarget({partyId:row.original.partyId,edit:true})}}><Pencil className="mr-1 h-4 w-4"/>Editar</Button><Button size="sm" variant="outline" disabled={!permissions.has("parties.deactivate")||statusMutation.isPending} onClick={async(e)=>{e.stopPropagation();try{await statusMutation.mutateAsync({partyId:row.original.partyId,isActive:!row.original.isActive,rowVersion:row.original.rowVersion});toast.success("Estado actualizado");}catch{toast.error("No fue posible cambiar el estado.")}}}>{row.original.isActive?"Desactivar":"Activar"}</Button></div>}
  ],[permissions,statusMutation]);
  const canCreate=Object.values(rolePermissions).some((p)=>permissions.has(p));
  return <div className="space-y-6">
    <header className="flex flex-col justify-between gap-4 xl:flex-row xl:items-end"><div><p className="text-sm font-medium text-primary">Identidad comercial unificada</p><h1 className="text-3xl font-semibold tracking-tight">Terceros</h1><p className="mt-1 max-w-3xl text-muted-foreground">Una identidad puede tener distintos roles y varias sedes, sin duplicar sus datos comunes.</p></div><Button disabled={!canCreate} onClick={()=>{setCreateSeed(undefined);setCreateOpen(true)}}><Plus className="mr-2 h-4 w-4"/>Nuevo tercero</Button></header>
    <section className="grid gap-3 md:grid-cols-3"><Summary icon={UsersRound} label="Terceros encontrados" value={String(query.data?.totalCount??0)}/><Summary icon={UserRound} label="Identidad" value="Party única"/><Summary icon={MapPin} label="Direcciones" value="Múltiples sedes"/></section>
    <section className="grid gap-3 rounded-2xl border bg-card p-4 xl:grid-cols-[minmax(0,1fr)_14rem_14rem]"><label className="relative"><Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground"/><Input className="pl-9" value={search} onChange={(e)=>{setSearch(e.target.value);setPage(1)}} placeholder="Nombre, documento, correo o teléfono"/></label><Select value={role} onValueChange={(v)=>{setRole(v);setPage(1)}}><SelectTrigger><SelectValue placeholder="Rol"/></SelectTrigger><SelectContent><SelectItem value="all">Todos los roles</SelectItem>{Object.entries(roleLabels).map(([value,label])=><SelectItem key={value} value={value}>{label}</SelectItem>)}</SelectContent></Select><Select value={status} onValueChange={(v)=>{setStatus(v);setPage(1)}}><SelectTrigger><SelectValue placeholder="Estado"/></SelectTrigger><SelectContent><SelectItem value="all">Todos los estados</SelectItem><SelectItem value="active">Activos</SelectItem><SelectItem value="inactive">Inactivos</SelectItem></SelectContent></Select></section>
    <DataTable columns={columns} data={query.data?.items??[]} isLoading={query.isLoading} page={query.data?.page} pageSize={query.data?.pageSize} pageCount={query.data?.totalPages} totalItems={query.data?.totalCount} enableRowSelection={false} onRowClick={(item)=>setDetailTarget({partyId:item.partyId,edit:false})} onPaginationChange={(p,s)=>{setPage(p);setPageSize(s)}}/>
    <CreateThirdPartyDialog open={createOpen} permissions={permissions} initialParty={createSeed} onClose={()=>{setCreateOpen(false);setCreateSeed(undefined)}}/><PartyDetailDialog key={detailTarget?.partyId??"none"} target={detailTarget} onClose={()=>setDetailTarget(undefined)} onAddRole={(party)=>{setDetailTarget(undefined);setCreateSeed(party);setCreateOpen(true)}}/>
  </div>;
}

function CreateThirdPartyDialog({open,permissions,initialParty,onClose}:{open:boolean;permissions:Set<string>;initialParty?:PartyWorkspaceDetail;onClose:()=>void}){
  const businessId=useBusinessContextStore((s)=>s.selectedBusinessId); const [selectedRole,setSelectedRole]=useState<PartyRole>();
  const role=selectedRole; const create=useCreateThirdParty(role??"Customer"); const pricingOptions=useCustomerPricingOptions(open&&role==="Customer");
  const countries=useCountries(); const [partyType,setPartyType]=useState("NaturalPerson"),[country,setCountry]=useState(""),[division,setDivision]=useState(""),[city,setCity]=useState("");
  const divisions=useDivisions(country),cities=useCities(division); const [pricingMode,setPricingMode]=useState("Default"),[pricingId,setPricingId]=useState("");
  const [form,setForm]=useState(emptyThirdPartyForm);
  const [lookupIdentification,setLookupIdentification]=useState("");
  const hydratedLookup=useRef("");
  useEffect(()=>{
    if(country||!countries.data?.length)return;
    const preferred=countries.data.find((item)=>["CO","COL"].includes(item.code.toUpperCase()))??countries.data.find((item)=>item.isActive);
    if(preferred)setCountry(preferred.countryId);
  },[countries.data,country]);
  useEffect(()=>{
    if(!open||!initialParty)return;
    const site=initialParty.primarySite;
    setPartyType(initialParty.partyType);
    setForm({...emptyThirdPartyForm,identificationTypeCode:initialParty.identificationTypeCode,identification:initialParty.identification,displayName:initialParty.displayName,legalName:initialParty.legalName??"",firstName:initialParty.firstName??"",lastName:initialParty.lastName??"",email:initialParty.email??"",phone:initialParty.phone??"",siteName:site?.name??"Principal",addressLine:site?.addressLine??"",neighborhood:site?.neighborhood??""});
    setCountry(site?.countryId??initialParty.identificationCountryId);
    setDivision(site?.administrativeDivisionId??"");
    setCity(site?.cityId??"");
    setLookupIdentification(initialParty.identification);
    hydratedLookup.current="";
  },[open,initialParty]);
  useEffect(()=>{
    const value=form.identification.trim();
    const timer=window.setTimeout(()=>setLookupIdentification(value),350);
    return()=>window.clearTimeout(timer);
  },[form.identification]);
  const identity=usePartyIdentity({
    countryId:country,
    identificationTypeCode:form.identificationTypeCode,
    identification:lookupIdentification,
    requestedRole:role??"Customer",
  },open&&!!role&&!!country&&lookupIdentification.length>=3);
  useEffect(()=>{
    const found=identity.data?.party;
    const lookupKey=`${role}|${country}|${form.identificationTypeCode}|${lookupIdentification.trim().toLocaleUpperCase()}`;
    if(!found||!lookupIdentification||hydratedLookup.current===lookupKey)return;
    hydratedLookup.current=lookupKey;
    setPartyType(found.partyType);
    setForm((current)=>({...current,
      identificationTypeCode:found.identificationTypeCode,
      identification:found.identification,
      displayName:found.displayName,
      legalName:found.legalName??"",
      firstName:found.firstName??"",
      lastName:found.lastName??"",
      email:found.email??"",
      phone:found.phone??"",
      siteName:found.primarySite?.name??current.siteName,
      addressLine:found.primarySite?.addressLine??current.addressLine,
      neighborhood:found.primarySite?.neighborhood??"",
    }));
    if(found.primarySite){
      setCountry(found.primarySite.countryId);
      setDivision(found.primarySite.administrativeDivisionId);
      setCity(found.primarySite.cityId);
    }else setCountry(found.identificationCountryId);
  },[identity.data,role,country,form.identificationTypeCode,lookupIdentification]);
  const set=(key:keyof typeof form,value:string)=>setForm((x)=>({...x,[key]:value}));
  const reset=()=>{setSelectedRole(undefined);setPartyType("NaturalPerson");setCountry("");setDivision("");setCity("");setForm(emptyThirdPartyForm);setLookupIdentification("");hydratedLookup.current="";setPricingMode("Default");setPricingId("");};
  const close=()=>{reset();onClose()};
  const submit=async()=>{
    if(!role||!businessId)return; if(identity.data?.hasRequestedRole)return toast.error("Esta identidad ya está registrada como "+roleLabels[role].toLowerCase()+" en el negocio."); if(!country||!division||!city)return toast.error("Selecciona país, departamento y ciudad.");
    if(!form.identification.trim()||!form.displayName.trim()||!form.addressLine.trim())return toast.error("Completa identificación, nombre y dirección.");
    if(role==="Customer"&&pricingMode!=="Default"&&!pricingId)return toast.error("Selecciona la lista o el canal.");
    const request={operationId:crypto.randomUUID(),businessId,party:{partyType,identificationCountryId:country,identificationTypeCode:form.identificationTypeCode,identification:form.identification,verificationDigit:null,displayName:form.displayName,legalName:partyType==="Organization"?(form.legalName||form.displayName):null,firstName:partyType==="NaturalPerson"?form.firstName:null,lastName:partyType==="NaturalPerson"?form.lastName:null,email:form.email||null,phone:form.phone||null},primarySite:{code:"PRINCIPAL",name:form.siteName,countryId:country,administrativeDivisionId:division,cityId:city,addressLine:form.addressLine,neighborhood:form.neighborhood||null,postalCode:null,email:form.email||null,phone:form.phone||null,isPrimary:true},pricing:role==="Customer"?{priceListId:pricingMode==="List"?pricingId:null,priceChannelId:pricingMode==="Channel"?pricingId:null}:undefined,code:form.code,defaultCommissionPercent:form.commission?Number(form.commission):null,commissionBasis:form.commissionBasis,commissionTrigger:form.commissionTrigger,transportationMode:form.transportationMode};
    try{await create.mutateAsync(request);toast.success(`${roleLabels[role]} creado y disponible en el listado`);close()}catch(error){toast.error(error instanceof Error?error.message:"No fue posible crear el tercero.")}
  };
  return <Dialog open={open} onOpenChange={(v)=>!v&&close()}><DialogContent className="max-h-[94vh] max-w-6xl overflow-hidden p-0"><div className="grid max-h-[94vh] lg:grid-cols-[250px_minmax(0,1fr)]"><aside className="hidden bg-gradient-to-b from-slate-950 to-teal-950 p-6 text-white lg:block"><p className="text-xs font-bold uppercase tracking-[.18em] text-teal-300">Terceros de Auraly</p><h2 className="mt-2 text-2xl font-semibold">Una identidad, varios roles</h2><p className="mt-3 text-sm text-slate-300">Los datos generales se comparten. Cliente, proveedor, vendedor y transportador conservan su propia configuración.</p><ol className="mt-8 space-y-4 text-sm"><li className="rounded-xl bg-white/10 p-3">1. Identidad</li><li className="rounded-xl bg-white/10 p-3">2. Ubicación</li><li className="rounded-xl bg-white/10 p-3">3. Rol comercial</li></ol></aside><div className="max-h-[94vh] overflow-y-auto p-6"><DialogHeader><DialogTitle>{role?`${initialParty?"Agregar":"Nuevo"} ${roleLabels[role].toLowerCase()}`:initialParty?`Agregar rol a ${initialParty.displayName}`:"Nuevo tercero"}</DialogTitle><DialogDescription>{role?"Completa los datos comunes y la configuración específica del rol.":"Selecciona un solo rol. Después podrás agregar otros roles a la misma identidad."}</DialogDescription></DialogHeader>
    {!role?<div className="mt-6 space-y-5">
      <div className="overflow-hidden rounded-2xl bg-gradient-to-r from-slate-950 via-slate-900 to-teal-950 p-5 text-white">
        <p className="text-xs font-bold uppercase tracking-[.18em] text-teal-300">Punto de partida</p>
        <h3 className="mt-2 text-xl font-semibold">¿Qué relación tendrá con el negocio?</h3>
        <p className="mt-1 max-w-2xl text-sm text-slate-300">Elige un rol para mostrar únicamente los datos que realmente necesita. Después podrás sumar otros roles sin duplicar la identidad.</p>
      </div>
      <div className="grid gap-4 sm:grid-cols-2">{(Object.keys(roleLabels) as PartyRole[]).map((item)=>{
        const disabled=!permissions.has(rolePermissions[item])||Boolean(initialParty?.roles.includes(item));
        const Icon=item==="Carrier"?Truck:item==="Customer"?UserRound:BriefcaseBusiness;
        return <button key={item} type="button" disabled={disabled} onClick={()=>setSelectedRole(item)} className="group relative min-h-32 overflow-hidden rounded-2xl border bg-card p-5 text-left shadow-sm transition hover:-translate-y-0.5 hover:border-teal-400 hover:shadow-md disabled:cursor-not-allowed disabled:opacity-45 disabled:hover:translate-y-0">
          <span className="flex h-11 w-11 items-center justify-center rounded-2xl bg-teal-50 text-teal-700 transition group-hover:bg-teal-500 group-hover:text-white"><Icon className="h-5 w-5"/></span>
          <span className="mt-4 flex items-center justify-between gap-3"><span><strong className="block text-base">{roleLabels[item]}</strong><span className="mt-1 block text-sm text-muted-foreground">{initialParty?.roles.includes(item)?"Este rol ya está asignado":roleDescriptions[item]}</span></span><ArrowRight className="h-5 w-5 text-muted-foreground transition group-hover:translate-x-1 group-hover:text-teal-600"/></span>
        </button>})}</div>
    </div>:<div className="mt-6 space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border bg-gradient-to-r from-teal-50 to-cyan-50 p-4">
        <div><p className="text-xs font-bold uppercase tracking-[.14em] text-teal-700">Rol seleccionado</p><h3 className="mt-1 text-lg font-semibold">{roleLabels[role]}</h3><p className="text-sm text-muted-foreground">{roleDescriptions[role]}</p></div>
        <Button type="button" variant="outline" onClick={()=>setSelectedRole(undefined)}>Cambiar rol</Button>
      </div>
      <IdentityNotice role={role} lookup={identity}/><div className="grid gap-4 md:grid-cols-2">
      <FormSectionTitle title="Identidad" description="Datos compartidos por todos los roles comerciales." /><Field label="Tipo de tercero"><Select value={partyType} onValueChange={setPartyType}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent><SelectItem value="NaturalPerson">Persona natural</SelectItem><SelectItem value="Organization">Organización</SelectItem></SelectContent></Select></Field>
      <Field label="Tipo de identificación"><Select value={form.identificationTypeCode} onValueChange={(v)=>set("identificationTypeCode",v)}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent><SelectItem value="CC">Cédula</SelectItem><SelectItem value="NIT">NIT</SelectItem><SelectItem value="CE">Cédula de extranjería</SelectItem><SelectItem value="PP">Pasaporte</SelectItem></SelectContent></Select></Field>
      <Field label="Identificación"><Input value={form.identification} onChange={(e)=>set("identification",e.target.value)} onBlur={()=>setLookupIdentification(form.identification.trim())}/></Field><Field label="Nombre visible"><Input value={form.displayName} onChange={(e)=>set("displayName",e.target.value)}/></Field>
      {partyType==="Organization"?<Field label="Razón social"><Input value={form.legalName} onChange={(e)=>set("legalName",e.target.value)}/></Field>:<><Field label="Nombres"><Input value={form.firstName} onChange={(e)=>set("firstName",e.target.value)}/></Field><Field label="Apellidos"><Input value={form.lastName} onChange={(e)=>set("lastName",e.target.value)}/></Field></>}
      <Field label="Teléfono"><Input value={form.phone} onChange={(e)=>set("phone",e.target.value)}/></Field><Field label="Correo"><Input type="email" value={form.email} onChange={(e)=>set("email",e.target.value)}/></Field>
      <FormSectionTitle title="Ubicación principal" description="País, departamento y ciudad se seleccionan; el barrio se escribe libremente." />      <Field label="País"><Select value={country} onValueChange={(v)=>{setCountry(v);setDivision("");setCity("")}} disabled={countries.isLoading}><SelectTrigger><SelectValue placeholder={countries.isLoading?"Cargando...":"Selecciona"}/></SelectTrigger><SelectContent>{countries.data?.filter(x=>x.isActive).map(x=><SelectItem key={x.countryId} value={x.countryId}>{x.name}</SelectItem>)}</SelectContent></Select></Field>
      <Field label="Departamento"><Select value={division} onValueChange={(v)=>{setDivision(v);setCity("")}} disabled={!country||divisions.isLoading}><SelectTrigger><SelectValue placeholder={!country?"Selecciona primero el país":divisions.isLoading?"Cargando...":"Selecciona"}/></SelectTrigger><SelectContent>{divisions.data?.filter(x=>x.isActive).map(x=><SelectItem key={x.administrativeDivisionId} value={x.administrativeDivisionId}>{x.name}</SelectItem>)}</SelectContent></Select></Field>
      <Field label="Ciudad"><Select value={city} onValueChange={setCity} disabled={!division||cities.isLoading}><SelectTrigger><SelectValue placeholder={!division?"Selecciona primero el departamento":cities.isLoading?"Cargando...":"Selecciona"}/></SelectTrigger><SelectContent>{cities.data?.filter(x=>x.isActive).map(x=><SelectItem key={x.cityId} value={x.cityId}>{x.name}</SelectItem>)}</SelectContent></Select></Field>
      <Field label="Nombre de la sede"><Input value={form.siteName} onChange={(e)=>set("siteName",e.target.value)}/></Field><Field label="Dirección"><Input value={form.addressLine} onChange={(e)=>set("addressLine",e.target.value)}/></Field><Field label="Barrio"><Input value={form.neighborhood} onChange={(e)=>set("neighborhood",e.target.value)}/></Field>
      <FormSectionTitle title={`Configuración de ${roleLabels[role].toLowerCase()}`} description={roleDescriptions[role]} />{role==="Customer"&&<><Field label="Precio asignado"><Select value={pricingMode} onValueChange={(v)=>{setPricingMode(v);setPricingId("")}}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent><SelectItem value="Default">Precio predeterminado del negocio</SelectItem><SelectItem value="List">Lista de precios</SelectItem><SelectItem value="Channel">Canal de precios</SelectItem></SelectContent></Select></Field>{pricingMode!=="Default"&&<Field label={pricingMode==="List"?"Lista":"Canal"}><Select value={pricingId} onValueChange={setPricingId} disabled={pricingOptions.isLoading}><SelectTrigger><SelectValue placeholder={pricingOptions.isLoading?"Cargando...":"Selecciona"}/></SelectTrigger><SelectContent>{(pricingMode==="List"?pricingOptions.data?.priceLists:pricingOptions.data?.priceChannels)?.map(x=><SelectItem key={x.id} value={x.id}>{x.name} ({x.code})</SelectItem>)}</SelectContent></Select></Field>}</>}
      {role==="Seller"&&<><Field label="Comisión %"><Input type="number" min="0" max="100" value={form.commission} onChange={(e)=>set("commission",e.target.value)}/></Field><Field label="Base de comisión"><Select value={form.commissionBasis} onValueChange={(v)=>set("commissionBasis",v)}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent><SelectItem value="SaleBeforeTax">Venta antes de IVA</SelectItem><SelectItem value="SaleAfterTax">Venta después de IVA</SelectItem><SelectItem value="GrossMargin">Margen bruto</SelectItem></SelectContent></Select></Field><Field label="Causación"><Select value={form.commissionTrigger} onValueChange={(v)=>set("commissionTrigger",v)}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent><SelectItem value="Sale">Al vender</SelectItem><SelectItem value="Collection">Al recaudar</SelectItem></SelectContent></Select></Field></>}
      {role==="Carrier"&&<Field label="Modalidad de transporte"><Select value={form.transportationMode} onValueChange={(v)=>set("transportationMode",v)}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent><SelectItem value="Road">Terrestre</SelectItem><SelectItem value="Air">Aérea</SelectItem><SelectItem value="Maritime">Marítima</SelectItem><SelectItem value="Other">Otra</SelectItem></SelectContent></Select></Field>}
    </div></div>}
    <DialogFooter className="mt-6 border-t pt-4"><Button variant="outline" onClick={close}>Cancelar</Button>{role&&<Button onClick={submit} disabled={create.isPending||identity.isFetching||identity.data?.hasRequestedRole}>{create.isPending?"Guardando...":"Guardar tercero"}</Button>}</DialogFooter></div></div></DialogContent></Dialog>;
}

function IdentityNotice({role,lookup}:{role:PartyRole;lookup:ReturnType<typeof usePartyIdentity>}){
  if(lookup.isFetching)return <div className="col-span-full rounded-xl border bg-muted/40 px-4 py-3 text-sm text-muted-foreground">Validando identidad...</div>;
  if(lookup.isError)return <div className="col-span-full rounded-xl border border-destructive/30 bg-destructive/5 px-4 py-3 text-sm text-destructive">No fue posible validar la identidad.</div>;
  if(!lookup.data?.exists)return null;
  if(lookup.data.hasRequestedRole)return <div className="col-span-full rounded-xl border border-destructive/30 bg-destructive/5 px-4 py-3 text-sm text-destructive">Esta persona ya está registrada como {roleLabels[role].toLowerCase()} en este negocio.</div>;
  return <div className="col-span-full rounded-xl border border-primary/30 bg-primary/5 px-4 py-3 text-sm"><strong>Identidad encontrada.</strong> Se reutilizarán sus datos generales y únicamente se agregará el rol {roleLabels[role].toLowerCase()}.</div>;
}

function PartyDetailDialog({target,onClose,onAddRole}:{target?:{partyId:string;edit:boolean};onClose:()=>void;onAddRole:(party:PartyWorkspaceDetail)=>void}){
  const detailQuery=usePartyDetail(target?.partyId);const update=useUpdateParty();const detail=detailQuery.data;const [editing,setEditing]=useState(Boolean(target?.edit));
  const [name,setName]=useState(""),[legalName,setLegalName]=useState(""),[firstName,setFirstName]=useState(""),[lastName,setLastName]=useState(""),[email,setEmail]=useState(""),[phone,setPhone]=useState("");
  useEffect(()=>{if(!detail)return;setName(detail.displayName);setLegalName(detail.legalName??"");setFirstName(detail.firstName??"");setLastName(detail.lastName??"");setEmail(detail.email??"");setPhone(detail.phone??"")},[detail]);
  if(!target)return null;
  const save=async()=>{if(!detail)return;try{await update.mutateAsync({partyId:detail.partyId,request:{partyType:detail.partyType,displayName:name,legalName:detail.partyType==="Organization"?(legalName||name):null,firstName:detail.partyType==="NaturalPerson"?firstName:null,lastName:detail.partyType==="NaturalPerson"?lastName:null,verificationDigit:detail.verificationDigit,email:email||null,phone:phone||null,rowVersion:detail.rowVersion}});await detailQuery.refetch();setEditing(false);toast.success("Tercero actualizado")}catch(error){toast.error(error instanceof Error?error.message:"No fue posible actualizar el tercero.")}};
  return <Dialog open onOpenChange={(value)=>!value&&onClose()}><DialogContent className="max-h-[92vh] max-w-5xl overflow-y-auto p-0"><div className="border-b bg-gradient-to-r from-slate-950 to-teal-950 px-6 py-5 text-white"><DialogHeader><DialogTitle className="text-white">{detail?.displayName??"Detalle del tercero"}</DialogTitle><DialogDescription className="text-slate-300">Una identidad compartida y una configuración independiente para cada rol.</DialogDescription></DialogHeader></div><div className="space-y-5 p-6">
    {detailQuery.isLoading?<div className="py-12 text-center text-muted-foreground">Cargando información...</div>:detailQuery.isError||!detail?<div className="rounded-xl border border-destructive/30 bg-destructive/5 p-4 text-destructive">No fue posible cargar el tercero.</div>:<Tabs id="party-identity" defaultValue="general" className="scroll-mt-5 space-y-5">
      <TabsList id="party-roles" className="h-auto scroll-mt-5 flex-wrap justify-start"><TabsTrigger value="general">Identidad y ubicación</TabsTrigger>{detail.roles.map((role)=><TabsTrigger key={role} value={role}>{roleLabels[role]}</TabsTrigger>)}</TabsList>
      <TabsContent value="general" className="space-y-5">
        <section className="rounded-2xl border bg-muted/15 p-5"><h3 className="mb-4 font-semibold">Identidad compartida</h3><div className="grid gap-4 md:grid-cols-2">{editing?<><Field label="Nombre visible"><Input value={name} onChange={(e)=>setName(e.target.value)}/></Field>{detail.partyType==="Organization"?<Field label="Razón social"><Input value={legalName} onChange={(e)=>setLegalName(e.target.value)}/></Field>:<><Field label="Nombres"><Input value={firstName} onChange={(e)=>setFirstName(e.target.value)}/></Field><Field label="Apellidos"><Input value={lastName} onChange={(e)=>setLastName(e.target.value)}/></Field></>}<Field label="Correo"><Input value={email} onChange={(e)=>setEmail(e.target.value)}/></Field><Field label="Teléfono"><Input value={phone} onChange={(e)=>setPhone(e.target.value)}/></Field></>:<><DetailValue label="Nombre" value={detail.displayName}/><DetailValue label="Identificación" value={`${detail.identificationTypeCode??""} ${detail.identification??"Sin documento"}`}/><DetailValue label="Correo" value={detail.email??"Sin correo"}/><DetailValue label="Teléfono" value={detail.phone??"Sin teléfono"}/></>}</div></section>
        <section className="rounded-2xl border p-5"><h3 className="font-semibold">Ubicación principal</h3>{detail.primarySite?<div className="mt-3 grid gap-3 text-sm md:grid-cols-2"><DetailValue label="Nombre" value={detail.primarySite.name}/><DetailValue label="Código" value={detail.primarySite.code}/><DetailValue label="Dirección" value={detail.primarySite.addressLine}/><DetailValue label="Barrio" value={detail.primarySite.neighborhood??"Sin barrio"}/></div>:<p className="mt-2 text-sm text-muted-foreground">No tiene una sede registrada.</p>}</section>
      </TabsContent>
      {detail.customer&&<TabsContent value="Customer"><RoleCard title="Cliente" rows={[["Estado",detail.customer.isActive?"Activo":"Inactivo"],["Lista de precios",detail.customer.priceListId??"Precio público"],["Canal de precios",detail.customer.priceChannelId??"No asignado"]]}/></TabsContent>}
      {detail.supplier&&<TabsContent value="Supplier"><RoleCard title="Proveedor" rows={[["Estado",detail.supplier.isActive?"Activo":"Inactivo"],["Identificador",detail.supplier.supplierId]]}/></TabsContent>}
      {detail.seller&&<TabsContent value="Seller"><RoleCard title="Vendedor" rows={[["Estado",detail.seller.isActive?"Activo":"Inactivo"],["Código",detail.seller.code],["Comisión",detail.seller.defaultCommissionPercent==null?"Sin comisión":detail.seller.defaultCommissionPercent+" %"],["Base",detail.seller.commissionBasis],["Causación",detail.seller.commissionTrigger]]}/></TabsContent>}
      {detail.carrier&&<TabsContent value="Carrier"><RoleCard title="Transportador" rows={[["Estado",detail.carrier.isActive?"Activo":"Inactivo"],["Código",detail.carrier.code],["Modalidad",detail.carrier.transportationMode]]}/></TabsContent>}
    </Tabs>}
    <DialogFooter><Button variant="outline" onClick={onClose}>Cerrar</Button>{detail&&!editing&&detail.roles.length<Object.keys(roleLabels).length&&<Button variant="outline" onClick={()=>onAddRole(detail)}><Plus className="mr-2 h-4 w-4"/>Agregar rol</Button>}{detail&&(!editing?<Button onClick={()=>setEditing(true)}><Pencil className="mr-2 h-4 w-4"/>Editar información</Button>:<><Button variant="ghost" onClick={()=>setEditing(false)}>Cancelar</Button><Button onClick={save} disabled={update.isPending}>{update.isPending?"Guardando...":"Guardar tercero"}</Button></>)}</DialogFooter>
  </div></DialogContent></Dialog>;
}

function RoleCard({title,rows}:{title:string;rows:[string,string][]}){return <section className="rounded-2xl border p-5"><h3 className="text-lg font-semibold">{title}</h3><div className="mt-4 grid gap-4 md:grid-cols-2">{rows.map(([label,value])=><DetailValue key={label} label={label} value={value}/>)}</div></section>}
function DetailValue({label,value}:{label:string;value:string}){return <div><p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{label}</p><p className="mt-1 break-words font-medium">{value}</p></div>}
function FormSectionTitle({title,description}:{title:string;description:string}){return <div className="col-span-full rounded-xl border bg-muted/30 px-4 py-3"><h3 className="font-semibold">{title}</h3><p className="text-sm text-muted-foreground">{description}</p></div>}
function Field({label,children}:{label:string;children:React.ReactNode}){return <div className="space-y-2"><Label>{label}</Label>{children}</div>}
function Summary({icon:Icon,label,value}:{icon:typeof UserRound;label:string;value:string}){return <Card><CardContent className="flex items-center gap-3 p-4"><span className="rounded-xl bg-primary/10 p-2 text-primary"><Icon className="h-5 w-5"/></span><div><p className="text-xs text-muted-foreground">{label}</p><p className="font-semibold">{value}</p></div></CardContent></Card>}