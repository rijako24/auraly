"use client";
import { ArrowLeft, Building2, CheckCircle2, Download, FileKey2, Loader2, MonitorSmartphone, Receipt, Warehouse } from "lucide-react";
import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";
import { FiscalResolutionForm } from "@/components/fiscal/fiscal-resolution-form";
import { InvoiceNumberingCard } from "@/components/fiscal/invoice-numbering-card";
import { fiscalConfigurationApi, type FiscalResolutionConfiguration, type SalesInvoiceNumberingConfiguration, type SaveFiscalResolutionConfiguration } from "@/services/api/fiscal-configuration";
import type { SalesWorkspaceOption } from "@/services/pos/online-pos-client";
import type { PosSaleDocumentType } from "@/services/pos/pos-edge-client";
import { loadPosInstaller, type PosInstaller } from "@/services/pos/pos-installer";

import { resolvePosWorkspaceSelection } from "@/services/pos/pos-workspace-selection";

type Props={options:SalesWorkspaceOption[];loading:boolean;error:string|null;tenantName:string;userDisplayName:string;onSelect:(option:SalesWorkspaceOption,documentType:PosSaleDocumentType)=>Promise<void>;onCancel?:()=>void;edgeCapable?:boolean;canEnrollOffline?:boolean;onEnroll?:(option:SalesWorkspaceOption,documentType:PosSaleDocumentType)=>Promise<void>};

