import assert from "node:assert/strict";
import test from "node:test";
import { authorizedNavigationGroups, authorizedNavigationItems } from "./sidebar-nav-config";

test("login permissions are the single visibility source for every navigation surface", () => {
  const items = authorizedNavigationItems(["catalog.read"]);

  assert.deepEqual(items.map((item) => item.name), ["Productos"]);
  assert.equal(items.some((item) => item.href === "/dashboard/settings/fiscal"), false);
  assert.deepEqual(
    authorizedNavigationGroups(["catalog.read"]),
    [{ label: "Catálogo", items }],
  );
});

test("the fiscal workspace is exposed as DIAN only with its backend permission", () => {
  assert.deepEqual(
    authorizedNavigationItems(["fiscal.configuration.read"])
      .map(({ name, href }) => ({ name, href })),
    [{ name: "DIAN", href: "/dashboard/settings/fiscal" }],
  );
});

test("reservations and calendar belong to attention and growth and remain opt-in", () => {
  assert.deepEqual(
    authorizedNavigationGroups(["reservations.read"])
      .map((group) => ({
        label: group.label,
        items: group.items.map((item) => item.name),
      })),
    [{
      label: "Atención y crecimiento",
      items: ["Reservaciones", "Calendario"],
    }],
  );

  assert.equal(
    authorizedNavigationItems([])
      .some((item) => item.name === "Reservaciones" || item.name === "Calendario"),
    false,
  );
});

test("cashier inventory availability does not expose the inventory workspace", () => {
  const cashierPermissions = [
    "sales.create",
    "sales.reprint",
    "pos.customer.create",
    "pos.orders",
    "orders.read",
    "work-sessions.read",
    "work-sessions.open",
    "work-sessions.cash.manage",
    "work-sessions.cash.drawer.open",
    "pos.synchronization.events.read",
    "pos.inventory.availability.read",
  ];

  assert.deepEqual(
    authorizedNavigationItems(cashierPermissions).map((item) => item.name),
    ["Punto de venta", "Pedidos"],
  );
});

test("orders and personal routes are independent navigation capabilities", () => {
  assert.deepEqual(
    authorizedNavigationItems(["orders.read"]).map((item) => item.name),
    ["Pedidos"],
  );
  assert.deepEqual(
    authorizedNavigationItems(["routes.read"]).map((item) => item.name),
    ["Mis rutas"],
  );
  assert.deepEqual(
    authorizedNavigationItems(["routes.read", "routes.read-all"]).map((item) => item.name),
    ["Mis rutas", "Rutas comerciales"],
  );
});

test("the own company view is independent from platform tenant administration", () => {
  assert.deepEqual(
    authorizedNavigationItems(["tenant.profile.read"]).map(({ name, href }) => ({ name, href })),
    [{ name: "Empresa", href: "/dashboard/company" }],
  );
  assert.deepEqual(
    authorizedNavigationItems(["tenants.read"]).map(({ name, href }) => ({ name, href })),
    [{ name: "Empresas", href: "/dashboard/tenants" }],
  );
});
