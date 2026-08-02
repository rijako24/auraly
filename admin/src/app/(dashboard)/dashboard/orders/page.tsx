"use client";

import { useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";

import { OrdersWorkspace } from "@/components/orders/orders-workspace";
import { PageError } from "@/components/ui/page-error";
import {
  loadCommerceOrder,
  loadCommerceOrders,
  invoiceCommerceOrders,
} from "@/services/orders/commerce-orders-client";
import {
  loadSalesWorkspaceOptions,
  rememberedSalesWorkspaceKey,
  salesWorkspaceKey,
  selectSalesWorkspace,
  type SalesWorkspaceOption,
} from "@/services/pos/online-pos-client";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";

export default function OrdersPage() {
  const router = useRouter();
  const user = useAuthStore((state) => state.user);
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const [workspaces, setWorkspaces] = useState<SalesWorkspaceOption[]>([]);
  const [workspaceError, setWorkspaceError] = useState<string | null>(null);

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

  const workspace = useMemo(() => {
    const remembered = rememberedSalesWorkspaceKey();
    return (
      workspaces.find(
        (option) =>
          salesWorkspaceKey(option.businessId, option.warehouseId) === remembered,
      ) ??
      workspaces[0] ??
      null
    );
  }, [workspaces]);

  if (!businessId)
    return <PageError message="Selecciona una sede para consultar sus pedidos." />;

  return (
    <div className="space-y-4">
      <header>
        <p className="text-sm font-semibold text-teal-700">Comercio</p>
        <h1 className="text-2xl font-black tracking-tight text-slate-950">Pedidos</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Los mismos pedidos creados por el bot, listos para recuperar o facturar.
        </p>
      </header>
      {workspaceError && (
        <div className="rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900">
          {workspaceError}
        </div>
      )}
      <OrdersWorkspace
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
            ? async (orders, paymentMethodCode) => {
                const context = await selectSalesWorkspace(workspace);
                const response = await invoiceCommerceOrders({
                  workSessionId: context.workSessionId,
                  warehouseId: context.warehouseId,
                  userId: user.userId,
                  orderIds: orders.map((order) => order.orderId),
                  paymentMethodCode,
                  paymentReference: null,
                });
                return {
                  completedCount: response.completedCount,
                  failedCount: response.failedCount,
                };
              }
            : undefined
        }
      />
      {!workspace && (
        <p className="rounded-xl border border-slate-200 bg-slate-50 p-4 text-sm text-slate-600">
          Configura una sede y una bodega para recuperar o facturar pedidos desde esta vista.
        </p>
      )}
    </div>
  );
}
