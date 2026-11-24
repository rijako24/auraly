import { expect, test } from "@playwright/test";

for (const width of [1366, 390]) for (const editing of [false, true]) {
  test(`pedido de ruta conserva búsqueda y aplica cantidad exacta ${editing ? "editar" : "crear"} ${width}`, async ({ page, baseURL }, info) => {
    await page.setViewportSize({ width, height: 900 });
    const tenantId = "11111111-1111-1111-1111-111111111111", businessId = "22222222-2222-2222-2222-222222222222";
    const warehouseId = "33333333-3333-3333-3333-333333333333", routeId = "44444444-4444-4444-4444-444444444444";
    const customerId = "55555555-5555-5555-5555-555555555555", partySiteId = "66666666-6666-6666-6666-666666666666";
    const orderId = "77777777-7777-7777-7777-777777777777";
    const user = { userId: "88888888-8888-8888-8888-888888888888", tenantId, username: "vendedor", firstName: "Vendedor", lastName: "Prueba", roles: ["Administrator"], permissions: ["dashboard.read", "routes.read", "orders.read", "orders.create", "orders.update", "sales.create"] };
    const stop = { routeStopId: "99999999-9999-9999-9999-999999999999", customerId, partySiteId, sequence: 1, customerName: "Cliente de ruta", identification: "100001", siteName: "Principal", addressLine: "Calle 1", cityName: "Valledupar", rowVersion: "AAAA" };
    const route = { routeId, businessId, code: "RUTA-QA", name: "Ruta de prueba", sellerId: user.userId, sellerName: "Vendedor", isActive: true, preparationStatus: "Ready", schedules: [1, 2, 3, 4, 5, 6, 7].map(dayOfWeek => ({ dayOfWeek, runOrder: 1, plannedStartTime: null })), stops: [stop], rowVersion: "AAAA" };
    const items = [{ productId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", productCode: "PRD-AGUA", name: "AGUA CRISTAL", unitCode: "EA", unitPrice: 1600, priceSource: "Public", quantityOnHand: 100, manageStock: true, documentUnitCost: 800 }, { productId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", productCode: "PRD-GRANEL", name: "AGUA A GRANEL", unitCode: "KG", unitPrice: 1000, priceSource: "Public", quantityOnHand: 100, manageStock: true, documentUnitCost: 500 }];
    const order = { orderId, orderNumber: "PED-QA-BUSQUEDA", businessId, warehouseId, customerId, partySiteId, customerName: stop.customerName, status: "Available", source: 1, total: 2000, currency: "COP", lineCount: 1, createdAt: new Date().toISOString(), lines: [{ orderItemId: "other-line", productId: "cccccccc-cccc-cccc-cccc-cccccccccccc", productName: "Producto original", productCode: "PRD-OTRO", quantity: 1, reservedQuantity: 1, unitCode: "EA", unitPrice: 2000, lineTotal: 2000, priceSource: "Public", documentUnitCost: 1000 }] };
    await page.context().addCookies([{ name: "auth_token", value: "e2e", url: baseURL!, httpOnly: true }]);
    await page.addInitScript(({ tenantId, businessId, user }) => {
      localStorage.setItem("selected_tenant_id", tenantId); localStorage.setItem("selected_business_id", businessId);
      localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
      localStorage.setItem("auraly:pwa-install-dismissed", "1");
    }, { tenantId, businessId, user });
    let catalogReads = 0, savedLines: Array<{ productId: string; quantity: number }> | undefined;
    await page.route("**/api/**", async request => {
      const path = new URL(request.request().url()).pathname;
      let body: unknown = [];
      if (path.endsWith("/auth/me")) body = user;
      else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Auraly" }];
      else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Auraly", timeZone: "America/Bogota" }];
      else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, roles: user.roles, permissions: user.permissions };
      else if (path.endsWith("/pos/workspace/options")) body = [{ businessId, businessName: "Auraly", warehouseId, warehouseCode: "VEN", warehouseName: "Ventas", warehouseAllowsNegativeStockSales: false, hasActiveEdgeEnrollment: false }];
      else if (path.endsWith("/routes")) body = { items: [{ ...route, days: [1, 2, 3, 4, 5, 6, 7], stopCount: 1 }], page: 1, pageSize: 100, totalCount: 1, totalPages: 1 };
      else if (path.endsWith(`/routes/${routeId}`)) body = route;
      else if (path.endsWith(`/routes/${routeId}/visits`)) body = request.request().method() === "PUT" ? { ...request.request().postDataJSON(), routeVisitId: "visit-1" } : [];
      else if (path.endsWith("/seller-orders/catalog")) { catalogReads++; body = { items, hasMore: false, nextOffset: null }; }
      else if (path.endsWith("/seller-orders") || path.endsWith(`/seller-orders/${orderId}`)) { savedLines = request.request().postDataJSON().lines; body = { orderId, orderNumber: order.orderNumber, status: "Confirmed", total: editing ? 7700 : 5700, requiresReview: false, warnings: [] }; }
      else if (path.endsWith(`/orders/${orderId}`)) body = order;
      else if (path.endsWith("/orders")) body = { items: editing || savedLines ? [order] : [], page: 1, pageSize: 100, totalCount: editing || savedLines ? 1 : 0, hasMore: false };
      await request.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
    });
    await page.goto("/dashboard/my-routes");
    if (editing) {
      await page.getByRole("button", { name: "Pedidos", exact: true }).click();
      await page.getByRole("button", { name: /PED-QA-BUSQUEDA/ }).click();
      await page.getByRole("button", { name: "Editar pedido", exact: true }).click();
    } else await page.getByRole("button", { name: "Tomar pedido", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: editing ? "Editar PED-QA-BUSQUEDA" : "Cliente de ruta", exact: true });
    const search = dialog.getByPlaceholder("Buscar por nombre, código o referencia");
    await search.fill("agua");
    const first = dialog.locator("article").filter({ hasText: "AGUA CRISTAL" });
    const second = dialog.locator("article").filter({ hasText: "AGUA A GRANEL" });
    await expect(first).toBeVisible();
    await first.getByRole("button").last().click();
    await expect(search).toHaveValue("agua");
    await expect(first.getByRole("button", { name: "1", exact: true })).toBeVisible();
    await second.getByRole("button", { name: "0", exact: true }).click();
    const quantity = page.getByRole("dialog", { name: "Cantidad", exact: true });
    await quantity.getByRole("textbox").fill("2.5");
    if (editing) await quantity.getByRole("textbox").press("Enter");
    else await quantity.getByRole("button", { name: "Aplicar", exact: true }).click();
    await expect(quantity).toBeHidden();
    await expect(second.getByRole("button", { name: "2.5", exact: true })).toBeVisible();
    await first.getByRole("button").last().click();
    await expect(search).toHaveValue("agua");
    await expect(first.getByRole("button", { name: "2", exact: true })).toBeVisible();
    expect(catalogReads).toBe(1);
    await page.screenshot({ path: info.outputPath(`seller-search-${editing ? "edit" : "create"}-${width}.png`) });
    await dialog.getByRole("button", { name: "Ver pedido", exact: true }).click();
    await expect(search).toHaveValue("");
    await dialog.getByRole("button", { name: editing ? /^Actualizar pedido/ : /^Crear pedido/ }).click();
    await expect(dialog).toBeHidden();
    expect(savedLines).toEqual(expect.arrayContaining(items.map((item, index) => expect.objectContaining({ productId: item.productId, quantity: index ? 2.5 : 2, unitPrice: item.unitPrice }))));
    if (editing) expect(savedLines).toEqual(expect.arrayContaining([expect.objectContaining({ productId: order.lines[0].productId, quantity: 1 })]));
    expect(catalogReads).toBe(1);
  });
}
