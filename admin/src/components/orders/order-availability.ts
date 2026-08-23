import type { CommerceOrderListItem } from "@/services/orders/commerce-orders-client";

type OrderAvailabilityInput = Pick<
  CommerceOrderListItem,
  "orderId" | "canInvoice" | "status" | "claim"
>;

export type OrderAvailability = {
  canUseInCurrentSession: boolean;
  label: string;
  actionLabel: string;
  tone: "available" | "owned" | "claimed" | "invoiced" | "cancelled" | "neutral";
};

export function getOrderAvailability(
  order: OrderAvailabilityInput,
  activeOrderId?: string | null,
): OrderAvailability {
  if (activeOrderId === order.orderId) {
    return {
      canUseInCurrentSession: false,
      label: "Ocupado",
      actionLabel: "Ocupado",
      tone: "owned",
    };
  }

  if (order.claim) {
    return order.claim.isOwnedByCurrentActor
      ? {
          canUseInCurrentSession: false,
          label: "Ocupado",
          actionLabel: "Ocupado",
          tone: "owned",
        }
      : {
          canUseInCurrentSession: false,
          label: "En proceso",
          actionLabel: "En proceso",
          tone: "claimed",
        };
  }

  if (order.status === "Available") {
    return {
      canUseInCurrentSession: order.canInvoice,
      label: "Disponible",
      actionLabel: "Recuperar",
      tone: "available",
    };
  }
  if (order.status === "Invoiced") {
    return { canUseInCurrentSession: false, label: "Facturado", actionLabel: "Facturado", tone: "invoiced" };
  }
  if (order.status === "Cancelled") {
    return { canUseInCurrentSession: false, label: "Cancelado", actionLabel: "Cancelado", tone: "cancelled" };
  }
  return {
    canUseInCurrentSession: false,
    label: order.status,
    actionLabel: order.status,
    tone: "neutral",
  };
}
