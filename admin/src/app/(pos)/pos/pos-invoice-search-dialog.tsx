"use client";

import { ChevronLeft, Eye, Loader2, Printer, ReceiptText, Search, SlidersHorizontal, X } from "lucide-react";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { DatePicker } from "@/components/ui/date-picker";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import type { PosCatalogProduct, PosCatalogSearchPage, PosCustomer, PosCustomerSearchPage, PosIssuedSaleFilters, PosIssuedSaleSearchPage, PosIssuedSaleSummary, PosPrintableReceipt } from "@/services/pos/pos-edge-client";
import { usePosModalBehavior } from "./use-pos-modal-behavior";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });
const dateTime = new Intl.DateTimeFormat("es-CO", { dateStyle: "short", timeStyle: "short" });
const EMPTY_FILTERS: PosIssuedSaleFilters = { search: "", customerId: null, partySiteId: null, from: "", to: "", productId: null, minimumTotal: null, maximumTotal: null };
const amount = (value: string) => value.trim() && Number(value) >= 0 ? Number(value) : null;

type PickerValue<T> = { key: string; label: string; value: T };
function ServerPicker<T>({ label, placeholder, selected, onSelect, onSearch }: { label: string; placeholder: string; selected: PickerValue<T> | null; onSelect: (value: T | null) => void; onSearch: (search: string, skip: number) => Promise<{items: PickerValue<T>[]; hasMore: boolean; nextOffset: number|null}> }) {
  const [open,setOpen]=useState(false), [query,setQuery]=useState(""), [items,setItems]=useState<PickerValue<T>[]>([]), [loading,setLoading]=useState(false);
  const [next,setNext]=useState<number|null>(null); const version=useRef(0), searchRef=useRef(onSearch); searchRef.current=onSearch;
  useEffect(()=>{if(!open)return;const current=++version.current;const timer=window.setTimeout(()=>{setLoading(true);void searchRef.current(query.trim(),0).then(page=>{if(current!==version.current)return;setItems(page.items);setNext(page.hasMore?page.nextOffset:null)}).finally(()=>{if(current===version.current)setLoading(false)})},query.trim()?180:0);return()=>window.clearTimeout(timer)},[open,query]);
  const more=useCallback(async()=>{if(loading||next===null)return;setLoading(true);try{const page=await searchRef.current(query.trim(),next);setItems(current=>{const keys=new Set(current.map(x=>x.key));return[...current,...page.items.filter(x=>!keys.has(x.key))]});setNext(page.hasMore?page.nextOffset:null)}finally{setLoading(false)}},[loading,next,query]);
  return <div className="relative grid gap-1.5 text-sm font-medium"><span>{label}</span><button type="button" onClick={()=>setOpen(v=>!v)} className="flex h-10 items-center justify-between rounded-xl border bg-white px-3 text-left font-normal shadow-sm"><span className="truncate">{selected?.label??placeholder}</span><Search className="h-4 w-4 text-teal-700"/></button>{open&&<div className="absolute inset-x-0 top-[4.5rem] z-30 overflow-hidden rounded-xl border bg-white shadow-xl"><div className="flex items-center gap-2 border-b p-2"><Search className="h-4 w-4 text-slate-400"/><input autoFocus value={query} onChange={e=>setQuery(e.target.value)} className="min-w-0 flex-1 py-1 outline-none" placeholder={placeholder}/>{loading&&<Loader2 className="h-4 w-4 animate-spin"/>}</div><div className="max-h-56 overflow-auto" onScroll={e=>{const el=e.currentTarget;if(el.scrollHeight-el.scrollTop-el.clientHeight<60)void more()}}><button type="button" className="block w-full border-b px-3 py-2 text-left text-slate-500 hover:bg-teal-50" onClick={()=>{onSelect(null);setOpen(false)}}>Todos</button>{items.map(item=><button key={item.key} type="button" className="block w-full border-b px-3 py-2 text-left hover:bg-teal-50" onClick={()=>{onSelect(item.value);setOpen(false)}}>{item.label}</button>)}{!loading&&!items.length&&<p className="p-4 text-center text-xs text-slate-500">Sin resultados.</p>}</div></div>}</div>;
}

