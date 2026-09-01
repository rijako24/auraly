"use client";

import { useEffect, useRef, useState, type ReactNode } from "react";
import { usePathname } from "next/navigation";
import { CloudOff } from "lucide-react";
import { toast } from "sonner";
import { flushDispatchOutbox } from "@/lib/dispatch-offline-store";
import { flushPendingRouteVisits } from "@/lib/daily-route-store";
import { flushSellerOrderOutbox } from "@/lib/seller-order-offline-store";
import { routesApi } from "@/services/api/routes";
import { sellerOrdersApi } from "@/services/api/seller-orders";
import { PwaInstallPrompt } from "@/components/pwa/pwa-install-prompt";
import { shouldOfferPwaInstall } from "@/lib/pwa-install-visibility";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { ensurePosApprovalPushSubscription } from "@/lib/pos-approval-push";
import { prepareCurrentAppShell } from "@/lib/offline-app-shell";
import { SELLER_ORDER_SYNC_COMPLETED_EVENT, SELLER_ORDER_SYNC_REQUEST_EVENT } from "@/services/orders/seller-order-reliability";

export function PwaProvider({ children }: { children: ReactNode }) {
  const [online,setOnline]=useState(true),[syncing,setSyncing]=useState(false);
  const pathname=usePathname();
  const isAuthenticated=useAuthStore(state=>state.isAuthenticated);
  const userId=useAuthStore(state=>state.user?.userId??"");
  const permissions=useAuthStore(state=>state.user?.permissions??[]);
  const businessId=useBusinessContextStore(state=>state.selectedBusinessId);
  const synchronizing=useRef(false);
  useEffect(()=>{
    let active=true;
    const synchronize=async()=>{if(!navigator.onLine||synchronizing.current)return;synchronizing.current=true;setSyncing(true);try{await flushDispatchOutbox();if(userId){const orders=await flushSellerOrderOutbox(userId,sellerOrdersApi.create,(routeId,request)=>routesApi.recordVisit(routeId,request));if(orders.reviews.length)toast.warning(`${orders.reviews.length} ${orders.reviews.length===1?"pedido requiere":"pedidos requieren"} ajustar inventario`,{description:"Ábrelos en Mi ruta, cambia las cantidades o elimina los productos sin existencia."});await flushPendingRouteVisits(userId,(routeId,request)=>routesApi.recordVisit(routeId,request));if(orders.uploaded)window.dispatchEvent(new Event(SELLER_ORDER_SYNC_COMPLETED_EVENT))}}finally{synchronizing.current=false;if(active)setSyncing(false)}};
    const connected=()=>{setOnline(true);void synchronize()};const disconnected=()=>setOnline(false);const visible=()=>{if(document.visibilityState==="visible")void synchronize()};
    setOnline(navigator.onLine);window.addEventListener("online",connected);window.addEventListener("offline",disconnected);window.addEventListener("focus",synchronize);window.addEventListener(SELLER_ORDER_SYNC_REQUEST_EVENT,synchronize);document.addEventListener("visibilitychange",visible);
    if("serviceWorker" in navigator&&process.env.NODE_ENV==="production")void navigator.serviceWorker.register("/app-sw.js",{scope:"/",updateViaCache:"none"}).then(registration=>registration.update());
    void synchronize();
    return()=>{active=false;window.removeEventListener("online",connected);window.removeEventListener("offline",disconnected);window.removeEventListener("focus",synchronize);window.removeEventListener(SELLER_ORDER_SYNC_REQUEST_EVENT,synchronize);document.removeEventListener("visibilitychange",visible)};
  },[userId]);
  useEffect(()=>{
    if(!isAuthenticated||!businessId||!permissions.includes("pos.approvals.receive_notifications")||typeof Notification==="undefined"||Notification.permission!=="granted")return;
    void ensurePosApprovalPushSubscription().catch(()=>undefined);
  },[isAuthenticated,businessId,permissions]);
  useEffect(()=>{
    if(!isAuthenticated||!online)return;
    void prepareCurrentAppShell(pathname).catch(()=>undefined);
  },[isAuthenticated,online,pathname]);
  return <>{children}{shouldOfferPwaInstall(isAuthenticated,pathname)&&<PwaInstallPrompt />}{!online&&<div role="status" className="fixed inset-x-3 bottom-3 z-[100] mx-auto flex max-w-md items-center justify-center gap-2 rounded-2xl bg-amber-950 px-4 py-3 text-sm font-medium text-white shadow-2xl"><CloudOff className="h-4 w-4"/>Sin conexión. Los cambios se guardarán y subirán automáticamente.</div>}{online&&syncing&&<div role="status" className="fixed bottom-3 right-3 z-[100] rounded-full bg-slate-950 px-3 py-2 text-xs text-white shadow-xl">Sincronizando…</div>}</>;
}
