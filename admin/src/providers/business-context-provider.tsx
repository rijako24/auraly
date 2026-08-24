"use client";

import { useEffect, type ReactNode } from "react";
import { usePathname, useRouter } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import { defaultStartRoute, shouldRestoreOperationalStart } from "@/lib/default-start-route";
import { executionContextApi } from "@/services/api/execution-context";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { useTenantContextStore } from "@/stores/tenant-context-store";
import { PageError } from "@/components/ui/page-error";
import { AuralyLoadingState } from "@/components/brand/auraly-loading-state";

export function BusinessContextProvider({ children }: { children: ReactNode }) {
  const pathname = usePathname();
  const router = useRouter();
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated);
  const userId = useAuthStore((state) => state.user?.userId ?? null);
  const setExecutionAccess = useAuthStore((state) => state.setExecutionAccess);
  const selectedTenantId = useTenantContextStore((state) => state.selectedTenantId);
  const setTenants = useTenantContextStore((state) => state.setTenants);
  const tenantsLoaded = useTenantContextStore((state) => state.isLoaded);
  const selectedBusinessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const setBusinesses = useBusinessContextStore((state) => state.setBusinesses);
  const businessesLoaded = useBusinessContextStore((state) => state.isLoaded);

  const tenantsQuery = useQuery({
    queryKey: ["execution-context", userId, "tenants"],
    queryFn: executionContextApi.tenants,
    enabled: isAuthenticated && Boolean(userId),
    staleTime: 60_000,
  });

  useEffect(() => {
    if (tenantsQuery.data) setTenants(tenantsQuery.data);
  }, [setTenants, tenantsQuery.data]);

  const businessesQuery = useQuery({
    queryKey: ["execution-context", userId, "businesses", selectedTenantId],
    queryFn: () => executionContextApi.businesses(selectedTenantId!),
    enabled: isAuthenticated && Boolean(selectedTenantId),
    staleTime: 60_000,
  });

  useEffect(() => {
    if (businessesQuery.data) setBusinesses(businessesQuery.data);
  }, [businessesQuery.data, setBusinesses]);

  const accessQuery = useQuery({
    queryKey: ["execution-context", userId, "access", selectedTenantId, selectedBusinessId],
    queryFn: () => executionContextApi.access(selectedTenantId!, selectedBusinessId!),
    enabled:
      isAuthenticated &&
      Boolean(selectedTenantId) &&
      Boolean(selectedBusinessId) &&
      Boolean(userId) &&
      businessesLoaded,
    staleTime: 30_000,
  });

  useEffect(() => {
    if (accessQuery.data) {
      setExecutionAccess(accessQuery.data.roles, accessQuery.data.permissions);
    }
  }, [accessQuery.data, setExecutionAccess]);

  useEffect(() => {
    if (!accessQuery.data) return;
    const target = defaultStartRoute(accessQuery.data.roles, accessQuery.data.permissions);
    if (shouldRestoreOperationalStart(pathname, target)) {
      if (typeof navigator !== "undefined" && !navigator.onLine)
        window.location.replace(target);
      else router.replace(target);
    }
  }, [accessQuery.data, pathname, router]);

  if (!isAuthenticated) return null;

  const failed = tenantsQuery.isError || businessesQuery.isError || accessQuery.isError;
  if (failed) {
    return (
      <div className="flex h-screen items-center justify-center p-8">
        <PageError
          message="No se pudo cargar el contexto autorizado de trabajo."
          onRetry={() => {
            void tenantsQuery.refetch();
            if (selectedTenantId) void businessesQuery.refetch();
            if (selectedBusinessId) void accessQuery.refetch();
          }}
        />
      </div>
    );
  }

  const loading =
    tenantsQuery.isLoading ||
    !tenantsLoaded ||
    (Boolean(selectedTenantId) && (businessesQuery.isLoading || !businessesLoaded)) ||
    (Boolean(selectedBusinessId) && accessQuery.isLoading);

  if (loading) {
    return <AuralyLoadingState
      title="Preparando tu espacio"
      description="Estamos cargando el negocio, tus permisos y la operación asignada."
    />;
  }

  if (!selectedTenantId || !selectedBusinessId) {
    return (
      <div className="flex h-screen items-center justify-center p-8">
        <PageError
          message={
            !selectedTenantId
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
