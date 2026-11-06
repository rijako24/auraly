import { expect, test, type Page } from "@playwright/test";

test.use({ serviceWorkers: "block", actionTimeout: 15000 });
const tenantId = "11111111-1111-1111-1111-111111111111";
const businessId = "22222222-2222-2222-2222-222222222222";
const sourceId = "33333333-3333-3333-3333-333333333333";
async function setup(page: Page, readOnly = false) {
  const permissions = ["dashboard.read", "businesses.read", ...readOnly ? [] : ["businesses.create", "businesses.update"]];
  const user = { userId: "44444444-4444-4444-4444-444444444444", tenantId, username: "qa", firstName: "QA", roles: ["Administrador"], permissions };
  await page.context().addCookies([{ name: "auth_token", value: "qa", url: "http://127.0.0.1:3000", httpOnly: true }]);
  await page.addInitScript(({ user, tenantId, businessId }) => {
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
    localStorage.setItem("selected_tenant_id", tenantId); localStorage.setItem("selected_business_id", businessId);
  }, { user, tenantId, businessId });
  const original = { businessId, tenantId, name: "Principal", description: "Sede principal", address: "Calle 1", phone: "3001234567", email: "principal@example.test", website: "https://example.test", timeZone: "America/Bogota", sharesProductPrices: false, pointOfSaleMode: "DirectSale", restaurantRequireWaiterCredential: true, restaurantRestrictTableToWaiter: false, restaurantSuggestedTipPercent: 0, isActive: true, createdAt: "2026-11-01T12:00:00Z", updatedAt: null };
  const businesses = [original, { ...original, businessId: sourceId, name: "Sucursal origen" }];
  const calls = { lists: 0, details: 0, contexts: 0, writes: [] as Array<{ method: string; body: Record<string, unknown> }> };
  let failSave = false;
  await page.route("**/api/**", async route => {
    const request = route.request(), url = new URL(request.url()), path = url.pathname;
    let value: unknown = [];
    if (path.endsWith("/auth/me")) value = user;
    else if (path.endsWith("/execution-context/tenants")) value = [{ tenantId, name: "Empresa" }];
    else if (path.endsWith("/execution-context/businesses")) { calls.contexts++; value = businesses; }
    else if (path.endsWith("/execution-context/access")) value = { tenantId, businessId, roles: user.roles, permissions };
    else if (path.endsWith("/reference-options/point-of-sale-mode")) value = [{ id: "direct", code: "DirectSale", label: "Venta directa" }, { id: "store", code: "Store", label: "Tiendas" }, { id: "restaurant", code: "RestaurantBar", label: "Restaurante y bar" }];
    else if (path.endsWith("/businesses") && request.method() === "GET") {
      calls.lists++; expect(Number(url.searchParams.get("pageSize"))).toBeLessThanOrEqual(20);
      const search = (url.searchParams.get("search") ?? "").toLowerCase();
      const items = businesses.filter(item => item.name.toLowerCase().includes(search) || item.email.toLowerCase().includes(search));
      value = { items, totalCount: items.length, page: 1, pageSize: 20, totalPages: 1 };
    } else if (path.endsWith("/businesses") && request.method() === "POST") {
      const body = request.postDataJSON(); calls.writes.push({ method: "POST", body });
      const created = { ...original, ...body, businessId: "55555555-5555-5555-5555-555555555555" };
      businesses.push(created); value = created;
    } else if (path.endsWith(`/businesses/${businessId}`)) {
      if (request.method() === "PUT") {
        const body = request.postDataJSON(); calls.writes.push({ method: "PUT", body });
        if (failSave) return route.fulfill({ status: 409, json: { detail: "La sede cambió. Revisa los datos antes de guardar." } });
        Object.assign(original, body); value = original;
      } else { calls.details++; value = original; }
    }
    await route.fulfill({ status: 200, json: value });
  });
  return { calls, businesses, failNextSave: () => { failSave = true; } };
}

async function open(page: Page, action: "Ver" | "Editar", name = "Principal") {
  await page.getByRole("button", { name: `Acciones de ${name}`, exact: true }).click();
  await page.getByRole("menuitem", { name: action, exact: true }).click();
  return page.getByRole("dialog", { name: action === "Ver" ? "Ver sede" : "Editar sede", exact: true });
}

