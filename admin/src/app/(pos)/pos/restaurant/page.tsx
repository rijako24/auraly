"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { apiClient } from "@/services/api/client";
import { loadSalesWorkspaceBootstrap } from "@/services/pos/online-pos-bootstrap";
import { rememberedSalesWorkspaceKey, rememberSalesWorkspace, salesWorkspaceKey,
  selectSalesWorkspace,
  type SalesWorkspaceOption, type SalesWorkspaceContext } from "@/services/pos/online-pos-client";
import { RestaurantPosWorkspace } from "@/components/restaurant/restaurant-pos-workspace";

export default function RestaurantPosPage() {
  const router = useRouter();
  const hasHydrated = useAuthStore((state) => state.hasHydrated);
  const permissions = useAuthStore((state) => state.user?.permissions ?? []);
  const canEnter = permissions.includes("restaurant.attend") || permissions.includes("sales.create");
  const selectedBusinessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const [selected, setSelected] = useState<
    SalesWorkspaceOption & { workSessionId?: string } | null>(null);
  const [loading, setLoading] = useState(true);

  const select = useCallback(async (option: SalesWorkspaceOption) => {
    setLoading(true);
    try {
      const context: SalesWorkspaceContext | {
        businessId: string; warehouseId: string; pointOfSaleMode: string;
      } = useAuthStore.getState().user?.permissions.includes("sales.create")
        ? await selectSalesWorkspace(option)
        : await apiClient.post<{
            businessId: string; warehouseId: string; pointOfSaleMode: string;
          }>("/commerce/v1/pos/workspace/select", {
            businessId: option.businessId, warehouseId: option.warehouseId,
          }, { headers: { "X-Business-Id": option.businessId } });
      if (context.pointOfSaleMode !== "RestaurantBar")
        throw new Error("La sede ya no está configurada como restaurante.");
      const activeBusinessId = useBusinessContextStore.getState().selectedBusinessId ??
        window.localStorage.getItem("selected_business_id");
      if (activeBusinessId && activeBusinessId !== option.businessId) {
        router.replace("/pos?workspace=change");
        return;
      }
      window.localStorage.setItem("selected_business_id", option.businessId);
      rememberSalesWorkspace(option);
      setSelected({ ...option, workSessionId: "workSessionId" in context
        ? context.workSessionId : undefined });
    } catch {
      router.replace("/pos?workspace=change");
    } finally {
      setLoading(false);
    }
  }, [router]);

  useEffect(() => {
    if (hasHydrated && !canEnter) router.replace("/dashboard/restaurant");
  }, [canEnter, hasHydrated, router]);

  useEffect(() => {
    if (!hasHydrated || !canEnter) return;
    let active = true;
    setSelected(null);
    setLoading(true);
    void loadSalesWorkspaceBootstrap().then((bootstrap) => {
      if (!active) return;
      const activeBusinessId = selectedBusinessId ?? window.localStorage.getItem("selected_business_id");
      const restaurantOptions = bootstrap.options.filter((option) =>
        option.pointOfSaleMode === "RestaurantBar" &&
        (!activeBusinessId || option.businessId === activeBusinessId));
      const remembered = rememberedSalesWorkspaceKey();
      const found = restaurantOptions.find((option) =>
        salesWorkspaceKey(option.businessId, option.warehouseId) === remembered);
      if (found) void select(found);
      else if (activeBusinessId && restaurantOptions.length === 1)
        void select(restaurantOptions[0]);
      else router.replace("/pos?workspace=change");
    }).catch(() => {
      if (active) {
        router.replace("/pos?workspace=change");
        setLoading(false);
      }
    });
    return () => { active = false; };
  }, [canEnter, hasHydrated, selectedBusinessId, router, select]);

  if (!hasHydrated || !canEnter) return null;
  if (selected && (!selectedBusinessId || selected.businessId === selectedBusinessId))
    return <RestaurantPosWorkspace option={selected} />;
  return <main className="min-h-screen bg-background" aria-busy={loading} />;
}