export function PosOnlineSetup({options,loading,error,tenantName,userDisplayName,onSelect,onCancel,edgeCapable=false,canEnrollOffline=false,onEnroll}:Props){
 const businesses=useMemo(()=>Array.from(new Map(options.map(o=>[o.businessId,o.businessName]))),[options]);
 const [businessId,setBusinessId]=useState(""); const [warehouseId,setWarehouseId]=useState("");
 const [busy,setBusy]=useState<"online"|"offline"|null>(null); const [fiscal,setFiscal]=useState<FiscalResolutionConfiguration|null>(null); const [fiscalDraft,setFiscalDraft]=useState<SaveFiscalResolutionConfiguration|null>(null); const [numbering,setNumbering]=useState<SalesInvoiceNumberingConfiguration|null>(null); const [initialConsecutive,setInitialConsecutive]=useState(1); const [fiscalLoading,setFiscalLoading]=useState(false); const [fiscalSaving,setFiscalSaving]=useState(false); const [fiscalError,setFiscalError]=useState<string|null>(null);
 const [documentType,setDocumentType]=useState<PosSaleDocumentType|"">("");
 const [installer,setInstaller]=useState<PosInstaller|null>(null); const [installerError,setInstallerError]=useState<string|null>(null);
 const warehouses=useMemo(()=>options.filter(o=>o.businessId===businessId),[businessId,options]);
 const selected=useMemo(()=>warehouses.find(o=>o.warehouseId===warehouseId),[warehouseId,warehouses]);
 useEffect(()=>{const value=resolvePosWorkspaceSelection(options,businessId,warehouseId);if(value.businessId!==businessId)setBusinessId(value.businessId);if(value.warehouseId!==warehouseId)setWarehouseId(value.warehouseId)},[options,businessId,warehouseId]);
 useEffect(()=>{if(!selected){setFiscal(null);return}let active=true;setFiscalLoading(true);setFiscalError(null);fiscalConfigurationApi.get(selected.businessId).then(v=>active&&setFiscal(v)).catch((e:any)=>active&&setFiscalError(e?.message||"No fue posible verificar la resolución fiscal.")).finally(()=>active&&setFiscalLoading(false));return()=>{active=false}},[selected]);
 const saveFiscal=async(request:SaveFiscalResolutionConfiguration)=>{if(!selected)return;setFiscalSaving(true);setFiscalError(null);try{setFiscal(await fiscalConfigurationApi.save(selected.businessId,request))}catch(e:unknown){const message=e instanceof Error?e.message:"No fue posible guardar la resolución.";setFiscalError(message);throw e}finally{setFiscalSaving(false)}};
 const captureNumbering=useCallback((value:SalesInvoiceNumberingConfiguration,initial:number)=>{setNumbering(value);setInitialConsecutive(initial)},[]);
 const captureFiscal=useCallback((request:SaveFiscalResolutionConfiguration)=>setFiscalDraft(request),[]);
 useEffect(()=>{if(edgeCapable||!canEnrollOffline)return;let active=true;loadPosInstaller().then(v=>active&&setInstaller(v)).catch((e:unknown)=>active&&setInstallerError(e instanceof Error?e.message:"No fue posible consultar el instalador."));return()=>{active=false}},[edgeCapable,canEnrollOffline]);
 const choose=async()=>{
  if(!selected)return;
  const mode=edgeCapable?"offline":"online";
  setBusy(mode);
  setFiscalError(null);
  try{
   if(documentType==="SalesReceipt"){
    if(edgeCapable)await onEnroll?.(selected,documentType);else await onSelect(selected,documentType);
    return;
   }
   if(documentType!=="SalesInvoice")return;
   if(!numbering){setFiscalError("Espera mientras Auraly carga la numeración de la sede.");return}
   if(numbering.canSetInitialConsecutive){
    if(initialConsecutive<1){setFiscalError("La primera factura debe ser un número mayor que cero.");return}
    const savedNumbering=await fiscalConfigurationApi.saveNumbering(selected.businessId,initialConsecutive);
    setNumbering(savedNumbering);
   }
   if(!fiscal?.isReadyForOnlineSales){
    if(!fiscalDraft){setFiscalError("Completa los campos requeridos de la resolución fiscal.");return}
    await saveFiscal(fiscalDraft);
   }
   const latestFiscal=await fiscalConfigurationApi.get(selected.businessId);
   setFiscal(latestFiscal);
   const ready=edgeCapable?latestFiscal.isReadyForEnrollment:latestFiscal.isReadyForOnlineSales;
   if(!ready){setFiscalError("Completa los campos requeridos de numeración y resolución fiscal.");return}
   if(edgeCapable)await onEnroll?.(selected,documentType);else await onSelect(selected,documentType);
  }catch(e:unknown){
   setFiscalError(e instanceof Error?e.message:"No fue posible cargar la configuración de esta ubicación.");
  }finally{setBusy(null)}
 };
 if(selected&&!documentType)return <DocumentChoice tenantName={tenantName} selected={selected} installer={installer} installerError={installerError} canInstall={!edgeCapable&&canEnrollOffline} onSelect={setDocumentType}/>;
 if(selected&&documentType==="SalesReceipt")return <ReceiptSetup tenantName={tenantName} selected={selected} busy={busy!==null} edgeCapable={edgeCapable} canEnrollOffline={canEnrollOffline} installer={installer} installerError={installerError} onBack={()=>setDocumentType("")} onStart={()=>void choose()}/>;
 return <main className="relative min-h-screen overflow-auto bg-[#071a1d] p-5 text-white">{onCancel?<button type="button" onClick={onCancel} className="fixed left-5 top-5 z-20 inline-flex h-10 items-center gap-2 rounded-xl border border-white/15 bg-[#0b2428] px-3 text-sm font-semibold"><ArrowLeft className="h-4 w-4"/>Volver al punto de venta</button>:<Link href="/dashboard" className="fixed left-5 top-5 z-20 inline-flex h-10 items-center gap-2 rounded-xl border border-white/15 bg-[#0b2428] px-3 text-sm font-semibold"><ArrowLeft className="h-4 w-4"/>Volver al panel</Link>}<div className="mx-auto flex min-h-[calc(100vh-2.5rem)] max-w-5xl items-center py-16"><section className="grid w-full overflow-hidden rounded-[2rem] border border-white/10 bg-[#0b2428] shadow-2xl md:grid-cols-[.75fr_1.45fr]"><aside className="bg-gradient-to-br from-teal-400/20 to-transparent p-8"><span className="grid h-12 w-12 place-items-center rounded-2xl bg-teal-300 text-[#071a1d]"><MonitorSmartphone/></span><p className="mt-8 text-xs font-bold uppercase tracking-[.15em] text-teal-200">{tenantName||"Auraly"}</p><h1 className="mt-3 text-3xl font-black">Prepara tu espacio de venta</h1><p className="mt-4 text-sm leading-6 text-slate-300">Hola, {userDisplayName}. Primero confirma la sede y la bodega. Auraly verificará después la resolución fiscal y solo pedirá lo que falte.</p><ol className="mt-8 space-y-3 text-sm"><Step number="1" text="Sede y bodega" active/><Step number="2" text="Numeración" active={!!selected}/><Step number="3" text="Resolución fiscal" active={!!selected}/><Step number="4" text="Acceso a ventas" active={!!fiscal?.isReadyForOnlineSales}/></ol></aside><div className="p-7 md:p-9">{loading?<Loading/>:<div className="space-y-5"><OptionGroup title="Sede" icon={Building2} items={businesses.map(([id,name])=>({id,name}))} selected={businessId} onSelect={id=>{setBusinessId(id);setWarehouseId("")}}/><OptionGroup title="Bodega" icon={Warehouse} items={warehouses.map(o=>({id:o.warehouseId,name:`${o.warehouseName} · ${o.warehouseCode}`}))} selected={warehouseId} onSelect={setWarehouseId} disabled={!businessId}/>{selected&&<div className="rounded-xl border border-teal-300/20 bg-teal-300/10 p-3 text-sm"><b>{selected.businessName}</b> · {selected.warehouseName}<p className="mt-1 text-xs text-slate-300">{selected.warehouseAllowsNegativeStockSales?"Permite inventario negativo":"Valida disponibilidad al capturar cantidades"}</p></div>}{selected&&<InvoiceNumberingCard key={selected.businessId} businessId={selected.businessId} onStateChange={captureNumbering} showSaveButton={false}/>}{selected&&fiscalLoading&&<Loading text="Verificando resolución fiscal…"/>}{selected&&!fiscalLoading&&fiscal&&!fiscal.isReadyForOnlineSales&&<div className="rounded-2xl border border-amber-300/25 bg-white p-5 text-slate-950"><div className="mb-5 flex gap-3"><span className="rounded-xl bg-amber-100 p-2 text-amber-700"><FileKey2/></span><div><p className="font-bold">Completa la resolución de esta sede</p><p className="text-sm text-slate-600">Se guarda para futuras aperturas y también podrás editarla desde Maestros.</p></div></div><FiscalResolutionForm value={fiscal} saving={fiscalSaving} onSave={saveFiscal} onChange={captureFiscal} showSaveButton={false}/></div>}{fiscal?.isReadyForOnlineSales&&<div className="flex items-center gap-2 rounded-xl border border-emerald-300/20 bg-emerald-300/10 p-3 text-sm text-emerald-100"><CheckCircle2 className="h-5 w-5"/>Resolución {fiscal.authorizationNumber} lista.</div>}{(error||fiscalError)&&<p className="rounded-xl border border-red-300/20 bg-red-400/10 p-3 text-sm text-red-100">{error||fiscalError} {fiscalError&&<Link href="/dashboard/settings/masters" className="font-bold underline">Abrir Maestros</Link>}</p>}{!options.length&&!error&&<p className="rounded-xl border border-amber-300/20 bg-amber-300/10 p-4 text-sm text-amber-100">No hay sedes con una bodega activa para este usuario. Configura la organización o revisa sus permisos.</p>}<button onClick={()=>void choose()} disabled={!selected||!numbering||fiscalLoading||!!busy||(edgeCapable&&!canEnrollOffline)} className="h-12 w-full rounded-xl bg-teal-300 font-bold text-[#071a1d] disabled:opacity-35">{busy?(edgeCapable?"Preparando caja…":"Abriendo…"):(edgeCapable?(canEnrollOffline?"Activar caja offline":"Sin permiso de enrolamiento"):"Continuar en línea")}</button></div>}</div></section></div></main>;
}
function Loading({text="Cargando sedes y bodegas…"}:{text?:string}){return <div className="flex min-h-24 items-center justify-center gap-3 text-sm text-slate-300"><Loader2 className="h-6 w-6 animate-spin text-teal-300"/>{text}</div>}
function Step({number,text,active}:{number:string;text:string;active:boolean}){return <li className={`flex items-center gap-3 ${active?"text-white":"text-slate-500"}`}><span className={`grid h-7 w-7 place-items-center rounded-full ${active?"bg-teal-300 text-[#071a1d]":"bg-white/10"}`}>{number}</span>{text}</li>}
function OptionGroup({title,icon:Icon,items,selected,onSelect,disabled=false}:{title:string;icon:typeof Building2;items:{id:string;name:string}[];selected:string;onSelect:(id:string)=>void;disabled?:boolean}){return <fieldset disabled={disabled}><legend className="mb-2 flex items-center gap-2 text-sm font-semibold"><Icon className="h-4 w-4 text-teal-200"/>{title}</legend>{items.length===1?<button type="button" onClick={()=>onSelect(items[0].id)} className="flex h-12 w-full items-center justify-between rounded-xl border border-teal-300/30 bg-[#102e33] px-4 text-left"><b>{items[0].name}</b><span className="rounded-full bg-teal-300/15 px-2 py-1 text-xs text-teal-100">Detectada</span></button>:<div className="grid gap-2 sm:grid-cols-2">{items.map(item=><button type="button" key={item.id} onClick={()=>onSelect(item.id)} className={`min-h-12 rounded-xl border px-4 text-left text-sm font-semibold ${selected===item.id?"border-teal-300 bg-teal-300/15":"border-white/15 bg-[#102e33]"}`}>{item.name}</button>)}</div>}</fieldset>}

