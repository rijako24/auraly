"use client";

import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from "react";
import { Download, RefreshCw } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Progress } from "@/components/ui/progress";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip";
import { desktopUpdateAction, isDesktopUpdateStatus, type DesktopUpdateStatus } from "@/lib/desktop-update-protocol";
import { loadPosInstaller } from "@/services/pos/pos-installer";
import { useAuthStore } from "@/stores/auth-store";

type DesktopWebView = {
  addEventListener(type: "message", listener: (event: MessageEvent<unknown>) => void): void;
  removeEventListener(type: "message", listener: (event: MessageEvent<unknown>) => void): void;
  postMessage(message: unknown): void;
};

function currentWebView() {
  return (window as typeof window & { chrome?: { webview?: DesktopWebView } }).chrome?.webview;
}

const UpdateContext = createContext<{ update: DesktopUpdateStatus | null; activate: () => void } | null>(null);

export function DesktopUpdateProvider({ children }: { children: ReactNode }) {
  const authenticated = useAuthStore(state => state.hasHydrated && state.isAuthenticated);
  const [update, setUpdate] = useState<DesktopUpdateStatus | null>(null);
  const [open, setOpen] = useState(false);
  const checkAgain = useRef<(() => Promise<boolean>) | null>(null);

  useEffect(() => {
    const webview = currentWebView();
    if (!webview || !authenticated) return;
    let cancelled = false;
    let checking = false;
    let discovered = false;
    const check = async () => {
      if (checking) return false;
      checking = true;
      try {
        const installer = await loadPosInstaller();
        if (!cancelled) webview.postMessage({
          type: "auraly-pos-update-discovered",
          downloadUrl: installer.downloadUrl,
          version: installer.version,
          sha256: installer.sha256,
        });
        return !cancelled;
      } catch {
        if (!cancelled) setUpdate({ type: "auraly-pos-update-status", status: "check-error",
          version: null, progress: null, message: "No fue posible consultar las actualizaciones. Puedes intentarlo nuevamente." });
        return false;
      } finally {
        checking = false;
      }
    };
    checkAgain.current = check;
    const receiveStatus = (event: MessageEvent<unknown>) => {
      if (!isDesktopUpdateStatus(event.data)) return;
      const status = event.data;
      setUpdate(status);
      if (status.status === "idle" && !discovered) {
        discovered = true;
        void check();
      }
      if (status.status === "ready" || status.status === "restart-error") setOpen(true);
    };
    webview.addEventListener("message", receiveStatus);
    const timer = window.setTimeout(() => webview.postMessage({ type: desktopUpdateAction("check") }), 3500);
    return () => {
      cancelled = true;
      checkAgain.current = null;
      window.clearTimeout(timer);
      webview.removeEventListener("message", receiveStatus);
    };
  }, [authenticated]);

  const visibleUpdate = authenticated ? update : null;
  const downloading = update?.status === "downloading" || update?.status === "verifying";
  const ready = update?.status === "ready" || update?.status === "restart-error";
  const restarting = update?.status === "restarting";
  const available = update?.status === "available" || update?.status === "error";
  const send = (action: "download" | "restart") => currentWebView()?.postMessage({ type: desktopUpdateAction(action) });
  const download = async () => {
    // The published installer can change while the app remains open. Refresh
    // its version/hash before retrying a rejected download, not on every render.
    if (update?.status === "error" && !await checkAgain.current?.()) return;
    send("download");
  };
  const activate = () => {
    setOpen(true);
    if (available) void download();
  };

  return <UpdateContext.Provider value={{ update: visibleUpdate, activate }}>
    {children}
    <Dialog open={authenticated && open} onOpenChange={value => { if (!restarting) setOpen(value); }}>
      <DialogContent showClose={!restarting}>
        <DialogHeader>
          <DialogTitle>Actualización de Auraly{update?.version ? ` · ${update.version}` : ""}</DialogTitle>
          <DialogDescription>{update?.message}</DialogDescription>
        </DialogHeader>
        {downloading && <Progress aria-label="Progreso de descarga" value={update?.progress ?? 0} />}
        {ready && <p className="text-sm text-muted-foreground">Al reiniciar se cerrará Auraly para instalar la actualización. Si eliges más tarde, la descarga se conserva y volveremos a recordártela al abrir la aplicación.</p>}
        <DialogFooter>
          {!restarting && <Button variant="outline" onClick={() => setOpen(false)}>{ready ? "Más tarde" : "Cerrar"}</Button>}
          {available && <Button onClick={() => void download()}><Download className="mr-2 size-4" />{update?.status === "error" ? "Reintentar descarga" : "Descargar"}</Button>}
          {update?.status === "check-error" && <Button onClick={() => { void checkAgain.current?.(); }}>Reintentar consulta</Button>}
          {ready && <Button onClick={() => send("restart")}><RefreshCw className="mr-2 size-4" />Reiniciar ahora</Button>}
          {restarting && <Button disabled><RefreshCw className="mr-2 size-4 animate-spin" />Abriendo instalador…</Button>}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </UpdateContext.Provider>;
}

export function DesktopUpdateButton() {
  const context = useContext(UpdateContext);
  const update = context?.update;
  if (!context || !update || update.status === "idle") return null;
  const busy = ["downloading", "verifying", "restarting"].includes(update.status);
  const ready = update.status === "ready" || update.status === "restart-error";
  const label = busy ? update.message : ready ? `Actualización ${update.version} lista para instalar`
    : update.status === "check-error" ? "Revisar actualizaciones de Auraly" : `Descargar nueva versión ${update.version}`;
  return <TooltipProvider delayDuration={200}><Tooltip><TooltipTrigger asChild>
    <Button type="button" size="icon" variant="ghost" aria-label={label} onClick={context.activate}
      className="relative mx-1 size-10 rounded-xl border border-sky-200 bg-sky-50 text-sky-700 shadow-sm transition hover:bg-sky-100 hover:text-sky-900 dark:border-sky-700 dark:bg-sky-950 dark:text-sky-300">
      {busy || ready ? <RefreshCw className={`size-5 ${busy ? "animate-spin" : ""}`} /> : <Download className="size-5" />}
      {!busy && <span className="absolute -right-0.5 -top-0.5 size-2.5 rounded-full border-2 border-background bg-sky-500" />}
    </Button>
  </TooltipTrigger><TooltipContent side="bottom">{label}</TooltipContent></Tooltip></TooltipProvider>;
}
