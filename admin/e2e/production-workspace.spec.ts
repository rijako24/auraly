import { expect, test, type Route } from "@playwright/test";

const tenantId = "11111111-1111-1111-1111-111111111111";
const businessId = "22222222-2222-2222-2222-222222222222";
const userId = "33333333-3333-3333-3333-333333333333";
const warehouseId = "44444444-4444-4444-4444-444444444444";
const productId = "55555555-5555-5555-5555-555555555555";
const ingredientId = "66666666-6666-6666-6666-666666666666";
const permissions = ["dashboard.read", "inventory.read", "inventory.production.confirm"];

test.use({ serviceWorkers: "block" });

const json = (route: Route, value: unknown, status = 200) => route.fulfill({
  status, contentType: "application/json", body: JSON.stringify(value),
});

test("produce desde Inventario y reintenta la misma operación tras un fallo de red", async ({ page }) => {
  const user = { userId, tenantId, tenantKey: "E2E", username: "inventario-prueba",
    email: "inventario@test.local", firstName: "Operador", lastName: "Inventario",
    roles: ["Administrador"], permissions };
  await page.context().addCookies([{ name: "auth_token", value: "production-test",
    url: "http://127.0.0.1:3000", httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ user, tenantId, businessId }) => {
    localStorage.setItem("selected_tenant_id", tenantId);
    localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("auth-state", JSON.stringify({ state: {
      isAuthenticated: true, user }, version: 0 }));
  }, { user, tenantId, businessId });

  const requests: Array<{ body: Record<string, unknown>; key: string | undefined }> = [];
  await page.route("**/api/**", (route) => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith("/auth/me")) return json(route, user);
    if (path.endsWith("/execution-context/tenants"))
      return json(route, [{ tenantId, name: "Auraly" }]);
    if (path.endsWith("/execution-context/businesses"))
      return json(route, [{ tenantId, businessId, name: "Sede restaurante" }]);
    if (path.endsWith("/execution-context/access"))
      return json(route, { tenantId, businessId, roles: user.roles, permissions });
    if (path.endsWith("/commerce/v1/inventory/warehouses"))
      return json(route, [{ warehouseId, code: "PRINCIPAL", name: "Bodega principal" }]);
    if (path.endsWith("/commerce/v1/inventory/balances"))
      return json(route, { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    if (path.endsWith("/commerce/v1/inventory/production-products"))
      return json(route, { items: [{ productId, productCode: "HAM-01",
        productName: "Hamburguesa", unitCode: "EA", recipeVersion: 3,
        quantityOnHand: 0 }], page: 1, pageSize: 10, totalCount: 1, totalPages: 1 });
    if (path.endsWith(`/commerce/v1/inventory/production-products/${productId}/preview`))
      return json(route, { productId, productCode: "HAM-01", productName: "Hamburguesa",
        unitCode: "EA", recipeVersion: 3, quantity: 4, quantityOnHand: 0,
        ingredients: [{ productId: ingredientId, productCode: "PAN-01", name: "Pan",
          unitCode: "EA", quantityPerUnit: 1, requiredQuantity: 4,
          quantityOnHand: 12, estimatedUnitCost: 2000 }] });
    if (path.endsWith("/commerce/v1/production/confirm")) {
      requests.push({ body: route.request().postDataJSON(),
        key: route.request().headers()["idempotency-key"] });
      if (requests.length === 1) return json(route, { detail: "Conexión interrumpida" }, 503);
      return json(route, { documentId: requests[0].body.documentId,
        documentNumber: "PRD-000001", status: "Accepted", idempotentReplay: true }, 202);
    }
    if (path.endsWith("/commerce/v1/inventory/operations"))
      return json(route, { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    return json(route, []);
  });

  await page.goto("/dashboard/inventory");
  await page.getByRole("button", { name: "Nueva operación" }).click();
  await page.getByRole("dialog", { name: "Nueva operación" })
    .screenshot({ path: "../tmp/inventory-production-option.png" });
  await page.getByRole("dialog", { name: "Nueva operación" })
    .getByRole("button", { name: /Producción.*Consume ingredientes/ }).click();
  const dialog = page.getByRole("dialog", { name: "Producción por receta" });
  await expect(dialog).toBeVisible();
  await dialog.getByRole("combobox", { name: "Producto a producir" }).click();
  await page.getByRole("option", { name: /HAM-01 · Hamburguesa/ }).click();
  await dialog.getByLabel("Cantidad a producir").fill("4");
  await dialog.getByRole("button", { name: "Revisar ingredientes y existencias" }).click();
  await expect(dialog.getByText("Requiere 4 EA · disponible 12")).toBeVisible();
  await dialog.getByRole("button", { name: "Confirmar producción" }).click();
  await expect(dialog.getByText(/formulario queda fijo/)).toBeVisible();
  await dialog.getByRole("button", { name: "Reintentar confirmación" }).click();
  await expect(dialog).not.toBeVisible();
  expect(requests).toHaveLength(2);
  expect(requests[1].body).toEqual(requests[0].body);
  expect(requests[0].body.expectedRecipeVersion).toBe(3);
  expect(requests[0].body.quantity).toBe(4);
  expect(requests[0].key).toBe(requests[1].key);
});
