"use client";

import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Building2, Check, ChevronDown, Search } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle, SheetTrigger } from "@/components/ui/sheet";
import { cn } from "@/lib/utils";
import { useBusinessContextStore } from "@/stores/business-context-store";

export function BusinessSwitcher() {
  const [open,setOpen]=useState(false),[mobileOpen,setMobileOpen]=useState(false),[search,setSearch]=useState("");
  const queryClient=useQueryClient();
  const {businesses,selectedBusinessId,selectBusiness}=useBusinessContextStore();
  const selected=businesses.find((business)=>business.businessId===selectedBusinessId);
  const filtered=businesses.filter((business)=>business.name.toLocaleLowerCase("es").includes(search.trim().toLocaleLowerCase("es")));
  if(!businesses.length)return null;
  const choose=(businessId:string)=>{if(businessId!==selectedBusinessId){selectBusiness(businessId);queryClient.removeQueries({predicate:(query)=>query.queryKey[0]!=="businesses"})}setOpen(false);setMobileOpen(false);setSearch("")};
  const list=<div className="space-y-2">{filtered.map((business)=><button type="button" key={business.businessId} onClick={()=>choose(business.businessId)} className={cn("flex w-full items-center gap-3 rounded-2xl border p-3 text-left transition",business.businessId===selectedBusinessId?"border-teal-300 bg-teal-50":"bg-card hover:border-teal-200")}><span className="grid h-11 w-11 shrink-0 place-items-center rounded-2xl bg-slate-950 text-white"><Building2 className="h-5 w-5"/></span><span className="min-w-0 flex-1"><strong className="block truncate">{business.name}</strong><small className="text-muted-foreground">Espacio de trabajo</small></span>{business.businessId===selectedBusinessId&&<Check className="h-5 w-5 text-teal-600"/>}</button>)}</div>;
  return <>
    <Sheet open={mobileOpen} onOpenChange={setMobileOpen}><SheetTrigger asChild><button type="button" className="flex min-w-0 flex-1 items-center gap-2 rounded-2xl px-1 py-1 text-left sm:hidden"><span className="grid h-10 w-10 shrink-0 place-items-center rounded-2xl bg-gradient-to-br from-slate-950 to-teal-700 text-white shadow-sm"><Building2 className="h-4 w-4"/></span><span className="min-w-0 flex-1"><small className="block text-[10px] font-bold uppercase tracking-wider text-teal-700">Negocio</small><strong className="block truncate text-sm">{selected?.name??"Seleccionar"}</strong></span><ChevronDown className="h-4 w-4 shrink-0 text-muted-foreground"/></button></SheetTrigger><SheetContent side="bottom" className="max-h-[82dvh] rounded-t-[2rem] px-4 pb-[max(1rem,env(safe-area-inset-bottom))]"><SheetHeader className="text-left"><SheetTitle>Cambiar negocio</SheetTitle><SheetDescription>Selecciona el espacio en el que vas a trabajar.</SheetDescription></SheetHeader><label className="relative my-4 block"><Search className="pointer-events-none absolute left-3 top-3 h-4 w-4 text-muted-foreground"/><Input value={search} onChange={(event)=>setSearch(event.target.value)} className="h-11 rounded-xl pl-9" placeholder="Buscar negocio"/></label><div className="max-h-[55dvh] overflow-y-auto">{list}</div></SheetContent></Sheet>
    <Popover open={open} onOpenChange={setOpen}><PopoverTrigger asChild><Button variant="outline" role="combobox" aria-expanded={open} className="hidden w-[220px] justify-between sm:flex" size="sm"><span className="flex min-w-0 items-center gap-2"><Building2 className="h-4 w-4 shrink-0"/><span className="truncate">{selected?.name??"Seleccionar negocio"}</span></span><ChevronDown className="h-4 w-4"/></Button></PopoverTrigger><PopoverContent className="w-80 p-3" align="start"><label className="relative mb-3 block"><Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground"/><Input value={search} onChange={(event)=>setSearch(event.target.value)} className="pl-9" placeholder="Buscar negocio"/></label>{list}</PopoverContent></Popover>
  </>;
}