function DocumentChoice({tenantName,selected,installer,installerError,canInstall,onSelect}:{tenantName:string;selected:SalesWorkspaceOption;installer:PosInstaller|null;installerError:string|null;canInstall:boolean;onSelect:(value:PosSaleDocumentType)=>void}){
 return <SetupShell tenantName={tenantName} title="Tipo de venta" subtitle={selected.businessName+" / "+selected.warehouseName}>
  <p className="text-sm text-slate-300">Elige como empezara esta caja. Podras cambiarlo despues desde el boton visible en ventas.</p>
  <div className="mt-5 grid gap-3 sm:grid-cols-2">
   <button type="button" onClick={()=>onSelect("SalesInvoice")} className="rounded-2xl border border-white/15 p-5 text-left transition hover:border-teal-300 hover:bg-teal-300/10">
    <FileKey2 className="mb-4 h-7 w-7 text-teal-200"/><b className="block">Factura electronica</b><span className="mt-1 block text-sm text-slate-300">Solicita numeracion y configuracion fiscal.</span>
   </button>
   <button type="button" onClick={()=>onSelect("SalesReceipt")} className="rounded-2xl border border-white/15 p-5 text-left transition hover:border-teal-300 hover:bg-teal-300/10">
    <Receipt className="mb-4 h-7 w-7 text-teal-200"/><b className="block">Comprobante de venta</b><span className="mt-1 block text-sm text-slate-300">Entra directamente, sin pedir datos fiscales.</span>
   </button>
  </div>
  {canInstall&&<InstallerCard installer={installer} error={installerError}/>}
 </SetupShell>;
}

