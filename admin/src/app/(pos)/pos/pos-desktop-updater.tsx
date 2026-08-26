"use client";

import { useEffect } from "react";

import { loadPosInstaller } from "@/services/pos/pos-installer";

type DesktopWebView = {
  postMessage(message: unknown): void;
};

export function PosDesktopUpdater() {
  useEffect(() => {
    const webview = (
      window as typeof window & { chrome?: { webview?: DesktopWebView } }
    ).chrome?.webview;
    if (!webview) return;

    let cancelled = false;
    const check = async () => {
      try {
        const installer = await loadPosInstaller();
        if (cancelled) return;
        webview.postMessage({
          type: "auraly-pos-update",
          downloadUrl: installer.downloadUrl,
          version: installer.version,
          sha256: installer.sha256,
        });
      } catch {
        // Update discovery is best-effort; the active sale must remain usable.
      }
    };

    const timer = window.setTimeout(check, 3500);
    return () => {
      cancelled = true;
      window.clearTimeout(timer);
    };
  }, []);

  return null;
}
