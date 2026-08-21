"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import type { ColumnDef } from "@tanstack/react-table";
import { ArrowRight, BriefcaseBusiness, KeyRound, MapPin, Pencil, Plus, Search, Scissors, Truck, UserRound, UsersRound, X } from "lucide-react";
import { toast } from "sonner";
import { WorkingHoursEditor } from "@/components/settings/working-hours-editor";
import { PartyEmployeeRolePanel, PartySupplierTaxRolePanel, PartyUserRolePanel } from "@/components/parties/party-operational-role-panels";
import { DataTable } from "@/components/tables/data-table";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { SiteLocationFields } from "@/components/parties/site-location-fields";
import { PartySitesSection } from "@/components/parties/party-sites-section";
import { CustomerMapPanel } from "@/components/parties/customer-map-panel";
import { useCities, useCountries, useCreateThirdParty, useCustomerPricingOptions, useDivisions, useParties, usePartyDetail, usePartyIdentity, useSetPartyStatus, useUpdateParty } from "@/hooks/use-parties";
import { useRoles } from "@/hooks/use-roles";
import { useServices } from "@/hooks/use-services";
import { employeesApi, usersApi } from "@/services/api";
import { partiesApi, type CommercialPartyRole, type PartyRole, type PartyWorkspaceDetail, type PartyWorkspaceItem, type SellerUserAccess } from "@/services/api/parties";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import type { WorkingHour } from "@/types/entities";

const roleLabels: Record<PartyRole,string>={Customer:"Cliente",Supplier:"Proveedor",Seller:"Vendedor",Carrier:"Transportador",Employee:"Empleado",User:"Usuario"};
const allRoles: PartyRole[]=["Customer","Supplier","Seller","Carrier","Employee","User"];
const commercialRoles: CommercialPartyRole[]=["Customer","Supplier","Seller","Carrier"];
const roleDescriptions: Record<PartyRole,string>={Customer:"Ventas, precios y cartera",Supplier:"Compras, costos y cuentas por pagar",Seller:"Comisiones, rutas y atención comercial",Carrier:"Despachos y transporte de mercancía",Employee:"Servicios, disponibilidad y calendario",User:"Acceso, contraseña y permisos del sistema"};
const rolePermissions: Record<PartyRole,string>={Customer:"customers.create",Supplier:"suppliers.create",Seller:"sellers.create",Carrier:"carriers.create",Employee:"employees.create",User:"users.create"};
const isCommercialRole=(role?:PartyRole):role is CommercialPartyRole=>Boolean(role&&commercialRoles.includes(role as CommercialPartyRole));
const emptyThirdPartyForm={identificationTypeCode:"CC",identification:"",displayName:"",legalName:"",firstName:"",lastName:"",email:"",phone:"",siteName:"Principal",addressLine:"",neighborhood:"",googleMapsUrl:"",googlePlaceId:"",latitude:"",longitude:"",code:"",commission:"",commissionBasis:"SaleAfterTax",commissionTrigger:"Sale",transportationMode:"Road"};

export default function PartiesPage(){
  const permissions=useAuthStore((s)=>new Set(s.user?.permissions??[]));
  const [page,setPage]=useState(1),[pageSize,setPageSize]=useState(25),[search,setSearch]=useState(""),[view,setView]=useState<"list"|"map">("list");
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
    <div className="grid grid-cols-2 gap-2 rounded-2xl bg-muted p-1 sm:w-72"><button onClick={()=>setView("list")} className={`rounded-xl px-3 py-2 text-sm font-semibold ${view==="list"?"bg-background shadow":"text-muted-foreground"}`}>Listado</button><button onClick={()=>setView("map")} className={`rounded-xl px-3 py-2 text-sm font-semibold ${view==="map"?"bg-background shadow":"text-muted-foreground"}`}>Mapa comercial</button></div>
    {view==="list"&&<><section className="grid gap-3 rounded-2xl border bg-card p-4 xl:grid-cols-[minmax(0,1fr)_14rem_14rem]"><label className="relative"><Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground"/><Input className="pl-9" value={search} onChange={(e)=>{setSearch(e.target.value);setPage(1)}} placeholder="Nombre, documento, correo o teléfono"/></label><Select value={role} onValueChange={(v)=>{setRole(v);setPage(1)}}><SelectTrigger><SelectValue placeholder="Rol"/></SelectTrigger><SelectContent><SelectItem value="all">Todos los roles</SelectItem>{Object.entries(roleLabels).map(([value,label])=><SelectItem key={value} value={value}>{label}</SelectItem>)}</SelectContent></Select><Select value={status} onValueChange={(v)=>{setStatus(v);setPage(1)}}><SelectTrigger><SelectValue placeholder="Estado"/></SelectTrigger><SelectContent><SelectItem value="all">Todos los estados</SelectItem><SelectItem value="active">Activos</SelectItem><SelectItem value="inactive">Inactivos</SelectItem></SelectContent></Select></section>
    <DataTable columns={columns} data={query.data?.items??[]} isLoading={query.isLoading} page={query.data?.page} pageSize={query.data?.pageSize} pageCount={query.data?.totalPages} totalItems={query.data?.totalCount} enableRowSelection={false} onRowClick={(item)=>setDetailTarget({partyId:item.partyId,edit:false})} onPaginationChange={(p,s)=>{setPage(p);setPageSize(s)}}/></>}
    {view==="map"&&<CustomerMapPanel onOpenCustomer={(partyId)=>setDetailTarget({partyId,edit:false})}/>}
    <CreateThirdPartyDialog open={createOpen} permissions={permissions} initialParty={createSeed} onClose={()=>{setCreateOpen(false);setCreateSeed(undefined)}}/><PartyDetailDialog key={detailTarget?.partyId??"none"} target={detailTarget} onClose={()=>setDetailTarget(undefined)} onAddRole={(party)=>{setDetailTarget(undefined);setCreateSeed(party);setCreateOpen(true)}}/>
  </div>;
}

