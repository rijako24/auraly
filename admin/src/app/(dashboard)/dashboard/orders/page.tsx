"use client";

import { useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";

import { OrdersWorkspace } from "@/components/orders/orders-workspace";
import { PageError } from "@/components/ui/page-error";
import {
  loadCommerceOrder,
  loadCommerceOrders,
  cancelCommerceOrder,
  retryCommerceOrderEmission,
  type CommerceOrderDetail,
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
import { routesApi, type SalesRouteListItem } from "@/services/api/routes";
import { PosPrinterDialog } from "@/app/(pos)/pos/pos-printer-dialog";
import { PosEdgeClient, readEdgeTokenFromLaunch, readEdgeUserSession } from "@/services/pos/pos-edge-client";
import { sellerOrdersApi } from "@/services/api/seller-orders";
import { SellerOrderCaptureDialog } from "@/components/orders/seller-order-capture-dialog";

export default function OrdersPage() {
  const router = useRouter();
  const user = useAuthStore((state) => state.user);
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const [workspaces, setWorkspaces] = useState<SalesWorkspaceOption[]>([]);
  const [workspaceError, setWorkspaceError] = useState<string | null>(null);
  const [routeOptions, setRouteOptions] = useState<SalesRouteListItem[]>([]);
  const [printerOpen, setPrinterOpen] = useState(false);
  const [editingOrder, setEditingOrder] = useState<CommerceOrderDetail | null>(null);
  const [ordersRevision, setOrdersRevision] = useState(0);
  const [printerClient] = useState(() => {
    const token = readEdgeTokenFromLaunch();
    return token ? new PosEdgeClient(token, readEdgeUserSession()) : null;
  });
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
  const editingWorkspace = useMemo(
    () => editingOrder?.warehouseId
      ? workspaces.find((option) => option.warehouseId === editingOrder.warehouseId) ?? workspace
      : workspace,
    [editingOrder?.warehouseId, workspace, workspaces],
  );

  if (!businessId)
    return <PageError message="Selecciona una sede para consultar sus pedidos." />;

  return (
    <div className="space-y-6">
      <header className="flex flex-col justify-between gap-3 sm:flex-row sm:items-end">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Pedidos</h1>
          <p className="text-muted-foreground">
            Los mismos pedidos creados por el bot, listos para recuperar o facturar.
          </p>
        </div>
      </header>
      {workspaceError && (
        <div className="rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900">
          {workspaceError}
        </div>
      )}
      <OrdersWorkspace
        key={ordersRevision}
        showHeader={false}
        routeOptions={routeOptions.map((route) => ({ routeId: route.routeId, name: route.name }))}
        loadPage={loadCommerceOrders}
        loadDetail={loadCommerceOrder}
        onRetryEmission={async (orderId) => {
          await retryCommerceOrderEmission(orderId);
        }}
        onCancelOrder={user?.permissions?.includes("orders.cancel") ? async (order) => {
          await cancelCommerceOrder(order.orderId);
        } : undefined}
        onConfirmReview={user?.permissions?.includes("orders.review") ? async (order, lines) => {
          if (!order.customerId) throw new Error("El pedido no tiene un cliente válido.");
          await sellerOrdersApi.update(order.orderId, {
            customerId: order.customerId,
            notes: order.notes,
            idempotencyKey: crypto.randomUUID(),
            lines,
          });
        } : undefined}
        onEditOrder={user?.permissions?.includes("orders.update") ? setEditingOrder : undefined}
        onPrintSelected={
          workspace && user
            ? async (orders) => {
                const context = await selectSalesWorkspace(workspace);
                return new OnlinePosClient(
                  context,
                  user.userId,
                  `${user.firstName} ${user.lastName}`.trim() || user.username,
                  readEdgeTokenFromLaunch(),
                ).printOrders(orders.map((order) => order.orderId));
              }
            : undefined
        }
        onRecover={
          workspace && user?.permissions?.includes("orders.recover")
            ? async (order) => {
                await selectSalesWorkspace(workspace);
                router.push(`/pos?recoverOrder=${encodeURIComponent(order.orderId)}`);
              }
            : undefined
        }
        onInvoiceSelected={
          workspace && user
            ? async (orders, documentType) => {
                const edgeToken = readEdgeTokenFromLaunch();
                const context = await selectSalesWorkspace(workspace);
                const client = new OnlinePosClient(
                  context,
                  user.userId,
                  `${user.firstName} ${user.lastName}`.trim() || user.username,
                  edgeToken,
                );
                const response = await client.invoiceOrders(
                  orders.map((order) => order.orderId),
                  "Cash",
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
      {editingOrder?.customerId && (editingOrder.warehouseId || editingWorkspace?.warehouseId) && (
        <SellerOrderCaptureDialog
          businessId={editingOrder.businessId}
          warehouseId={editingOrder.warehouseId ?? editingWorkspace!.warehouseId}
          route={null}
          stop={{
            routeStopId: `order-${editingOrder.orderId}`,
            customerId: editingOrder.customerId,
            partySiteId: "",
            sequence: 0,
            customerName: editingOrder.customerName ?? "Cliente",
            identification: editingOrder.customerIdentification,
            siteName: "Pedido comercial",
            addressLine: editingOrder.deliveryAddress ?? "",
            neighborhood: null,
            cityName: "",
            phone: editingOrder.customerPhone,
            googleMapsUrl: null,
            latitude: null,
            longitude: null,
            plannedVisitTime: null,
            visitNote: null,
            rowVersion: "",
          }}
          editing={editingOrder}
          allowsNegativeStockSales={editingWorkspace?.warehouseAllowsNegativeStockSales ?? false}
          onClose={() => setEditingOrder(null)}
          onCreated={async () => { setEditingOrder(null); setOrdersRevision((value) => value + 1); }}
        />
      )}
      {!workspace && (
        <p className="rounded-xl border border-slate-200 bg-slate-50 p-4 text-sm text-slate-600">
          Configura una sede y una bodega para recuperar o facturar pedidos desde esta vista.
        </p>
      )}
    </div>
  );
}
