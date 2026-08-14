import assert from "node:assert/strict";
import test from "node:test";
import { defaultStartRoute, shouldApplyDefaultStart } from "./default-start-route";

test("seller-only users start in today's route", () => {
  assert.equal(defaultStartRoute(["Vendedor"], ["orders.read"]), "/dashboard/orders?view=today-route");
  assert.equal(defaultStartRoute(["seller"], ["orders.read"]), "/dashboard/orders?view=today-route");
});

test("transporter-only users start in assigned dispatches", () => {
  assert.equal(defaultStartRoute(["Transportador"], ["dispatches.delivery.execute"]), "/dashboard/dispatches?view=my-deliveries");
  assert.equal(defaultStartRoute(["driver"], ["dispatches.delivery.execute"]), "/dashboard/dispatches?view=my-deliveries");
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
