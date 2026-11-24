"use client";

import { Pencil, RefreshCw, ChevronLeft, Eye, Loader2, Printer, ReceiptText, Search, SlidersHorizontal, X } from "lucide-react";
import { useCallback, useEffect, useId, useMemo, useRef, useState } from "react";
import { PagedEntitySelect } from "@/components/forms/paged-entity-select";
import type { PartyRoleOption, PartyRoleOptionPage } from "@/services/api/parties";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { DatePicker } from "@/components/ui/date-picker";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import type { PosCatalogProduct, PosCatalogSearchPage, PosCustomer, PosCustomerSearchPage, PosIssuedSaleFilters, PosIssuedSaleSearchPage, PosIssuedSaleSummary, PosPrintableReceipt } from "@/services/pos/pos-edge-client";
import { workSessionPaymentMethodName } from "@/services/pos/pos-work-session-close";
import { SalePaymentCorrectionDialog } from "@/components/payments/sale-payment-correction-dialog";
import { usePosModalBehavior } from "./use-pos-modal-behavior";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });
const EMPTY_FILTERS: PosIssuedSaleFilters = { search: "", customerId: null, partySiteId: null, from: "", to: "", productId: null, minimumTotal: null, maximumTotal: null };
const amount = (value: string) => value.trim() && Number(value) >= 0 ? Number(value) : null;

type PickerValue<T> = { key: string; label: string; value: T };
function ServerPicker<T>({ label, placeholder, selected, onSelect, onSearch }: { label: string; placeholder: string; selected: PickerValue<T> | null; onSelect: (value: T | null) => void; onSearch: (search: string, skip: number) => Promise<{items: PickerValue<T>[]; hasMore: boolean; nextOffset: number|null}> }) {
  const [open,setOpen]=useState(false), [query,setQuery]=useState(""), [items,setItems]=useState<PickerValue<T>[]>([]), [loading,setLoading]=useState(false);
  const [error,setError]=useState<string|null>(null);
  const [next,setNext]=useState<number|null>(null); const version=useRef(0), searchRef=useRef(onSearch); searchRef.current=onSearch;
  useEffect(()=>{if(!open)return;const current=++version.current;const timer=window.setTimeout(()=>{setLoading(true);setError(null);void searchRef.current(query.trim(),0).then(page=>{if(current!==version.current)return;setItems(page.items);setNext(page.hasMore?page.nextOffset:null)}).catch(()=>{if(current===version.current){setItems([]);setNext(null);setError("No fue posible consultar. Reintenta la búsqueda.")}}).finally(()=>{if(current===version.current)setLoading(false)})},query.trim()?180:0);return()=>{window.clearTimeout(timer);version.current=current+1;}},[open,query]);
  const moreInFlight=useRef(false);
  const more=useCallback(async()=>{
    if(loading||next===null||moreInFlight.current)return;
    const current=version.current;moreInFlight.current=true;setLoading(true);setError(null);
    try {
      const page=await searchRef.current(query.trim(),next);if(current!==version.current)return;
      setItems(values=>{const keys=new Set(values.map(value=>value.key));return [...values,...page.items.filter(value=>!keys.has(value.key))];});
      setNext(page.hasMore?page.nextOffset:null);
    } catch {if(current===version.current)setError("No fue posible cargar más resultados.");}
    finally {moreInFlight.current=false;if(current===version.current)setLoading(false);}
  },[loading,next,query]);

  const trigger=useRef<HTMLButtonElement>(null);
  const optionsId=useId();
  return <div className="grid gap-1.5 text-sm font-medium"><span>{label}</span><Popover modal open={open} onOpenChange={setOpen}>
    <PopoverTrigger asChild><button ref={trigger} type="button" role="combobox" aria-label={label} aria-expanded={open} aria-controls={optionsId} aria-haspopup="listbox"
      className="flex h-10 min-w-0 items-center justify-between rounded-xl border bg-white px-3 text-left font-normal shadow-sm">
      <span className="truncate">{selected?.label??placeholder}</span><Search className="h-4 w-4 shrink-0 text-teal-700"/></button></PopoverTrigger>
    <PopoverContent className="w-[min(28rem,calc(100vw-2rem))] overflow-hidden p-0" align="start"
      portalContainer={trigger.current?.closest<HTMLElement>("[role='dialog']")??undefined}>
      <div className="flex items-center gap-2 border-b p-2"><Search className="h-4 w-4 text-slate-400"/>
        <input autoFocus value={query} onChange={event=>setQuery(event.target.value)} className="min-w-0 flex-1 py-1 outline-none" placeholder={placeholder}/>
        {loading&&<Loader2 className="h-4 w-4 animate-spin"/>}</div>
      <div id={optionsId} role="listbox" aria-label={label} className="max-h-56 overflow-auto" onScroll={event=>{const element=event.currentTarget;if(element.scrollHeight-element.scrollTop-element.clientHeight<60)void more();}}>
        <button type="button" role="option" aria-selected={!selected} className="block w-full border-b px-3 py-2 text-left text-slate-500 hover:bg-teal-50" onClick={()=>{onSelect(null);setOpen(false);}}>Todos</button>
        {items.map(item=><button key={item.key} type="button" role="option" aria-selected={selected?.key===item.key} className="block w-full break-words border-b px-3 py-2 text-left hover:bg-teal-50" onClick={()=>{onSelect(item.value);setOpen(false);}}>{item.label}</button>)}
        {error&&<p role="alert" className="p-3 text-sm text-red-700">{error}</p>}
        {!loading&&!items.length&&!error&&<p className="p-4 text-center text-xs text-slate-500">Sin resultados.</p>}
      </div>
    </PopoverContent>
  </Popover></div>;

}

