"use client";

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useEffect, useState, type ReactNode } from "react";
import { LogOut } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import {
  SESSION_EXPIRED_EVENT,
  type SessionExpiredEventDetail,
} from "@/lib/auth-session";

export function QueryProvider({ children }: { children: ReactNode }) {
  const [expired, setExpired] = useState<SessionExpiredEventDetail | null>(null);
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
    const showSessionClosure = (event: Event) => {
      event.preventDefault();
      queryClient.clear();
      const detail = (event as CustomEvent<SessionExpiredEventDetail>).detail;
      setExpired(detail ?? {
        destination: "/login",
        title: "Sesión finalizada",
        message: "Esta sesión ya no está activa. Inicia sesión nuevamente para continuar.",
      });
    };
    window.addEventListener(SESSION_EXPIRED_EVENT, showSessionClosure);
    return () => window.removeEventListener(SESSION_EXPIRED_EVENT, showSessionClosure);
  }, [queryClient]);

  return (
    <QueryClientProvider client={queryClient}>
      {children}
      <Dialog open={expired !== null}>
        <DialogContent
          showClose={false}
          className="max-w-md rounded-3xl border-teal-100 p-0 shadow-2xl"
          onEscapeKeyDown={(event) => event.preventDefault()}
          onPointerDownOutside={(event) => event.preventDefault()}
        >
          <DialogHeader className="px-7 pt-7 text-left">
            <div className="mb-3 flex h-12 w-12 items-center justify-center rounded-2xl bg-teal-50 text-teal-700">
              <LogOut className="h-6 w-6" aria-hidden="true" />
            </div>
            <DialogTitle className="text-xl text-slate-950">
              {expired?.title}
            </DialogTitle>
            <DialogDescription className="pt-2 text-base leading-6 text-slate-600">
              {expired?.message}
            </DialogDescription>
          </DialogHeader>
          <DialogFooter className="border-t bg-slate-50/80 px-7 py-5">
            <Button
              type="button"
              className="min-w-32 rounded-xl bg-teal-700 hover:bg-teal-800"
              onClick={() => {
                if (expired) window.location.replace(expired.destination);
              }}
            >
              Entendido
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </QueryClientProvider>
  );
}
