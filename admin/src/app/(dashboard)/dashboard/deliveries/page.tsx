"use client";

import { TransportDispatchApp } from "@/components/dispatching/transport-dispatch-app";
import { useAuthStore } from "@/stores/auth-store";

export default function MyDeliveriesPage() {
  const permissions = new Set(useAuthStore((state) => state.user?.permissions ?? []));
  return <TransportDispatchApp canExecute={permissions.has("dispatches.delivery.execute")} />;
}
