"use client";

import { useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";

import { DailyRouteApp } from "@/components/orders/daily-route-app";
import { OrdersWorkspace } from "@/components/orders/orders-workspace";
import { Button } from "@/components/ui/button";
import { PageError } from "@/components/ui/page-error";
import {
  loadCommerceOrder,
  loadCommerceOrders,
} from "@/services/orders/commerce-orders-client";
import {
  loadSalesWorkspaceOptions,
  OnlinePosClient,
  rememberedSalesWorkspaceKey,
  salesWorkspaceKey,
  selectSalesWorkspace,
  type SalesWorkspaceOption,
} from "@/services/pos/online-pos-client";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { isSellerOperationalProfile, ordersLandingView } from "@/lib/default-start-route";
import { routesApi, type SalesRouteListItem } from "@/services/api/routes";
import { PosPrinterDialog } from "@/app/(pos)/pos/pos-printer-dialog";
import { PosEdgeClient, readEdgeTokenFromLaunch, readEdgeUserSession } from "@/services/pos/pos-edge-client";

export default function OrdersPage() {
  const router = useRouter();
  const user = useAuthStore((state) => state.user);
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const [workspaces, setWorkspaces] = useState<SalesWorkspaceOption[]>([]);
  const [workspaceError, setWorkspaceError] = useState<string | null>(null);
  const [routeOptions, setRouteOptions] = useState<SalesRouteListItem[]>([]);
  const [printerOpen, setPrinterOpen] = useState(false);
  const [printerClient] = useState(() => {
    const token = readEdgeTokenFromLaunch();
    return token ? new PosEdgeClient(token, readEdgeUserSession()) : null;
  });
  const [routeMode, setRouteMode] = useState(
    () =>
      typeof window !== "undefined" &&
      new URLSearchParams(window.location.search).get("view") === "today-route",
  );

  useEffect(() => {
    if (!user || typeof window === "undefined") return;
    const view = ordersLandingView(window.location.search, user.roles ?? [], user.permissions ?? []);
    setRouteMode(view === "today-route");
    if (view === "today-route" && new URLSearchParams(window.location.search).get("view") !== "today-route")
      router.replace("/dashboard/orders?view=today-route");
  }, [router, user]);

  useEffect(() => {
    let active = true;
    void loadSalesWorkspaceOptions()
      .then((values) => {
        if (active)
          setWorkspaces(values.filter((option) => option.businessId === businessId));
      })
      .catch((error) => {
        if (active)
          setWorkspaceError(
            error instanceof Error
              ? error.message
              : "No fue posible consultar las sedes y bodegas disponibles.",
          );
      });
    return () => {
      active = false;
    };
  }, [businessId]);

  useEffect(() => {
    let active = true;
    void routesApi.page({ page: 1, pageSize: 100, isActive: true })
      .then((value) => { if (active) setRouteOptions(value.items); })
      .catch(() => { if (active) setRouteOptions([]); });
    return () => { active = false; };
  }, [businessId]);

  const workspace = useMemo(() => {
    const remembered = rememberedSalesWorkspaceKey();
    return (
      workspaces.find(
        (option) =>
          salesWorkspaceKey(option.businessId, option.warehouseId) === remembered,
      ) ?? workspaces[0] ?? null
    );
  }, [workspaces]);

  if (!businessId)
    return <PageError message="Selecciona una sede para consultar sus pedidos." />;

  if (routeMode)
    return (
      <DailyRouteApp
        businessId={businessId}
        onAdministrative={() => {
          setRouteMode(false);
          router.replace("/dashboard/orders?view=all");
        }}
      />
    );

  return (
    <div className="space-y-6">
      <header className="flex flex-col justify-between gap-3 sm:flex-row sm:items-end">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Pedidos</h1>
          <p className="text-muted-foreground">
            Los mismos pedidos creados por el bot, listos para recuperar o facturar.
          </p>
        </div>
        <Button
          onClick={() => {
            setRouteMode(true);
            router.replace("/dashboard/orders?view=today-route");
          }}
        >
          Abrir ruta de hoy
        </Button>
      </header>
      {workspaceError && (
        <div className="rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900">
          {workspaceError}
        </div>
      )}
      <OrdersWorkspace
        showHeader={false}
        routeOptions={routeOptions.map((route) => ({ routeId: route.routeId, name: route.name }))}
        onlyMine={!!user && isSellerOperationalProfile(user.roles ?? [], user.permissions ?? [])}
        source={user && isSellerOperationalProfile(user.roles ?? [], user.permissions ?? []) ? 1 : undefined}
        loadPage={loadCommerceOrders}
        loadDetail={loadCommerceOrder}
        onRecover={
          workspace && user
            ? async (order) => {
                await selectSalesWorkspace(workspace);
                router.push(`/pos?recoverOrder=${encodeURIComponent(order.orderId)}`);
              }
            : undefined
        }
        onInvoiceSelected={
          workspace && user
            ? async (orders, paymentMethodCode, documentType) => {
                const edgeToken = readEdgeTokenFromLaunch();
                if (!edgeToken)
                  throw new Error("Prepara este equipo para facturar e imprimir pedidos directamente.");
                const context = await selectSalesWorkspace(workspace);
                const client = new OnlinePosClient(
                  context,
                  user.userId,
                  `${user.firstName} ${user.lastName}`.trim() || user.username,
                  edgeToken,
                );
                const response = await client.invoiceOrders(
                  orders.map((order) => order.orderId),
                  paymentMethodCode,
                  documentType,
                );
                return {
                  completedCount: response.completedCount,
                  failedCount: response.failedCount,
                  printError: response.printError,
                };
              }
            : undefined
        }
        onConfigurePrinting={() => setPrinterOpen(true)}
      />
      {printerOpen && (
        <PosPrinterDialog client={printerClient} onClose={() => setPrinterOpen(false)} />
      )}
      {!workspace && (
        <p className="rounded-xl border border-slate-200 bg-slate-50 p-4 text-sm text-slate-600">
          Configura una sede y una bodega para recuperar o facturar pedidos desde esta vista.
        </p>
      )}
    </div>
  );
}
