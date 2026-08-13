import assert from "node:assert/strict";
import test from "node:test";
import { defaultStartRoute, shouldApplyDefaultStart } from "./default-start-route";

test("seller-only users start in today's route", () => {
  assert.equal(defaultStartRoute(["Vendedor"], ["orders.read"]), "/dashboard/orders?view=today-route");
  assert.equal(defaultStartRoute(["seller"], ["orders.read"]), "/dashboard/orders?view=today-route");
});

test("mixed roles and users without order access keep the dashboard", () => {
  assert.equal(defaultStartRoute(["Vendedor", "Administrador"], ["orders.read"]), "/dashboard");
  assert.equal(defaultStartRoute(["Vendedor"], []), "/dashboard");
});

test("the automatic redirect only owns the dashboard root", () => {
  assert.equal(shouldApplyDefaultStart("/dashboard"), true);
  assert.equal(shouldApplyDefaultStart("/dashboard/orders"), false);
});