test("ver es de solo lectura; editar guarda todos los datos en un único comando", async ({ page }) => {
  const { calls } = await setup(page);
  await page.goto("/dashboard/businesses");
  const dialog = await open(page, "Ver");
  await expect(dialog.getByLabel("Nombre", { exact: true })).toHaveAttribute("readonly", "");
  await expect(dialog.getByRole("combobox", { name: "Tipo de operación" })).toBeDisabled();
  await expect(dialog.getByRole("button", { name: "Guardar", exact: true })).toHaveCount(0);
  await dialog.getByRole("button", { name: "Editar", exact: true }).click();
  const editor = page.getByRole("dialog", { name: "Editar sede", exact: true });
  await expect(editor).toBeVisible();
  expect(calls.writes).toHaveLength(0);
  await editor.getByLabel("Nombre", { exact: true }).fill("Principal editada");
  await editor.getByLabel("Dirección", { exact: true }).fill("Carrera 2");
  await editor.getByLabel("Correo", { exact: true }).fill("editada@example.test");
  await editor.getByLabel("Teléfono", { exact: true }).fill("3011112233");
  await editor.getByRole("combobox", { name: "Tipo de operación" }).click();
  await page.getByRole("option", { name: "Restaurante y bar", exact: true }).click();
  await editor.getByLabel("Propina sugerida en la precuenta (%)").fill("2.5");
  await editor.getByRole("switch", { name: "Restringir mesa al mesero responsable" }).click();
  await editor.getByRole("switch", { name: "Compartir precios y costos" }).click();
  expect(calls.writes).toHaveLength(0);
  await editor.getByRole("button", { name: "Guardar", exact: true }).click();
  await expect(editor).toBeHidden();
  await expect(page.getByRole("button", { name: "Acciones de Principal editada", exact: true })).toBeVisible();
  expect(calls.writes).toEqual([{ method: "PUT", body: expect.objectContaining({ name: "Principal editada", address: "Carrera 2", email: "editada@example.test", phone: "3011112233", pointOfSaleMode: "RestaurantBar", restaurantSuggestedTipPercent: 2.5, restaurantRestrictTableToWaiter: true, sharesProductPrices: true }) }]);
  expect(calls.writes[0].body).not.toHaveProperty("priceSourceBusinessId");
  expect(calls).toMatchObject({ lists: 2, details: 0, contexts: 1 });
  const reopened = await open(page, "Ver", "Principal editada");
  await expect(reopened.getByLabel("Dirección", { exact: true })).toHaveValue("Carrera 2");
  expect(calls.details).toBe(0);
});

test("cancelar descarta cambios; un error conserva el formulario y no guarda al perder foco", async ({ page }) => {
  const data = await setup(page);
  await page.goto("/dashboard/businesses");
  let editor = await open(page, "Editar");
  await editor.getByLabel("Nombre", { exact: true }).fill("No guardar");
  await editor.getByRole("button", { name: "Cancelar", exact: true }).click();
  expect(data.calls.writes).toHaveLength(0);
  editor = await open(page, "Editar");
  await expect(editor.getByLabel("Nombre", { exact: true })).toHaveValue("Principal");
  data.failNextSave();
  await editor.getByLabel("Nombre", { exact: true }).fill("Cambio pendiente");
  await editor.getByRole("button", { name: "Guardar", exact: true }).click();
  await expect(editor.getByRole("alert")).toContainText("La sede cambió");
  await expect(editor.getByLabel("Nombre", { exact: true })).toHaveValue("Cambio pendiente");
  expect(data.calls).toMatchObject({ lists: 1, details: 0 });
});

test("nueva sede usa el mismo modal, origen paginado y crear/cancelar", async ({ page }) => {
  const { calls } = await setup(page);
  await page.goto("/dashboard/businesses");
  await page.getByRole("button", { name: "Nueva sede", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Nueva sede", exact: true });
  await dialog.getByLabel("Nombre", { exact: true }).fill("Tienda nueva");
  await dialog.getByLabel("Dirección", { exact: true }).fill("Calle 3");
  await dialog.getByLabel("Teléfono", { exact: true }).fill("3002223344");
  await dialog.getByLabel("Correo", { exact: true }).fill("tienda@example.test");
  await dialog.getByRole("combobox", { name: "Tipo de operación" }).click();
  await page.getByRole("option", { name: "Tiendas", exact: true }).click();
  await dialog.getByRole("combobox", { name: "Copiar precios desde" }).click();
  await page.getByRole("option", { name: "Sucursal origen", exact: true }).click();
  expect(calls.writes).toHaveLength(0);
  await dialog.getByRole("button", { name: "Crear", exact: true }).click();
  await expect(dialog).toBeHidden();
  await expect(page.getByRole("button", { name: "Acciones de Tienda nueva", exact: true })).toBeVisible();
  expect(calls.writes).toEqual([{ method: "POST", body: expect.objectContaining({ name: "Tienda nueva", pointOfSaleMode: "Store", priceSourceBusinessId: sourceId }) }]);
  expect(calls).toMatchObject({ lists: 2, details: 0, contexts: 2 });
  await expect(page).toHaveURL(/\/dashboard\/businesses$/);
});

test("solo lectura no ofrece crear ni editar; las rutas previas abren el mismo modal", async ({ page }) => {
  const { calls } = await setup(page, true);
  await page.goto(`/dashboard/businesses/${businessId}`);
  const dialog = page.getByRole("dialog", { name: "Ver sede", exact: true });
  await expect(dialog.getByLabel("Nombre", { exact: true })).toHaveValue("Principal");
  await expect(dialog.getByRole("button", { name: "Editar", exact: true })).toHaveCount(0);
  await dialog.getByRole("button", { name: "Cancelar", exact: true }).click();
  await expect(page).toHaveURL(/\/dashboard\/businesses$/);
  await expect(page.getByRole("button", { name: "Nueva sede", exact: true })).toHaveCount(0);
  expect(calls.writes).toHaveLength(0);
});
