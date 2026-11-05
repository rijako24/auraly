import { expect, test } from "@playwright/test";

test.use({ serviceWorkers: "block" });

test("Tiendas reutiliza el borrador, la captura, las cantidades y el cobro de venta directa", async ({ page, baseURL }) => {
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
    else if (path.endsWith("/work-sessions/current")) body = { workSessionId: "session" };
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
  expect(categoriesReads).toBe(1);
  expect(browseRequests).toHaveLength(1);
  await catalog.getByRole("button", { name: "Ver familia" }).first().click();
  await expect.poll(() => browseRequests.length).toBe(2);
  expect(browseRequests[1].familyProductId).toBe("product-0");
  await catalog.getByRole("button", { name: "Agregar Camiseta negra M", exact: true }).click();
  await expect(page.getByRole("button", { name: "Agregar uno de Camiseta negra M", exact: true })).toBeVisible();
  expect(browseRequests).toHaveLength(2);
  await page.getByRole("button", { name: "Agregar uno de Camiseta negra M", exact: true }).click();
  await expect(page.getByRole("spinbutton", { name: "Cantidad de Camiseta negra M" })).toHaveValue("2");
  expect(writes).toEqual(["/api/commerce/v1/pos/drafts/same-direct-sale-draft/items",
    "/api/commerce/v1/pos/drafts/same-direct-sale-draft/lines/line-1/quantity"]);
  expect(browseRequests).toHaveLength(2);
  await page.screenshot({ path: "test-results/pos-store-catalog.png" });
  await page.setViewportSize({ width: 1024, height: 768 });
  await expect(catalog.getByRole("button", { name: "Agregar Camiseta negra M", exact: true })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.keyboard.press("F8");
  await expect(page.getByRole("dialog", { name: "Finalizar venta" })).toBeVisible();
  await page.screenshot({ path: "test-results/pos-store-payment.png" });
});
