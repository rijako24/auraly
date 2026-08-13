"use client";

import { useEffect, type ReactNode } from "react";
import { usePathname, useRouter } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import { defaultStartRoute, shouldApplyDefaultStart } from "@/lib/default-start-route";
import { executionContextApi } from "@/services/api/execution-context";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { useTenantContextStore } from "@/stores/tenant-context-store";
import { PageLoading } from "@/components/ui/page-loading";
import { PageError } from "@/components/ui/page-error";

export function BusinessContextProvider({ children }: { children: ReactNode }) {
  const pathname = usePathname();
  const router = useRouter();
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated);
  const setExecutionAccess = useAuthStore((state) => state.setExecutionAccess);
  const selectedTenantId = useTenantContextStore((state) => state.selectedTenantId);
  const setTenants = useTenantContextStore((state) => state.setTenants);
  const tenantsLoaded = useTenantContextStore((state) => state.isLoaded);
  const selectedBusinessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const setBusinesses = useBusinessContextStore((state) => state.setBusinesses);
  const businessesLoaded = useBusinessContextStore((state) => state.isLoaded);

  const tenantsQuery = useQuery({
    queryKey: ["execution-context", "tenants"],
    queryFn: executionContextApi.tenants,
    enabled: isAuthenticated,
    staleTime: 60_000,
  });

  useEffect(() => {
    if (tenantsQuery.data) setTenants(tenantsQuery.data);
  }, [setTenants, tenantsQuery.data]);

  const businessesQuery = useQuery({
    queryKey: ["execution-context", "businesses", selectedTenantId],
    queryFn: () => executionContextApi.businesses(selectedTenantId!),
    enabled: isAuthenticated && Boolean(selectedTenantId),
    staleTime: 60_000,
  });

  useEffect(() => {
    if (businessesQuery.data) setBusinesses(businessesQuery.data);
  }, [businessesQuery.data, setBusinesses]);

  const accessQuery = useQuery({
    queryKey: ["execution-context", "access", selectedTenantId, selectedBusinessId],
    queryFn: () => executionContextApi.access(selectedTenantId!, selectedBusinessId!),
    enabled:
      isAuthenticated &&
      Boolean(selectedTenantId) &&
      Boolean(selectedBusinessId) &&
      businessesLoaded,
    staleTime: 30_000,
  });

  useEffect(() => {
    if (accessQuery.data) {
      setExecutionAccess(accessQuery.data.roles, accessQuery.data.permissions);
    }
  }, [accessQuery.data, setExecutionAccess]);

  useEffect(() => {
    if (!accessQuery.data || !shouldApplyDefaultStart(pathname)) return;
    const target = defaultStartRoute(accessQuery.data.roles, accessQuery.data.permissions);
    if (target !== "/dashboard") router.replace(target);
  }, [accessQuery.data, pathname, router]);

  if (!isAuthenticated) return null;

  const loading =
    tenantsQuery.isLoading ||
    !tenantsLoaded ||
    (Boolean(selectedTenantId) && (businessesQuery.isLoading || !businessesLoaded)) ||
    (Boolean(selectedBusinessId) && accessQuery.isLoading);

  if (loading) {
    return (
      <div className="flex h-screen items-center justify-center">
        <PageLoading cards={0} />
      </div>
    );
  }

  const failed = tenantsQuery.isError || businessesQuery.isError || accessQuery.isError;
  if (failed || !selectedTenantId || !selectedBusinessId) {
    return (
      <div className="flex h-screen items-center justify-center p-8">
        <PageError
          message={
            failed
              ? "No se pudo cargar el contexto autorizado de trabajo."
              : !selectedTenantId
                ? "Este usuario no tiene acceso a ningún tenant activo."
                : "Este usuario no tiene acceso a ningún negocio activo del tenant."
          }
          onRetry={() => {
            void tenantsQuery.refetch();
            if (selectedTenantId) void businessesQuery.refetch();
            if (selectedBusinessId) void accessQuery.refetch();
          }}
        />
      </div>
    );
  }

  return <>{children}</>;
}