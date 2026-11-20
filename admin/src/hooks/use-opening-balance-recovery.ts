"use client";

import { useCallback, useEffect, useMemo, useRef, useState, type SetStateAction } from "react";
import type { AccountingOpeningBalance } from "@/services/api/accounting";
import { loadCatalogDraft, removeCatalogDraft, saveCatalogDraft } from "@/lib/catalog-draft-store";
import { createLocalDraftCoordinator } from "@/lib/local-draft-coordinator";
import { openingCapture, openingCaptureMatchesServer, openingCaptureSignature, openingRecoveryKey, openingRecoveryScope,
  readOpeningCapture, validOpeningDate, type OpeningCapture, type OpeningDraftLine, type StoredOpeningCapture } from "@/lib/accounting-opening-recovery";

const storageMessage = "No fue posible recuperar o guardar la captura en este equipo. Usa Guardar borrador antes de salir.";

// Reuses the existing local store and serialized coordinator. This owns only
// recovery of the form; the accounting aggregate and its commands stay online.
export function useOpeningRecoveryScope(tenantId: string | null, userId: string | undefined, businessId: string | null, fallbackDate: string, enabled: boolean) {
  const scope = tenantId && userId && businessId && enabled ? openingRecoveryScope(tenantId, userId, businessId) : null;
  const [effectiveOn, setDate] = useState(fallbackDate), [loadedScope, setLoadedScope] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const coordinator = useMemo(() => createLocalDraftCoordinator<unknown>({ load: loadCatalogDraft, save: saveCatalogDraft, remove: removeCatalogDraft },
    () => setError(storageMessage)), []);
  const preferenceKey = scope ? `${scope}:selected-date` : null;
  const ready = !scope || loadedScope === scope;
  useEffect(() => {
    let active = true;
    if (!preferenceKey || !scope) { setDate(fallbackDate); setLoadedScope(null); return; }
    void coordinator.load(preferenceKey).then(value => {
      if (!active) return;
      const date = value && typeof value === "object" && "effectiveOn" in value ? value.effectiveOn : undefined;
      if (value !== undefined && !validOpeningDate(date)) setError(storageMessage);
      setDate(validOpeningDate(date) ? date : fallbackDate); setLoadedScope(scope);
    }).catch(() => { if (active) { setError(storageMessage); setDate(fallbackDate); setLoadedScope(scope); } });
    return () => { active = false; };
  }, [coordinator, fallbackDate, preferenceKey, scope]);
  useEffect(() => {
    const flush = () => { void coordinator.flushAll().catch(() => setError(storageMessage)); };
    const visibility = () => { if (document.visibilityState === "hidden") flush(); };
    window.addEventListener("pagehide", flush); document.addEventListener("visibilitychange", visibility);
    return () => { window.removeEventListener("pagehide", flush); document.removeEventListener("visibilitychange", visibility); flush(); };
  }, [coordinator]);
  const setEffectiveOn = useCallback((date: string) => {
    setDate(date);
    if (preferenceKey && validOpeningDate(date)) coordinator.update(preferenceKey, { effectiveOn: date });
  }, [coordinator, preferenceKey]);
  return { scope, coordinator, effectiveOn, setEffectiveOn, ready, error, setError };
}
export type OpeningRecoveryScope = ReturnType<typeof useOpeningRecoveryScope>;

