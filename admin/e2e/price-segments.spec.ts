import { expect, test, type Locator, type Page } from "@playwright/test";

async function login(page: Page) {
  const tenantId = "11111111-1111-1111-1111-111111111111";
  const businessId = "22222222-2222-2222-2222-222222222222";
  await page.context().addCookies([{ name: "auth_token", value: "e2e", url: process.env.AURALY_E2E_BASE_URL ?? "http://127.0.0.1:3000", httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ tenantId: tenant, businessId: business }) => {
    localStorage.setItem("selected_tenant_id", tenant);
    localStorage.setItem("selected_business_id", business);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user: { userId: "33333333-3333-3333-3333-333333333333", tenantId: tenant, tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test", firstName: "Prueba", lastName: "E2E", avatarUrl: null, roles: ["Administrator"], permissions: ["dashboard.read", "catalog.read", "pricing.read", "pricing.cost-basis.read", "pricing.segments.read", "pricing.segments.manage"] } }, version: 0 }));
  }, { tenantId, businessId });
}

async function expectMobileDialogFits(page: Page, dialog: Locator) {
  const viewport = page.viewportSize()!;
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await dialog.evaluate(element => {
    const bounds = element.getBoundingClientRect();
    return [...element.children].every(child => {
      const childBounds = child.getBoundingClientRect();
      return childBounds.left >= bounds.left && childBounds.right <= bounds.right;
    });
  })).toBe(true);
  await page.setViewportSize(viewport);
}