export function PosInvoiceSearchDialog({busy,onSearch,onSearchCustomers,onSearchProducts,onDetail,onReprint,onCancel,onSearchRoleOptions,scopeKey,embedded=false,canCorrect=false,canCorrectDispatch=false,timeZone,correctionBusinessId}:{
  busy:boolean;
  scopeKey:string;
  onSearchRoleOptions:(role:"Seller"|"User",search:string,page:number,pageSize:number)=>Promise<PartyRoleOptionPage>;
  embedded?:boolean;
  timeZone?:string;
  canCorrect?:boolean;
  canCorrectDispatch?:boolean;
  correctionBusinessId?:string;
  onSearch:(filters:PosIssuedSaleFilters,skip:number)=>Promise<PosIssuedSaleSearchPage>;
  onSearchCustomers:(search:string,skip:number)=>Promise<PosCustomerSearchPage>;
  onSearchProducts:(search:string,skip:number)=>Promise<PosCatalogSearchPage>;
  onDetail:(sale:PosIssuedSaleSummary)=>Promise<PosPrintableReceipt>;
  onReprint:(sale:PosIssuedSaleSummary)=>Promise<void>;
  onCancel:()=>void;
}) {
  const [correcting,setCorrecting]=useState<PosIssuedSaleSummary|null>(null);
  useEffect(()=>setCorrecting(null),[scopeKey]);
  const dateTime=useMemo(()=>new Intl.DateTimeFormat("es-CO",{dateStyle:"short",timeStyle:"short",timeZone:timeZone??"UTC"}),[timeZone]);
  const [seller,setSeller]=useState<PartyRoleOption|null>(null),[billedBy,setBilledBy]=useState<PartyRoleOption|null>(null);
  const [filters,setFilters]=useState(EMPTY_FILTERS),[customer,setCustomer]=useState<PosCustomer|null>(null),[product,setProduct]=useState<PosCatalogProduct|null>(null);
  const [minimum,setMinimum]=useState(""),[maximum,setMaximum]=useState(""),[results,setResults]=useState<PosIssuedSaleSummary[]>([]),[next,setNext]=useState<number|null>(null);
  const [loading,setLoading]=useState(true),[loadingMore,setLoadingMore]=useState(false),[error,setError]=useState<string|null>(null),[detail,setDetail]=useState<PosPrintableReceipt|null>(null),[detailLoading,setDetailLoading]=useState(false);
  const [filtersOpen,setFiltersOpen]=useState(false);
  const [revision,setRevision]=useState(0);
  const [printingDocumentId,setPrintingDocumentId]=useState<string|null>(null);
  const requestVersion=useRef(0),modal=useRef<HTMLElement>(null),input=useRef<HTMLInputElement>(null);
  usePosModalBehavior({modalRef:modal,initialFocusRef:input,escapeDisabled:busy,onEscape:onCancel,enabled:!embedded&&!correcting});
  const effective=useMemo(()=>({...filters,customerId:customer?.customerId??null,partySiteId:customer?.partySiteId??null,productId:product?.productId??null,minimumTotal:amount(minimum),maximumTotal:amount(maximum),sellerId:seller?.roleId??null,billedByUserId:billedBy?.roleId??null}),[filters,customer,product,minimum,maximum,seller,billedBy]);
  useEffect(()=>{const version=++requestVersion.current;const timer=window.setTimeout(()=>{setLoading(true);setError(null);void onSearch(effective,0).then(page=>{if(requestVersion.current!==version)return;setResults(page.items);setNext(page.hasMore?page.nextOffset:null)}).catch(()=>{if(requestVersion.current===version){setResults([]);setNext(null);setError("No fue posible consultar los comprobantes de esta sede.")}}).finally(()=>{if(requestVersion.current===version)setLoading(false)})},effective.search.trim()?180:80);return()=>{window.clearTimeout(timer);requestVersion.current=version+1;}},[onSearch,effective,revision]);
  const moreInFlight=useRef(false);
  const loadMore=useCallback(async()=>{
    if(busy||loading||loadingMore||next===null||moreInFlight.current)return;
    const version=requestVersion.current;moreInFlight.current=true;setLoadingMore(true);
    try {
      const page=await onSearch(effective,next);if(requestVersion.current!==version)return;
      setResults(current=>{const ids=new Set(current.map(value=>value.documentId.value));return [...current,...page.items.filter(value=>!ids.has(value.documentId.value))];});
      setNext(page.hasMore?page.nextOffset:null);
    } catch {if(requestVersion.current===version)setError("No fue posible cargar más comprobantes.");}
    finally {moreInFlight.current=false;setLoadingMore(false);}
  },[busy,loading,loadingMore,next,onSearch,effective]);

  async function showDetail(sale:PosIssuedSaleSummary){setDetailLoading(true);setError(null);try{setDetail(await onDetail(sale))}catch{setError("No fue posible cargar el detalle original de la factura.")}finally{setDetailLoading(false)}}
  async function reprint(sale:PosIssuedSaleSummary){const documentId=sale.documentId.value;if(printingDocumentId===documentId)return;setPrintingDocumentId(documentId);try{await onReprint(sale)}catch{setError("No fue posible imprimir el comprobante.")}finally{setPrintingDocumentId(current=>current===documentId?null:current)}}
  const customerPicker=customer?{key:`${customer.customerId}:${customer.partySiteId}`,label:`${customer.name}${customer.siteName?` · ${customer.siteName}`:""}`,value:customer} : null;
  const productPicker=product?{key:product.productId,label:`${product.name} · ${product.reference??product.productCode}`,value:product} : null;
  return <div className={embedded?"min-w-0":"fixed inset-0 z-50 grid place-items-center bg-slate-950/60 p-2 sm:p-4"} data-pos-focus-surface={embedded?undefined:"modal"}><section ref={modal} tabIndex={-1} role={embedded?"region":"dialog"} aria-label="Facturas y comprobantes" aria-modal={embedded?undefined:true} className={`flex w-full flex-col overflow-hidden rounded-2xl border bg-white shadow-lg ${embedded?"h-[calc(100dvh-10rem)] min-h-96":"max-h-[96dvh] max-w-6xl"}`}>
    <header className="flex items-start justify-between gap-4 bg-gradient-to-r from-slate-950 via-teal-950 to-cyan-800 p-4 text-white sm:p-5"><div><h2 className="flex items-center gap-2 text-xl font-semibold"><ReceiptText className="h-5 w-5 text-cyan-300"/>{detail?"Detalle de factura":"Facturas y comprobantes"}</h2><p className="mt-1 text-sm text-cyan-50/75">Revisa las facturas, sus pagos y vuelve a imprimirlas.</p></div><div className="flex items-center gap-2">{!detail&&<button type="button" onClick={()=>setRevision(current=>current+1)} disabled={loading||busy} aria-label="Actualizar facturas" className="grid h-10 w-10 place-items-center rounded-xl bg-white/10 hover:bg-white/20"><RefreshCw className="h-4 w-4"/></button>}{(!embedded||detail)&&<button type="button" onClick={detail?()=>setDetail(null):onCancel} disabled={busy} className="grid h-10 w-10 place-items-center rounded-xl bg-white/10 hover:bg-white/20" aria-label={detail?"Volver":"Cerrar"}>{detail?<ChevronLeft/>:<X/>}</button>}</div></header>
    {detail?<div className="min-h-0 flex-1 overflow-auto p-4 sm:p-6"><div className="grid gap-3 rounded-2xl border bg-slate-50 p-4 sm:grid-cols-3"><div><small className="text-slate-500">Documento</small><p className="font-bold">{detail.documentNumber}</p></div><div><small className="text-slate-500">Cliente</small><p className="font-bold">{detail.customerName}</p><p className="text-xs text-slate-500">{detail.customerIdentification}</p></div><div><small className="text-slate-500">Fecha y total</small><p>{timeZone?dateTime.format(new Date(detail.issuedAt)):detail.issuedAt}</p><p className="text-lg font-black text-teal-800">{money.format(detail.payableAmount)}</p></div></div><div className="mt-4 overflow-hidden rounded-2xl border">
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
      <div className="shrink-0 border-b bg-slate-50 p-3 sm:p-4"><div className="flex items-end gap-2">
        <label className="grid gap-1.5 text-sm font-medium min-w-0 flex-1"><span>Número de factura o DIAN</span><span className="relative"><Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400"/><input ref={input} autoFocus value={filters.search} onChange={e=>setFilters(v=>({...v,search:e.target.value}))} className="h-10 w-full rounded-xl border bg-white pl-9 pr-9 outline-none focus:ring-2 focus:ring-teal-600" placeholder="Ej. FV-1024"/>{loading&&<span className="absolute right-4 top-1/2 grid h-5 w-5 -translate-y-1/2 place-items-center"><Loader2 className="h-4 w-4 animate-spin text-teal-700"/></span>}</span></label><button type="button" aria-expanded={filtersOpen} onClick={()=>setFiltersOpen(current=>!current)} className="flex h-10 shrink-0 items-center gap-2 rounded-xl border bg-white px-3 text-sm font-semibold"><SlidersHorizontal className="h-4 w-4"/>Filtros</button></div>
      {filtersOpen&&<div className="mt-3 grid max-h-[30dvh] gap-3 overflow-y-auto sm:grid-cols-2 lg:grid-cols-4">
        <div className="grid gap-1.5 text-sm font-medium"><span>Vendedor</span><PagedEntitySelect<PartyRoleOption>
          queryKey={["party-role-options",scopeKey,"Seller","invoice-filter"]} value={seller?.roleId??"all"}
          selectedOption={seller?{value:seller.roleId,label:seller.displayName,description:seller.identification}:null}
          leadingOptions={[{value:"all",label:"Todos los vendedores"}]} ariaLabel="Filtrar facturas por vendedor"
          loadPage={(term,page,pageSize)=>onSearchRoleOptions("Seller",term,page,pageSize)}
          getOption={item=>({value:item.roleId,label:item.displayName,description:item.identification})}
          onChange={(_id,_option,item)=>setSeller(item??null)} wrapLabels contentClassName="w-[min(28rem,calc(100vw-2rem))]"/></div>
        <div className="grid gap-1.5 text-sm font-medium"><span>Usuario que facturó</span><PagedEntitySelect<PartyRoleOption>
          queryKey={["party-role-options",scopeKey,"User","invoice-filter"]} value={billedBy?.roleId??"all"}
          selectedOption={billedBy?{value:billedBy.roleId,label:billedBy.displayName,description:billedBy.identification}:null}
          leadingOptions={[{value:"all",label:"Todos los usuarios"}]} ariaLabel="Filtrar facturas por usuario que facturó"
          loadPage={(term,page,pageSize)=>onSearchRoleOptions("User",term,page,pageSize)}
          getOption={item=>({value:item.roleId,label:item.displayName,description:item.identification})}
          onChange={(_id,_option,item)=>setBilledBy(item??null)} wrapLabels contentClassName="w-[min(28rem,calc(100vw-2rem))]"/></div>

        <ServerPicker label="Cliente y sede" placeholder="Buscar cliente o sede" selected={customerPicker} onSelect={setCustomer} onSearch={async(q,s)=>{const p=await onSearchCustomers(q,s);return{...p,items:p.items.map(i=>({key:`${i.customerId}:${i.partySiteId}`,label:`${i.name}${i.siteName?` · ${i.siteName}`:""}`,value:i}))}}}/>
        <ServerPicker label="Producto" placeholder="Buscar nombre o referencia" selected={productPicker} onSelect={setProduct} onSearch={async(q,s)=>{const p=await onSearchProducts(q,s);return{...p,items:p.items.map(i=>({key:i.productId,label:`${i.name} · ${i.reference??i.productCode}`,value:i}))}}}/>
        <label className="grid gap-1.5 text-sm font-medium"><span>Desde</span><DatePicker value={filters.from} max={filters.to||undefined} onChange={from=>setFilters(v=>({...v,from}))}/></label><label className="grid gap-1.5 text-sm font-medium"><span>Hasta</span><DatePicker value={filters.to} min={filters.from||undefined} onChange={to=>setFilters(v=>({...v,to}))}/></label>
        <label className="grid gap-1.5 text-sm font-medium"><span>Precio mínimo</span><input type="number" min="0" step="1" value={minimum} onChange={e=>setMinimum(e.target.value)} className="h-10 rounded-xl border px-3" placeholder="$ 0"/></label><label className="grid gap-1.5 text-sm font-medium"><span>Precio máximo</span><input type="number" min="0" step="1" value={maximum} onChange={e=>setMaximum(e.target.value)} className="h-10 rounded-xl border px-3" placeholder="Sin límite"/></label>
      </div>}</div>
      <div className="min-h-40 flex-1 overflow-auto p-3 sm:p-5" onScroll={e=>{const el=e.currentTarget;if(el.scrollHeight-el.scrollTop-el.clientHeight<120)void loadMore()}}><div className="grid gap-2">{results.map(sale=>{const printing=printingDocumentId===sale.documentId.value;return <article key={sale.documentId.value} className="grid gap-3 rounded-xl border p-3 hover:border-teal-300 hover:bg-teal-50/40 sm:grid-cols-[minmax(0,1fr)_140px_minmax(130px,180px)_auto] sm:items-center"><div className="min-w-0"><p className="font-bold">{sale.documentNumber}</p><p className="truncate text-xs text-slate-500">{sale.fiscalNumber?`DIAN ${sale.fiscalNumber} · `:""}{sale.customerName} · {sale.customerIdentification}</p>{sale.sellerName&&<p className="mt-1 break-words text-xs text-slate-600">Vendedor: {sale.sellerName}</p>}</div><span className="text-sm text-slate-600">{timeZone?dateTime.format(new Date(sale.issuedAt)):sale.issuedAt}</span><div><strong className="text-teal-800">{money.format(sale.total)}</strong><div className="mt-1 space-y-1 text-xs text-slate-600">{sale.payments?.map(payment=><p key={payment.paymentNumber} className="break-words"><span className="font-medium">{payment.formName||workSessionPaymentMethodName(payment.methodCode)}</span> · {money.format(payment.amount)}</p>)}</div></div><div className="flex gap-2"><button type="button" disabled={busy||detailLoading} onClick={()=>void showDetail(sale)} aria-label={`Ver detalle de ${sale.documentNumber}`} title="Ver detalle" className="grid h-9 w-9 place-items-center rounded-lg border bg-white"><Eye className="h-4 w-4"/></button><button type="button" disabled={busy||printing} aria-busy={printing} onClick={()=>void reprint(sale)} aria-label={`Imprimir ${sale.documentNumber}`} title="Imprimir" className="grid h-9 w-9 place-items-center rounded-lg bg-teal-700 text-white disabled:cursor-wait disabled:opacity-70">{printing?<Loader2 className="h-4 w-4 animate-spin"/>:<Printer className="h-4 w-4"/>}</button>{(sale.dispatchId?canCorrectDispatch:canCorrect)&&sale.documentType==="SalesReceipt"&&<button type="button" aria-label={`Corregir pago de ${sale.documentNumber}`} title={sale.closureId||sale.dispatchId?"Corregir el medio de recaudo de este comprobante":"Disponible cuando se cierre la sesión de caja"}
          onClick={()=>setCorrecting(sale)} disabled={!correctionBusinessId||!sale.closureId&&!sale.dispatchId}
          className="grid h-9 w-9 place-items-center rounded-lg border bg-white hover:bg-teal-50 disabled:cursor-not-allowed disabled:opacity-40"><Pencil className="h-4 w-4"/></button>}</div></article>})}</div>{!loading&&!results.length&&!error&&<p className="grid min-h-40 place-items-center text-center text-sm text-slate-500">No encontramos facturas con esos filtros.</p>}{loadingMore&&<p className="py-4 text-center text-sm text-teal-800">Cargando 20 más…</p>}{error&&<p className="py-4 text-center text-sm font-medium text-red-700" role="alert">{error}</p>}</div>
    </>}
  </section>{correcting&&correctionBusinessId&&<SalePaymentCorrectionDialog documentId={correcting.documentId.value} documentNumber={correcting.documentNumber}
    businessId={correctionBusinessId} dispatchId={correcting.dispatchId} onClose={()=>setCorrecting(null)}/>}</div>;
}
