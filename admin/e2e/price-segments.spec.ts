import { expect, test, type Page } from "@playwright/test";

async function login(page: Page) {
  const tenantId = "11111111-1111-1111-1111-111111111111";
  const businessId = "22222222-2222-2222-2222-222222222222";
  await page.context().addCookies([{ name: "auth_token", value: "e2e", domain: "127.0.0.1", path: "/", httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ tenantId: tenant, businessId: business }) => {
    localStorage.setItem("selected_tenant_id", tenant);
    localStorage.setItem("selected_business_id", business);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user: { userId: "33333333-3333-3333-3333-333333333333", tenantId: tenant, tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test", firstName: "Prueba", lastName: "E2E", avatarUrl: null, roles: ["Administrator"], permissions: ["dashboard.read", "catalog.read", "pricing.read", "pricing.segments.read", "pricing.segments.manage"] } }, version: 0 }));
  }, { tenantId, businessId });
}

test("listas y canales administra condiciones y cada guardado cierra su modal", async ({ page }) => {
  test.setTimeout(120_000);
  const listId = "99999999-9999-7999-8999-999999999999";
  const productId = "77777777-7777-7777-7777-777777777777";
  let segments: Array<Record<string, unknown>> = [
    { id: listId, kind: "PriceList", code: "MAYORISTA", name: "Mayoristas", strategy: null, value: null, isActive: true, createdAt: "2026-08-14T10:00:00Z", productCount: 0, customerCount: 2 },
    { id: "88888888-8888-7888-8888-888888888888", kind: "PriceChannel", code: "WEB", name: "Tienda web", strategy: "PercentageOverBasePrice", value: -5, isActive: true, createdAt: "2026-08-14T10:00:00Z", productCount: 0, customerCount: 0 },
  ];
  let savedItem: Record<string, unknown> | null = null;
  let createdRequest: Record<string, unknown> | null = null;

  await page.route("**/api/auth/me", async (route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ userId: "33333333-3333-3333-3333-333333333333", tenantId: "11111111-1111-1111-1111-111111111111", tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test", firstName: "Prueba", lastName: "E2E", avatarUrl: null, roles: ["Administrator"], permissions: ["dashboard.read", "catalog.read", "pricing.read", "pricing.segments.read", "pricing.segments.manage"] }) }));

  await page.route("**/api/commerce/v1/pricing/segments**", async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    if (request.method() === "GET" && /\/items$/.test(url.pathname)) {
      await route.fulfill({ status: 200, contentType: "application/json", body: "[]" });
      return;
    }
    if (request.method() === "GET") {
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(segments) });
      return;
    }
    if (request.method() === "POST") {
      const body = request.postDataJSON() as { kind: "PriceList" | "PriceChannel"; name: string; priceVariationPercent?: number; items?: unknown[] };
      createdRequest = body as unknown as Record<string, unknown>;
      const created = { id: crypto.randomUUID(), kind: body.kind, code: "LST-AUTO", name: body.name, isActive: true, createdAt: new Date().toISOString(), productCount: body.items?.length ?? 0, customerCount: 0, priceVariationPercent: body.priceVariationPercent ?? null };
      segments = [...segments, created];
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(created) });
      return;
    }
    if (request.method() === "PUT") {
      savedItem = request.postDataJSON() as Record<string, unknown>;
      await route.fulfill({ status: 204 });
      return;
    }
    await route.fulfill({ status: 204 });
  });

  await page.route("**/api/businesses/*/products**", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        items: [{ productId, businessId: "22222222-2222-2222-2222-222222222222", productCode: "PRD-000001", sku: "ACE-3L", name: "Aceite vegetal 3000 ml", unitPrice: 29500, currency: "COP", manageStock: true, isActive: true }],
        page: 1,
        pageSize: 50,
        totalCount: 1,
        totalPages: 1,
      }),
    });
  });

  await page.route("**/api/execution-context/access", async (route) => {
    const headers = route.request().headers();
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        tenantId: headers["x-tenant-id"] ?? "22222222-2222-2222-2222-222222222222",
        businessId: headers["x-business-id"] ?? "22222222-2222-2222-2222-222222222222",
        roles: ["Administrator"],
        permissions: ["dashboard.read", "catalog.read", "pricing.read", "pricing.segments.read", "pricing.segments.manage"],
      }),
    });
  });
  await page.route("**/api/execution-context/tenants", async (route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify([{ tenantId: "11111111-1111-1111-1111-111111111111", name: "Auraly" }]) }));
  await page.route("**/api/execution-context/businesses", async (route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify([{ tenantId: "11111111-1111-1111-1111-111111111111", businessId: "22222222-2222-2222-2222-222222222222", name: "Auraly" }]) }));

  await login(page);
  await page.goto("/dashboard/products/price-segments");
  await expect(page.getByRole("heading", { name: "Listas y canales comerciales" })).toBeVisible();

  await page.getByRole("button", { name: "Nueva lista o canal" }).click();
  const createDialog = page.getByRole("dialog", { name: "Nueva lista o canal" });
  await expect(createDialog.getByText("Código *", { exact: true })).toHaveCount(0);
  await createDialog.getByText("Nombre *", { exact: true }).locator("..").getByRole("textbox").fill("Distribuidores");
  await createDialog.getByPlaceholder(/Código interno/).fill("Aceite");
  await expect(createDialog.getByRole("button", { name: "Agregar", exact: true })).toBeEnabled();
  await createDialog.getByRole("button", { name: "Agregar", exact: true }).click();
  await expect(createDialog.getByText("Aceite vegetal 3000 ml", { exact: true })).toBeVisible();
  await createDialog.getByText("Desde cantidad", { exact: true }).locator("..").locator("input").fill("3");
  await createDialog.getByRole("button", { name: "Agregar precio" }).click();
  await createDialog.getByRole("button", { name: /Crear lista · 1 precios/ }).click();
  await expect(createDialog).toBeHidden();
  await expect(page.getByRole("dialog", { name: "Distribuidores" })).toBeVisible();
  expect(createdRequest).toMatchObject({ kind: "PriceList", name: "Distribuidores", items: [{ productId, amount: 29500, minimumQuantity: 3 }] });
  await page.keyboard.press("Escape");
  await expect(page.getByRole("cell", { name: "Distribuidores" })).toBeVisible();

  await page.getByRole("cell", { name: "Mayoristas" }).click();
  const detailDialog = page.getByRole("dialog", { name: "Mayoristas" });
  await expect(detailDialog.getByText("Escalas por cantidad")).toBeVisible();
  await detailDialog.getByRole("button", { name: "Agregar producto" }).click();

  const itemDialog = page.getByRole("dialog", { name: "Agregar producto" });
  await itemDialog.getByPlaceholder(/Código interno/).fill("Aceite");
  await expect(itemDialog.getByRole("button", { name: "Agregar", exact: true })).toBeEnabled();
  await itemDialog.getByRole("button", { name: "Agregar", exact: true }).click();
  const minimum = itemDialog.getByText("Cantidad mínima *", { exact: true }).locator("..").locator("input");
  await minimum.fill("12");
  await itemDialog.getByRole("button", { name: "Guardar condición" }).click();
  await expect(itemDialog).toBeHidden();
  await expect(detailDialog).toBeVisible();
  expect(savedItem).toMatchObject({ amount: 29500, minimumQuantity: 12, excluded: false });
});
