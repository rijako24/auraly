"use client";

import { useEffect, useRef, useState } from "react";
import { Search, UserRound, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import type { PosSeller, PosSellerPage } from "@/services/pos/pos-edge-client";
import { usePosModalBehavior } from "./use-pos-modal-behavior";

export function PosSellerSearchDialog({ busy, onSearch, onSelect, onCancel }: {
  busy:boolean; onSearch:(term:string,skip:number)=>Promise<PosSellerPage>;
  onSelect:(id:string|null)=>Promise<void>; onCancel:()=>void;
}) {
  const [retry,setRetry]=useState(0);
  const [term,setTerm]=useState("");
  const [rows,setRows]=useState<PosSeller[]>([]);
  const [next,setNext]=useState<number|null>(null);
  const [loading,setLoading]=useState(true);
  const [error,setError]=useState<string|null>(null);
  const version=useRef(0);
  const panel=useRef<HTMLElement>(null);
  const input=useRef<HTMLInputElement>(null);
  usePosModalBehavior({modalRef:panel,initialFocusRef:input,onEscape:onCancel,escapeDisabled:busy});
  useEffect(()=>{
    const request=++version.current;
    const timer=window.setTimeout(()=>{
      setLoading(true); setError(null);
      void onSearch(term.trim(),0).then(page=>{
        if(version.current!==request)return;
        setRows(page.items);setNext(page.nextOffset);
      }).catch(()=>{
        if(version.current!==request)return;
        setRows([]);setNext(null);setError("No fue posible consultar los vendedores. Reintenta la búsqueda o actualiza los datos de la caja.");
      }).finally(()=>{if(version.current===request)setLoading(false);});
    },200);
    return ()=>{window.clearTimeout(timer);version.current=request+1;};
  },[term,onSearch,retry]);
  async function choose(id:string|null) {
    setError(null);
    try {await onSelect(id);}
    catch(caught) {setError(caught instanceof Error?caught.message:"No fue posible asignar el vendedor.");}
  }
  async function more() {
    if(next===null||loading)return;
    const request=version.current;setLoading(true);setError(null);
    try {const page=await onSearch(term.trim(),next);if(version.current!==request)return;
      setRows(current=>[...current,...page.items]);setNext(page.nextOffset);
    } catch {if(version.current===request)setError("No fue posible cargar más vendedores. Intenta de nuevo.");}
    finally {if(version.current===request)setLoading(false);}
  }
  return <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-3" data-pos-focus-surface="modal">
    <section ref={panel} role="dialog" aria-modal="true" aria-labelledby="seller-title" tabIndex={-1}
      className="flex max-h-[90dvh] w-full max-w-xl flex-col overflow-hidden rounded-2xl border bg-background text-foreground shadow-xl">
      <header className="flex shrink-0 items-center justify-between gap-3 border-b p-4"><div><h2 id="seller-title" className="text-lg font-semibold">Seleccionar vendedor</h2>
        <p className="text-sm text-muted-foreground">Se conserva al guardar, recuperar y facturar.</p></div>
        <Button size="icon" variant="ghost" onClick={onCancel} disabled={busy} aria-label="Cerrar vendedores"><X className="h-4 w-4"/></Button></header>
      <div className="shrink-0 p-4"><label className="mb-2 flex items-center gap-2 text-sm" htmlFor="seller-search"><Search className="h-4 w-4"/>Nombre, identificación o código</label>
        <Input id="seller-search" ref={input} value={term} onChange={event=>setTerm(event.target.value)}
          onKeyDown={event=>{if(event.key==="Enter"&&!loading&&rows[0]){event.preventDefault();void choose(rows[0].sellerId);}}}/></div>
      <div className="min-h-0 overflow-y-auto px-4 pb-4" aria-busy={loading}>
        {error&&<div role="alert" className="mb-3 text-sm text-destructive"><p>{error}</p><button type="button" disabled={busy} onClick={()=>setRetry(current=>current+1)} className="mt-2 underline">Volver a consultar vendedores</button></div>}
        {rows.map(row=><button type="button" key={row.sellerId} disabled={busy||loading} onClick={()=>void choose(row.sellerId)}
          className="mb-2 flex w-full items-center gap-3 rounded-lg border p-3 text-left hover:bg-muted disabled:opacity-50">
          <UserRound className="h-4 w-4 shrink-0"/><span className="min-w-0"><span className="block break-words font-medium">{row.name}</span>
          <span className="text-xs text-muted-foreground">{[row.identification,row.code].filter(Boolean).join(" · ")}</span></span></button>)}
        {!rows.length&&!loading&&!error&&<p className="py-6 text-center text-sm text-muted-foreground">No hay vendedores activos para esta búsqueda.</p>}
        {loading&&<p className="py-3 text-center text-sm" role="status">Consultando vendedores…</p>}
        {next!==null&&<Button variant="outline" disabled={loading||busy} onClick={()=>void more()} className="w-full">Cargar más vendedores</Button>}
      </div>
      <footer className="flex shrink-0 flex-wrap justify-end gap-2 border-t p-4"><Button variant="outline" disabled={busy} onClick={()=>void choose(null)}>Vendedor automático</Button>
        <Button variant="ghost" disabled={busy} onClick={onCancel}>Cancelar</Button></footer>
    </section>
  </div>;
}