export function PosInvoiceSearchDialog({busy,onSearch,onSearchCustomers,onSearchProducts,onDetail,onReprint,onCancel}:{
  busy:boolean;
  onSearch:(filters:PosIssuedSaleFilters,skip:number)=>Promise<PosIssuedSaleSearchPage>;
  onSearchCustomers:(search:string,skip:number)=>Promise<PosCustomerSearchPage>;
  onSearchProducts:(search:string,skip:number)=>Promise<PosCatalogSearchPage>;
  onDetail:(sale:PosIssuedSaleSummary)=>Promise<PosPrintableReceipt>;
  onReprint:(sale:PosIssuedSaleSummary)=>Promise<void>;
  onCancel:()=>void;
}) {
  const [filters,setFilters]=useState(EMPTY_FILTERS),[customer,setCustomer]=useState<PosCustomer|null>(null),[product,setProduct]=useState<PosCatalogProduct|null>(null);
  const [minimum,setMinimum]=useState(""),[maximum,setMaximum]=useState(""),[results,setResults]=useState<PosIssuedSaleSummary[]>([]),[next,setNext]=useState<number|null>(null);
  const [loading,setLoading]=useState(true),[loadingMore,setLoadingMore]=useState(false),[error,setError]=useState<string|null>(null),[detail,setDetail]=useState<PosPrintableReceipt|null>(null),[detailLoading,setDetailLoading]=useState(false);
  const [printingDocumentId,setPrintingDocumentId]=useState<string|null>(null);
  const requestVersion=useRef(0),modal=useRef<HTMLElement>(null),input=useRef<HTMLInputElement>(null);
  usePosModalBehavior({modalRef:modal,initialFocusRef:input,escapeDisabled:busy,onEscape:onCancel});
  const effective=useMemo(()=>({...filters,customerId:customer?.customerId??null,partySiteId:customer?.partySiteId??null,productId:product?.productId??null,minimumTotal:amount(minimum),maximumTotal:amount(maximum)}),[filters,customer,product,minimum,maximum]);
  useEffect(()=>{const version=++requestVersion.current;const timer=window.setTimeout(()=>{setLoading(true);setError(null);void onSearch(effective,0).then(page=>{if(requestVersion.current!==version)return;setResults(page.items);setNext(page.hasMore?page.nextOffset:null)}).catch(()=>{if(requestVersion.current===version){setResults([]);setNext(null);setError("No fue posible consultar los comprobantes de esta sede.")}}).finally(()=>{if(requestVersion.current===version)setLoading(false)})},effective.search.trim()?180:80);return()=>window.clearTimeout(timer)},[onSearch,effective]);
  const loadMore=useCallback(async()=>{if(busy||loading||loadingMore||next===null)return;setLoadingMore(true);try{const page=await onSearch(effective,next);setResults(current=>{const ids=new Set(current.map(x=>x.documentId.value));return[...current,...page.items.filter(x=>!ids.has(x.documentId.value))]});setNext(page.hasMore?page.nextOffset:null)}catch{setError("No fue posible cargar más comprobantes.")}finally{setLoadingMore(false)}},[busy,loading,loadingMore,next,onSearch,effective]);
  async function showDetail(sale:PosIssuedSaleSummary){setDetailLoading(true);setError(null);try{setDetail(await onDetail(sale))}catch{setError("No fue posible cargar el detalle original de la factura.")}finally{setDetailLoading(false)}}
  async function reprint(sale:PosIssuedSaleSummary){const documentId=sale.documentId.value;if(printingDocumentId===documentId)return;setPrintingDocumentId(documentId);try{await onReprint(sale)}finally{setPrintingDocumentId(current=>current===documentId?null:current)}}
  const customerPicker=customer?{key:`${customer.customerId}:${customer.partySiteId}`,label:`${customer.name}${customer.siteName?` · ${customer.siteName}`:""}`,value:customer} : null;
  const productPicker=product?{key:product.productId,label:`${product.name} · ${product.reference??product.productCode}`,value:product} : null;
  return <div className="fixed inset-0 z-50 grid place-items-center bg-slate-950/60 p-2 sm:p-4" data-pos-focus-surface="modal"><section ref={modal} tabIndex={-1} role="dialog" aria-modal="true" className="flex max-h-[96vh] w-full max-w-6xl flex-col overflow-hidden rounded-2xl bg-white shadow-2xl">
    <header className="flex items-start justify-between gap-4 bg-gradient-to-r from-slate-950 via-teal-950 to-cyan-800 p-4 text-white sm:p-5"><div><h2 className="flex items-center gap-2 text-xl font-semibold"><ReceiptText className="h-5 w-5 text-cyan-300"/>{detail?"Detalle de factura":"Facturas y comprobantes"}</h2><p className="mt-1 text-sm text-cyan-50/75">Consulta histórica en servidor, detalle original y reimpresión auditada.</p></div><button type="button" onClick={detail?()=>setDetail(null):onCancel} disabled={busy} className="grid h-10 w-10 place-items-center rounded-xl bg-white/10 hover:bg-white/20" aria-label={detail?"Volver":"Cerrar"}>{detail?<ChevronLeft/>:<X/>}</button></header>
    {detail?<div className="min-h-0 flex-1 overflow-auto p-4 sm:p-6"><div className="grid gap-3 rounded-2xl border bg-slate-50 p-4 sm:grid-cols-3"><div><small className="text-slate-500">Documento</small><p className="font-bold">{detail.documentNumber}</p></div><div><small className="text-slate-500">Cliente</small><p className="font-bold">{detail.customerName}</p><p className="text-xs text-slate-500">{detail.customerIdentification}</p></div><div><small className="text-slate-500">Fecha y total</small><p>{dateTime.format(new Date(detail.issuedAt))}</p><p className="text-lg font-black text-teal-800">{money.format(detail.payableAmount)}</p></div></div><div className="mt-4 overflow-hidden rounded-2xl border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Producto</TableHead>
              <TableHead className="text-right">Cantidad</TableHead>
              <TableHead className="text-right">Precio</TableHead>
              <TableHead className="text-right">Total</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {detail.lines.map((line,index)=><TableRow key={`${line.productCode}-${index}`}>
              <TableCell><strong>{line.description}</strong><small className="block text-slate-500">{line.productCode}</small></TableCell>
              <TableCell className="text-right">{line.quantity}</TableCell>
              <TableCell className="text-right">{money.format(line.unitPrice)}</TableCell>
              <TableCell className="text-right font-bold">{money.format(line.total)}</TableCell>
            </TableRow>)}
          </TableBody>
        </Table>
      </div></div>:<>
      <div className="border-b bg-slate-50 p-3 sm:p-5"><div className="mb-3 flex items-center gap-2 text-sm font-bold text-teal-900"><SlidersHorizontal className="h-4 w-4"/>Filtros en servidor</div><div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <label className="grid gap-1.5 text-sm font-medium sm:col-span-2"><span>Número de factura o DIAN</span><span className="relative"><Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400"/><input ref={input} autoFocus value={filters.search} onChange={e=>setFilters(v=>({...v,search:e.target.value}))} className="h-10 w-full rounded-xl border bg-white pl-9 pr-9 outline-none focus:ring-2 focus:ring-teal-600" placeholder="Ej. FV-1024"/>{loading&&<span className="absolute right-4 top-1/2 grid h-5 w-5 -translate-y-1/2 place-items-center"><Loader2 className="h-4 w-4 animate-spin text-teal-700"/></span>}</span></label>
        <ServerPicker label="Cliente y sede" placeholder="Buscar cliente o sede" selected={customerPicker} onSelect={setCustomer} onSearch={async(q,s)=>{const p=await onSearchCustomers(q,s);return{...p,items:p.items.map(i=>({key:`${i.customerId}:${i.partySiteId}`,label:`${i.name}${i.siteName?` · ${i.siteName}`:""}`,value:i}))}}}/>
        <ServerPicker label="Producto" placeholder="Buscar nombre o referencia" selected={productPicker} onSelect={setProduct} onSearch={async(q,s)=>{const p=await onSearchProducts(q,s);return{...p,items:p.items.map(i=>({key:i.productId,label:`${i.name} · ${i.reference??i.productCode}`,value:i}))}}}/>
        <label className="grid gap-1.5 text-sm font-medium"><span>Desde</span><DatePicker value={filters.from} max={filters.to||undefined} onChange={from=>setFilters(v=>({...v,from}))}/></label><label className="grid gap-1.5 text-sm font-medium"><span>Hasta</span><DatePicker value={filters.to} min={filters.from||undefined} onChange={to=>setFilters(v=>({...v,to}))}/></label>
        <label className="grid gap-1.5 text-sm font-medium"><span>Precio mínimo</span><input type="number" min="0" step="1" value={minimum} onChange={e=>setMinimum(e.target.value)} className="h-10 rounded-xl border px-3" placeholder="$ 0"/></label><label className="grid gap-1.5 text-sm font-medium"><span>Precio máximo</span><input type="number" min="0" step="1" value={maximum} onChange={e=>setMaximum(e.target.value)} className="h-10 rounded-xl border px-3" placeholder="Sin límite"/></label>
      </div></div>
      <div className="min-h-56 flex-1 overflow-auto p-3 sm:p-5" onScroll={e=>{const el=e.currentTarget;if(el.scrollHeight-el.scrollTop-el.clientHeight<120)void loadMore()}}><div className="grid gap-2">{results.map(sale=>{const printing=printingDocumentId===sale.documentId.value;return <article key={sale.documentId.value} className="grid gap-3 rounded-xl border p-3 hover:border-teal-300 hover:bg-teal-50/40 sm:grid-cols-[minmax(0,1fr)_150px_130px_auto] sm:items-center"><div className="min-w-0"><p className="font-bold">{sale.documentNumber}</p><p className="truncate text-xs text-slate-500">{sale.fiscalNumber?`DIAN ${sale.fiscalNumber} · `:""}{sale.customerName} · {sale.customerIdentification}</p></div><span className="text-sm text-slate-600">{dateTime.format(new Date(sale.issuedAt))}</span><strong className="text-teal-800">{money.format(sale.total)}</strong><div className="flex gap-2"><button type="button" disabled={busy||detailLoading} onClick={()=>void showDetail(sale)} className="flex h-9 items-center gap-1 rounded-lg border bg-white px-3 text-sm font-semibold"><Eye className="h-4 w-4"/>Detalle</button><button type="button" disabled={busy||printing} aria-busy={printing} onClick={()=>void reprint(sale)} className="flex h-9 min-w-24 items-center justify-center gap-1 rounded-lg bg-teal-700 px-3 text-sm font-semibold text-white disabled:cursor-wait disabled:opacity-70">{printing?<><Loader2 className="h-4 w-4 animate-spin"/>Abriendo…</>:<><Printer className="h-4 w-4"/>Imprimir</>}</button></div></article>})}</div>{!loading&&!results.length&&!error&&<p className="grid min-h-40 place-items-center text-center text-sm text-slate-500">No encontramos facturas con esos filtros.</p>}{loadingMore&&<p className="py-4 text-center text-sm text-teal-800">Cargando 20 más…</p>}{error&&<p className="py-4 text-center text-sm font-medium text-red-700" role="alert">{error}</p>}</div>
    </>}
  </section></div>;
}