test("canales administra precios por cantidad, modos calculados e informes", async ({ page }) => {
  test.setTimeout(120_000);
  const tierChannelId = "99999999-9999-7999-8999-999999999999";
  const productId = "77777777-7777-7777-7777-777777777777";
  let segments: Array<Record<string, unknown>> = [
    { id: tierChannelId, code: "MAYORISTA", name: "Mayoristas", strategy: "TieredProductPrice", value: null, isActive: true, createdAt: "2026-08-14T10:00:00Z", productCount: 0, customerCount: 2 },
    { id: "88888888-8888-7888-8888-888888888888", code: "WEB", name: "Tienda web", strategy: "PercentageOverBasePrice", value: -5, isActive: true, createdAt: "2026-08-14T10:00:00Z", productCount: 0, customerCount: 0 },
  ];
  let savedItem: Record<string, unknown> | null = null;
  let settingsWrites = 0;
  let itemReads = 0;
  let savedExclusion: Record<string, unknown> | null = null;
  let createdRequest: Record<string, unknown> | null = null;
  let costGate: Promise<void> | null = null;
  let itemSaveGate: Promise<void> | null = null;
  let itemWrites = 0;

  await page.route("**/api/auth/me", async (route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ userId: "33333333-3333-3333-3333-333333333333", tenantId: "11111111-1111-1111-1111-111111111111", tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test", firstName: "Prueba", lastName: "E2E", avatarUrl: null, roles: ["Administrator"], permissions: ["dashboard.read", "catalog.read", "pricing.read", "pricing.cost-basis.read", "pricing.segments.read", "pricing.segments.manage"] }) }));

  await page.route("**/api/commerce/v1/pricing/segments**", async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    if (/\/products\/[^/]+\/cost$/.test(url.pathname)) {
      if (costGate) await costGate;
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ costBasisAmount: 20000, salesTaxRate: 0, currentMarginPercent: 32.203389 }) });
      return;
    }
    if (/\/exclusions(?:\/[^/]+)?$/.test(url.pathname)) {
      if (request.method() === "GET") await route.fulfill({ status: 200, contentType: "application/json", body: "[]" });
      else if (request.method() === "POST") { savedExclusion = request.postDataJSON() as Record<string, unknown>; await route.fulfill({ status: 201, contentType: "application/json", body: JSON.stringify({ exclusionId: crypto.randomUUID() }) }); }
      else await route.fulfill({ status: 204 });
      return;
    }
    if (request.method() === "GET" && /\/items$/.test(url.pathname)) {
      itemReads += 1;
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify([
        { productId, productCode: "PRD-000001", productName: "Aceite vegetal 3000 ml", amount: 29500, currencyCode: "COP", minimumQuantity: 1, targetMarginPercent: 32.203389, autoUpdatePrice: false, costBasisAmount: 20000, salesTaxRate: 0 },
        { productId, productCode: "PRD-000001", productName: "Aceite vegetal 3000 ml", amount: 27000, currencyCode: "COP", minimumQuantity: 12, targetMarginPercent: 25.925926, autoUpdatePrice: true, costBasisAmount: 20000, salesTaxRate: 0 },
      ]) });
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
      if (url.pathname.endsWith("/settings")) {
        settingsWrites += 1;
        if (itemSaveGate) await itemSaveGate;
        const newItems = (savedItem.newItems ?? []) as Record<string, unknown>[];
        const retiredItems = (savedItem.retiredItems ?? []) as unknown[];
        await route.fulfill({ json: { autoUpdatePrice: savedItem.autoUpdatePrice, itemSync: savedItem.itemSync ?? [],
          addedItems: newItems.map(item => ({ ...item, costBasisAmount: 20000, salesTaxRate: 0 })),
          productCount: 2 + newItems.length - retiredItems.length } });
      } else {
        itemWrites++;
        if (itemSaveGate) await itemSaveGate;
        await route.fulfill({ status: 204 });
      }
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
        permissions: ["dashboard.read", "catalog.read", "pricing.read", "pricing.cost-basis.read", "pricing.segments.read", "pricing.segments.manage"],
      }),
    });
  });
  await page.route("**/api/execution-context/tenants", async (route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify([{ tenantId: "11111111-1111-1111-1111-111111111111", name: "Auraly" }]) }));
  await page.route("**/api/execution-context/businesses", async (route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify([{ tenantId: "11111111-1111-1111-1111-111111111111", businessId: "22222222-2222-2222-2222-222222222222", name: "Auraly" }]) }));

  await login(page);
  await page.goto("/dashboard/products/price-segments");
  await expect(page.getByRole("heading", { name: "Canales de precios" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Lista de precios" })).toHaveCount(2);

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
  await createDialog.getByRole("checkbox", { name: "Sincronizar precio de todos los productos" }).check();
  await createDialog.getByPlaceholder(/Código interno/).fill("Aceite");
  await createDialog.getByRole("option", { name: /Aceite vegetal 3000 ml/ }).click();
  const pricesDialog = page.getByRole("dialog", { name: "Precios por cantidad" });
  await expect(pricesDialog.getByText(/Aceite vegetal 3000 ml/)).toBeVisible();
  await expect(pricesDialog.getByText("Costo base de la sede:", { exact: false })).toBeVisible();
  await expect(pricesDialog.getByRole("checkbox")).toHaveCount(0);
  await expectMobileDialogFits(page, pricesDialog);
  await pricesDialog.screenshot({ path: test.info().outputPath("precios-por-cantidad-editor.png") });
  await pricesDialog.locator("#new-channel-product-minimum-0").fill("3");
  await pricesDialog.getByRole("button", { name: "Agregar precios" }).click();
  await expectMobileDialogFits(page, createDialog);
  await createDialog.screenshot({ path: test.info().outputPath("precios-por-cantidad-canal.png") });
  await createDialog.getByRole("button", { name: /Crear canal · 1 precios/ }).click();
  await expect(createDialog).toBeHidden();
  await expect(page.getByRole("dialog", { name: "Distribuidores" })).toHaveCount(0);
  expect(createdRequest).toMatchObject({ name: "Distribuidores", channelStrategy: "TieredProductPrice", items: [{ productId, amount: 29500, minimumQuantity: 3, autoUpdatePrice: true, targetMarginPercent: 32.20339 }] });
  await expect(page.getByRole("cell", { name: "Distribuidores" })).toBeVisible();

  await page.getByRole("cell", { name: "Mayoristas" }).click();
  const detailDialog = page.getByRole("dialog", { name: "Mayoristas" });
  await expect(detailDialog.getByRole("button", { name: "Agregar producto" })).toHaveCount(0);
  await expect(detailDialog.getByRole("button", { name: "Precios por producto y cantidad" })).toBeVisible();
  await expect(detailDialog.getByRole("button", { name: "Ajustar margen del producto" })).toBeVisible();
  await expectMobileDialogFits(page, detailDialog);
  await page.setViewportSize({ width: 390, height: 844 });
  const groupedScales = detailDialog.getByRole("table", { name: "Escalas de Aceite vegetal 3000 ml" });
  await expect(groupedScales).toHaveCount(1);
  await expect(groupedScales.locator("tbody tr")).toHaveCount(2);
  const scale = groupedScales.locator("tbody tr").first();
  await scale.scrollIntoViewIfNeeded();
  await expect(scale.getByRole("cell").filter({hasText:"29.500"})).toBeInViewport();
  await expect(scale.getByRole("checkbox")).toBeInViewport();
  expect(await scale.evaluate(element=>{
    const bounds=element.getBoundingClientRect();
    return [...element.querySelectorAll("td")].every(cell=>{
      const field=cell.getBoundingClientRect();return field.left>=bounds.left&&field.right<=bounds.right+1;
    });
  })).toBe(true);
  await detailDialog.screenshot({path:test.info().outputPath("precios-escalas-mobile.png")});
  await detailDialog.getByRole("button", { name: "Editar canal" }).scrollIntoViewIfNeeded();
  await expect(detailDialog.getByRole("button", { name: "Editar canal" })).toBeInViewport();
  await page.setViewportSize({ width: 1366, height: 768 });
  await detailDialog.getByRole("button", { name: "Editar canal" }).click();
  const editDialog = page.getByRole("dialog", { name: "Editar canal de precios" });
  const synchronizeAll = editDialog.getByRole("checkbox", { name: "Sincronizar precio de todos los productos" });
  await expect(synchronizeAll).toHaveAttribute("data-state", "indeterminate");
  const readsBeforeSave = itemReads;
  await synchronizeAll.check();
  await expect(editDialog.getByRole("checkbox")).toHaveCount(3);
  await editDialog.getByRole("checkbox", { name: "Sincronizar Aceite vegetal 3000 ml desde 12 unidades" }).uncheck();
  await expect(synchronizeAll).toHaveAttribute("data-state", "indeterminate");
  await editDialog.screenshot({ path: test.info().outputPath("precios-por-cantidad-editar.png") });
  await editDialog.getByRole("button", { name: "Guardar canal" }).click();
  await expect(detailDialog).toBeVisible();
  expect(settingsWrites).toBe(1);
  expect(savedItem).toMatchObject({ autoUpdatePrice: true, itemSync: [{ productId, minimumQuantity: 12, autoUpdatePrice: false }] });
  expect(itemReads).toBe(readsBeforeSave);
  await expect(detailDialog.getByRole("checkbox", { name: "Sincronizar precio de todos los productos" })).toHaveAttribute("data-state", "indeterminate");
  await expect(detailDialog.getByRole("checkbox", { name: "Sincronizar Aceite vegetal 3000 ml desde 12 unidades" })).not.toBeChecked();
  // Mixed selection can be cleared without changing prices or refetching items.
  await detailDialog.getByRole("button", { name: "Editar canal" }).click();
  await expect(synchronizeAll).toBeEnabled();
  await synchronizeAll.check();
  await expect(synchronizeAll).toHaveAttribute("data-state", "checked");
  await synchronizeAll.uncheck();
  await expect(synchronizeAll).toHaveAttribute("data-state", "unchecked");
  await editDialog.getByRole("button", { name: "Guardar canal" }).click();
  await expect(detailDialog).toBeVisible();
  expect(settingsWrites).toBe(2);
  expect(savedItem).toMatchObject({ autoUpdatePrice: false, itemSync: [] });
  expect(itemReads).toBe(readsBeforeSave);
  await expect(detailDialog.getByRole("checkbox", { name: "Sincronizar precio de todos los productos" })).not.toBeChecked();
  await expect(detailDialog.getByRole("checkbox", { name: "Sincronizar Aceite vegetal 3000 ml desde 1 unidades" })).not.toBeChecked();
  await expect(detailDialog.getByRole("checkbox", { name: "Sincronizar Aceite vegetal 3000 ml desde 12 unidades" })).not.toBeChecked();
  await expect(groupedScales.locator("tbody tr").first()).toContainText("29.500");
  await detailDialog.getByRole("button", { name: "Editar canal" }).click();
  let releaseCost!: () => void;
  costGate = new Promise<void>((resolve) => { releaseCost = resolve; });
  await editDialog.getByRole("button", { name: "Agregar producto" }).click();

  const itemDialog = page.getByRole("dialog", { name: "Agregar producto" });
  await itemDialog.getByPlaceholder(/Código interno/).fill("Aceite");
  await itemDialog.getByRole("option", { name: /Aceite vegetal 3000 ml/ }).click();
  const firstTier = itemDialog.getByRole("group", { name: "Escala 1", exact: true });
  const minimum = firstTier.getByLabel("Cantidad mínima *", { exact: true });
  const price = firstTier.getByLabel("Precio *", { exact: true });
  const margin = firstTier.getByLabel("Margen (%)", { exact: true });
  await minimum.fill("5");
  await expect(price).toBeDisabled();
  await expect(itemDialog.getByRole("button", { name: "Agregar precios", exact: true })).toBeDisabled();
  releaseCost();
  await expect(itemDialog.getByText(/Costo base de la sede/)).toBeVisible();
  await expect(minimum).toHaveValue("5");
  const previewRequests: string[] = [];
  page.on("request", request => {
    if (request.url().endsWith("/pricing/calculate")) previewRequests.push(request.url());
  });
  await price.fill("32000");
  await expect(price).toBeFocused();
  await expect(margin).toHaveValue("37.5");
  await price.fill("40000");
  await expect(price).toBeFocused();
  await expect(margin).toHaveValue("50");
  await itemDialog.getByRole("button", { name: "Agregar otro precio", exact: true }).click();
  const secondTier = itemDialog.getByRole("group", { name: "Escala 2", exact: true });
  const secondMinimum = secondTier.getByLabel("Cantidad mínima *", { exact: true });
  await secondMinimum.fill("5");
  await expect(itemDialog.getByRole("alert")).toContainText("Cada cantidad debe ser única");
  await expect(itemDialog.getByRole("button", { name: "Agregar precios", exact: true })).toBeDisabled();
  await secondMinimum.fill("-1");
  await expect(secondMinimum).toHaveAttribute("aria-invalid", "true");
  await expect(itemDialog.getByRole("button", { name: "Agregar precios", exact: true })).toBeDisabled();
  await secondMinimum.fill("0");
  await expect(secondMinimum).toHaveAttribute("aria-invalid", "true");
  await secondMinimum.fill("12");
  await expect(itemDialog.getByRole("alert")).toContainText("Cada cantidad debe ser única");
  await secondMinimum.fill("10");
  const secondMargin = secondTier.getByLabel("Margen (%)", { exact: true });
  await secondMargin.fill("20");
  await expect(secondMargin).toBeFocused();
  await expect(secondTier.getByLabel("Precio *", { exact: true })).toHaveValue("25 000");
  await expectMobileDialogFits(page, itemDialog);
  await itemDialog.getByRole("button", { name: "Agregar precios", exact: true }).click();
  await expect(itemDialog).toBeHidden();
  await expect(editDialog.getByText("Por guardar", { exact: true })).toHaveCount(2);
  expect(previewRequests).toHaveLength(0);
  expect(itemWrites).toBe(0);
  expect(settingsWrites).toBe(2);
  const readsBeforeTiers = itemReads;
  let releaseSave!: () => void;
  itemSaveGate = new Promise<void>(resolve => { releaseSave = resolve; });
  await editDialog.getByRole("button", { name: "Guardar canal", exact: true }).click();
  await expect(editDialog.getByRole("button", { name: "Guardando…", exact: true })).toBeDisabled();
  releaseSave();
  await expect(detailDialog.getByText("Por guardar", { exact: true })).toHaveCount(0);
  expect(settingsWrites).toBe(3);
  expect(itemReads).toBe(readsBeforeTiers);
  expect(savedItem).toMatchObject({ newItems: [
    { productId, minimumQuantity: 5, amount: 40000, targetMarginPercent: 50, inputMode: "SalePrice" },
    { productId, minimumQuantity: 10, amount: 25000, targetMarginPercent: 20, inputMode: "Margin" },
  ] });
  await detailDialog.getByRole("button", { name: "Editar canal", exact: true }).click();
  const editProduct = editDialog.getByRole("button", { name: "Editar precios de Aceite vegetal 3000 ml" });
  await expect(editProduct).toHaveCount(1);
  await editProduct.click();
  const editTiers = page.getByRole("dialog", { name: "Editar precios por cantidad" });
  await expect(editTiers.getByRole("group", { name: /^Escala/ })).toHaveCount(4);
  await expectMobileDialogFits(page, editTiers);
  const editedQuantity = editTiers.getByRole("group", { name: "Escala 1", exact: true }).getByLabel("Cantidad mínima *");
  await editedQuantity.fill("5");
  await expect(editTiers.getByRole("button", { name: "Aplicar cambios" })).toBeDisabled();
  await editedQuantity.fill("2");
  await editTiers.getByRole("group", { name: "Escala 2", exact: true }).getByLabel("Precio *").fill("35000");
  await editTiers.getByRole("button", { name: "Aplicar cambios" }).click();
  await editDialog.getByRole("button", { name: "Retirar escala de Aceite vegetal 3000 ml desde 12" }).click();
  expect(settingsWrites).toBe(3);
  await editDialog.getByRole("button", { name: "Guardar canal", exact: true }).click();
  await expect(detailDialog).toBeVisible();
  expect(settingsWrites).toBe(4);
  expect(itemWrites).toBe(0);
  expect(itemReads).toBe(readsBeforeTiers);
  expect(savedItem).toMatchObject({
    newItems: [{ productId, minimumQuantity: 2, amount: 29500 }, { productId, minimumQuantity: 5, amount: 35000 }],
    retiredItems: [{ productId, minimumQuantity: 1 }, { productId, minimumQuantity: 5 }, { productId, minimumQuantity: 12 }],
  });
  await expect(groupedScales.locator("tbody tr")).toHaveCount(3);
  await expect(groupedScales.getByText(/35.000/)).toBeVisible();
  await detailDialog.getByRole("button", { name: "Editar canal", exact: true }).click();
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
