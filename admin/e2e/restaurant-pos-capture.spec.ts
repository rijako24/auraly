import { expect, test, type Route } from "@playwright/test";

const tenantId = "11111111-1111-1111-1111-111111111111";
const businessId = "22222222-2222-2222-2222-222222222222";
const userId = "33333333-3333-3333-3333-333333333333";
const warehouseId = "44444444-4444-4444-4444-444444444444";
const tableId = "55555555-5555-5555-5555-555555555555";
const productId = "66666666-6666-6666-6666-666666666666";
const categoryId = "77777777-7777-7777-7777-777777777777";
const stationId = "88888888-8888-8888-8888-888888888888";
const permissions = ["restaurant.attend"];
const now = "2026-10-09T10:00:00-05:00";
const baseUrl = process.env.AURALY_E2E_BASE_URL ?? "http://127.0.0.1:3000";

const json = (route: Route, value: unknown, status = 200) => route.fulfill({
  status, contentType: "application/json", body: JSON.stringify(value),
});

test.use({ serviceWorkers: "block" });

test("abre una mesa sin plano, captura y corrige un producto desde la vista táctil", async ({ page }) => {
  const user = { userId, tenantId, tenantKey: "E2E", username: "mesero-prueba",
    email: "mesero@test.local", firstName: "Mesero", lastName: "Prueba",
    roles: ["Mesero"], permissions };
  await page.context().addCookies([{ name: "auth_token", value: "restaurant-pos-test",
    url: baseUrl, httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ user, tenantId, businessId }) => {
    localStorage.setItem("selected_tenant_id", tenantId);
    localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("theme", "light");
    localStorage.setItem("auth-state", JSON.stringify({ state: {
      isAuthenticated: true, user }, version: 0 }));
  }, { user, tenantId, businessId });
  const option = { businessId, businessName: "Auraly Restaurante", warehouseId,
    warehouseCode: "P01", warehouseName: "Principal",
    warehouseAllowsNegativeStockSales: false, hasActiveEdgeEnrollment: false,
    pointOfSaleMode: "RestaurantBar" };
  let accountId = "";
  let version = 0;
  const attentionToken = "attention-test-token";
  let attentionEnded = false;
  const additions: Array<Record<string, unknown>> = [];
  let directWorkSessions = 0;
  await page.route("**/api/**", (route) => {
    const path = new URL(route.request().url()).pathname;
    const method = route.request().method();
    if (path.endsWith("/auth/me")) return json(route, user);
    if (path.endsWith("/execution-context/tenants"))
      return json(route, [{ tenantId, name: "Auraly" }]);
    if (path.endsWith("/execution-context/businesses"))
      return json(route, [{ tenantId, businessId, name: "Auraly Restaurante" }]);
    if (path.endsWith("/execution-context/access"))
      return json(route, { tenantId, businessId, roles: user.roles, permissions });
    if (path.endsWith("/commerce/v1/pos/workspace/bootstrap"))
      return json(route, { tenantId, tenantName: "Auraly", userId,
        userDisplayName: "Mesero Prueba", options: [option],
        canEnrollPosDevice: false, hasEnrollmentPermission: false,
        activeEnrolledDeviceCount: 0, maximumEnrolledDevices: 0,
        enrollmentUnavailableReason: null });
    if (path.endsWith("/commerce/v1/pos/workspace/select")) {
      expect(route.request().headers()["x-business-id"]).toBe(businessId);
      return json(route, option);
    }
    if (path.endsWith("/commerce/v1/work-sessions/current")) {
      directWorkSessions++;
      return json(route, { workSessionId: crypto.randomUUID() });
    }
    if (path.endsWith("/commerce/v1/restaurant/rooms"))
      return json(route, { items: [], page: 1, pageSize: 100, totalCount: 0 });
    if (path.endsWith("/commerce/v1/restaurant/tables"))
      return json(route, { items: [{ tableId, businessId, roomId: null,
        code: "M-01", name: null, capacity: 4, isActive: true,
        rowVersion: "AAAAAAAAAAA=" }], page: 1, pageSize: 100, totalCount: 1 });
    if (path.endsWith("/commerce/v1/restaurant/occupancy"))
      return json(route, [{ tableId, accountId: accountId || null,
        state: accountId ? "Open" : "Free", responsibleUserId: accountId ? userId : null,
        openedAt: accountId ? now : null, accountVersion: accountId ? version : null }]);
    if (path.endsWith("/commerce/v1/restaurant/waiters"))
      return json(route, { requireCredential: true, restrictTableToWaiter: false,
        items: [{ userId, name: "Mesero Prueba", shortName: "Mesero" }],
        page: 1, pageSize: 40, totalCount: 1 });
    if (path.endsWith("/commerce/v1/restaurant/attention") && method === "POST") {
      expect(route.request().postDataJSON()).toMatchObject({
        tableId, waiterUserId: userId, secret: "1234" });
      return json(route, { token: attentionToken, tableId,
        waiterUserId: userId });
    }
    if (path.endsWith("/commerce/v1/restaurant/attention") && method === "DELETE") {
      attentionEnded = true;
      expect(route.request().headers()["x-restaurant-attention"], path).toBe(attentionToken);
      return route.fulfill({ status: 204 });
    }
    if (path.includes("/commerce/v1/restaurant/accounts"))
      expect(route.request().headers()["x-restaurant-attention"], path).toBe(attentionToken);
    const account = () => ({ accountId, businessId, tableId, warehouseId,
      responsibleUserId: userId, status: "Open", version, openedAt: now, updatedAt: now });
    if (path.endsWith("/commerce/v1/restaurant/accounts") && method === "POST") {
      accountId = route.request().postDataJSON().accountId;
      version = 1;
      return json(route, account(), 201);
    }
    if (path.endsWith("/categories"))
      return json(route, [{ categoryId, name: "Hamburguesas", productCount: 1 }]);
    if (path.endsWith("/products"))
      return json(route, { items: [{ productId, productCode: "H-01", reference: null,
        name: "Hamburguesa clásica", baseUnitCode: "UND", taxCode: "IVA",
        taxRate: 0, unitPrice: 25000, currencyCode: "COP", isActive: true,
        isWeighable: false, allowsFractionalSale: false, priceSource: "Base",
        promotionDiscount: 0, productCategoryId: categoryId,
        categoryName: "Hamburguesas", imageUrl: null }],
      hasMore: false, nextOffset: null });
    if (path.endsWith("/draft")) return json(route, { account: account(), additions });
    if (path.endsWith("/additions") && method === "POST") {
      const request = route.request().postDataJSON();
      expect(request.expectedVersion).toBe(version);
      additions.push({ additionId: request.additionId, accountId, productId,
        productCode: "H-01", description: "Hamburguesa clásica", unitCode: "UND",
        taxCode: "IVA", taxRate: 0, quantity: request.quantity,
        allowsFractionalSale: false, publicUnitPrice: 25000,
        promotionDiscountPerUnit: 0, currencyCode: "COP", priceSource: "Base",
        addedByUserId: userId, addedByName: "Mesero Prueba", addedAt: now,
        status: "Pending", stationId, stationName: "Cocina" });
      version++;
      return json(route, { account: account(), additions });
    }
    if (path.endsWith("/quantity") && method === "PUT") {
      const request = route.request().postDataJSON();
      expect(request.expectedVersion).toBe(version);
      const addition = additions.find((item) => item.additionId === path.split("/").at(-2));
      expect(addition).toBeTruthy();
      if (request.quantity === 0) addition!.status = "Cancelled";
      else addition!.quantity = request.quantity;
      version++;
      return json(route, { account: account(), additions });
    }
    if (path.endsWith("/commands") && method === "POST") {
      const request = route.request().postDataJSON();
      expect(request.expectedVersion).toBe(version);
      const sent = additions.filter((item) => item.status === "Pending" &&
        (!request.additionId || item.additionId === request.additionId));
      for (const item of sent) item.status = "Sent";
      version++;
      return json(route, { roundId: crypto.randomUUID(),
        draft: { account: account(), additions },
        cards: [{ cardId: crypto.randomUUID(), stationId,
          stationName: "Cocina", state: "Pending", lineCount: sent.length }] });
    }
    return json(route, []);
  });

  await page.goto("/pos/restaurant");
  await expect(page.getByRole("heading", { name: "¿A qué mesa atendemos?" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Venta directa" })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Ir a venta directa" })).toHaveCount(0);
  await expect(page.getByRole("button", { name: /M-01/ })).toBeVisible();
  await expect(page.getByText("Iniciando Auraly")).toBeHidden();
  await page.screenshot({ path: "../tmp/restaurant-pos-map-light.png", fullPage: true });
  await page.getByRole("button", { name: /M-01/ }).click();
  await expect(page.getByRole("dialog", { name: "Identificar mesero" })).toBeVisible();
  await page.getByRole("button", { name: "Mesero" }).click();
  await expect(page.getByPlaceholder("Principal o secundaria")).toBeFocused();
  await page.getByPlaceholder("Principal o secundaria").fill("1234");
  await expect(page.getByLabel("Teclado numérico")).toHaveCount(0);
  await page.getByRole("button", { name: "Abrir mesa", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Elige productos" })).toBeVisible();
  await page.getByRole("button", { name: "Agregar Hamburguesa clásica" }).click();
  await expect(page.getByText("Consumo estimado")).toBeVisible();
  await page.screenshot({ path: "../tmp/restaurant-pos-order-light.png", fullPage: true });
  await page.evaluate(() => document.documentElement.classList.add("dark"));
  await page.screenshot({ path: "../tmp/restaurant-pos-order-dark.png", fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole("button", { name: /Cuenta ·/ }).click();
  await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight));
  const mobileTabs = page.getByRole("navigation", { name: "Vista de la mesa" });
  await expect(mobileTabs).toBeInViewport();
  expect((await mobileTabs.boundingBox())!.y).toBeGreaterThanOrEqual(0);
  await page.screenshot({ path: "../tmp/restaurant-pos-order-mobile.png", fullPage: true });
  await expect(page.getByRole("button", { name: /Eliminar producto pendiente Hamburguesa clásica/ })).toBeEnabled();
  await page.getByRole("button", { name: /Eliminar producto pendiente Hamburguesa clásica/ }).click();
  await expect(page.getByText("Toca un producto para empezar el pedido.")).toBeVisible();
  await page.getByRole("button", { name: "Productos", exact: true }).click();
  await page.getByRole("button", { name: "Agregar Hamburguesa clásica" }).click();
  await page.getByRole("button", { name: /Cuenta ·/ }).click();
  await expect(page.getByText("1 pendientes · Cocina")).toBeVisible();
  await page.getByRole("button", { name: /Enviar pendientes/ }).click();
  await expect(page.getByText("1 enviadas · Cocina")).toBeVisible();
  await page.getByRole("button", { name: "Mesas" }).click();
  await expect.poll(() => attentionEnded).toBe(true);
  await page.goto("/pos");
  await expect(page).toHaveURL(/\/pos\/restaurant$/);
  await expect(page.getByRole("heading", { name: "¿A qué mesa atendemos?" })).toBeVisible();
  expect(directWorkSessions).toBe(0);
});

test("el cajero conserva su sesión de trabajo al entrar a restaurante", async ({ page }) => {
  const cashierPermissions = ["sales.create"];
  const user = { userId, tenantId, tenantKey: "E2E", username: "cajero-prueba",
    email: "cajero@test.local", firstName: "Caja", lastName: "Prueba",
    roles: ["Cajero"], permissions: cashierPermissions };
  await page.context().addCookies([{ name: "auth_token", value: "restaurant-cashier-test",
    url: baseUrl, httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ user, tenantId, businessId }) => {
    localStorage.setItem("selected_tenant_id", tenantId);
    localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("auth-state", JSON.stringify({ state: {
      isAuthenticated: true, user }, version: 0 }));
  }, { user, tenantId, businessId });
  const option = { businessId, businessName: "Auraly Restaurante", warehouseId,
    warehouseCode: "P01", warehouseName: "Principal",
    warehouseAllowsNegativeStockSales: false, hasActiveEdgeEnrollment: false,
    pointOfSaleMode: "RestaurantBar" };
  let workSessions = 0;
  await page.route("**/api/**", (route) => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith("/auth/me")) return json(route, user);
    if (path.endsWith("/execution-context/tenants"))
      return json(route, [{ tenantId, name: "Auraly" }]);
    if (path.endsWith("/execution-context/businesses"))
      return json(route, [{ tenantId, businessId, name: "Auraly Restaurante" }]);
    if (path.endsWith("/execution-context/access"))
      return json(route, { tenantId, businessId, roles: user.roles,
        permissions: cashierPermissions });
    if (path.endsWith("/commerce/v1/pos/workspace/bootstrap"))
      return json(route, { tenantId, tenantName: "Auraly", userId,
        userDisplayName: "Caja Prueba", options: [option],
        canEnrollPosDevice: false, hasEnrollmentPermission: false,
        activeEnrolledDeviceCount: 0, maximumEnrolledDevices: 0,
        enrollmentUnavailableReason: null });
    if (path.endsWith("/commerce/v1/pos/workspace/select")) {
      expect(route.request().headers()["x-business-id"]).toBe(businessId);
      return json(route, option);
    }
    if (path.endsWith("/commerce/v1/work-sessions/current")) {
      expect(route.request().headers()["x-business-id"]).toBe(businessId);
      workSessions++;
      return json(route, { workSessionId: crypto.randomUUID() });
    }
    if (path.endsWith("/commerce/v1/restaurant/rooms") ||
        path.endsWith("/commerce/v1/restaurant/tables"))
      return json(route, { items: [], page: 1, pageSize: 100, totalCount: 0 });
    if (path.endsWith("/commerce/v1/restaurant/occupancy"))
      return json(route, []);
    return json(route, []);
  });
  await page.goto("/pos/restaurant");
  await expect(page.getByRole("heading", { name: "¿A qué mesa atendemos?" })).toBeVisible();
  expect(workSessions).toBe(1);
  await expect(page.getByRole("button", { name: "Venta directa" })).toHaveCount(0);
});
