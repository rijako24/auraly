import { expect, test } from "@playwright/test";

test("recorre columnas y líneas del editor con flechas y desplaza su contenido", async ({ page, baseURL }) => {
  const tenantId = "11111111-1111-1111-1111-111111111111";
  const businessId = "22222222-2222-2222-2222-222222222222";
  const warehouseId = "33333333-3333-3333-3333-333333333333";
  const permissions = [
    "pos.sales.create",
    "sales.change-price",
    "sales.lines.change-description",
    "sales.lines.cost-margin.read",
  ];
  const user = {
    userId: "44444444-4444-4444-4444-444444444444",
    tenantId,
    tenantKey: "TEST",
    username: "cajero",
    firstName: "Cajero",
    lastName: "Prueba",
    roles: ["ADMIN"],
    permissions,
  };
  const workspace = {
    businessId,
    warehouseId,
    businessName: "Sede prueba",
    warehouseName: "Principal",
    warehouseCode: "B01",
    warehouseAllowsNegativeStockSales: true,
    hasActiveEdgeEnrollment: false,
  };
  const lines = Array.from({ length: 7 }, (_, index) => ({
    lineId: `line-${index}`,
    productId: `product-${index}`,
    productCode: `PRD-${index}`,
    description: `Producto ${index + 1}`,
    unitCode: "EA",
    taxCode: "01",
    taxRate: 19,
    quantity: 1,
    baseUnitPrice: 10_000,
    unitPrice: 10_000,
    currencyCode: "COP",
    priceSource: "Public",
    discount: 0,
    documentUnitCost: 6_000,
    allowsDocumentCostOverride: index !== 1,
    allowsFractionalSale: false,
    net: 10_000,
    tax: 1_900,
    total: 11_900,
  }));
  const draft = {
    draftId: "draft",
    ...workspace,
    userId: user.userId,
    workSessionId: "session",
    status: "Active",
    version: 1,
    name: null,
    reference: null,
    observation: null,
    customerId: null,
    sellerId: null,
    lines,
    untaxedAmount: 70_000,
    taxAmount: 13_300,
    payableAmount: 83_300,
  };

  await page.context().addCookies([{ name: "auth_token", value: "e2e", url: baseURL!, httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ tenantId, businessId, warehouseId, user }) => {
    localStorage.setItem("selected_tenant_id", tenantId);
    localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
    localStorage.setItem(`auraly.pos.sales-workspace:${tenantId}:${user.userId}`, `${businessId}:${warehouseId}`);
  }, { tenantId, businessId, warehouseId, user });
  await page.route("**/api/**", async route => {
    const path = new URL(route.request().url()).pathname;
    let body: unknown = [];
    if (path === "/api/auth/me") body = user;
    else if (path.endsWith("/workspace/bootstrap")) body = {
      tenantId,
      tenantName: "Prueba",
      userId: user.userId,
      userDisplayName: "Cajero Prueba",
      options: [workspace],
      canEnrollPosDevice: false,
      activeEnrolledDeviceCount: 0,
      maximumEnrolledDevices: 0,
    };
    else if (path.endsWith("/workspace/options")) body = [workspace];
    else if (path.endsWith("/workspace/select")) body = workspace;
    else if (path.endsWith("/work-sessions/current")) body = { workSessionId: "session" };
    else if (path.endsWith("/drafts/active")) body = draft;
    else if (path.endsWith("/settlement-configuration")) body = { isAccountingEnabled: false, bankAccounts: [] };
    else if (path.includes("/orders")) body = { items: [], totalCount: 0, totalPages: 0, page: 1, pageSize: 25 };
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
  });

  await page.setViewportSize({ width: 1100, height: 650 });
  await page.goto("/pos");
  await expect(page.locator("#pos-scanner")).toBeEnabled();
  await page.keyboard.press("F2");

  const editor = page.locator('form[aria-keyshortcuts="Enter Escape"]').filter({ hasText: "Editar líneas" });
  const scrollRegion = editor.getByTestId("pos-line-editor-scroll-region");
  const discounts = editor.locator('input[data-editor-column="3"]');
  await expect(editor).toBeVisible();
  await expect(discounts).toHaveCount(lines.length);
  await expect(discounts.first()).toBeFocused();

  await page.keyboard.press("ArrowRight");
  await expect(editor.locator('input[data-editor-row="0"][data-editor-column="4"]')).toBeFocused();
  await page.keyboard.press("ArrowRight");
  const firstPrice = editor.locator('input[data-editor-row="0"][data-editor-column="5"]');
  await expect(firstPrice).toBeFocused();
  await page.keyboard.press("ArrowRight");
  await expect(firstPrice).toBeFocused();
  await page.keyboard.press("ArrowLeft");
  await page.keyboard.press("ArrowLeft");
  await expect(discounts.first()).toBeFocused();
  await page.keyboard.press("ArrowLeft");
  await expect(editor.locator('input[data-editor-row="0"][data-editor-column="2"]')).toBeFocused();
  await page.keyboard.press("ArrowLeft");
  await expect(editor.locator('input[data-editor-row="0"][data-editor-column="1"]')).toBeFocused();
  await page.keyboard.press("ArrowLeft");
  const firstDescription = editor.locator('input[data-editor-row="0"][data-editor-column="0"]');
  await expect(firstDescription).toBeFocused();
  await page.keyboard.press("ArrowLeft");
  await expect(firstDescription).toBeFocused();
  await page.keyboard.press("ArrowRight");
  await page.keyboard.press("ArrowRight");
  await page.keyboard.press("ArrowRight");
  await expect(discounts.first()).toBeFocused();

  const initialScrollTop = await scrollRegion.evaluate(element => element.scrollTop);
  for (let index = 1; index < lines.length; index += 1) {
    await page.keyboard.press("ArrowDown");
    await expect(discounts.nth(index)).toBeFocused();
  }
  expect(await scrollRegion.evaluate(element => element.scrollTop)).toBeGreaterThan(initialScrollTop);

  await page.keyboard.press("ArrowDown");
  await expect(discounts.last()).toBeFocused();
  await page.keyboard.press("ArrowUp");
  await expect(discounts.nth(lines.length - 2)).toBeFocused();
});
