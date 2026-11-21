"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { PosInvoiceSearchDialog } from "@/app/(pos)/pos/pos-invoice-search-dialog";
import { posPublicError } from "@/app/(pos)/pos/pos-public-error";
import { PageError } from "@/components/ui/page-error";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { useTenantContextStore } from "@/stores/tenant-context-store";
import { partiesApi } from "@/services/api/parties";
import { tenantsApi } from "@/services/api/tenants";
import { closePrintPreview, loadServerIssuedSaleReceipt, openHalfLetterPrintPreview,
  recordServerIssuedSaleReprint, renderReceiptsHalfLetter, renderReceiptsReceipt,
  searchServerHistoryCustomers, searchServerHistoryProducts, searchServerIssuedSales } from "@/services/pos/online-pos-client";
import { loadBrowserPrinterConfiguration, PosEdgeClient, readEdgeTokenFromLaunch, readEdgeUserSession,
  type PosIssuedSaleFilters, type PosIssuedSaleSummary, type PosPrintableReceipt } from "@/services/pos/pos-edge-client";
import { resolvePosExecutionMode } from "@/services/pos/pos-launch-session";

export default function InvoicesPage() {
  const user=useAuthStore(state=>state.user);
  const business=useBusinessContextStore(state=>state.businesses.find(value=>value.businessId===state.selectedBusinessId));
  const businessId=useBusinessContextStore(state=>state.selectedBusinessId);
  const tenantName=useTenantContextStore(state=>state.tenants.find(value=>value.tenantId===state.selectedTenantId)?.name);
  const canRead=user?.permissions.includes("sales.reprint")??false;
  const [printingReady,setPrintingReady]=useState(false);
  const [error,setError]=useState<string|null>(null);
  const edge=useRef<PosEdgeClient|null>(null);
  const receipts=useRef(new Map<string,PosPrintableReceipt>());
  const scope=useMemo(()=>businessId?{businessId}:null,[businessId]);
  useEffect(()=>{
    let active=true;
    receipts.current.clear();edge.current=null;
    setPrintingReady(false);setError(null);
    if(!businessId||!canRead)return;
    void (async()=>{
      // React may clean up an entry before its effect becomes active.
      await Promise.resolve();
      if(!active)return;
      const token=readEdgeTokenFromLaunch();
      const client=token?new PosEdgeClient(token,readEdgeUserSession()):null;
      const health=client?await client.health().catch(()=>null):null;
      if(!active)return;
      if(client&&health?.businessId===businessId&&resolvePosExecutionMode(true,health)==="edge") edge.current=client;
      else await tenantsApi.preparePrintBrandingForWorkspaceEntry();
      if(active)setPrintingReady(true);
    })().catch(()=>{if(active)setError("No fue posible preparar la impresión de esta sede. Actualiza la página para reintentar.");});
    return ()=>{active=false;};
  },[businessId,canRead]);
  const search=useCallback((filters:PosIssuedSaleFilters,skip:number)=>{
    if(!scope)throw new Error("Selecciona una sede.");return searchServerIssuedSales(scope,filters,skip,20);
  },[scope]);
  const customers=useCallback((term:string,skip:number)=>{
    if(!scope)throw new Error("Selecciona una sede.");return searchServerHistoryCustomers(scope,term,skip,20);
  },[scope]);
  const roleOptions=useCallback((role:"Seller"|"User",search:string,page:number,pageSize:number)=>partiesApi.roleOptions({role,search,page,pageSize,includeInactive:true}),[]);
  const products=useCallback((term:string,skip:number)=>{
    if(!scope)throw new Error("Selecciona una sede.");return searchServerHistoryProducts(scope,term,skip,20);
  },[scope]);
  const detail=useCallback(async(sale:PosIssuedSaleSummary)=>{
    if(!scope)throw new Error("Selecciona una sede.");
    const cached=receipts.current.get(sale.documentId.value);if(cached)return cached;
    const receipt=await loadServerIssuedSaleReceipt(scope,sale.documentId.value);
    receipts.current.set(sale.documentId.value,receipt);return receipt;
  },[scope]);
  const reprint=useCallback(async(sale:PosIssuedSaleSummary)=>{
    if(!scope||!printingReady)throw new Error("Espera a que se prepare la impresión.");
    setError(null);
    try {
      const receipt=await detail(sale);
      if(edge.current)await edge.current.printReceipt(receipt,null);
      else {
        const preview=openHalfLetterPrintPreview();
        try {
          const config=loadBrowserPrinterConfiguration();
          const context={...scope,businessName:business?.name,tenantName};
          const format=config.posOutputFormat??"Receipt";
          if(format==="Receipt")await renderReceiptsReceipt(preview,[receipt],context,config.receiptPaperWidthMillimeters);
          else await renderReceiptsHalfLetter(preview,[receipt],context,format);
        } catch(caught) {closePrintPreview(preview);throw caught;}
      }
      await recordServerIssuedSaleReprint(scope,sale.documentId.value);
    } catch(caught) {setError(posPublicError(caught instanceof Error?caught.message:null,"No fue posible imprimir el comprobante. Intenta de nuevo.")??"No fue posible imprimir el comprobante.");}
  },[scope,printingReady,detail,business?.name,tenantName]);
  if(!businessId)return <PageError message="Selecciona una sede para consultar sus facturas."/>;
  if(!user?.permissions.includes("sales.reprint"))return <PageError message="No tienes permiso para consultar y reimprimir facturas."/>;
  return <div className="min-w-0 space-y-3">{error&&<p role="alert" className="rounded-xl border border-red-200 bg-red-50 p-3 text-sm text-red-700">{error}</p>}
    <PosInvoiceSearchDialog key={businessId} embedded timeZone={business?.timeZone??undefined} busy={!printingReady} canCorrect={user.permissions.includes("sales.payments.correct")&&user.permissions.includes("work-sessions.closures.reconcile")&&user.permissions.includes("work-sessions.differences.read")} canCorrectDispatch={user.permissions.includes("sales.payments.correct")&&user.permissions.includes("dispatches.settle")}
      scopeKey={businessId} onSearchRoleOptions={roleOptions} onSearch={search} onSearchCustomers={customers} onSearchProducts={products} onDetail={detail} onReprint={reprint} onCancel={()=>undefined}/>
  </div>;
}
