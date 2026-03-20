"use client";

import { useEffect, type ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import { businessesApi } from "@/services/api/businesses";
import type { PagedResponse } from "@/types/api";
import type { Business } from "@/types/entities";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { PageLoading } from "@/components/ui/page-loading";
import { PageError } from "@/components/ui/page-error";

const CONTEXT_PAGE_SIZE = 250;

async function fetchAllBusinessesForTenant(): Promise<Business[]> {
  const first = await businessesApi.list({ page: 1, pageSize: CONTEXT_PAGE_SIZE });
  if (Array.isArray(first)) return first;

  const paged = first as PagedResponse<Business>;
  const items = [...(paged.items ?? [])];
  const totalPages = Math.max(1, paged.totalPages ?? 1);
  for (let page = 2; page <= totalPages; page++) {
    const next = await businessesApi.list({ page, pageSize: CONTEXT_PAGE_SIZE });
    if (Array.isArray(next)) {
      items.push(...next);
      break;
    }
    items.push(...((next as PagedResponse<Business>).items ?? []));
  }
  return items;
}

export function BusinessContextProvider({
  children,
}: {
  children: ReactNode;
}) {
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  const { setBusinesses, isLoaded } = useBusinessContextStore();

  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: ["businesses", "context", "all-pages"],
    queryFn: fetchAllBusinessesForTenant,
    enabled: isAuthenticated,
    staleTime: 5 * 60 * 1000,
  });

  useEffect(() => {
    if (data === undefined) return;
    setBusinesses(data);
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
          onRetry={() => void refetch()}
        />
      </div>
    );
  }

  return <>{children}</>;
}
