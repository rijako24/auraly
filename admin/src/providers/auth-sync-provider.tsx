"use client";

import { useEffect, type ReactNode } from "react";
import { authApi } from "@/services/api/auth";
import { useAuthStore } from "@/stores/auth-store";

/**
 * Syncs auth store with server when user has cookies but store is empty
 * (e.g. after refresh with cleared localStorage, or new tab with existing session).
 */
export function AuthSyncProvider({ children }: { children: ReactNode }) {
  const user = useAuthStore((s) => s.user);
  const setAuth = useAuthStore((s) => s.setAuth);

  useEffect(() => {
    if (user) return;

    authApi
      .me()
      .then((data) => setAuth(data))
      .catch(() => {
        /* 401 handled by api client redirect */
      });
  }, [user, setAuth]);

  return <>{children}</>;
}
