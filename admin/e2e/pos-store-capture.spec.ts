import { expect, test } from "@playwright/test";

test.use({ serviceWorkers: "block" });

test("Tiendas reutiliza el borrador, la captura, las cantidades y el cobro de venta directa", async ({ page, baseURL }) => {
  test.setTimeout(120_000);
  await page.setViewportSize({ width: 1280, height: 720 });
  await page.addInitScript(() => {
    const state = window as unknown as { directSaleRendered?: boolean };
    new MutationObserver(() => {
      if (document.querySelector("table thead")) state.directSaleRendered = true;
    }).observe(document, { childList: true, subtree: true });
  });
  const tenantId = "11111111-1111-1111-1111-111111111111";
  const businessId = "22222222-2222-2222-2222-222222222222";
  const warehouseId = "33333333-3333-3333-3333-333333333333";
  const user = { userId: "44444444-4444-4444-4444-444444444444", tenantId, tenantKey: "TEST",
    username: "cajero", firstName: "Caja", lastName: "Prueba", roles: ["Cajero"], permissions: ["sales.create"] };
  const workspace = { businessId, warehouseId, businessName: "Tienda de prueba", warehouseName: "Principal",
    warehouseCode: "B01", warehouseAllowsNegativeStockSales: true, hasActiveEdgeEnrollment: false, pointOfSaleMode: "Store" };
  const products = ["S", "M"].map((size, index) => ({ productId: `product-${index}`, productCode: `PRD-${index}`,
    imageUrl: "/demo/products/shirt-black.svg", name: `Camiseta negra ${size}`, baseUnitCode: "EA", unitPrice: 49_900, taxCode: "IVA", taxRate: 0,
    currencyCode: "COP", isActive: true, isWeighable: false, allowsFractionalSale: false, priceSource: "Base",
    presentation: { familyProductId: "product-0", colorName: "Negro", sizeName: size } }));
  let lines: Array<Record<string, unknown>> = [];
  let version = 1;
  const draft = () => ({ draftId: "same-direct-sale-draft", ...workspace, workSessionId: "session", userId: user.userId,
    status: "Active", version, lines, untaxedAmount: lines.length ? 49_900 * Number(lines[0].quantity) : 0,
    taxAmount: 0, payableAmount: lines.length ? 49_900 * Number(lines[0].quantity) : 0 });
  const writes: string[] = [];
  const browseRequests: Array<Record<string, unknown>> = [];
  let categoriesReads = 0;
  await page.context().addCookies([{ name: "auth_token", value: "e2e", url: baseURL!, httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ tenantId, businessId, warehouseId, user }) => {
    localStorage.setItem("selected_tenant_id", tenantId); localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
    localStorage.setItem(`auraly.pos.sales-workspace:${tenantId}:${user.userId}`, `${businessId}:${warehouseId}`);
  }, { tenantId, businessId, warehouseId, user });
  await page.route("**/api/**", async route => {
    const path = new URL(route.request().url()).pathname;
    let body: unknown = [];
    if (path.endsWith("/auth/me")) body = user;
    else if (path.endsWith("/workspace/bootstrap")) body = { tenantId, tenantName: "Prueba", userId: user.userId,
      userDisplayName: "Caja Prueba", options: [workspace], canEnrollPosDevice: false, activeEnrolledDeviceCount: 0, maximumEnrolledDevices: 0 };
    else if (path.endsWith("/workspace/options")) body = [workspace];
    else if (path.endsWith("/workspace/select")) body = workspace;
    else if (path.endsWith("/work-sessions/current")) {
      await new Promise(resolve => setTimeout(resolve, 500));
      body = { workSessionId: "session" };
    }
    else if (path.endsWith("/drafts/active")) body = draft();
    else if (path.endsWith("/pos/catalog/categories")) { categoriesReads++; body = [{ categoryId: "shirts", name: "Camisetas" }]; }
    else if (path.endsWith("/pos/catalog/products/search")) {
      const request = route.request().postDataJSON(); browseRequests.push(request);
      body = { items: products, hasMore: false, nextOffset: null };
    } else if (path.endsWith("/same-direct-sale-draft/items")) {
      writes.push(path); const request = route.request().postDataJSON();
      expect(request).toMatchObject({ selector: "product-1", quantity: 1, expectedVersion: version });
      version++;
      lines = [{ lineId: "line-1", productId: "product-1", productCode: "PRD-1", description: "Camiseta negra M",
        unitCode: "EA", quantity: 1, untaxedAmount: 49_900, tax: 0, total: 49_900, unitPrice: 49_900,
        discount: 0, promotionDiscount: 0, taxCode: "IVA", taxRate: 0, priceSource: "Base", allowsFractionalSale: false }];
      body = draft();
    } else if (path.endsWith("/same-direct-sale-draft/lines/line-1/quantity")) {
      writes.push(path); const request = route.request().postDataJSON();
      expect(request).toMatchObject({ quantity: 2, expectedVersion: version });
      version++; lines[0] = { ...lines[0], quantity: 2, total: 99_800 }; body = draft();
    } else if (path.endsWith("/inventory-validation")) body = { isValid: true, wasValidated: true, issues: [] };
    else if (path.endsWith("/settlement-configuration")) body = { isAccountingEnabled: false, bankAccounts: [] };
    else if (path.endsWith("/settlement")) body = { grossAmount: 99_800, withholdingTotal: 0, netAmount: 99_800, lines: [] };
    else if (path.endsWith("/reference-options/payment-method")) body = [{ id: "cash", code: "Cash", label: "Efectivo", isActive: true, sortOrder: 1 }];
    else if (path.includes("/orders")) body = { items: [], totalCount: 0, totalPages: 0, page: 1, pageSize: 25 };
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
  });
  await page.goto("/pos");
  const catalog = page.getByRole("region", { name: "Catálogo de tiendas" });
  await expect(catalog.getByRole("button", { name: "Agregar Camiseta negra M", exact: true })).toBeVisible();
  expect.soft(await page.evaluate(() => (window as unknown as { directSaleRendered?: boolean }).directSaleRendered ?? false)).toBe(false);
  await expect.soft(page.getByText("Lista para vender", { exact: true })).toBeInViewport();
  await expect(page.getByRole("button", { name: "Buscar F1", exact: true })).toHaveCount(0);
  await page.keyboard.press("F1");
  await expect(page.getByRole("textbox", { name: "Buscar producto en tiendas" })).toBeFocused();
  await expect(page.getByRole("region", { name: "Productos de la venta" })).toBeVisible();
  expect(categoriesReads).toBe(1);
  expect(browseRequests).toHaveLength(1);
  const captureActions = page.getByText("Acciones de captura", { exact: true });
  await expect(captureActions).toHaveCount(1);
  const charges = page.getByRole("button", { name: "Cargos de facturación", exact: true });
  await expect(charges).toBeVisible();
  await expect(charges).toBeDisabled();
  await expect(captureActions).toHaveCSS("text-transform", "uppercase");
  await expect(page.getByText("Subtotal", { exact: true })).toBeVisible();
  await expect(page.getByText("Total impuestos", { exact: true })).toBeVisible();
  await expect(page.getByText("Detalle de totales", { exact: true })).toHaveCount(0);
  await captureActions.click();
  await expect(page.getByRole("button", { name: /^Editar líneas/ })).toBeVisible();
  await captureActions.click();
  await expect(page.getByRole("button", { name: /^Editar líneas/ })).not.toBeVisible();
  const familyEye = catalog.getByRole("button", { name: "Ver familia de Camiseta negra S", exact: true });
  const tile = familyEye.locator("..");
  await expect(tile.getByRole("button", { name: "Agregar Camiseta negra S", exact: true })).toBeVisible();
  const priceBox = await tile.locator("p").filter({ hasText: /\$/ }).boundingBox();
  const eyeBox = await familyEye.boundingBox();
  expect(eyeBox!.x).toBeGreaterThan(priceBox!.x);
  expect(Math.abs(eyeBox!.y + eyeBox!.height / 2 - priceBox!.y - priceBox!.height / 2)).toBeLessThan(6);
  await familyEye.click();
  await expect.poll(() => browseRequests.length).toBe(2);
  expect(browseRequests[1].familyProductId).toBe("product-0");
  expect(writes).toHaveLength(0);
  await catalog.getByRole("button", { name: "Agregar Camiseta negra M", exact: true }).click();
  await expect(page.getByRole("button", { name: "Agregar uno de Camiseta negra M", exact: true })).toBeVisible();
  expect(browseRequests).toHaveLength(2);
  await page.getByRole("button", { name: "Agregar uno de Camiseta negra M", exact: true }).click();
  await expect(page.getByRole("spinbutton", { name: "Cantidad de Camiseta negra M" })).toHaveValue("2");
  await expect(charges).toBeEnabled();
  const customer = page.getByRole("button", { name: "Buscar cliente", exact: true });
  const customerBox = await customer.boundingBox();
  const chargesBox = await charges.boundingBox();
  expect(chargesBox!.x).toBeGreaterThan(customerBox!.x + customerBox!.width);
  expect(chargesBox!.y).toBe(customerBox!.y);
  expect(await charges.innerText()).toBe("");
  for (const viewport of [{ width: 1280, height: 720 }, { width: 1536, height: 864 }, { width: 1024, height: 768 }, { width: 938, height: 868 }]) {
    await page.setViewportSize(viewport);
    const quantity = page.getByRole("spinbutton", { name: "Cantidad de Camiseta negra M" });
    await expect(quantity).toBeInViewport();
    await expect(catalog.getByRole("button", { name: "Agregar Camiseta negra M", exact: true })).toBeInViewport();
    expect(await quantity.evaluate(element => {
      const rect = element.getBoundingClientRect();
      const hit = document.elementFromPoint(rect.x + rect.width / 2, rect.y + rect.height / 2);
      return hit === element || element.contains(hit);
    })).toBe(true);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  }
  expect(writes).toEqual(["/api/commerce/v1/pos/drafts/same-direct-sale-draft/items",
    "/api/commerce/v1/pos/drafts/same-direct-sale-draft/lines/line-1/quantity"]);
  expect(browseRequests).toHaveLength(2);
  await page.screenshot({ path: "test-results/pos-store-catalog.png" });
  await page.setViewportSize({ width: 1024, height: 768 });
  await expect(catalog.getByRole("button", { name: "Agregar Camiseta negra M", exact: true })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.setViewportSize({ width: 1037, height: 520 });
  await page.keyboard.press("F8");
  const payment = page.getByRole("dialog", { name: "Finalizar venta" });
  await expect(payment).toBeVisible();
  const cashAmount = payment.getByRole("textbox", { name: "Valor recibido en Efectivo", exact: true });
  await expect(cashAmount).toBeInViewport();
  await expect(cashAmount).toBeFocused();
  await page.screenshot({ path: "test-results/pos-store-payment.png" });
  await payment.getByRole("button", { name: /^Cerrar/ }).click();
  await page.route("**/same-direct-sale-draft/items", async route => {
    lines.push({ ...lines[0], lineId: "line-2", productId: "product-0", description: "Camiseta negra S", quantity: 1 });
    lines.push({ ...lines[0], lineId: "line-3", description: "Tercera camiseta", quantity: 1 });
    version++;
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(draft()) });
  });
  await page.setViewportSize({ width: 1440, height: 1000 });
  await catalog.getByRole("button", { name: "Agregar Camiseta negra S", exact: true }).click();
  const saleLines = page.getByRole("region", { name: "Productos de la venta", exact: true });
  await expect(saleLines.getByRole("spinbutton")).toHaveCount(3);
  const footer = page.locator("aside").filter({ has: page.getByRole("button", { name: /^Cobrar/ }) });
  const aligned = async () => {
    const totals = await footer.boundingBox(); const products = await catalog.boundingBox();
    return Math.abs(totals!.y + totals!.height - products!.y - products!.height);
  };
  await expect.poll(aligned).toBeLessThan(3);
  for (const name of ["Cantidad de Camiseta negra M", "Cantidad de Camiseta negra S"]) {
    const card = saleLines.locator(".rounded-2xl").filter({ has: page.getByRole("spinbutton", { name, exact: true }) });
    await expect(card).toBeInViewport({ ratio: 1 });
  }
  await page.screenshot({ path: "test-results/pos-store-footer-anchored.png" });
  await page.setViewportSize({ width: 1280, height: 580 });
  await expect.poll(async () => {
    const totals = await footer.boundingBox(); return totals!.y + totals!.height;
  }).toBeGreaterThan(580);
  const first = await saleLines.getByRole("spinbutton", { name: "Cantidad de Camiseta negra M", exact: true }).boundingBox();
  const second = await saleLines.getByRole("spinbutton", { name: "Cantidad de Camiseta negra S", exact: true }).boundingBox();
  expect(second!.y).toBeGreaterThan(first!.y + first!.height);
  for (const viewport of [
    { width: 1920, height: 1080 }, { width: 1536, height: 864 },
    { width: 1366, height: 768 }, { width: 1280, height: 720 },
    { width: 1024, height: 768 }, { width: 938, height: 868 },
    { width: 768, height: 1024 }, { width: 390, height: 844 },
    { width: 360, height: 740 }, { width: 320, height: 568 },
  ]) {
    await page.setViewportSize(viewport);
    await page.evaluate(() => window.scrollTo(0, 0));
    const overflow = await page.evaluate(() => ({ fits: document.documentElement.scrollWidth <= innerWidth,
      elements: Array.from(document.querySelectorAll("body *")).filter(element => {
        const rect = element.getBoundingClientRect(); return rect.width > 0 && rect.right > innerWidth + 1;
      }).slice(0, 8).map(element => ({ tag: element.tagName, className: element.getAttribute("class"), parent: element.parentElement?.getAttribute("class"), right: element.getBoundingClientRect().right })) }));
    expect(overflow.fits, `${viewport.width}x${viewport.height}: ${JSON.stringify(overflow.elements)}`).toBe(true);
    await expect(saleLines.getByRole("spinbutton")).toHaveCount(3);
    const linesBox = await saleLines.boundingBox();
    for (const name of ["Cantidad de Camiseta negra M", "Cantidad de Camiseta negra S"]) {
      const card = await saleLines.locator(".rounded-2xl").filter({ has: page.getByRole("spinbutton", { name, exact: true }) }).boundingBox();
      expect(card!.y, `${viewport.width}x${viewport.height}: tarjeta dentro de la lista`).toBeGreaterThanOrEqual(linesBox!.y);
      expect(card!.y + card!.height, `${viewport.width}x${viewport.height}: dos tarjetas completas`).toBeLessThanOrEqual(linesBox!.y + linesBox!.height);
    }
    if (viewport.width >= 768) await expect.poll(aligned).toBeLessThan(3);
    const totals = await footer.boundingBox();
    expect(totals!.y).toBeGreaterThanOrEqual(linesBox!.y + linesBox!.height);
    await page.screenshot({ path: `test-results/pos-store-${viewport.width}x${viewport.height}.png`, fullPage: true });
  }
  await expect(page.getByRole("heading", { name: "Ventas en espera", exact: true })).toHaveCount(0);
  const actionsSummary = page.locator("summary").filter({ hasText: "Acciones de captura" });
  const summaryBox = await actionsSummary.boundingBox();
  for (const name of ["Ver ventas en espera", "Ver pedidos"]) {
    const box = await page.getByRole("button", { name, exact: true }).boundingBox();
    expect(Math.abs(box!.y + box!.height / 2 - summaryBox!.y - summaryBox!.height / 2), `${name} centrado con el encabezado`).toBeLessThanOrEqual(1);
  }
  const expanded = await actionsSummary.locator("..").getAttribute("open");
  await page.getByRole("button", { name: "Ver ventas en espera", exact: true }).click();
  expect(await actionsSummary.locator("..").getAttribute("open")).toBe(expanded);
  await expect(page.getByRole("heading", { name: "Ventas y pedidos", exact: true })).toBeVisible();
});
