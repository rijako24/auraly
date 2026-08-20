"use client";

import { useEffect, useState } from "react";
import Image from "next/image";
import { Download, Share, Smartphone, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";

type InstallPromptEvent=Event&{prompt:()=>Promise<void>;userChoice:Promise<{outcome:"accepted"|"dismissed"}>};
const dismissedKey="auraly:pwa-install-dismissed";
const standalone=()=>window.matchMedia("(display-mode: standalone)").matches||(navigator as Navigator&{standalone?:boolean}).standalone===true;

export function PwaInstallPrompt(){
  const [ready,setReady]=useState(false),[prompt,setPrompt]=useState<InstallPromptEvent|null>(null),[guide,setGuide]=useState(false);
  useEffect(()=>{if(standalone()||localStorage.getItem(dismissedKey)==="1")return;const timer=window.setTimeout(()=>setReady(true),1800);const capture=(event:Event)=>{event.preventDefault();setPrompt(event as InstallPromptEvent);setReady(true)};const installed=()=>{setReady(false);setGuide(false)};window.addEventListener("beforeinstallprompt",capture);window.addEventListener("appinstalled",installed);return()=>{clearTimeout(timer);window.removeEventListener("beforeinstallprompt",capture);window.removeEventListener("appinstalled",installed)}},[]);
  if(!ready)return null;
  const install=async()=>{if(prompt){await prompt.prompt();const choice=await prompt.userChoice;if(choice.outcome==="accepted")setReady(false);setPrompt(null)}else setGuide(true)};
  const dismiss=()=>{localStorage.setItem(dismissedKey,"1");setReady(false);setGuide(false)};
  return <><aside className="fixed bottom-[calc(5rem+env(safe-area-inset-bottom))] left-3 right-3 z-[60] mx-auto flex max-w-md items-center gap-3 rounded-3xl border border-teal-200 bg-white/95 p-3 shadow-2xl shadow-slate-950/15 backdrop-blur lg:bottom-5 lg:left-auto lg:right-5">
    <span className="grid h-12 w-12 shrink-0 place-items-center overflow-hidden rounded-2xl bg-teal-50"><Image src="/brand/auraly-app-icon-192.png" width={48} height={48} alt="Auraly"/></span><button type="button" onClick={install} className="min-w-0 flex-1 text-left"><strong className="block text-sm">Instalar Auraly</strong><small className="block text-xs text-muted-foreground">Ábrela como una app, sin el navegador.</small></button><Button size="sm" onClick={install}><Download className="mr-1 h-4 w-4"/>Instalar</Button><button type="button" onClick={dismiss} aria-label="No mostrar de nuevo" className="rounded-full p-1 text-muted-foreground"><X className="h-4 w-4"/></button>
  </aside><Sheet open={guide} onOpenChange={setGuide}><SheetContent side="bottom" className="rounded-t-[2rem] px-5 pb-[max(1.25rem,env(safe-area-inset-bottom))]"><SheetHeader className="text-left"><div className="mb-2 flex items-center gap-3"><Image src="/brand/auraly-app-icon-192.png" width={64} height={64} className="rounded-2xl shadow-lg" alt="Auraly"/><div><SheetTitle>Instala Auraly en tu iPhone</SheetTitle><SheetDescription>Quedará en el inicio y abrirá a pantalla completa.</SheetDescription></div></div></SheetHeader><ol className="my-5 space-y-3"><Step icon={Share} number="1" title="Toca Compartir" text="Usa el botón de compartir del navegador."/><Step icon={Smartphone} number="2" title="Añadir a pantalla de inicio" text="Busca esa opción en la hoja de acciones."/><Step icon={Download} number="3" title="Confirma Añadir" text="Auraly aparecerá con su nuevo icono."/></ol><div className="grid grid-cols-2 gap-2"><Button variant="outline" onClick={dismiss}>No mostrar</Button><Button onClick={()=>setGuide(false)}>Entendido</Button></div></SheetContent></Sheet></>;
}
function Step({icon:Icon,number,title,text}:{icon:typeof Share;number:string;title:string;text:string}){return <li className="flex items-center gap-3 rounded-2xl border bg-muted/20 p-3"><span className="grid h-11 w-11 shrink-0 place-items-center rounded-2xl bg-slate-950 text-white"><Icon className="h-5 w-5"/></span><span className="min-w-0"><strong className="block text-sm">{number}. {title}</strong><small className="text-muted-foreground">{text}</small></span></li>}
