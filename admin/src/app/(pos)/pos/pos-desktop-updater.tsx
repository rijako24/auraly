"use client";

import { useEffect, useState } from "react";
import { Download, RefreshCw, X } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Progress } from "@/components/ui/progress";
import { loadPosInstaller } from "@/services/pos/pos-installer";
import {
  desktopUpdateAction,
  isDesktopUpdateStatus,
  type DesktopUpdateStatus,
} from "./pos-desktop-update-protocol";

type DesktopWebView = {
  addEventListener(type: "message", listener: (event: MessageEvent<unknown>) => void): void;
  removeEventListener(type: "message", listener: (event: MessageEvent<unknown>) => void): void;
  postMessage(message: unknown): void;
};

function currentWebView() {
  return (
    window as typeof window & { chrome?: { webview?: DesktopWebView } }
  ).chrome?.webview;
}

export function PosDesktopUpdater() {
  const [update, setUpdate] = useState<DesktopUpdateStatus | null>(null);

  useEffect(() => {
    const webview = currentWebView();
    if (!webview) return;

    let cancelled = false;
    const receiveStatus = (event: MessageEvent<unknown>) => {
      if (isDesktopUpdateStatus(event.data)) setUpdate(event.data);
    };
    webview.addEventListener("message", receiveStatus);

    const check = async () => {
      try {
        const installer = await loadPosInstaller();
        if (cancelled) return;
        webview.postMessage({
          type: "auraly-pos-update-discovered",
          downloadUrl: installer.downloadUrl,
          version: installer.version,
          sha256: installer.sha256,
        });
      } catch {
        // El descubrimiento no debe interrumpir una venta activa.
      }
    };

    const timer = window.setTimeout(check, 3500);
    return () => {
      cancelled = true;
      window.clearTimeout(timer);
      webview.removeEventListener("message", receiveStatus);
    };
  }, []);

  if (!update || update.status === "deferred") return null;

  const downloading = update.status === "downloading" || update.status === "verifying";
  const ready = update.status === "ready";
  const available = update.status === "available" || update.status === "error";
  const send = (action: "download" | "restart" | "later") => {
    currentWebView()?.postMessage({ type: desktopUpdateAction(action) });
    if (action === "later") setUpdate(null);
  };

  return (
    <aside
      aria-live="polite"
      className="fixed bottom-4 right-4 z-[90] w-[min(24rem,calc(100vw-2rem))] rounded-xl border border-border/80 bg-background/95 p-4 shadow-xl backdrop-blur"
    >
      <div className="flex items-start gap-3">
        <div className="mt-0.5 rounded-full bg-primary/10 p-2 text-primary">
          {downloading ? <RefreshCw className="size-4 animate-spin" /> : <Download className="size-4" />}
        </div>
        <div className="min-w-0 flex-1">
          <div className="flex items-start justify-between gap-3">
            <div>
              <p className="text-sm font-semibold">Actualización de Auraly</p>
              <p className="mt-1 text-xs leading-5 text-muted-foreground">{update.message}</p>
            </div>
            {!downloading && !ready ? (
              <Button
                aria-label="Cerrar aviso de actualización"
                className="-mr-2 -mt-2"
                onClick={() => setUpdate(null)}
                size="icon"
                variant="ghost"
              >
                <X />
              </Button>
            ) : null}
          </div>

          {downloading ? <Progress className="mt-3 h-1.5" value={update.progress ?? 0} /> : null}
          {available ? (
            <Button className="mt-3" onClick={() => send("download")} size="sm">
              {update.status === "error" ? "Intentar de nuevo" : "Descargar"}
            </Button>
          ) : null}
          {ready ? (
            <div className="mt-3 flex flex-wrap gap-2">
              <Button onClick={() => send("restart")} size="sm">Reiniciar ahora</Button>
              <Button onClick={() => send("later")} size="sm" variant="outline">Más tarde</Button>
            </div>
          ) : null}
        </div>
      </div>
    </aside>
  );
}
