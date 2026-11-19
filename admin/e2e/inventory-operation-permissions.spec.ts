import { expect, test, type Page } from "@playwright/test";

async function workspace(page: Page, extraPermissions: string[] = []) {
  const tenantId = "11111111-1111-1111-1111-111111111111";
  const businessId = "22222222-2222-2222-2222-222222222222";
  const permissions = ["dashboard.read", "inventory.read", ...extraPermissions];
  const user = { userId: "33333333-3333-3333-3333-333333333333", tenantId, tenantKey: "AURALY",
    username: "inventory-viewer", firstName: "Prueba", lastName: "Permisos", roles: ["Inventory viewer"], permissions };
  const requests: string[] = [];
  await page.context().addCookies([{ name: "auth_token", value: "e2e",
    url: process.env.AURALY_E2E_BASE_URL ?? "http://127.0.0.1:3000", httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ tenantId, businessId, user }) => {
    localStorage.setItem("selected_tenant_id", tenantId);
    localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
  }, { tenantId, businessId, user });
  await page.route("**/api/**", async (route) => {
    const url = new URL(route.request().url());
    requests.push(url.pathname + url.search);
    let body: unknown = [];
    if (url.pathname.endsWith("/auth/me")) body = user;
    else if (url.pathname.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Prueba inventario" }];
    else if (url.pathname.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Sede prueba", timeZone: "America/Bogota" }];
    else if (url.pathname.endsWith("/execution-context/access")) body = { tenantId, businessId, roles: user.roles, permissions };
    else if (url.pathname.endsWith("/inventory/warehouses")) body = [{ warehouseId: "44444444-4444-4444-4444-444444444444", code: "PPL", name: "Principal" }];
    else if (url.pathname.endsWith("/inventory/reasons")) body = [{ inventoryReasonId: "55555555-5555-5555-5555-555555555555", code: "PRESENTATION_CHANGE", name: "Cambio de presentación", operationType: "ProductConversion", isActive: true }];
    else if (/\/inventory\/(balances|movements|operations|conversion-products)$/.test(url.pathname)) body = { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 };
    else if (url.pathname.endsWith("/inventory/movements/filter-options")) body = { documentTypes: [], movementTypes: [] };
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
  });
  await page.goto("/dashboard/inventory");
  await expect(page.getByRole("heading", { name: "Inventario", exact: true })).toBeVisible();
  return requests;
}

test("inventario de solo lectura mantiene existencias y kárdex, sin operaciones ni editores", async ({ page }) => {
  const requests = await workspace(page);
  await expect(page.getByRole("tab", { name: "Existencias", exact: true })).toBeEnabled();
  await expect(page.getByRole("tab", { name: "Operaciones", exact: true })).toBeDisabled();
  await expect(page.getByRole("button", { name: "Nueva operación", exact: true })).toBeDisabled();
  await page.getByRole("tab", { name: "Kárdex", exact: true }).click();
  await expect(page.getByRole("tab", { name: "Kárdex", exact: true })).toHaveAttribute("data-state", "active");
  expect(requests.some((path) => path.includes("/inventory/operations"))).toBe(false);
  expect(requests.some((path) => path.includes("/physical-count"))).toBe(false);
});

test("solo conversiones habilita su pestaña y formulario, sin montar conteos ni otras operaciones", async ({ page }) => {
  const requests = await workspace(page, ["inventory.conversions.confirm"]);
  await page.getByRole("tab", { name: "Operaciones", exact: true }).click();
  for (const name of ["Inventarios", "Movimientos de mercancía", "Recepciones", "Traslados", "Producción", "Averías"])
    await expect(page.getByRole("button", { name, exact: true })).toBeDisabled();
  await page.getByRole("button", { name: "Conversiones", exact: true }).click();
  await expect(page.getByRole("button", { name: "Conversiones", exact: true })).toBeEnabled();
  await page.getByRole("button", { name: "Nueva operación", exact: true }).click();
  const editor = page.getByRole("dialog", { name: "Nueva operación", exact: true });
  await expect(editor.getByRole("button", { name: /Conteo físico/ })).toBeDisabled();
  await expect(editor.getByRole("heading", { name: "Conversión", exact: true })).toBeVisible();
  expect(requests.some((path) => /operationType=StockCount|documentType=StockCount|physical-count/.test(path))).toBe(false);
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(editor.getByRole("heading", { name: "Conversión", exact: true })).toBeVisible();
});