function ReceiptSetup({tenantName,selected,busy,edgeCapable,canEnrollOffline,installer,installerError,onBack,onStart}:{tenantName:string;selected:SalesWorkspaceOption;busy:boolean;edgeCapable:boolean;canEnrollOffline:boolean;installer:PosInstaller|null;installerError:string|null;onBack:()=>void;onStart:()=>void}){
 return <SetupShell tenantName={tenantName} title="Comprobante de venta" subtitle={selected.businessName+" / "+selected.warehouseName}>
  <div className="rounded-2xl border border-emerald-300/20 bg-emerald-300/10 p-4 text-sm text-emerald-100">
   <CheckCircle2 className="mb-2 h-6 w-6"/>No necesitas resolucion fiscal para comenzar.
  </div>
  <div className="mt-5 flex gap-2">
   <button type="button" onClick={onBack} disabled={busy} className="h-12 flex-1 rounded-xl border border-white/15 font-semibold">Cambiar tipo</button>
   <button type="button" onClick={onStart} disabled={busy||(edgeCapable&&!canEnrollOffline)} className="h-12 flex-[2] rounded-xl bg-teal-300 font-bold text-[#071a1d] disabled:opacity-40">
    {busy?"Preparando caja...":edgeCapable?"Activar caja desconectada":"Entrar a ventas"}
   </button>
  </div>
  {!edgeCapable&&canEnrollOffline&&<InstallerCard installer={installer} error={installerError}/>}
 </SetupShell>;
}

function InstallerCard({installer,error}:{installer:PosInstaller|null;error:string|null}){
 return <div className="mt-6 rounded-2xl border border-white/10 bg-white/5 p-4 text-sm text-slate-300">
  <p className="font-bold text-white">Preparar esta caja para vender sin internet</p>
  <p className="mt-1">El instalador es generico: no lleva empresa ni credenciales. Despues del login Auraly enrola el equipo y sincroniza sus datos locales.</p>
  {installer?<a href={installer.downloadUrl} className="mt-3 inline-flex h-10 items-center gap-2 rounded-xl border border-teal-300/40 px-4 font-bold text-teal-200"><Download className="h-4 w-4"/>Descargar Auraly POS {installer.version}</a>:error?<p className="mt-3 text-amber-200">{error}</p>:<Loading text="Consultando instalador..."/>}
 </div>;
}

function SetupShell({tenantName,title,subtitle,children}:{tenantName:string;title:string;subtitle:string;children:React.ReactNode}){
 return <main className="min-h-screen bg-[#071a1d] p-5 text-white">
  <div className="mx-auto flex min-h-[calc(100vh-2.5rem)] max-w-3xl items-center">
   <section className="w-full rounded-[2rem] border border-white/10 bg-[#0b2428] p-8 shadow-2xl">
    <span className="grid h-12 w-12 place-items-center rounded-2xl bg-teal-300 text-[#071a1d]"><MonitorSmartphone/></span>
    <p className="mt-6 text-xs font-bold uppercase tracking-[.15em] text-teal-200">{tenantName||"Auraly"}</p>
    <h1 className="mt-2 text-3xl font-black">{title}</h1>
    <p className="mt-1 text-sm text-slate-400">{subtitle}</p>
    <div className="mt-7">{children}</div>
   </section>
  </div>
 </main>;
}
