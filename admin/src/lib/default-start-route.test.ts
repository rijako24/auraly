import assert from "node:assert/strict";
import test from "node:test";
import {
  canOpenPosAdministrativeMenu,
  defaultStartRoute,
  requiresCloudWorkspace,
  shouldRestoreOperationalStart,
} from "./default-start-route";

test("seller-only users start in today's route", () => {
  assert.equal(defaultStartRoute(["Vendedor"], ["routes.read"]), "/dashboard/my-routes");
  assert.equal(defaultStartRoute(["seller"], ["routes.read"]), "/dashboard/my-routes");
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
  assert.equal(defaultStartRoute(["Administrador"], ["agents.read"]), "/dashboard/agents");
  assert.equal(defaultStartRoute(["Administrador"], ["reservations.read"]), "/dashboard/reservations");
});

test("users without any navigable permission keep the neutral dashboard", () => {
  assert.equal(defaultStartRoute(["Vendedor"], []), "/dashboard");
  assert.equal(defaultStartRoute(["Transportador"], []), "/dashboard");
});

test("installed login distinguishes local POS access from administrative workspaces", () => {
  assert.equal(requiresCloudWorkspace(["sales.create", "sales.change-price"]), false);
  assert.equal(requiresCloudWorkspace(["sales.create", "sales.reports.read"]), true);
  assert.equal(requiresCloudWorkspace(["catalog.read"]), true);
});

test("POS menu requires both a cloud session and another authorized module", () => {
  assert.equal(canOpenPosAdministrativeMenu(true, ["sales.create", "catalog.read"]), true);
  assert.equal(canOpenPosAdministrativeMenu(true, ["sales.create"]), false);
  assert.equal(canOpenPosAdministrativeMenu(false, ["sales.create", "catalog.read"]), false);
});

test("the dashboard root remains available after the initial login redirect", () => {
  assert.equal(shouldRestoreOperationalStart("/dashboard", "/pos"), false);
  assert.equal(shouldRestoreOperationalStart("/dashboard/", "/dashboard/my-routes"), false);
});

test("exclusive operational profiles recover from a route restored for another user", () => {
  assert.equal(shouldRestoreOperationalStart("/dashboard/my-routes", "/dashboard/deliveries"), true);
  assert.equal(shouldRestoreOperationalStart("/dashboard/deliveries", "/dashboard/my-routes"), true);
  assert.equal(shouldRestoreOperationalStart("/dashboard/orders", "/dashboard"), false);
  assert.equal(shouldRestoreOperationalStart("/dashboard/deliveries", "/dashboard"), false);
});
