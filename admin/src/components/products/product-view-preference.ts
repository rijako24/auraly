"use client";

import { useCallback, useSyncExternalStore } from "react";
import { toast } from "sonner";
import type { ViewMode } from "@/components/tables/data-table-toolbar";

const changedEvent = "auraly:products-view-changed";
let snapshot: { key: string; mode: ViewMode } | null = null;

function subscribe(onChanged: () => void) {
  const onStorage = () => { snapshot = null; onChanged(); };
  window.addEventListener("storage", onStorage);
  window.addEventListener(changedEvent, onChanged);
  return () => {
    window.removeEventListener("storage", onStorage);
    window.removeEventListener(changedEvent, onChanged);
  };
}

function read(key: string | null): ViewMode {
  if (!key) return "table";
  if (snapshot?.key === key) return snapshot.mode;
  let mode: ViewMode = "table";
  try {
    const stored = window.localStorage.getItem(key);
    if (stored === "card" || stored === "list") mode = stored;
  } catch {
    // Storage restrictions affect only this visual preference; table remains usable.
  }
  snapshot = { key, mode };
  return mode;
}

export function useProductViewPreference(tenantId?: string | null, userId?: string) {
  const key = tenantId && userId ? `auraly.products.view:${tenantId}:${userId}` : null;
  const viewMode = useSyncExternalStore(subscribe, () => read(key), (): ViewMode | null => null);
  const setViewMode = useCallback((mode: ViewMode) => {
    if (!key) return;
    snapshot = { key, mode };
    try { window.localStorage.setItem(key, mode); }
    catch { toast.error("La vista cambió, pero tu navegador no pudo guardarla para la próxima visita."); }
    window.dispatchEvent(new Event(changedEvent));
  }, [key]);
  return { viewMode, setViewMode };
}
