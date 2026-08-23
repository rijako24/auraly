import assert from "node:assert/strict";
import test from "node:test";

import { getOrderAvailability } from "./order-availability";

const claim = {
  claimId: "claim-1",
  workSessionId: "session-1",
  deviceId: null,
  userId: "user-1",
  expiresAt: "2026-08-23T20:10:00Z",
  isOwnedByCurrentActor: false,
};

test("un pedido reclamado deja de mostrarse y operar como disponible", () => {
  assert.deepEqual(
    getOrderAvailability({ orderId: "order-1", canInvoice: true, status: "Available", claim }),
    {
      canUseInCurrentSession: false,
      label: "En proceso",
      actionLabel: "En proceso",
      tone: "claimed",
    },
  );
});

test("la misma sesión distingue el pedido que ya tiene en venta", () => {
  assert.deepEqual(
    getOrderAvailability({
      orderId: "order-1",
      canInvoice: true,
      status: "Available",
      claim: { ...claim, isOwnedByCurrentActor: true },
    }),
    {
      canUseInCurrentSession: false,
      label: "Ocupado",
      actionLabel: "Ocupado",
      tone: "owned",
    },
  );
});

test("un pedido sin reclamo conserva el estado disponible", () => {
  assert.deepEqual(
    getOrderAvailability({ orderId: "order-1", canInvoice: true, status: "Available", claim: null }),
    {
      canUseInCurrentSession: true,
      label: "Disponible",
      actionLabel: "Recuperar",
      tone: "available",
    },
  );
});

test("el pedido cargado queda ocupado aunque el listado todavía no refleje el reclamo", () => {
  assert.deepEqual(
    getOrderAvailability(
      { orderId: "order-1", canInvoice: true, status: "Available", claim: null },
      "order-1",
    ),
    {
      canUseInCurrentSession: false,
      label: "Ocupado",
      actionLabel: "Ocupado",
      tone: "owned",
    },
  );
});
