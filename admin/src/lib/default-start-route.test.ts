import assert from "node:assert/strict";
import test from "node:test";
import { defaultStartRoute, ordersLandingView, shouldRestoreOperationalStart } from "./default-start-route";

test("seller-only users start in today's route", () => {
  assert.equal(defaultStartRoute(["Vendedor"], ["orders.read"]), "/dashboard/orders?view=today-route");
  assert.equal(defaultStartRoute(["seller"], ["orders.read"]), "/dashboard/orders?view=today-route");
});

test("transporter-only users start in assigned dispatches", () => {
  assert.equal(defaultStartRoute(["Transportador"], ["dispatches.delivery.execute"]), "/dashboard/deliveries");
  assert.equal(defaultStartRoute(["driver"], ["dispatches.delivery.execute"]), "/dashboard/deliveries");
});

test("the first authorized view owns the generic landing", () => {
  assert.equal(defaultStartRoute(["Vendedor", "Administrador"], ["orders.read"]), "/dashboard/orders");
  assert.equal(defaultStartRoute(["Cajero"], ["sales.create"]), "/pos");
  assert.equal(defaultStartRoute(["Administrador"], ["parties.read", "catalog.read"]), "/dashboard/products");
  assert.equal(defaultStartRoute(["Administrador"], ["sales.reports.read", "catalog.read"]), "/dashboard");
});

test("the generic landing follows the first visible navigation item", () => {
  assert.equal(defaultStartRoute(["Administrador"], ["business_config.read"]), "/dashboard/channels");
  assert.equal(defaultStartRoute(["Administrador"], ["reservations.read"]), "/dashboard/reservations");
});

test("users without any navigable permission keep the neutral dashboard", () => {
  assert.equal(defaultStartRoute(["Vendedor"], []), "/dashboard");
  assert.equal(defaultStartRoute(["Transportador"], []), "/dashboard");
});

test("the dashboard root remains available after the initial login redirect", () => {
  assert.equal(shouldRestoreOperationalStart("/dashboard", "/pos"), false);
  assert.equal(shouldRestoreOperationalStart("/dashboard/", "/dashboard/orders?view=today-route"), false);
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
