import assert from "node:assert/strict";
import test from "node:test";
import { defaultStartRoute, ordersLandingView, shouldApplyDefaultStart, shouldRestoreOperationalStart } from "./default-start-route";

test("seller-only users start in today's route", () => {
  assert.equal(defaultStartRoute(["Vendedor"], ["orders.read"]), "/dashboard/orders?view=today-route");
  assert.equal(defaultStartRoute(["seller"], ["orders.read"]), "/dashboard/orders?view=today-route");
});

test("transporter-only users start in assigned dispatches", () => {
  assert.equal(defaultStartRoute(["Transportador"], ["dispatches.delivery.execute"]), "/dashboard/deliveries");
  assert.equal(defaultStartRoute(["driver"], ["dispatches.delivery.execute"]), "/dashboard/deliveries");
});

test("mixed roles and users without operational access keep the dashboard", () => {
  assert.equal(defaultStartRoute(["Vendedor", "Administrador"], ["orders.read"]), "/dashboard");
  assert.equal(defaultStartRoute(["Vendedor"], []), "/dashboard");
  assert.equal(defaultStartRoute(["Transportador"], []), "/dashboard");
});

test("the automatic redirect only owns the dashboard root", () => {
  assert.equal(shouldApplyDefaultStart("/dashboard"), true);
  assert.equal(shouldApplyDefaultStart("/dashboard/orders"), false);
});

test("exclusive operational profiles recover from a route restored for another user", () => {
  assert.equal(shouldRestoreOperationalStart("/dashboard/orders?view=today-route", "/dashboard/deliveries"), true);
  assert.equal(shouldRestoreOperationalStart("/dashboard/deliveries", "/dashboard/orders?view=today-route"), true);
  assert.equal(shouldRestoreOperationalStart("/dashboard/orders", "/dashboard"), false);
  assert.equal(shouldRestoreOperationalStart("/dashboard/deliveries", "/dashboard"), false);
});


test("seller order navigation always opens the operational route unless all orders was requested explicitly", () => {
  assert.equal(ordersLandingView("", ["Vendedor"], ["orders.read"]), "today-route");
  assert.equal(ordersLandingView("?view=today-route", ["Vendedor"], ["orders.read"]), "today-route");
  assert.equal(ordersLandingView("?view=all", ["Vendedor"], ["orders.read"]), "all");
  assert.equal(ordersLandingView("", ["Administrador"], ["orders.read"]), "all");
});
