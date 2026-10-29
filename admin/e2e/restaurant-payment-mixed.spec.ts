import { expect, test, type Route } from "@playwright/test";

const baseUrl = process.env.AURALY_E2E_BASE_URL ?? "http://127.0.0.1:3000";
const tenantId = "11111111-1111-1111-1111-111111111111";
const businessId = "22222222-2222-2222-2222-222222222222";
const userId = "33333333-3333-3333-3333-333333333333";
const warehouseId = "44444444-4444-4444-4444-444444444444";
const tableId = "55555555-5555-5555-5555-555555555555";
const accountId = "66666666-6666-6666-6666-666666666666";
const workSessionId = "77777777-7777-7777-7777-777777777777";
const firstId = "88888888-8888-8888-8888-888888888888";
const secondId = "99999999-9999-9999-9999-999999999999";

const respond = (route: Route, value: unknown) => route.fulfill({
  contentType: "application/json", body: JSON.stringify(value),
});

test.use({ serviceWorkers: "block" });

test("muestra lo pagado, cobra solo el saldo y combina tarjeta, efectivo y propina", async ({ page }) => {
  const user = { userId, tenantId, tenantKey: "E2E", username: "cajero",
    firstName: "Caja", lastName: "Prueba", roles: ["Cajero"],
    permissions: ["sales.create", "restaurant.tables.open_without_waiter"] };
  const option = { businessId, businessName: "Restaurante", warehouseId,
    warehouseCode: "P01", warehouseName: "Principal",
    warehouseAllowsNegativeStockSales: false, hasActiveEdgeEnrollment: false,
    pointOfSaleMode: "RestaurantBar", fiscalReadyForOnlineSales: false,
    hasDianDocumentQuota: false };
  const account = { accountId, businessId, tableId, warehouseId,
    responsibleUserId: userId, status: "Open", version: 1,
    openedAt: "2026-10-29T12:00:00Z", updatedAt: "2026-10-29T12:00:00Z" };
  const additions = [
    { additionId: firstId, description: "Sopa", quantity: 1,
      publicUnitPrice: 10_000, promotionDiscountPerUnit: 0 },
    { additionId: secondId, description: "Pasta", quantity: 1,
      publicUnitPrice: 20_000, promotionDiscountPerUnit: 0 },
  ].map((item) => ({ ...item, accountId, productId: item.additionId,
    productCode: item.description, unitCode: "UND", taxCode: "IVA",
    taxRate: 0, allowsFractionalSale: false, currencyCode: "COP",
    priceSource: "Base", addedByUserId: userId, addedByName: "Caja",
    addedAt: "2026-10-29T12:00:00Z", status: "Sent",
    stationId: null, stationName: null, preparationNote: null }));
  const collections: Array<{ collectionId: string; accountId: string;
    workSessionId: string; cashierUserId: string; amount: number;
    capturedAt: string; allocations: { additionId: string; quantity: number }[];
    payments: Array<{ methodCode: string; amount: number }>;
    tipPayment: { methodCode: string; amount: number } | null }> = [];
  let collectionReads = 0;
  let suggestedTipPercent = 10;
  const summary = () => {
    const coveredQuantities = collections.flatMap((entry) => entry.allocations);
    const collected = collections.reduce((sum, entry) => sum + entry.amount, 0);
    return { accountId, accountVersion: 1, total: 30_000, collected,
      remaining: 30_000 - collected, collections, coveredQuantities,
      tipsCollected: collections.reduce((sum, entry) =>
        sum + (entry.tipPayment?.amount ?? 0), 0), suggestedTipPercent: 10 };
  };
  await page.context().addCookies([{ name: "auth_token", value: "restaurant-test",
    url: baseUrl, httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ user, tenantId, businessId }) => {
    localStorage.setItem("selected_tenant_id", tenantId);
    localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("auth-state", JSON.stringify({ state: {
      isAuthenticated: true, user }, version: 0 }));
  }, { user, tenantId, businessId });
  await page.route("**/api/**", (route) => {
    const path = new URL(route.request().url()).pathname;
    const method = route.request().method();
    if (path.endsWith("/auth/me")) return respond(route, user);
    if (path.endsWith("/execution-context/tenants"))
      return respond(route, [{ tenantId, name: "Restaurante" }]);
    if (path.endsWith("/execution-context/businesses"))
      return respond(route, [{ tenantId, businessId, name: "Restaurante" }]);
    if (path.endsWith("/execution-context/access"))
      return respond(route, { tenantId, businessId, roles: user.roles,
        permissions: user.permissions });
    if (path.endsWith("/commerce/v1/pos/workspace/bootstrap"))
      return respond(route, { tenantId, tenantName: "Restaurante", userId,
        userDisplayName: "Caja", options: [option], canEnrollPosDevice: false,
        hasEnrollmentPermission: false, activeEnrolledDeviceCount: 0,
        maximumEnrolledDevices: 0, enrollmentUnavailableReason: null });
    if (path.endsWith("/commerce/v1/pos/workspace/select"))
      return respond(route, option);
    if (path.endsWith("/commerce/v1/work-sessions/current"))
      return respond(route, { workSessionId });
    if (path.endsWith("/commerce/v1/restaurant/rooms"))
      return respond(route, { items: [], page: 1, pageSize: 100, totalCount: 0 });
    if (path.endsWith("/commerce/v1/restaurant/tables"))
      return respond(route, { items: [{ tableId, businessId, roomId: null,
        code: "M-01", name: null, capacity: 4, isActive: true,
        rowVersion: "AAAAAAAAAAA=" }], page: 1, pageSize: 100, totalCount: 1 });
    if (path.endsWith("/commerce/v1/restaurant/occupancy"))
      return respond(route, [{ tableId, accountId, state: "Open",
        responsibleUserId: userId, openedAt: account.openedAt, accountVersion: 1 }]);
    if (path.endsWith("/draft"))
      return respond(route, { account, additions });
    if (path.endsWith("/cancellations")) return respond(route, []);
    if (path.endsWith("/categories")) return respond(route, []);
    if (path.endsWith("/products"))
      return respond(route, { items: [], hasMore: false, nextOffset: null });
    if (path.endsWith("/settlement-preview"))
      return respond(route, { grossAmount: 30_000, withholdingTotal: 0,
        netAmount: 30_000 });
    if (path.endsWith("/prebill"))
      return respond(route, { accountId, tableCode: "M-01", accountVersion: 1,
        printedAt: "2026-10-29T12:00:00Z", total: 30_000,
        suggestedTipPercent, suggestedTipAmount: 30_000 * suggestedTipPercent / 100,
        lines: additions.map((item) => ({ description: item.description,
          quantity: 1, unitPrice: item.publicUnitPrice,
          total: item.publicUnitPrice, addedByName: "Caja" })) });
    if (path.endsWith("/collections") && method === "GET") {
      collectionReads++;
      return respond(route, summary());
    }
    if (path.endsWith("/collections") && method === "POST") {
      const request = route.request().postDataJSON();
      const amount = request.allocations.reduce((sum: number,
        line: { additionId: string; quantity: number }) =>
        sum + line.quantity * (line.additionId === firstId ? 10_000 : 20_000), 0);
      expect(request.payments.reduce((sum: number,
        payment: { amount: number }) => sum + payment.amount, 0)).toBe(amount);
      collections.push({ collectionId: request.collectionId, accountId,
        workSessionId, cashierUserId: userId, amount,
        capturedAt: "2026-10-29T12:00:00Z", allocations: request.allocations,
        payments: request.payments, tipPayment: request.tipPayment ?? null });
      return respond(route, summary());
    }
    if (path.endsWith("/complete") && method === "POST") {
      const request = route.request().postDataJSON();
      expect(request.checkout.payments.reduce((sum: number,
        item: { amount: number }) => sum + item.amount, 0)).toBe(30_000);
      return respond(route, { receipt: { documentNumber: "R-1" }, isReplay: false });
    }
    if (path.endsWith("/commerce/v1/reference-options/payment-method"))
      return respond(route, [
        { id: "1", code: "Cash", label: "Efectivo", sortOrder: 1 },
        { id: "2", code: "DebitCard", label: "Tarjeta débito", sortOrder: 2 },
      ]);
    if (path.endsWith("/commerce/v1/reference-options/card-franchise"))
      return respond(route, [{ id: "3", code: "Visa", label: "Visa", sortOrder: 1 }]);
    if (path.endsWith("/commerce/v1/pos/settlement-configuration"))
      return respond(route, { isAccountingEnabled: false, bankAccounts: [] });
    return respond(route, []);
  });

  await page.goto("/pos/restaurant");
  await page.getByRole("button", { name: /M-01/ }).click();
  const popupPromise = page.waitForEvent("popup");
  await page.getByRole("button", { name: "Imprimir", exact: true }).click();
  const prebill = await popupPromise;
  await expect(prebill.getByText("Propina voluntaria sugerida (10 %)"))
    .toBeVisible();
  await prebill.close();
  await expect(page.getByRole("dialog", { name: "Preparar precuenta" })).toHaveCount(0);
  suggestedTipPercent = 0;
  const zeroTipPopup = page.waitForEvent("popup");
  await page.getByRole("button", { name: "Imprimir", exact: true }).click();
  const zeroTipPrebill = await zeroTipPopup;
  await expect(zeroTipPrebill.getByText("Propina voluntaria sugerida (0 %)"))
    .toBeVisible();
  await zeroTipPrebill.close();
  await page.getByRole("button", { name: "Facturar mesa" }).click();
  const payment = page.getByRole("dialog", { name: "Cobrar mesa" });
  await expect(payment.getByText("Por cobrar").locator("..")).toContainText("30.000");
  await payment.getByRole("button", { name: "Elegir productos" }).click();
  await payment.getByRole("button", { name: "Marcar Sopa" }).click();
  await payment.getByRole("button", { name: "Guardar este pago" }).click();
  await expect(payment.getByRole("button", { name: "Sopa pagado" })).toBeDisabled();
  await expect(payment.getByText("Por cobrar").locator("..")).toContainText("20.000");
  await expect(payment.getByText("Pago de esta persona · consumo").locator("..")).toContainText("20.000");
  await payment.getByLabel("Propina voluntaria recibida").fill("2000");
  await expect(payment.getByRole("textbox", { name: /Efectivo entregado/ })).toHaveValue("22000");
  await payment.getByRole("combobox", { name: "Medio de pago" }).click();
  await page.getByRole("option", { name: "Tarjeta débito" }).click();
  await expect(payment.getByRole("textbox", { name: "Valor cobrado por consumo" })).toHaveValue("20000");
  await payment.getByRole("combobox", { name: "Medio de pago" }).click();
  await page.getByRole("option", { name: "Efectivo" }).click();
  await expect(payment.getByRole("textbox", { name: /Efectivo entregado/ })).toHaveValue("22000");
  await payment.getByRole("button", { name: "Agregar otro medio" }).click();
  await expect(payment.getByRole("button", { name: "Cobrar y emitir documento" })).toBeDisabled();
  await payment.getByRole("combobox", { name: "Franquicia" }).click();
  await page.getByRole("option", { name: "Visa" }).click();
  await payment.getByRole("textbox", { name: "Número de aprobación" }).fill("AP-1");
  await payment.getByLabel("Propina voluntaria recibida").fill("3000");
  await expect(payment.getByRole("button", { name: "Cobrar y emitir documento" })).toBeEnabled();
  await payment.screenshot({ path: "../tmp/restaurant-payment-mixed.png" });
  await payment.getByRole("button", { name: "Cobrar y emitir documento" }).click();
  await expect(page.getByRole("heading", { name: "¿A qué mesa atendemos?" })).toBeVisible();
  expect(collections).toHaveLength(2);
  expect(collectionReads).toBe(1);
  expect(collections[1].tipPayment).toMatchObject({ methodCode: "Cash", amount: 3000 });
});
