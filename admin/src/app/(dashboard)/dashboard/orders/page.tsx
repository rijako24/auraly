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
  loadOnlineRegisterOptions,
  rememberedOnlineRegisterId,
  selectOnlineRegister,
  type OnlineRegisterOption,
} from "@/services/pos/online-pos-client";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";

export default function OrdersPage() {
  const router = useRouter();
  const user = useAuthStore((state) => state.user);
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const [registers, setRegisters] = useState<OnlineRegisterOption[]>([]);
  const [registerError, setRegisterError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    void loadOnlineRegisterOptions()
      .then((values) => {
        if (active)
          setRegisters(
            values.filter(
              (option) =>
                option.businessId === businessId &&
                !option.hasActiveEdgeEnrollment,
            ),
          );
      })
      .catch((error) => {
        if (active)
          setRegisterError(
            error instanceof Error ? error.message : "No fue posible consultar las cajas.",
          );
      });
    return () => {
      active = false;
    };
  }, [businessId]);

  const register = useMemo(() => {
    const remembered = rememberedOnlineRegisterId();
    return (
      registers.find((option) => option.registerId === remembered) ??
      registers[0] ??
      null
    );
  }, [registers]);

  if (!businessId)
    return <PageError message="Selecciona un negocio para consultar sus pedidos." />;

  return (
    <div className="space-y-4">
      <header>
        <p className="text-sm font-semibold text-teal-700">Comercio</p>
        <h1 className="text-2xl font-black tracking-tight text-slate-950">Pedidos</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Los mismos pedidos creados por el bot, listos para recuperar o facturar.
        </p>
      </header>
      {registerError && (
        <div className="rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900">
          {registerError}
        </div>
      )}
      <OrdersWorkspace
        loadPage={loadCommerceOrders}
        loadDetail={loadCommerceOrder}
        onRecover={
          register && user
            ? async (order) => {
                await selectOnlineRegister(register);
                router.push(`/pos?recoverOrder=${encodeURIComponent(order.orderId)}`);
              }
            : undefined
        }
        onInvoiceSelected={
          register && user
            ? async (orders, paymentMethodCode) => {
                const response = await invoiceCommerceOrders({
                  registerId: register.registerId,
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
      {!register && (
        <p className="rounded-xl border border-slate-200 bg-slate-50 p-4 text-sm text-slate-600">
          Configura una caja online para recuperar o facturar pedidos desde esta vista.
        </p>
      )}
    </div>
  );
}
