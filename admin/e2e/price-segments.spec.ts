import { expect, test, type Page } from "@playwright/test";

async function login(page: Page) {
  const tenantId = "11111111-1111-1111-1111-111111111111";
  const businessId = "22222222-2222-2222-2222-222222222222";
  await page.context().addCookies([{ name: "auth_token", value: "e2e", url: process.env.AURALY_E2E_BASE_URL ?? "http://127.0.0.1:3000", httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ tenantId: tenant, businessId: business }) => {
    localStorage.setItem("selected_tenant_id", tenant);
    localStorage.setItem("selected_business_id", business);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user: { userId: "33333333-3333-3333-3333-333333333333", tenantId: tenant, tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test", firstName: "Prueba", lastName: "E2E", avatarUrl: null, roles: ["Administrator"], permissions: ["dashboard.read", "catalog.read", "pricing.read", "pricing.segments.read", "pricing.segments.manage"] } }, version: 0 }));
  }, { tenantId, businessId });
}

test("canales administra precios por cantidad y modos calculados sin listas", async ({ page }) => {
  test.setTimeout(120_000);
  const tierChannelId = "99999999-9999-7999-8999-999999999999";
  const productId = "77777777-7777-7777-7777-777777777777";
  let segments: Array<Record<string, unknown>> = [
    { id: tierChannelId, code: "MAYORISTA", name: "Mayoristas", strategy: "TieredProductPrice", value: null, isActive: true, createdAt: "2026-08-14T10:00:00Z", productCount: 0, customerCount: 2 },
    { id: "88888888-8888-7888-8888-888888888888", code: "WEB", name: "Tienda web", strategy: "PercentageOverBasePrice", value: -5, isActive: true, createdAt: "2026-08-14T10:00:00Z", productCount: 0, customerCount: 0 },
  ];
  let savedItem: Record<string, unknown> | null = null;
  let savedExclusion: Record<string, unknown> | null = null;
  let createdRequest: Record<string, unknown> | null = null;

  await page.route("**/api/auth/me", async (route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ userId: "33333333-3333-3333-3333-333333333333", tenantId: "11111111-1111-1111-1111-111111111111", tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test", firstName: "Prueba", lastName: "E2E", avatarUrl: null, roles: ["Administrator"], permissions: ["dashboard.read", "catalog.read", "pricing.read", "pricing.segments.read", "pricing.segments.manage"] }) }));

  await page.route("**/api/commerce/v1/pricing/segments**", async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    if (/\/exclusions(?:\/[^/]+)?$/.test(url.pathname)) {
      if (request.method() === "GET") await route.fulfill({ status: 200, contentType: "application/json", body: "[]" });
      else if (request.method() === "POST") { savedExclusion = request.postDataJSON() as Record<string, unknown>; await route.fulfill({ status: 201, contentType: "application/json", body: JSON.stringify({ exclusionId: crypto.randomUUID() }) }); }
      else await route.fulfill({ status: 204 });
      return;
    }
    if (request.method() === "GET" && /\/items$/.test(url.pathname)) {
      await route.fulfill({ status: 200, contentType: "application/json", body: "[]" });
      return;
    }
    if (request.method() === "GET") {
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(segments) });
      return;
    }
    if (request.method() === "POST") {
      const body = request.postDataJSON() as { name: string; channelStrategy: string; channelValue?: number | null; items?: unknown[] };
      createdRequest = body as unknown as Record<string, unknown>;
      const created = { id: crypto.randomUUID(), code: "CNL-AUTO", name: body.name, isActive: true, createdAt: new Date().toISOString(), productCount: body.items?.length ?? 0, customerCount: 0, strategy: body.channelStrategy, value: body.channelValue ?? null };
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
  await page.route("**/api/businesses/*/product-categories**", async (route) => route.fulfill({ status: 200, contentType: "application/json", body: "[]" }));
  await page.route("**/api/commerce/v1/product-brands**", async (route) => route.fulfill({ status: 200, contentType: "application/json", body: "[]" }));

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
  await expect(page.getByRole("heading", { name: "Canales de precios" })).toBeVisible();
  await expect(page.getByText(/lista de precios/i)).toHaveCount(0);

  await page.getByRole("button", { name: "Nuevo canal" }).click();
  const createDialog = page.getByRole("dialog", { name: "Nuevo canal de precios" });
  await expect(createDialog.getByText("Código *", { exact: true })).toHaveCount(0);
  await expect(createDialog.getByRole("button", { name: "Precios por producto y cantidad" })).toBeVisible();
  await expect(createDialog.getByRole("button", { name: "% sobre precio público" })).toBeVisible();
  await expect(createDialog.getByRole("button", { name: "Descuento sobre precio público" })).toHaveCount(0);
  await expect(createDialog.getByRole("button", { name: "Margen sobre último costo" })).toBeVisible();
  await expect(createDialog.getByRole("button", { name: "Margen sobre costo promedio" })).toBeVisible();
  await expect(createDialog.getByRole("button", { name: "Ajustar margen del producto" })).toBeVisible();
  await expect(createDialog.getByRole("button", { name: "Vender al costo promedio" })).toBeVisible();
  await createDialog.getByText("Nombre *", { exact: true }).locator("..").getByRole("textbox").fill("Distribuidores");
  await createDialog.getByPlaceholder(/Código interno/).fill("Aceite");
  await createDialog.getByRole("option", { name: /Aceite vegetal 3000 ml/ }).click();
  const pricesDialog = page.getByRole("dialog", { name: "Precios por cantidad" });
  await expect(pricesDialog.getByText(/Aceite vegetal 3000 ml/)).toBeVisible();
  await pricesDialog.locator("#new-channel-product-minimum-0").fill("3");
  await pricesDialog.getByRole("button", { name: "Agregar precios" }).click();
  await createDialog.getByRole("button", { name: /Crear canal · 1 precios/ }).click();
  await expect(createDialog).toBeHidden();
  await expect(page.getByRole("dialog", { name: "Distribuidores" })).toHaveCount(0);
  expect(createdRequest).toMatchObject({ name: "Distribuidores", channelStrategy: "TieredProductPrice", items: [{ productId, amount: 29500, minimumQuantity: 3 }] });
  await expect(page.getByRole("cell", { name: "Distribuidores" })).toBeVisible();

  await page.getByRole("cell", { name: "Mayoristas" }).click();
  const detailDialog = page.getByRole("dialog", { name: "Mayoristas" });
  await expect(detailDialog.getByRole("button", { name: "Agregar producto" })).toHaveCount(0);
  await expect(detailDialog.getByRole("button", { name: "Precios por producto y cantidad" })).toBeVisible();
  await expect(detailDialog.getByRole("button", { name: "Ajustar margen del producto" })).toBeVisible();
  await detailDialog.getByRole("button", { name: "Editar canal" }).click();
  const editDialog = page.getByRole("dialog", { name: "Editar canal de precios" });
  await editDialog.getByRole("button", { name: "Agregar producto" }).click();

  const itemDialog = page.getByRole("dialog", { name: "Agregar producto" });
  await itemDialog.getByPlaceholder(/Código interno/).fill("Aceite");
  await itemDialog.getByRole("option", { name: /Aceite vegetal 3000 ml/ }).click();
  const minimum = itemDialog.getByText("Cantidad mínima *", { exact: true }).locator("..").locator("input");
  await minimum.fill("12");
  await itemDialog.getByRole("button", { name: "Guardar condición" }).click();
  await expect(itemDialog).toBeHidden();
  await expect(editDialog).toBeVisible();
  expect(savedItem).toMatchObject({ amount: 29500, minimumQuantity: 12 });
  const exclusions = editDialog.getByRole("heading", { name: "Excluidos" }).locator("xpath=ancestor::section");
  await exclusions.getByRole("button", { name: "Producto", exact: true }).click();
  await exclusions.getByPlaceholder(/Código interno/).fill("Aceite");
  await exclusions.getByRole("option", { name: /Aceite vegetal 3000 ml/ }).click();
  expect(savedExclusion).toMatchObject({ scopeType: "Product", scopeId: productId });

  await page.keyboard.press("Escape");
  await page.getByRole("button", { name: "Nuevo canal" }).click();
  const channelDialog = page.getByRole("dialog", { name: "Nuevo canal de precios" });
  await channelDialog.getByText("Nombre *", { exact: true }).locator("..").getByRole("textbox").fill("Distribuidores costo");
  await channelDialog.getByRole("button", { name: "Margen sobre costo promedio" }).click();
  await channelDialog.getByText("Margen objetivo (%)", { exact: true }).locator("..").locator("input").fill("22");
  await channelDialog.getByRole("button", { name: "Crear canal" }).click();
  await expect(channelDialog).toBeHidden();
  expect(createdRequest).toMatchObject({ name: "Distribuidores costo", channelStrategy: "FixedMarginOverAverageCost", channelValue: 22 });

  await page.getByRole("button", { name: "Nuevo canal" }).click();
  const costDialog = page.getByRole("dialog", { name: "Nuevo canal de precios" });
  await costDialog.getByText("Nombre *", { exact: true }).locator("..").getByRole("textbox").fill("Margen último costo");
  await costDialog.getByRole("button", { name: "Margen sobre último costo" }).click();
  const costPercentage = costDialog.getByText("Margen objetivo (%)", { exact: true }).locator("..").locator("input");
  await costPercentage.fill("4");
  await expect(costPercentage).toHaveValue("4");
  await costDialog.getByRole("button", { name: "Crear canal" }).click();
  await expect(costDialog).toBeHidden();
  expect(createdRequest).toMatchObject({ name: "Margen último costo", channelStrategy: "MarginOverLatestCost", channelValue: 4 });

  await page.getByRole("button", { name: "Nuevo canal" }).click();
  const marginAdjustmentDialog = page.getByRole("dialog", { name: "Nuevo canal de precios" });
  await marginAdjustmentDialog.getByText("Nombre *", { exact: true }).locator("..").getByRole("textbox").fill("Margen especial");
  await marginAdjustmentDialog.getByRole("button", { name: "Ajustar margen del producto" }).click();
  await marginAdjustmentDialog.getByText("Puntos de ajuste", { exact: true }).locator("..").locator("input").fill("-10");
  await expect(marginAdjustmentDialog.getByText(/20% \+ 10 puntos = 30%/)).toBeVisible();
  await marginAdjustmentDialog.getByRole("button", { name: "Crear canal" }).click();
  await expect(marginAdjustmentDialog).toBeHidden();
  expect(createdRequest).toMatchObject({ name: "Margen especial", channelStrategy: "ProductMarginAdjustment", channelValue: -10 });

  await page.getByRole("button", { name: "Nuevo canal" }).click();
  const discountDialog = page.getByRole("dialog", { name: "Nuevo canal de precios" });
  await discountDialog.getByText("Nombre *", { exact: true }).locator("..").getByRole("textbox").fill("Descuento web");
  await discountDialog.getByRole("button", { name: "% sobre precio público" }).click();
  await discountDialog.getByText(/Variación/).locator("..").locator("input").fill("-10");
  await discountDialog.getByRole("button", { name: "Crear canal" }).click();
  await expect(discountDialog).toBeHidden();
  expect(createdRequest).toMatchObject({ name: "Descuento web", channelStrategy: "PercentageOverBasePrice", channelValue: -10 });
});
