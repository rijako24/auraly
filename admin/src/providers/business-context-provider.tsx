"use client";

import { useEffect, type ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import { businessesApi } from "@/services/api/businesses";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { PageLoading } from "@/components/ui/page-loading";
import { PageError } from "@/components/ui/page-error";

export function BusinessContextProvider({
  children,
}: {
  children: ReactNode;
}) {
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  const { setBusinesses, isLoaded } = useBusinessContextStore();

  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: ["businesses", "context"],
    queryFn: () => businessesApi.list(),
    enabled: isAuthenticated,
    staleTime: 5 * 60 * 1000,
  });

  useEffect(() => {
    if (!data) return;

    const items = Array.isArray(data) ? data : data.items ?? [];
    if (items.length > 0) {
      setBusinesses(items);
    }
  }, [data, setBusinesses]);

  if (!isAuthenticated) return null;

  if (isLoading && !isLoaded) {
    return (
      <div className="flex h-screen items-center justify-center">
        <PageLoading cards={0} />
      </div>
    );
  }

  if (isError && !isLoaded) {
    return (
      <div className="flex h-screen items-center justify-center p-8">
        <PageError
          message="No se pudieron cargar los negocios. Verifica tu conexión."
          onRetry={refetch}
        />
      </div>
    );
  }

  return <>{children}</>;
}
