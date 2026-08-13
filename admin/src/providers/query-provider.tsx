"use client";

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useEffect, useState, type ReactNode } from "react";
import { SESSION_EXPIRED_EVENT } from "@/lib/auth-session";

export function QueryProvider({ children }: { children: ReactNode }) {
  const [queryClient] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            staleTime: 60 * 1000,
            retry: 1,
            refetchOnWindowFocus: false,
          },
        },
      })
  );

  useEffect(() => {
    const clearSessionData = () => queryClient.clear();
    window.addEventListener(SESSION_EXPIRED_EVENT, clearSessionData);
    return () => window.removeEventListener(SESSION_EXPIRED_EVENT, clearSessionData);
  }, [queryClient]);

  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}
