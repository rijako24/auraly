"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ArrowLeft } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";
import { useCreateBusiness } from "@/hooks/use-businesses";

export default function NewBusinessPage() {
  const router = useRouter();
  const create = useCreateBusiness();
  const [form,setForm]=useState({name:"",description:"",address:"",phone:"",email:"",website:"",timeZone:"America/Bogota",sharesProductPrices:false});
  const set=<K extends keyof typeof form>(key:K,value:(typeof form)[K])=>setForm(current=>({...current,[key]:value}));
  const valid=Boolean(form.name.trim()&&form.address.trim()&&form.phone.trim()&&form.email.trim());
  const save=()=>create.mutate(form,{onSuccess:(business)=>{toast.success("Sede creada y aprovisionada.");router.push(`/dashboard/businesses/${business.businessId}`)},onError:()=>toast.error("No fue posible crear la sede.")});
  return <div className="mx-auto max-w-3xl space-y-5"><header className="flex items-center gap-3"><Button variant="ghost" size="icon" asChild><Link href="/dashboard/businesses"><ArrowLeft className="h-4 w-4"/></Link></Button><div><h1 className="text-2xl font-semibold">Nueva sede</h1><p className="text-sm text-muted-foreground">Se aprovisionan bodegas, contabilidad y el catálogo tenant automáticamente.</p></div></header><Card><CardHeader><CardTitle>Datos de la sede</CardTitle></CardHeader><CardContent className="grid gap-4 sm:grid-cols-2"><Field label="Nombre"><Input value={form.name} onChange={event=>set("name",event.target.value)}/></Field><Field label="Correo"><Input type="email" value={form.email} onChange={event=>set("email",event.target.value)}/></Field><Field label="Dirección"><Input value={form.address} onChange={event=>set("address",event.target.value)}/></Field><Field label="Teléfono"><Input value={form.phone} onChange={event=>set("phone",event.target.value)}/></Field><Field label="Sitio web"><Input value={form.website} onChange={event=>set("website",event.target.value)}/></Field><Field label="Zona horaria"><Input value={form.timeZone} onChange={event=>set("timeZone",event.target.value)}/></Field><Field label="Descripción" className="sm:col-span-2"><Textarea value={form.description} onChange={event=>set("description",event.target.value)}/></Field><div className="flex items-center justify-between gap-4 rounded-2xl border bg-muted/30 p-4 sm:col-span-2"><div><Label>Compartir precios y costos</Label><p className="mt-1 text-xs text-muted-foreground">La sede se integra al grupo que prepara, publica y calcula costo promedio en conjunto.</p></div><Switch checked={form.sharesProductPrices} onCheckedChange={value=>set("sharesProductPrices",value)} /></div><div className="flex justify-end gap-2 sm:col-span-2"><Button variant="outline" asChild><Link href="/dashboard/businesses">Cancelar</Link></Button><Button disabled={!valid||create.isPending} onClick={save}>{create.isPending?"Creando…":"Crear sede"}</Button></div></CardContent></Card></div>;
}

function Field({label,className,children}:{label:string;className?:string;children:React.ReactNode}){return <div className={`space-y-2 ${className??""}`}><Label>{label}</Label>{children}</div>}