export function useOpeningBalanceRecovery(recovery: OpeningRecoveryScope, effectiveOn: string, server: AccountingOpeningBalance | null | undefined, canRecover: boolean | undefined) {
  const [capture, setCapture] = useState(() => openingCapture(null, effectiveOn));
  const [ready, setReady] = useState(false), [restored, setRestored] = useState(false);
  const [conflict, setConflict] = useState<OpeningCapture | null>(null);
  const handled = useRef<string | null>(null), baseline = useRef<string | null>(null), hasLocal = useRef(false), available = useRef(true);
  const localRead = useRef(false);
  const queued = useRef<OpeningCapture | null>(null);
  const latest = useRef(capture); latest.current = capture;
  const { scope, coordinator, setError } = recovery;
  const key = scope ? openingRecoveryKey(scope, effectiveOn) : null;
  const stamp = `${key ?? effectiveOn}:${server?.batchId ?? "new"}:${server?.rowVersion ?? "new"}:${server?.status ?? "Draft"}`;
  const hasChanges = baseline.current !== null && openingCaptureSignature(capture) !== baseline.current;
  const removeLocal = useCallback(() => {
    hasLocal.current = false;
    queued.current = null;
    if (key) void coordinator.remove(key).catch(() => setError(storageMessage));
  }, [coordinator, key, setError]);
  const acceptServer = useCallback((value: AccountingOpeningBalance) => {
    removeLocal();
    const next = openingCapture(value, effectiveOn);
    baseline.current = openingCaptureSignature(next);
    localRead.current = true;
    handled.current = `${key ?? effectiveOn}:${value.batchId}:${value.rowVersion}:${value.status}`;
    setCapture(next); setReady(true); setRestored(false); setConflict(null);
  }, [effectiveOn, key, removeLocal]);

  useEffect(() => {
    if (server === undefined || canRecover === undefined || !recovery.ready || handled.current === stamp && (!canRecover || localRead.current)) return;
    let active = true;
    const next = openingCapture(server, effectiveOn), signature = openingCaptureSignature(next);
    const wasClean = localRead.current && baseline.current !== null && openingCaptureSignature(latest.current) === baseline.current && !hasLocal.current;
    const apply = (value: OpeningCapture) => { baseline.current = signature; handled.current = stamp; setCapture(value); setReady(true); };
    setReady(false); setConflict(null); setRestored(false);
    if (!key || !scope || !canRecover || server && server.status !== "Draft" || wasClean) {
      if (server && server.status !== "Draft") localRead.current = true;
      if (key && server && server.status !== "Draft" && hasLocal.current) removeLocal();
      apply(next); return;
    }
    void coordinator.load(key).then(value => {
      if (!active) return;
      localRead.current = true;
      if (value === undefined) { hasLocal.current = false; apply(next); return; }
      const stored = readOpeningCapture(value, scope, effectiveOn);
      if (!stored) { available.current = false; setError(storageMessage); apply(next); return; }
      hasLocal.current = true;
      queued.current = stored.capture;
      if (!openingCaptureMatchesServer(stored.capture, server)) { setConflict(stored.capture); apply(next); return; }
      setRestored(true); apply(stored.capture);
    }).catch(() => { if (active) { localRead.current = true; available.current = false; setError(storageMessage); apply(next); } });
    return () => { active = false; };
  }, [canRecover, coordinator, effectiveOn, key, recovery.ready, removeLocal, scope, server, setError, stamp]);

  useEffect(() => {
    if (!ready || conflict || !available.current || !key || !scope || !canRecover || handled.current !== stamp || server && server.status !== "Draft") return;
    if (hasChanges) {
      if (queued.current === capture) return;
      const snapshot: StoredOpeningCapture = { version: 1, kind: "capture", scope, effectiveOn, capture };
      hasLocal.current = true; queued.current = capture; coordinator.update(key, snapshot);
    } else if (hasLocal.current) removeLocal();
  }, [canRecover, capture, conflict, coordinator, effectiveOn, hasChanges, key, ready, removeLocal, scope, server, stamp]);
  useEffect(() => () => { if (key) void coordinator.flush(key).catch(() => setError(storageMessage)); }, [coordinator, key, setError]);

  const resolveConflict = (useLocal: boolean) => {
    if (!conflict || server === undefined) return;
    const base = openingCapture(server, effectiveOn);
    if (useLocal) {
      setCapture({ ...conflict, batchId: base.batchId, rowVersion: base.rowVersion }); setRestored(true);
    } else { removeLocal(); setCapture(base); }
    setConflict(null);
  };
  const setDescription = (description: string) => setCapture(value => ({ ...value, description }));
  const setLines = useCallback((update: SetStateAction<OpeningDraftLine[]>) => setCapture(value => ({ ...value, lines: typeof update === "function" ? update(value.lines) : update })), []);
  return { ...capture, setDescription, setLines, ready: ready && canRecover !== undefined && handled.current === stamp && (!canRecover || localRead.current), hasChanges, restored, conflict, resolveConflict, acceptServer };
}
