"use client";

import { useCallback, useEffect, useRef, useState } from "react";

import { loadCatalogDraft, removeCatalogDraft, saveCatalogDraft } from "@/lib/catalog-draft-store";

export function useCatalogDraft<T>({
  draftKey,
  enabled,
  value,
  restore,
  onError,
}: {
  draftKey: string | null;
  enabled: boolean;
  value: T;
  restore: (value: T) => void;
  onError: (operation: "load" | "save" | "remove") => void;
}) {
  const [hydratedKey, setHydratedKey] = useState<string | null>(null);
  const writeQueue = useRef(Promise.resolve());
  const pendingSave = useRef<{ key: string; value: T } | null>(null);
  const saveTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const restoreRef = useRef(restore);
  const errorRef = useRef(onError);
  const saveFailureShown = useRef(false);
  restoreRef.current = restore;
  errorRef.current = onError;

  const flushPendingSave = useCallback(() => {
    if (saveTimer.current !== null) clearTimeout(saveTimer.current);
    saveTimer.current = null;
    const pending = pendingSave.current;
    pendingSave.current = null;
    if (!pending) return;
    writeQueue.current = writeQueue.current
      .then(() => saveCatalogDraft(pending.key, pending.value))
      .catch(() => {
        if (saveFailureShown.current) return;
        saveFailureShown.current = true;
        errorRef.current("save");
      });
  }, []);

  useEffect(() => () => flushPendingSave(), [draftKey, enabled, flushPendingSave]);

  useEffect(() => {
    if (!enabled || !draftKey) {
      setHydratedKey(null);
      return;
    }
    let active = true;
    setHydratedKey(null);
    void writeQueue.current.then(() => loadCatalogDraft<T>(draftKey))
      .then(stored => {
        if (!active) return;
        if (stored) restoreRef.current(stored);
        setHydratedKey(draftKey);
      })
      .catch(() => {
        if (!active) return;
        errorRef.current("load");
        setHydratedKey(draftKey);
      });
    return () => { active = false; };
  }, [draftKey, enabled]);

  useEffect(() => {
    if (!enabled || !draftKey || hydratedKey !== draftKey) return;
    pendingSave.current = { key: draftKey, value };
    if (saveTimer.current !== null) clearTimeout(saveTimer.current);
    saveTimer.current = setTimeout(flushPendingSave, 200);
  }, [draftKey, enabled, flushPendingSave, hydratedKey, value]);

  return useCallback(async () => {
    if (!draftKey) return;
    setHydratedKey(null);
    if (saveTimer.current !== null) clearTimeout(saveTimer.current);
    saveTimer.current = null;
    pendingSave.current = null;
    try {
      await writeQueue.current;
      await removeCatalogDraft(draftKey);
    } catch {
      errorRef.current("remove");
    }
  }, [draftKey]);
}
