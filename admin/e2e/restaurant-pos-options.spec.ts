import { expect, test, type Route } from "@playwright/test";

const tenantId = "11111111-1111-1111-1111-111111111111";
const businessId = "22222222-2222-2222-2222-222222222222";
const userId = "33333333-3333-3333-3333-333333333333";
const warehouseId = "44444444-4444-4444-4444-444444444444";
const tableId = "55555555-5555-5555-5555-555555555555";
const accountId = "66666666-6666-6666-6666-666666666666";
const baseUrl = process.env.AURALY_E2E_BASE_URL ?? "http://127.0.0.1:3000";
const json = (route: Route, value: unknown, status = 200) => route.fulfill({
  status, contentType: "application/json", body: JSON.stringify(value),
});
test.use({ serviceWorkers: "block" });

test("compacta la cuenta, comparte clientes y pagina canales sin cargar vistas ocultas", async ({ page }) => {
  const permissions = ["restaurant.attend", "restaurant.tables.open_without_waiter", "restaurant.price_channels.select", "orders.read", "orders.recover", "sales.create"];
  const user = { userId, tenantId, tenantKey: "E2E", username: "caja", firstName: "Ana",
    lastName: "Gómez", roles: ["Administrador"], permissions };
  const option = { businessId, businessName: "Restaurante", warehouseId, warehouseCode: "P01",
    warehouseName: "Principal", warehouseAllowsNegativeStockSales: false,
    hasActiveEdgeEnrollment: false, pointOfSaleMode: "RestaurantBar" };
  const account = { accountId, businessId, tableId, warehouseId, responsibleUserId: userId,
    responsibleUserName: "Ana Gómez", status: "Open", version: 1,
    openedAt: "2026-11-06T12:00:00Z", updatedAt: "2026-11-06T12:00:00Z",
    customerId: null as string | null, customerName: null as string | null,
    priceChannelId: null as string | null, priceChannelName: null as string | null,
    customerPriceChannelLocked: false };
  const additions = ["Hamburguesa clásica", "Agua mineral", "Papas fritas"].map((description, i) => ({
    additionId: `addition-${i}`, productId: `product-${i}`, accountId, description, quantity: 1,
    productCode: `P-${i}`, unitCode: "UND", taxCode: "IVA", taxRate: 0, currencyCode: "COP",
    publicUnitPrice: 5000, promotionDiscountPerUnit: 0, priceSource: "Base",
    addedByUserId: userId, addedByName: "Ana Gómez", addedAt: account.openedAt,
    status: "Direct", stationId: null, stationName: null, preparationNote: null,
  }));
  const channels = Array.from({ length: 55 }, (_, i) => ({ priceChannelId: `channel-${i + 1}`,
    name: `Canal ${String(i + 1).padStart(2, "0")}` }));
  const customers = Array.from({ length: 45 }, (_, i) => ({ partyId: `party-${i + 1}`,
    roleId: `customer-${i + 1}`, role: "Customer", displayName: `Cliente ${i + 1}`,
    identification: `100${i + 1}`, supplierPurchaseEvidencePolicy: null,
    supplierDefaultPaymentDueDays: null }));
  const channelReads: Array<{ skip: number; take: number; search: string }> = [];
  const customerReads: Array<{ page: number; pageSize: number; search: string }> = [];
  let draftReads = 0;
  let productReads = 0;
  let orderReads = 0;
  await page.context().addCookies([{ name: "auth_token", value: "restaurant-options-test",
    url: baseUrl, httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ user, tenantId, businessId }) => {
    localStorage.setItem("selected_tenant_id", tenantId);
    localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
  }, { user, tenantId, businessId });
  await page.route("**/api/**", (route) => {
    const url = new URL(route.request().url()); const path = url.pathname;
    if (path.endsWith("/auth/me")) return json(route, user);
    if (path.endsWith("/work-sessions/current")) return json(route, { workSessionId: "77777777-7777-7777-7777-777777777777", businessId, userId, status: "Open" });
    if (path.endsWith("/execution-context/tenants")) return json(route, [{ tenantId, name: "Restaurante" }]);
    if (path.endsWith("/execution-context/businesses")) return json(route, [{ tenantId, businessId, name: "Restaurante" }]);
    if (path.endsWith("/execution-context/access")) return json(route, { tenantId, businessId, roles: user.roles, permissions });
    if (path.endsWith("/pos/workspace/bootstrap")) return json(route, { tenantId, tenantName: "Restaurante", userId,
      userDisplayName: "Ana Gómez", options: [option], canEnrollPosDevice: false, hasEnrollmentPermission: false,
      activeEnrolledDeviceCount: 0, maximumEnrolledDevices: 0, enrollmentUnavailableReason: null });
    if (path.endsWith("/pos/workspace/select")) return json(route, option);
    if (path.endsWith("/restaurant/rooms")) return json(route, { items: [], page: 1, pageSize: 100, totalCount: 0 });
    if (path.endsWith("/restaurant/tables")) return json(route, { items: [{ tableId, businessId, roomId: null,
      code: "P-01", capacity: 4, isActive: true, rowVersion: "AAAAAAAAAAA=" }], totalCount: 1, page: 1, pageSize: 100 });
    if (path.endsWith("/restaurant/occupancy")) return json(route, [{ tableId, accountId, state: "Open", accountVersion: 1 }]);
    if (path.endsWith("/draft")) { draftReads++; return json(route, { account, additions }); }
    if (path.endsWith("/cancellations") || path.endsWith("/categories")) return json(route, []);
    if (path.endsWith("/products")) { productReads++; return json(route, { items: [], hasMore: false, nextOffset: null }); }
    if (path.endsWith("/commerce/v1/orders")) {
      orderReads++; return json(route, { items: [{ orderId: "88888888-8888-8888-8888-888888888888",
        orderNumber: "PED-01", status: "Available", source: 0, customerName: "Cliente pedido",
        currency: "COP", total: 5000, lineCount: 1, createdAt: account.openedAt, canInvoice: true,
        invoiceDocumentId: null, claim: null }], page: 1, pageSize: 8, totalCount: 1, hasMore: false });
    }
    if (path.endsWith("/restaurant/price-channels")) {
      const skip = Number(url.searchParams.get("skip")); const take = Number(url.searchParams.get("take"));
      const search = url.searchParams.get("search") ?? ""; channelReads.push({ skip, take, search });
      return json(route, channels.filter(item => item.name.includes(search)).slice(skip, skip + take));
    }
    if (path.endsWith("/parties/role-options")) {
      expect(url.searchParams.get("role")).toBe("Customer");
      const current = Number(url.searchParams.get("page")); const pageSize = Number(url.searchParams.get("pageSize"));
      const search = url.searchParams.get("search") ?? ""; customerReads.push({ page: current, pageSize, search });
      const matching = customers.filter(customer => `${customer.displayName} ${customer.identification}`.includes(search));
      return json(route, { items: matching.slice((current - 1) * pageSize, current * pageSize),
        page: current, pageSize, totalCount: matching.length, totalPages: Math.ceil(matching.length / pageSize) });
    }
    if (path.endsWith("/price-channel")) {
      const request = route.request().postDataJSON(); expect(request.expectedVersion).toBe(account.version);
      const channel = channels.find(item => item.priceChannelId === request.priceChannelId);
      Object.assign(account, { priceChannelId: channel?.priceChannelId ?? null, priceChannelName: channel?.name ?? null,
        version: account.version + 1 }); return json(route, { account, additions });
    }
    if (path.endsWith("/customer")) {
      const request = route.request().postDataJSON(); expect(request.expectedVersion).toBe(account.version);
      const customer = customers.find(item => item.roleId === request.customerId);
      Object.assign(account, { customerId: customer?.roleId ?? null, customerName: customer?.displayName ?? null,
        customerPriceChannelLocked: Boolean(customer), priceChannelId: customer ? channels[0].priceChannelId : null,
        priceChannelName: customer ? channels[0].name : null, version: account.version + 1 });
      return json(route, { account, additions });
    }
    return json(route, []);
  });
  await page.goto("/pos/restaurant"); await page.getByRole("button", { name: /P-01/ }).click();
  await expect(page.getByRole("button", { name: "Volver", exact: true })).toBeVisible();
  await expect(page.getByText("Atendiendo ahora:", { exact: false })).toHaveCount(0);
  const customerButton = page.getByRole("button", { name: "Buscar cliente: Consumidor final", exact: true });
  const channelButton = page.getByRole("button", { name: "Canal de precio: Sin canal", exact: true });
  const clientBox = await customerButton.boundingBox(); const channelBox = await channelButton.boundingBox();
  expect(Math.abs(clientBox!.y - channelBox!.y)).toBeLessThan(1);
  expect(channelReads).toHaveLength(0); expect(customerReads).toHaveLength(0);
  expect(orderReads).toBe(0);
  await page.getByRole("button", { name: "Ver pedidos", exact: true }).click();
  const orders = page.getByRole("dialog", { name: "Pedidos", exact: true });
  await expect(orders.getByRole("button", { name: /^Recuperar/ })).toBeDisabled();
  await expect(orders.getByRole("status")).toHaveText("La captura tiene productos. Termínala o vacíala antes de recuperar.");
  expect(orderReads).toBe(1);
  await expect(orders.getByRole("tab", { name: /En espera/ })).toHaveCount(0);
  await page.keyboard.press("Escape");
  await customerButton.click(); const customerDialog = page.getByRole("dialog", { name: "Buscar cliente", exact: true });
  await expect(customerDialog.getByPlaceholder("Nombre o identificación")).toBeFocused();
  await expect(customerDialog.getByRole("button", { name: "Nuevo cliente", exact: true })).toHaveCount(0);
  await expect(customerDialog.getByRole("button", { name: "Cliente 20 10020", exact: true })).toBeVisible();
  await customerDialog.getByRole("button", { name: "Cliente 20 10020", exact: true }).focus();
  await expect(customerDialog.getByRole("button", { name: "Cliente 21 10021", exact: true })).toBeAttached();
  expect(customerReads.filter(read => read.page === 2)).toHaveLength(1);
  await customerDialog.getByPlaceholder("Nombre o identificación").fill("Cliente 24");
  await customerDialog.getByRole("button", { name: "Cliente 24 10024", exact: true }).click();
  await expect(page.getByRole("button", { name: "Buscar cliente: Cliente 24", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Canal de precio: Canal 01", exact: true })).toBeDisabled();
  await page.getByRole("button", { name: "Buscar cliente: Cliente 24", exact: true }).click();
  await customerDialog.getByRole("button", { name: "Consumidor final", exact: true }).click();
  await channelButton.click(); const dialog = page.getByRole("dialog", { name: "Canal de precio", exact: true });
  await expect(dialog.locator('[aria-label="Canales de precio"] button')).toHaveCount(20);
  expect(channelReads).toEqual([{ skip: 0, take: 21, search: "" }]);
  await dialog.getByRole("button", { name: "Siguiente", exact: true }).click();
  await expect(dialog.getByRole("button", { name: "Canal 21", exact: true })).toBeVisible();
  expect(channelReads.at(-1)).toEqual({ skip: 20, take: 21, search: "" });
  await dialog.getByLabel("Buscar canal de precio").fill("55");
  await expect(dialog.getByRole("button", { name: "Canal 55", exact: true })).toBeVisible();
  expect(channelReads.at(-1)).toEqual({ skip: 0, take: 21, search: "55" });
  await expect(dialog.getByRole("button", { name: "Siguiente", exact: true })).toBeDisabled();
  await dialog.getByRole("button", { name: "Canal 55", exact: true }).click();
  await expect(page.getByRole("button", { name: "Canal de precio: Canal 55", exact: true })).toBeVisible();
  expect(draftReads).toBe(1);
  await expect.poll(() => productReads).toBe(4);
  for (const viewport of [{ width: 1920, height: 1080 }, { width: 1366, height: 768 },
    { width: 1024, height: 768 }, { width: 768, height: 1024 }, { width: 390, height: 844 }, { width: 320, height: 568 }]) {
    await page.setViewportSize(viewport);
    if (viewport.width < 1024) await page.getByRole("button", { name: "Cuenta · 3", exact: true }).click();
    const lines = page.getByRole("region", { name: "Productos de la mesa", exact: true });
    await expect(lines).toBeVisible();
    const boundary = await lines.boundingBox();
    for (const card of (await lines.locator("[data-sale-item]").all()).slice(0, 2)) {
      const box = await card.boundingBox(); expect(box!.y).toBeGreaterThanOrEqual(boundary!.y);
      expect(box!.y + box!.height).toBeLessThanOrEqual(boundary!.y + boundary!.height);
    }
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.getByRole("button", { name: "Canal de precio: Canal 55", exact: true }).click();
    await expect(dialog.getByRole("button", { name: "Canal 01", exact: true })).toBeVisible();
    const box = await dialog.boundingBox(); expect(box!.y).toBeGreaterThanOrEqual(0);
    expect(box!.y + box!.height).toBeLessThanOrEqual(viewport.height);
    const next = await dialog.getByRole("button", { name: "Siguiente", exact: true }).boundingBox();
    expect(next!.x + next!.width).toBeLessThanOrEqual(box!.x + box!.width);
    await page.keyboard.press("Escape");
    await page.getByRole("button", { name: "Imprimir", exact: true }).scrollIntoViewIfNeeded();
    await expect(page.getByRole("button", { name: "Imprimir", exact: true })).toBeInViewport();
    await page.screenshot({ path: `test-results/restaurant-compact-${viewport.width}x${viewport.height}.png`, fullPage: true });
  }
});