function CreateThirdPartyDialog({open,permissions,initialParty,onClose}:{open:boolean;permissions:Set<string>;initialParty?:PartyWorkspaceDetail;onClose:()=>void}){
  const businessId=useBusinessContextStore((s)=>s.selectedBusinessId); const [selectedRole,setSelectedRole]=useState<PartyRole>();
  const role=selectedRole; const create=useCreateThirdParty(isCommercialRole(role)?role:"Customer"); const pricingOptions=useCustomerPricingOptions(open&&role==="Customer");
  const services=useServices({page:1,pageSize:500}); const roles=useRoles({page:1,pageSize:500});
  const [selectedServiceIds,setSelectedServiceIds]=useState<Set<string>>(new Set()),[serviceToAdd,setServiceToAdd]=useState("");
  const [selectedUserRoleIds,setSelectedUserRoleIds]=useState<Set<string>>(new Set()),[userRoleToAdd,setUserRoleToAdd]=useState("");
  const [username,setUsername]=useState(""),[password,setPassword]=useState(""),[saving,setSaving]=useState(false);
  const [customSchedule,setCustomSchedule]=useState(false),[workingHours,setWorkingHours]=useState<WorkingHour[]>([{dayOfWeek:1,openTime:"08:00",closeTime:"17:00",isActive:true}]);
  const countries=useCountries(); const [partyType,setPartyType]=useState("NaturalPerson"),[country,setCountry]=useState(""),[division,setDivision]=useState(""),[city,setCity]=useState("");
  const divisions=useDivisions(country),cities=useCities(division); const [pricingMode,setPricingMode]=useState("Default"),[pricingId,setPricingId]=useState("");
  const [requiresElectronicInvoice,setRequiresElectronicInvoice]=useState(false);
  const [form,setForm]=useState(emptyThirdPartyForm);
  const [fieldErrors,setFieldErrors]=useState<Record<string,string>>({});
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
    setForm({...emptyThirdPartyForm,identificationTypeCode:initialParty.identificationTypeCode??"",identification:initialParty.identification??"",displayName:initialParty.displayName,legalName:initialParty.legalName??"",firstName:initialParty.firstName??"",lastName:initialParty.lastName??"",email:initialParty.email??"",phone:initialParty.phone??"",siteName:site?.name??"Principal",addressLine:site?.addressLine??"",neighborhood:site?.neighborhood??""});
    setCountry(site?.countryId??initialParty.identificationCountryId??"");
    setDivision(site?.administrativeDivisionId??"");
    setCity(site?.cityId??"");
    setLookupIdentification(initialParty.identification??"");
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
      identificationTypeCode:found.identificationTypeCode??current.identificationTypeCode,
      identification:found.identification??current.identification,
      displayName:found.displayName,
      legalName:found.legalName??"",
      firstName:found.firstName??"",
      lastName:found.lastName??"",
      email:found.email??"",
      phone:found.phone??"",
      siteName:found.primarySite?.name??current.siteName,
      addressLine:found.primarySite?.addressLine??current.addressLine,
      neighborhood:found.primarySite?.neighborhood??"",
      googleMapsUrl:found.primarySite?.googleMapsUrl??"",
      googlePlaceId:found.primarySite?.googlePlaceId??"",
      latitude:found.primarySite?.latitude==null?"":String(found.primarySite.latitude),
      longitude:found.primarySite?.longitude==null?"":String(found.primarySite.longitude),
    }));
    if(found.primarySite){
      setCountry(found.primarySite.countryId);
      setDivision(found.primarySite.administrativeDivisionId);
      setCity(found.primarySite.cityId);
    }else setCountry(found.identificationCountryId??country);
  },[identity.data,role,country,form.identificationTypeCode,lookupIdentification]);
  const set=(key:keyof typeof form,value:string)=>setForm((x)=>({...x,[key]:value}));
  const reset=()=>{setFieldErrors({});setSelectedRole(undefined);setPartyType("NaturalPerson");setCountry("");setDivision("");setCity("");setForm(emptyThirdPartyForm);setLookupIdentification("");hydratedLookup.current="";setPricingMode("Default");setPricingId("");setRequiresElectronicInvoice(false);setSelectedServiceIds(new Set());setServiceToAdd("");setSelectedUserRoleIds(new Set());setUserRoleToAdd("");setUsername("");setPassword("");setSaving(false);setCustomSchedule(false);setWorkingHours([{dayOfWeek:1,openTime:"08:00",closeTime:"17:00",isActive:true}]);};
  const close=()=>{reset();onClose()};
  const submit=async()=>{
    if(!role||!businessId)return;
    if(identity.data?.hasRequestedRole)return toast.error("Esta identidad ya está registrada como "+roleLabels[role].toLowerCase()+" en el negocio.");
    const nextErrors:Record<string,string>={};
    if(!form.identificationTypeCode)nextErrors.identificationTypeCode="Este campo es requerido";
    if(!form.identification.trim())nextErrors.identification="Este campo es requerido";
    if(!form.displayName.trim())nextErrors.displayName="Este campo es requerido";
    if(!country)nextErrors.country="Este campo es requerido";
    if(!division)nextErrors.division="Este campo es requerido";
    if(!city)nextErrors.city="Este campo es requerido";
    if(!form.siteName.trim())nextErrors.siteName="Este campo es requerido";
    if(!form.addressLine.trim())nextErrors.addressLine="Este campo es requerido";
    if(role==="Customer"&&pricingMode!=="Default"&&!pricingId)nextErrors.pricingId="Este campo es requerido";
    if(role==="User"&&!form.email.trim())nextErrors.email="Este campo es requerido";
    if(role==="User"&&!username.trim())nextErrors.username="Este campo es requerido";
    if(role==="User"&&!password.trim())nextErrors.password="Este campo es requerido";
    if(role==="User"&&selectedUserRoleIds.size===0)nextErrors.userRoles="Este campo es requerido";
    setFieldErrors(nextErrors);if(Object.keys(nextErrors).length>0)return;
    const request={operationId:crypto.randomUUID(),businessId,party:{partyType,identificationCountryId:country,identificationTypeCode:form.identificationTypeCode,identification:form.identification,verificationDigit:null,displayName:form.displayName,legalName:partyType==="Organization"?(form.legalName||form.displayName):null,firstName:partyType==="NaturalPerson"?form.firstName:null,lastName:partyType==="NaturalPerson"?form.lastName:null,email:form.email||null,phone:form.phone||null},primarySite:{code:"PRINCIPAL",name:form.siteName,countryId:country,administrativeDivisionId:division,cityId:city,addressLine:form.addressLine,neighborhood:form.neighborhood||null,postalCode:null,email:form.email||null,phone:form.phone||null,isPrimary:true,googleMapsUrl:form.googleMapsUrl||null,googlePlaceId:form.googlePlaceId||null,latitude:form.latitude?Number(form.latitude):null,longitude:form.longitude?Number(form.longitude):null},pricing:role==="Customer"?{priceListId:pricingMode==="List"?pricingId:null,priceChannelId:pricingMode==="Channel"?pricingId:null}:undefined,requiresElectronicInvoice:role==="Customer"?requiresElectronicInvoice:undefined,code:form.code,defaultCommissionPercent:form.commission?Number(form.commission):null,commissionBasis:form.commissionBasis,commissionTrigger:form.commissionTrigger,transportationMode:form.transportationMode};
    setSaving(true);
    try{
      if(isCommercialRole(role)) await create.mutateAsync(request);
      else{
        let partyId=initialParty?.partyId??identity.data?.party?.partyId;
        if(!partyId){const accepted=await partiesApi.createIdentity({...request,targetRole:role});partyId=accepted.partyId;}
        if(role==="Employee"){const employee=await employeesApi.create({businessId,name:form.displayName.trim(),partyId,serviceIds:[...selectedServiceIds]});if(customSchedule)await employeesApi.updateWorkingHours(employee.employeeId,workingHours);}
        else{
          const nameParts=form.displayName.trim().split(/\s+/);const firstName=form.firstName.trim()||nameParts.shift()||form.displayName.trim();const lastName=form.lastName.trim()||nameParts.join(" ")||"-";
          const created=await usersApi.create({firstName,lastName,email:form.email.trim(),username:username.trim(),password,phoneNumber:form.phone.trim()||null,partyId} as never);
          await Promise.all([...selectedUserRoleIds].map((roleId)=>usersApi.assignRole(created.userId,{roleId,businessId})));
        }
      }
      toast.success(`${roleLabels[role]} creado y disponible en el listado`);close();
    }catch(error){toast.error(error instanceof Error?error.message:"No fue posible crear el tercero.");}
    finally{setSaving(false);}
  };
  return <Dialog open={open} onOpenChange={(v)=>!v&&close()}><DialogContent className="max-h-[94vh] max-w-6xl overflow-hidden p-0"><div className="grid max-h-[94vh] lg:grid-cols-[250px_minmax(0,1fr)]"><aside className="hidden bg-gradient-to-b from-slate-950 to-teal-950 p-6 text-white lg:block"><p className="text-xs font-bold uppercase tracking-[.18em] text-teal-300">Terceros de Auraly</p><h2 className="mt-2 text-2xl font-semibold">Una identidad, varios roles</h2><p className="mt-3 text-sm text-slate-300">Los datos generales se comparten. Cada tipo de tercero conserva su configuración específica.</p><ol className="mt-8 space-y-4 text-sm"><li className="rounded-xl bg-white/10 p-3">1. Identidad</li><li className="rounded-xl bg-white/10 p-3">2. Ubicación</li><li className="rounded-xl bg-white/10 p-3">3. Configuración del tipo</li></ol></aside><div className="max-h-[94vh] overflow-y-auto p-6"><DialogHeader><DialogTitle>{role?`${initialParty?"Agregar":"Nuevo"} ${roleLabels[role].toLowerCase()}`:initialParty?`Agregar rol a ${initialParty.displayName}`:"Nuevo tercero"}</DialogTitle><DialogDescription>{role?"Completa los datos comunes y la configuración específica del rol.":"Selecciona un solo rol. Después podrás agregar otros roles a la misma identidad."}</DialogDescription></DialogHeader>
    {!role?<div className="mt-6 space-y-5">
      <div className="overflow-hidden rounded-2xl bg-gradient-to-r from-slate-950 via-slate-900 to-teal-950 p-5 text-white">
        <p className="text-xs font-bold uppercase tracking-[.18em] text-teal-300">Punto de partida</p>
        <h3 className="mt-2 text-xl font-semibold">¿Qué relación tendrá con el negocio?</h3>
        <p className="mt-1 max-w-2xl text-sm text-slate-300">Elige un rol para mostrar únicamente los datos que realmente necesita. Después podrás sumar otros roles sin duplicar la identidad.</p>
      </div>
      <div className="grid gap-4 sm:grid-cols-2">{allRoles.map((item)=>{
        const disabled=!permissions.has(rolePermissions[item])||Boolean(initialParty?.roles.includes(item));
        const Icon=item==="Carrier"?Truck:item==="Customer"?UserRound:item==="Employee"?Scissors:item==="User"?KeyRound:BriefcaseBusiness;
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
      <Field label="Tipo de identificación" error={fieldErrors.identificationTypeCode}><Select value={form.identificationTypeCode} onValueChange={(v)=>set("identificationTypeCode",v)}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent><SelectItem value="CC">Cédula</SelectItem><SelectItem value="NIT">NIT</SelectItem><SelectItem value="CE">Cédula de extranjería</SelectItem><SelectItem value="PP">Pasaporte</SelectItem></SelectContent></Select></Field>
      <Field label="Identificación" error={fieldErrors.identification}><Input aria-invalid={Boolean(fieldErrors.identification)} value={form.identification} onChange={(e)=>set("identification",e.target.value)} onBlur={()=>setLookupIdentification(form.identification.trim())}/></Field><Field label="Nombre visible" error={fieldErrors.displayName}><Input aria-invalid={Boolean(fieldErrors.displayName)} value={form.displayName} onChange={(e)=>set("displayName",e.target.value)}/></Field>
      {partyType==="Organization"?<Field label="Razón social"><Input value={form.legalName} onChange={(e)=>set("legalName",e.target.value)}/></Field>:<><Field label="Nombres"><Input value={form.firstName} onChange={(e)=>set("firstName",e.target.value)}/></Field><Field label="Apellidos"><Input value={form.lastName} onChange={(e)=>set("lastName",e.target.value)}/></Field></>}
      <Field label="Teléfono"><Input value={form.phone} onChange={(e)=>set("phone",e.target.value)}/></Field><Field label="Correo" error={fieldErrors.email}><Input type="email" aria-invalid={Boolean(fieldErrors.email)} value={form.email} onChange={(e)=>set("email",e.target.value)}/></Field>
      <FormSectionTitle title="Ubicación principal" description="País, departamento y ciudad se seleccionan; el barrio se escribe libremente." />      <Field label="País" error={fieldErrors.country}><Select value={country} onValueChange={(v)=>{setCountry(v);setDivision("");setCity("")}} disabled={countries.isLoading}><SelectTrigger><SelectValue placeholder={countries.isLoading?"Cargando...":"Selecciona"}/></SelectTrigger><SelectContent>{countries.data?.filter(x=>x.isActive).map(x=><SelectItem key={x.countryId} value={x.countryId}>{x.name}</SelectItem>)}</SelectContent></Select></Field>
      <Field label="Departamento" error={fieldErrors.division}><Select value={division} onValueChange={(v)=>{setDivision(v);setCity("")}} disabled={!country||divisions.isLoading}><SelectTrigger><SelectValue placeholder={!country?"Selecciona primero el país":divisions.isLoading?"Cargando...":"Selecciona"}/></SelectTrigger><SelectContent>{divisions.data?.filter(x=>x.isActive).map(x=><SelectItem key={x.administrativeDivisionId} value={x.administrativeDivisionId}>{x.name}</SelectItem>)}</SelectContent></Select></Field>
      <Field label="Ciudad" error={fieldErrors.city}><Select value={city} onValueChange={setCity} disabled={!division||cities.isLoading}><SelectTrigger><SelectValue placeholder={!division?"Selecciona primero el departamento":cities.isLoading?"Cargando...":"Selecciona"}/></SelectTrigger><SelectContent>{cities.data?.filter(x=>x.isActive).map(x=><SelectItem key={x.cityId} value={x.cityId}>{x.name}</SelectItem>)}</SelectContent></Select></Field>
      <Field label="Nombre de la sede" error={fieldErrors.siteName}><Input aria-invalid={Boolean(fieldErrors.siteName)} value={form.siteName} onChange={(e)=>set("siteName",e.target.value)}/></Field><Field label="Dirección" error={fieldErrors.addressLine}><Input aria-invalid={Boolean(fieldErrors.addressLine)} value={form.addressLine} onChange={(e)=>set("addressLine",e.target.value)}/></Field><Field label="Barrio"><Input value={form.neighborhood} onChange={(e)=>set("neighborhood",e.target.value)}/></Field><SiteLocationFields value={{googleMapsUrl:form.googleMapsUrl,googlePlaceId:form.googlePlaceId,latitude:form.latitude,longitude:form.longitude}} onChange={location=>setForm(current=>({...current,...location}))}/>
      <FormSectionTitle title={`Configuración de ${roleLabels[role].toLowerCase()}`} description={roleDescriptions[role]} />{role==="Customer"&&<><Field label="Precio asignado"><Select value={pricingMode} onValueChange={(v)=>{setPricingMode(v);setPricingId("")}}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent><SelectItem value="Default">Precio predeterminado del negocio</SelectItem><SelectItem value="List">Lista de precios</SelectItem><SelectItem value="Channel">Canal de precios</SelectItem></SelectContent></Select></Field>{pricingMode!=="Default"&&<Field label={pricingMode==="List"?"Lista":"Canal"} error={fieldErrors.pricingId}><Select value={pricingId} onValueChange={setPricingId} disabled={pricingOptions.isLoading}><SelectTrigger><SelectValue placeholder={pricingOptions.isLoading?"Cargando...":"Selecciona"}/></SelectTrigger><SelectContent>{(pricingMode==="List"?pricingOptions.data?.priceLists:pricingOptions.data?.priceChannels)?.map(x=><SelectItem key={x.id} value={x.id}>{x.name} ({x.code})</SelectItem>)}</SelectContent></Select></Field>}<label className="col-span-full flex items-center justify-between rounded-xl border p-4"><span><b className="block text-sm">Siempre emitir factura electrónica</b><small className="text-muted-foreground">Al seleccionar este cliente en caja, el tipo de documento queda fijado en factura electrónica.</small></span><Switch checked={requiresElectronicInvoice} onCheckedChange={setRequiresElectronicInvoice}/></label></>}
      {role==="Seller"&&<><Field label="Comisión %"><Input type="number" min="0" max="100" value={form.commission} onChange={(e)=>set("commission",e.target.value)}/></Field><Field label="Base de comisión"><Select value={form.commissionBasis} onValueChange={(v)=>set("commissionBasis",v)}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent><SelectItem value="SaleBeforeTax">Venta antes de IVA</SelectItem><SelectItem value="SaleAfterTax">Venta después de IVA</SelectItem><SelectItem value="GrossMargin">Margen bruto</SelectItem></SelectContent></Select></Field><Field label="Causación"><Select value={form.commissionTrigger} onValueChange={(v)=>set("commissionTrigger",v)}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent><SelectItem value="Sale">Al vender</SelectItem><SelectItem value="Collection">Al recaudar</SelectItem></SelectContent></Select></Field></>}
      {role==="Carrier"&&<Field label="Modalidad de transporte"><Select value={form.transportationMode} onValueChange={(v)=>set("transportationMode",v)}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent><SelectItem value="Road">Terrestre</SelectItem><SelectItem value="Air">Aérea</SelectItem><SelectItem value="Maritime">Marítima</SelectItem><SelectItem value="Other">Otra</SelectItem></SelectContent></Select></Field>}
      {role==="Employee"&&<div className="col-span-full space-y-3"><Select value={serviceToAdd} onValueChange={(value)=>{setServiceToAdd("");setSelectedServiceIds((current)=>new Set(current).add(value))}}><SelectTrigger><SelectValue placeholder={services.isLoading?"Cargando servicios...":"Agregar servicio"}/></SelectTrigger><SelectContent>{(services.data?.items??[]).filter((service)=>!selectedServiceIds.has(service.serviceId)).map((service)=><SelectItem key={service.serviceId} value={service.serviceId}>{service.serviceName}</SelectItem>)}</SelectContent></Select><div className="grid gap-3 sm:grid-cols-2">{[...selectedServiceIds].map((serviceId)=>{const service=services.data?.items.find((item)=>item.serviceId===serviceId);return <div key={serviceId} className="flex items-center justify-between rounded-xl border bg-card p-4"><div><p className="font-medium">{service?.serviceName??"Servicio"}</p><p className="text-xs text-muted-foreground">Asignado al empleado</p></div><Button type="button" variant="ghost" size="icon" onClick={()=>setSelectedServiceIds((current)=>{const next=new Set(current);next.delete(serviceId);return next})}><X className="h-4 w-4"/></Button></div>})}{selectedServiceIds.size===0&&<div className="col-span-full rounded-xl border border-dashed p-5 text-center text-sm text-muted-foreground">Sin datos. Puedes crear el empleado sin servicios y asignarlos después.</div>}</div><div className="space-y-4 rounded-xl border bg-muted/30 p-4"><div className="flex items-center justify-between gap-4"><div><strong className="text-sm">Horario personalizado</strong><p className="text-xs text-muted-foreground">Desactivado usa automáticamente el calendario activo del negocio.</p></div><Switch checked={customSchedule} onCheckedChange={setCustomSchedule}/></div>{customSchedule&&<WorkingHoursEditor value={workingHours} onChange={setWorkingHours}/>}</div></div>}
      {role==="User"&&<><Field label="Nombre de usuario" error={fieldErrors.username}><Input aria-invalid={Boolean(fieldErrors.username)} value={username} onChange={(event)=>setUsername(event.target.value)} autoComplete="off"/></Field><Field label="Contraseña de acceso y modo sin conexión POS" error={fieldErrors.password}><Input type="password" aria-invalid={Boolean(fieldErrors.password)} value={password} onChange={(event)=>setPassword(event.target.value)} autoComplete="new-password"/></Field><div className="col-span-full space-y-3"><Field label="Roles de acceso" error={fieldErrors.userRoles}><Select value={userRoleToAdd} onValueChange={(value)=>{setUserRoleToAdd("");setSelectedUserRoleIds((current)=>new Set(current).add(value))}}><SelectTrigger><SelectValue placeholder={roles.isLoading?"Cargando roles...":"Agregar rol de acceso"}/></SelectTrigger><SelectContent>{(roles.data?.items??[]).filter((item)=>item.isActive&&!selectedUserRoleIds.has(item.roleId)).map((item)=><SelectItem key={item.roleId} value={item.roleId}>{item.name}</SelectItem>)}</SelectContent></Select></Field><div className="grid gap-3 sm:grid-cols-2">{[...selectedUserRoleIds].map((roleId)=>{const assigned=roles.data?.items.find((item)=>item.roleId===roleId);return <div key={roleId} className="flex items-center justify-between rounded-xl border bg-card p-4"><div><p className="font-medium">{assigned?.name??"Rol"}</p><p className="text-xs text-muted-foreground">{assigned?.description??"Permisos asignados"}</p></div><Button type="button" variant="ghost" size="icon" onClick={()=>setSelectedUserRoleIds((current)=>{const next=new Set(current);next.delete(roleId);return next})}><X className="h-4 w-4"/></Button></div>})}{selectedUserRoleIds.size===0&&<div className="col-span-full rounded-xl border border-dashed p-5 text-center text-sm text-muted-foreground">Sin datos. Agrega al menos un rol para habilitar el acceso.</div>}</div></div></>}
    </div></div>}
    <DialogFooter className="mt-6 border-t pt-4"><Button variant="outline" onClick={close}>Cancelar</Button>{role&&<Button onClick={submit} disabled={saving||identity.isFetching||identity.data?.hasRequestedRole}>{saving?"Guardando...":"Guardar tercero"}</Button>}</DialogFooter></div></div></DialogContent></Dialog>;
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
        <PartySitesSection detail={detail}/>
      </TabsContent>
      {detail.customer&&<TabsContent value="Customer" className="space-y-4"><RoleCard title="Cliente" rows={[["Estado",detail.customer.isActive?"Activo":"Inactivo"],["Lista de precios",detail.customer.priceListId??"Precio público"],["Canal de precios",detail.customer.priceChannelId??"No asignado"]]}/><CustomerBillingCard detail={detail} onSaved={()=>detailQuery.refetch()}/></TabsContent>}
      {detail.supplier&&<TabsContent value="Supplier" className="space-y-4"><RoleCard title="Proveedor" rows={[["Estado",detail.supplier.isActive?"Activo":"Inactivo"],["Identificador",detail.supplier.supplierId]]}/><PartySupplierTaxRolePanel supplierId={detail.supplier.supplierId}/></TabsContent>}
      {detail.seller&&<TabsContent value="Seller" className="space-y-4"><RoleCard title="Vendedor" rows={[["Estado",detail.seller.isActive?"Activo":"Inactivo"],["Código",detail.seller.code],["Comisión",detail.seller.defaultCommissionPercent==null?"Sin comisión":detail.seller.defaultCommissionPercent+" %"],["Base",detail.seller.commissionBasis],["Causación",detail.seller.commissionTrigger]]}/><SellerAccessCard detail={detail}/></TabsContent>}
      {detail.carrier&&<TabsContent value="Carrier"><RoleCard title="Transportador" rows={[["Estado",detail.carrier.isActive?"Activo":"Inactivo"],["Código",detail.carrier.code],["Modalidad",detail.carrier.transportationMode]]}/></TabsContent>}
      {detail.employee&&<TabsContent value="Employee"><PartyEmployeeRolePanel employeeId={detail.employee.employeeId}/></TabsContent>}
      {detail.user&&<TabsContent value="User"><PartyUserRolePanel userId={detail.user.userId}/></TabsContent>}
    </Tabs>}
    <DialogFooter><Button variant="outline" onClick={onClose}>Cerrar</Button>{detail&&!editing&&detail.roles.length<Object.keys(roleLabels).length&&<Button variant="outline" onClick={()=>onAddRole(detail)}><Plus className="mr-2 h-4 w-4"/>Agregar rol</Button>}{detail&&(!editing?<Button onClick={()=>setEditing(true)}><Pencil className="mr-2 h-4 w-4"/>Editar información</Button>:<><Button variant="ghost" onClick={()=>setEditing(false)}>Cancelar</Button><Button onClick={save} disabled={update.isPending}>{update.isPending?"Guardando...":"Guardar tercero"}</Button></>)}</DialogFooter>
  </div></DialogContent></Dialog>;
}

function CustomerBillingCard({detail,onSaved}:{detail:PartyWorkspaceDetail;onSaved:()=>Promise<unknown>}){
  const canManage=useAuthStore(state=>state.user?.permissions.includes("parties.update")??false);
  const [saving,setSaving]=useState(false);
  const required=detail.customer?.requiresElectronicInvoice??false;
  const change=async(value:boolean)=>{setSaving(true);try{await partiesApi.saveCustomerBilling(detail.partyId,value);await onSaved();toast.success("Preferencia de facturación actualizada y enviada al POS.")}catch(error){toast.error(error instanceof Error?error.message:"No fue posible actualizar la facturación del cliente.")}finally{setSaving(false)}};
  return <section className="rounded-2xl border border-primary/20 bg-primary/5 p-5"><div className="flex items-center justify-between gap-4"><div><h3 className="font-semibold">Siempre emitir factura electrónica</h3><p className="mt-1 text-sm text-muted-foreground">Al seleccionar este cliente en caja, el documento cambia automáticamente a factura electrónica.</p></div><Switch checked={required} onCheckedChange={value=>void change(value)} disabled={!canManage||saving}/></div></section>;
}

function SellerAccessCard({detail}:{detail:PartyWorkspaceDetail}){
  const permissions=useAuthStore(state=>new Set(state.user?.permissions??[]));
  const canManage=["users.create","users.assign_role","security.users.link-party"].every(permission=>permissions.has(permission));
  const [account,setAccount]=useState<SellerUserAccess|null>(),[open,setOpen]=useState(false),[saving,setSaving]=useState(false);
  const [username,setUsername]=useState(detail.email?.split("@")[0]??""),[email,setEmail]=useState(detail.email??""),[password,setPassword]=useState(""),[confirmation,setConfirmation]=useState(""),[firstName,setFirstName]=useState(detail.firstName??""),[lastName,setLastName]=useState(detail.lastName??""),[phone,setPhone]=useState(detail.phone??"");
  useEffect(()=>{if(!canManage)return;let active=true;void partiesApi.sellerAccess(detail.partyId).then(value=>active&&setAccount(value)).catch(()=>active&&setAccount(null));return()=>{active=false}},[canManage,detail.partyId]);
  if(!canManage)return null;
  const create=async()=>{if(password!==confirmation){toast.error("Las contraseñas no coinciden.");return}setSaving(true);try{const created=await partiesApi.createSellerAccess(detail.partyId,{username,email,password,firstName,lastName,phoneNumber:phone||null});setAccount(created);setOpen(false);setPassword("");setConfirmation("");toast.success("Acceso de vendedor creado con su rol y permisos.")}catch(error){toast.error(error instanceof Error?error.message:"No fue posible crear el acceso.")}finally{setSaving(false)}};
  return <section className="rounded-2xl border border-teal-200 bg-teal-50/30 p-5"><div className="flex flex-col justify-between gap-3 sm:flex-row sm:items-center"><div><h3 className="font-semibold">Acceso a la aplicación</h3>{account===undefined?<p className="text-sm text-muted-foreground">Consultando acceso…</p>:account?<p className="text-sm text-muted-foreground">{account.username} · rol {account.roleName} · {account.isActive?"activo":"inactivo"}</p>:<p className="text-sm text-muted-foreground">La ficha comercial existe, pero todavía no tiene usuario para iniciar sesión.</p>}</div>{account===null&&<Button onClick={()=>setOpen(true)}>Crear acceso</Button>}</div>{open&&<Dialog open onOpenChange={value=>!value&&setOpen(false)}><DialogContent className="w-[calc(100%-1.5rem)] rounded-3xl sm:max-w-lg"><DialogHeader><DialogTitle>Acceso del vendedor</DialogTitle><DialogDescription>Se enlaza con {detail.displayName} y recibe automáticamente el rol Vendedor.</DialogDescription></DialogHeader><div className="grid gap-3 sm:grid-cols-2"><Field label="Usuario"><Input value={username} onChange={event=>setUsername(event.target.value)} autoComplete="username"/></Field><Field label="Correo"><Input type="email" value={email} onChange={event=>setEmail(event.target.value)} autoComplete="email"/></Field><Field label="Nombres"><Input value={firstName} onChange={event=>setFirstName(event.target.value)}/></Field><Field label="Apellidos"><Input value={lastName} onChange={event=>setLastName(event.target.value)}/></Field><Field label="Contraseña"><Input type="password" value={password} onChange={event=>setPassword(event.target.value)} autoComplete="new-password"/></Field><Field label="Confirmar contraseña"><Input type="password" value={confirmation} onChange={event=>setConfirmation(event.target.value)} autoComplete="new-password"/></Field><div className="sm:col-span-2"><Field label="Teléfono"><Input value={phone} onChange={event=>setPhone(event.target.value)}/></Field></div></div><DialogFooter><Button variant="outline" onClick={()=>setOpen(false)}>Cancelar</Button><Button disabled={saving||!username.trim()||!email.trim()||!firstName.trim()||!lastName.trim()||password.length<8||password!==confirmation} onClick={create}>{saving?"Creando…":"Crear acceso"}</Button></DialogFooter></DialogContent></Dialog>}</section>;
}

function RoleCard({title,rows}:{title:string;rows:[string,string][]}){return <section className="rounded-2xl border p-5"><h3 className="text-lg font-semibold">{title}</h3><div className="mt-4 grid gap-4 md:grid-cols-2">{rows.map(([label,value])=><DetailValue key={label} label={label} value={value}/>)}</div></section>}
function DetailValue({label,value}:{label:string;value:string}){return <div><p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{label}</p><p className="mt-1 break-words font-medium">{value}</p></div>}
function FormSectionTitle({title,description}:{title:string;description:string}){return <div className="col-span-full rounded-xl border bg-muted/30 px-4 py-3"><h3 className="font-semibold">{title}</h3><p className="text-sm text-muted-foreground">{description}</p></div>}
function Field({label,children,error}:{label:string;children:React.ReactNode;error?:string}){return <div className={`space-y-2 rounded-lg ${error?"ring-1 ring-destructive/40 p-2 [&_input]:border-destructive [&_button]:border-destructive":""}`}><Label>{label}{error&&<span className="text-destructive"> *</span>}</Label>{children}{error&&<p className="text-sm text-destructive">{error}</p>}</div>}
function Summary({icon:Icon,label,value}:{icon:typeof UserRound;label:string;value:string}){return <Card><CardContent className="flex items-center gap-3 p-4"><span className="rounded-xl bg-primary/10 p-2 text-primary"><Icon className="h-5 w-5"/></span><div><p className="text-xs text-muted-foreground">{label}</p><p className="font-semibold">{value}</p></div></CardContent></Card>}
