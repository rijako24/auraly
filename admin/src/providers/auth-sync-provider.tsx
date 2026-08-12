"use client";

import { useEffect, type ReactNode } from "react";
import { authApi } from "@/services/api/auth";
import { useAuthStore } from "@/stores/auth-store";

/**
 * Revalidates the persisted browser projection against the authoritative
 * server session whenever the application shell is mounted.
 */
export function AuthSyncProvider({ children }: { children: ReactNode }) {
  const clearAuth = useAuthStore((s) => s.clearAuth);
  const setAuth = useAuthStore((s) => s.setAuth);

  useEffect(() => {

    authApi
      .me()
      .then((data) => setAuth(data))
      .catch(clearAuth);
  }, [clearAuth, setAuth]);

  return <>{children}</>;
}
