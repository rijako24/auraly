"use client";

import { useEffect, useRef, useState, type ReactNode } from "react";
import { CloudOff } from "lucide-react";
import { flushDispatchOutbox } from "@/lib/dispatch-offline-store";
import { flushPendingRouteVisits } from "@/lib/daily-route-store";
import { flushSellerOrderOutbox } from "@/lib/seller-order-offline-store";
import { routesApi } from "@/services/api/routes";
import { sellerOrdersApi } from "@/services/api/seller-orders";
import { PwaInstallPrompt } from "@/components/pwa/pwa-install-prompt";

export function PwaProvider({ children }: { children: ReactNode }) {
  const [online,setOnline]=useState(true),[syncing,setSyncing]=useState(false);
  const synchronizing=useRef(false);
  useEffect(()=>{
    let active=true;
    const synchronize=async()=>{if(!navigator.onLine||synchronizing.current)return;synchronizing.current=true;setSyncing(true);try{await flushDispatchOutbox();await flushSellerOrderOutbox(sellerOrdersApi.create,(routeId,request)=>routesApi.recordVisit(routeId,request));await flushPendingRouteVisits((routeId,request)=>routesApi.recordVisit(routeId,request))}finally{synchronizing.current=false;if(active)setSyncing(false)}};
    const connected=()=>{setOnline(true);void synchronize()};const disconnected=()=>setOnline(false);const visible=()=>{if(document.visibilityState==="visible")void synchronize()};
    setOnline(navigator.onLine);window.addEventListener("online",connected);window.addEventListener("offline",disconnected);window.addEventListener("focus",synchronize);document.addEventListener("visibilitychange",visible);
    if("serviceWorker" in navigator&&process.env.NODE_ENV==="production")void navigator.serviceWorker.register("/app-sw.js",{scope:"/"});
    void synchronize();
    return()=>{active=false;window.removeEventListener("online",connected);window.removeEventListener("offline",disconnected);window.removeEventListener("focus",synchronize);document.removeEventListener("visibilitychange",visible)};
  },[]);
  return <>{children}<PwaInstallPrompt />{!online&&<div role="status" className="fixed inset-x-3 bottom-3 z-[100] mx-auto flex max-w-md items-center justify-center gap-2 rounded-2xl bg-amber-950 px-4 py-3 text-sm font-medium text-white shadow-2xl"><CloudOff className="h-4 w-4"/>Sin conexión. Los cambios se guardarán y subirán automáticamente.</div>}{online&&syncing&&<div role="status" className="fixed bottom-3 right-3 z-[100] rounded-full bg-slate-950 px-3 py-2 text-xs text-white shadow-xl">Sincronizando…</div>}</>;
}
