import { expect, test, type Route, type Page } from "@playwright/test";
import { paymentCatalogFixture } from "./support/payment-catalog";
import { expectNoHorizontalOverflow } from "./support/layout-bounds";

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

async function prepareRestaurant(page: Page, options: {
  firstPrice?: number; firstQuantity?: number; secondPrice?: number;
  status?: "Sent" | "Direct" | "Pending"; secondDebit?: boolean;
} = {}) {
  await page.addInitScript(() => {
    window.print = () => document.documentElement.setAttribute("data-print-requested", "1");
  });
  const user = { userId, tenantId, tenantKey: "E2E", username: "cajero",
    firstName: "Caja", lastName: "Prueba", roles: ["Cajero"],
    permissions: ["sales.create", "restaurant.tables.open_without_waiter"] };
  const option = { businessId, businessName: "Restaurante", warehouseId,
    warehouseCode: "P01", warehouseName: "Principal",
    warehouseAllowsNegativeStockSales: false, hasActiveEdgeEnrollment: false,
    restaurantRequireWaiterCredential: false, pointOfSaleMode: "RestaurantBar", fiscalReadyForOnlineSales: false,
    hasDianDocumentQuota: false };
  const account = { accountId, businessId, tableId, warehouseId,
    responsibleUserId: userId, status: "Open", version: 1,
    openedAt: "2026-10-29T12:00:00Z", updatedAt: "2026-10-29T12:00:00Z" };
  const additions = [
    { additionId: firstId, description: "Sopa", quantity: options.firstQuantity ?? 2,
      publicUnitPrice: options.firstPrice ?? 10_000, promotionDiscountPerUnit: 0 },
    { additionId: secondId, description: "Pasta", quantity: 1,
      publicUnitPrice: options.secondPrice ?? 20_000, promotionDiscountPerUnit: 0 },
  ].map((item) => ({ ...item, accountId, productId: item.additionId,
    productCode: item.description, unitCode: "UND", taxCode: "IVA",
    taxRate: 0, allowsFractionalSale: false, currencyCode: "COP",
    priceSource: "Base", addedByUserId: userId, addedByName: "Caja",
    addedAt: "2026-10-29T12:00:00Z", status: (options.status ?? "Sent") as string,
    stationId: null, stationName: null, preparationNote: null }));
  const collections: Array<{ collectionId: string; accountId: string;
    workSessionId: string; cashierUserId: string; amount: number;
    capturedAt: string; allocations: { additionId: string; quantity: number }[];
    payments: Array<{ methodCode: string; amount: number }>;
    tipPayment: { methodCode: string; amount: number } | null }> = [];
  let collectionReads = 0;
  let suggestedTipPercent = 10;
  const summary = () => {
    const coveredQuantities = additions.map((addition) => ({ additionId: addition.additionId,
      quantity: collections.flatMap((entry) => entry.allocations).filter((item) =>
        item.additionId === addition.additionId).reduce((sum, item) => sum + item.quantity, 0) }));
    const collected = collections.reduce((sum, entry) => sum + entry.amount, 0);
    const total = additions.filter((item) => item.status !== "Cancelled").reduce((sum, item) =>
      sum + item.quantity * item.publicUnitPrice, 0);
    return { accountId, accountVersion: account.version, total, collected,
      remaining: Math.round((total - collected) * 10_000) / 10_000, collections, coveredQuantities,
      tipsCollected: collections.reduce((sum, entry) =>
        sum + (entry.tipPayment?.amount ?? 0), 0), suggestedTipPercent: 10 };
  };
  const currentDraft = () => ({ account, additions: additions.map((item) => ({ ...item,
    coveredQuantity: summary().coveredQuantities.find((line) =>
      line.additionId === item.additionId)?.quantity ?? 0 })) });
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
      return respond(route, [{ tenantId, businessId, name: "Restaurante", timeZone: "America/Bogota" }]);
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
      return respond(route, currentDraft());
    if (path.endsWith("/quantity") && method === "PUT") {
      const request = route.request().postDataJSON();
      expect(request.expectedVersion).toBe(account.version);
      const addition = additions.find((item) => path.includes(item.additionId))!;
      const covered = summary().coveredQuantities.find((item) => item.additionId === addition.additionId)!.quantity;
      if (request.quantity < covered) return route.fulfill({ status: 400,
        contentType: "application/json", body: JSON.stringify({ message:
          "Este producto tiene un pago registrado. Anula el cobro que lo incluye en Cobros recibidos." }) });
      if (request.quantity === 0) addition.status = "Cancelled";
      else addition.quantity = request.quantity;
      account.version++;
      return respond(route, currentDraft());
    }
    if (path.endsWith("/cancellations")) return respond(route, []);
    if (path.endsWith("/categories")) return respond(route, []);
    if (path.endsWith("/products"))
      return respond(route, { items: [], hasMore: false, nextOffset: null });
    if (path.endsWith("/settlement-preview"))
      return respond(route, { grossAmount: 40_000, withholdingTotal: 0,
        netAmount: 40_000 });
    if (path.endsWith("/prebill"))
      return respond(route, { accountId, tableCode: "M-01", accountVersion: account.version,
        printedAt: "2026-10-29T12:00:00Z", total: 40_000,
        suggestedTipPercent, suggestedTipAmount: 40_000 * suggestedTipPercent / 100,
        lines: additions.filter((item) => item.status !== "Cancelled").map((item) => ({ description: item.description,
          quantity: item.quantity, unitPrice: item.publicUnitPrice,
          total: item.quantity * item.publicUnitPrice, addedByName: "Caja" })) });
    if (path.endsWith("/collections") && method === "GET") {
      collectionReads++;
      return respond(route, summary());
    }
    if (path.endsWith("/collections") && method === "POST") {
      const request = route.request().postDataJSON();
      expect(request.expectedAccountVersion).toBe(account.version);
      const amount = request.allocations.reduce((sum: number,
        line: { additionId: string; quantity: number }) =>
        sum + line.quantity * additions.find((item) => item.additionId === line.additionId)!.publicUnitPrice, 0);
      expect(request.payments.reduce((sum: number,
        payment: { amount: number }) => sum + payment.amount, 0)).toBe(amount);
      collections.push({ collectionId: request.collectionId, accountId,
        workSessionId, cashierUserId: userId, amount,
        capturedAt: "2026-10-29T12:00:00Z", allocations: request.allocations,
        payments: request.payments, tipPayment: request.tipPayment ?? null });
      account.version++;
      return respond(route, summary());
    }
    if (path.endsWith("/complete") && method === "POST") {
      const request = route.request().postDataJSON();
      expect(request.checkout.payments.reduce((sum: number,
        item: { amount: number }) => sum + item.amount, 0)).toBe(summary().total);
      return respond(route, { receipt: { documentNumber: "R-1" }, isReplay: false });
    }
    if (path.endsWith("/commerce/v1/reference-options/payment-method"))
      return respond(route, [
        { id: "1", code: "Cash", label: "Efectivo", sortOrder: 1 },
        { id: "2", code: "DebitCard", label: "Tarjeta débito", sortOrder: 2 },
        { id: "4", code: "CreditCard", label: "Tarjeta crédito", sortOrder: 3 },
        { id: "5", code: "Transfer", label: "Transferencia", sortOrder: 4 },
      ]);
    if (path.endsWith("/commerce/v1/reference-options/card-franchise"))
      return respond(route, [{ id: "3", code: "Visa", label: "Visa", sortOrder: 1 }]);
    if (path.endsWith("/commerce/v1/pos/settlement-configuration"))
    {
      const paymentMethods = paymentCatalogFixture();
      if (options.secondDebit) paymentMethods.forms.push({ ...paymentMethods.forms[1],
        paymentMethodId: "10000000-0000-0000-0000-000000000005", code: "SECOND-DEBIT",
        name: "Segundo datáfono", accountId: "account-second-debit" });
      return respond(route, { isAccountingEnabled: false, bankAccounts: [], paymentMethods });
    }
    return respond(route, []);
  });

  return { account, additions, collections, collectionReads: () => collectionReads,
    setSuggestedTipPercent: (value: number) => { suggestedTipPercent = value; } };
}

test("muestra lo pagado, cobra solo el saldo y combina tarjeta, efectivo y propina", async ({ page }) => {
  const scenario = await prepareRestaurant(page);
  await page.goto("/pos/restaurant");
  await page.getByRole("button", { name: /M-01/ }).click();
  await page.getByRole("button", { name: "Imprimir", exact: true }).click();
  const prebill = page.frameLocator('iframe[aria-hidden="true"]');
  await expect(prebill.locator("html")).toHaveAttribute("data-print-requested", "1");
  await expect(prebill.getByRole("heading", { name: "Mesa M-01", includeHidden: true })).toHaveCount(1);
  await expect(prebill.locator("body")).toContainText("Propina voluntaria sugerida (10 %)");
  const prebillBody = await prebill.locator("body").textContent();
  expect(prebillBody?.match(/Precuenta/g)).toHaveLength(1);
  await expect(prebill.locator("button")).toHaveCount(0);
  expect(page.context().pages()).toHaveLength(1);
  await page.locator('iframe[aria-hidden="true"]').evaluate(element =>
    (element as HTMLIFrameElement).contentWindow?.dispatchEvent(new Event("afterprint")));
  await expect(page.locator('iframe[aria-hidden="true"]')).toHaveCount(0);
  await expect(page.getByRole("dialog", { name: "Preparar precuenta" })).toHaveCount(0);
  scenario.setSuggestedTipPercent(0);
  await page.getByRole("button", { name: "Imprimir", exact: true }).click();
  await expect(prebill.locator("html")).toHaveAttribute("data-print-requested", "1");
  await expect(prebill.locator("body")).toContainText("Propina voluntaria sugerida (0 %)");
  await page.locator('iframe[aria-hidden="true"]').evaluate(element =>
    (element as HTMLIFrameElement).contentWindow?.dispatchEvent(new Event("afterprint")));
  await expect(page.locator('iframe[aria-hidden="true"]')).toHaveCount(0);
  await page.getByRole("button", { name: "Facturar mesa" }).click();
  const payment = page.getByRole("dialog", { name: "Cobrar mesa" });
  await expect(payment.getByText("Por cobrar").locator("..")).toContainText("40.000");
  await payment.getByRole("button", { name: "Elegir productos" }).click();
  await payment.getByRole("button", { name: "Marcar Sopa" }).click();
  await payment.getByRole("button", { name: "Quitar Sopa" }).click();
  await payment.getByRole("button", { name: "Guardar este pago" }).click();
  await expect(payment.getByText("Pendientes de pago")).toBeVisible();
  await expect(payment.getByText("Ya pagados")).toHaveCount(0);
  const received = payment.locator("details");
  await expect(received).toHaveCount(1);
  await expect(received.getByLabel("Detalle del cobro")).toBeHidden();
  await received.locator("summary").click();
  await expect(received.getByText("Sopa", { exact: true })).toBeVisible();
  await expect(received.getByText("1 pagado", { exact: false })).toBeVisible();
  await received.locator("summary").click();
  await expect(received.getByLabel("Detalle del cobro")).toBeHidden();
  await expect(payment.getByText("Por cobrar").locator("..")).toContainText("30.000");
  await expect(payment.getByText("Pago de esta persona · consumo").locator("..")).toContainText("30.000");
  await payment.getByRole("textbox", { name: /Efectivo entregado/ }).fill("40000");
  await expect(payment.getByLabel("Estado del pago")).toContainText("Cambio a entregar");
  await expect(payment.getByText("Pago completo", { exact: true })).toHaveCount(0);
  await payment.getByRole("textbox", { name: /Efectivo entregado/ }).fill("10000");
  await expect(payment.getByLabel("Estado del pago")).toContainText("Falta");
  await payment.getByRole("textbox", { name: /Efectivo entregado/ }).fill("30000");
  await expect(payment.getByLabel("Estado del pago")).toHaveText("Pago completo");
  await payment.getByLabel("Propina voluntaria recibida").fill("2000");
  await expect(payment.getByRole("textbox", { name: /Efectivo entregado/ })).toHaveValue("32.000");
  await payment.getByRole("combobox", { name: "Medio de pago" }).click();
  await page.getByRole("option", { name: "Tarjeta débito" }).click();
  await expect(payment.getByRole("textbox", { name: "Valor cobrado por consumo" })).toHaveValue("30.000");
  await payment.getByRole("combobox", { name: "Medio de pago" }).click();
  await page.getByRole("option", { name: "Efectivo" }).click();
  await expect(payment.getByRole("textbox", { name: /Efectivo entregado/ })).toHaveValue("32.000");
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
  expect(scenario.collections).toHaveLength(2);
  expect(scenario.collectionReads()).toBe(1);
  expect(scenario.collections[1].tipPayment).toMatchObject({ methodCode: "Cash", amount: 3000 });
});


test("cobra el saldo decimal con tarjeta y propina en efectivo después de un pago parcial con propina", async ({ page }) => {
  const scenario = await prepareRestaurant(page, { firstPrice: 30_654.4,
    firstQuantity: 1, secondPrice: 7_540, status: "Direct" });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/pos/restaurant");
  await page.getByRole("button", { name: /M-01/ }).click();
  await page.getByRole("button", { name: "Cuenta · 2" }).click();
  await page.getByRole("button", { name: "Facturar mesa" }).click();
  const payment = page.getByRole("dialog", { name: "Cobrar mesa" });
  await payment.getByRole("button", { name: "Elegir productos" }).click();
  await payment.getByRole("button", { name: "Marcar Sopa" }).click();
  await payment.getByLabel("Propina voluntaria recibida").fill("1000");
  await expect(payment.getByRole("textbox", { name: /Efectivo entregado/ })).toHaveValue("31.700");
  await payment.getByRole("button", { name: "Guardar este pago" }).click();
  await expect(payment.getByRole("textbox", { name: /Efectivo entregado/ })).toHaveValue("7.500");
  await payment.getByRole("textbox", { name: /Efectivo entregado/ }).fill("1000");
  await payment.getByRole("button", { name: "Todo lo pendiente" }).click();
  await expect(payment.getByRole("textbox", { name: /Efectivo entregado/ })).toHaveValue("7.500");
  await payment.getByRole("combobox", { name: "Medio de pago", exact: true }).click();
  await page.getByRole("option", { name: "Tarjeta débito" }).click();
  await payment.getByRole("combobox", { name: "Franquicia", exact: true }).click();
  await page.getByRole("option", { name: "Visa" }).click();
  await payment.getByRole("textbox", { name: "Número de aprobación", exact: true }).fill("DECIMAL-1");
  await payment.getByLabel("Propina voluntaria recibida").fill("1000");
  await payment.getByRole("textbox", { name: "Efectivo entregado para la propina" }).fill("1000");
  await expect(payment.getByLabel("Propina voluntaria recibida")).toHaveValue("1.000");
  await expect(payment.getByRole("button", { name: "Cobrar y emitir documento" })).toBeEnabled();
  await payment.getByRole("textbox", { name: "Valor cobrado por consumo" }).fill("7546");
  await expect(payment.getByLabel("Estado del pago")).toContainText("46");
  await expect(payment.getByRole("button", { name: "Cobrar y emitir documento" })).toBeDisabled();
  await payment.getByRole("textbox", { name: "Valor cobrado por consumo" }).fill("7500");
  await expect(payment.getByRole("button", { name: "Cobrar y emitir documento" })).toBeEnabled();
  await payment.screenshot({ path: "../.codex-build/restaurant-decimal-tip-mobile.png" });
  await payment.getByRole("button", { name: "Cobrar y emitir documento" }).click();
  await expect(page.getByRole("heading", { name: "¿A qué mesa atendemos?" })).toBeVisible();
  expect(scenario.collections[1].payments).toMatchObject([
    { methodCode: "DebitCard", amount: 7_540, roundingAdjustment: -40 }]);
  expect(scenario.collections[1].tipPayment).toMatchObject({ methodCode: "Cash", amount: 1_000, tenderedAmount: 1_000 });
  expect(scenario.collectionReads()).toBe(1);
});

test("un cobro parcial no obliga a reabrir la mesa para quitar productos sin pago ni imprimir la precuenta", async ({ page }) => {
  const scenario = await prepareRestaurant(page, { firstQuantity: 1, status: "Direct" });
  await page.goto("/pos/restaurant");
  await page.getByRole("button", { name: /M-01/ }).click();
  await page.getByRole("button", { name: "Facturar mesa" }).click();
  const payment = page.getByRole("dialog", { name: "Cobrar mesa" });
  await payment.getByRole("button", { name: "Elegir productos" }).click();
  await payment.getByRole("button", { name: "Marcar Sopa" }).click();
  await payment.getByRole("button", { name: "Guardar este pago" }).click();
  await payment.getByRole("button", { name: "Cerrar cobro" }).click();
  let changes = 0;
  page.on("request", request => { if (request.url().endsWith("/quantity")) changes++; });
  await page.getByRole("button", { name: "Quitar uno de Sopa", exact: true }).click();
  await expect(page.getByText(/Este producto tiene un pago registrado/)).toBeVisible();
  expect(changes).toBe(0);
  await page.getByRole("button", { name: "Quitar uno de Pasta", exact: true }).click();
  await expect(page.getByRole("region", { name: "Productos de la mesa" }).getByText("Pasta", { exact: true })).toHaveCount(0);
  expect(changes).toBe(1);
  expect(scenario.account.version).toBe(3);
  await page.getByRole("button", { name: "Imprimir", exact: true }).click();
  const prebill = page.frameLocator('iframe[aria-hidden="true"]');
  await expect(prebill.locator("html")).toHaveAttribute("data-print-requested", "1");
  await expect(prebill.locator("body")).toContainText("Revisión 3");
  await expect(prebill.locator("body")).not.toContainText("Pasta");
});


test("la precuenta permite imprimir productos aún pendientes de enviar", async ({ page }) => {
  await prepareRestaurant(page, { status: "Pending" });
  await page.goto("/pos/restaurant");
  await page.getByRole("button", { name: /M-01/ }).click();
  await expect(page.getByRole("button", { name: "Facturar mesa" })).toBeDisabled();
  await page.getByRole("button", { name: "Imprimir", exact: true }).click();
  const prebill = page.frameLocator('iframe[aria-hidden="true"]');
  await expect(prebill.locator("html")).toHaveAttribute("data-print-requested", "1");
  await expect(prebill.locator("body")).toContainText("Sopa");
});


const paymentMethods = [
  ["Cash", "Efectivo"], ["DebitCard", "Tarjeta débito"],
  ["CreditCard", "Tarjeta crédito"], ["Transfer", "Transferencia"],
] as const;

for (const [consumption, consumptionLabel] of paymentMethods) {
  for (const [tip, tipLabel] of paymentMethods) {
    test(`consumo ${consumptionLabel} y propina ${tipLabel} conservan importes, cambio y documento`, async ({ page }) => {
      const scenario = await prepareRestaurant(page);
      await page.goto("/pos/restaurant");
      await page.getByRole("button", { name: /M-01/ }).click();
      await page.getByRole("button", { name: "Facturar mesa" }).click();
      const payment = page.getByRole("dialog", { name: "Cobrar mesa" });
      if (consumption !== "Cash") {
        await payment.getByRole("combobox", { name: "Medio de pago", exact: true }).click();
        await page.getByRole("option", { name: consumptionLabel, exact: true }).click();
      }
      if (consumption === "DebitCard" || consumption === "CreditCard") {
        await payment.getByRole("combobox", { name: "Franquicia", exact: true }).click();
        await page.getByRole("option", { name: "Visa", exact: true }).click();
        await payment.getByRole("textbox", { name: "Número de aprobación", exact: true }).fill("CONSUMO-1");
      } else if (consumption === "Transfer") {
        await payment.getByRole("textbox", { name: "Referencia de transferencia", exact: true }).fill("CONSUMO-TR-1");
      }
      await payment.getByLabel("Propina voluntaria recibida").fill("1000");
      if (tip !== "Cash") {
        await payment.getByRole("combobox", { name: "Medio de la propina", exact: true }).click();
        await page.getByRole("option", { name: tipLabel, exact: true }).click();
      }
      if (tip === "DebitCard" || tip === "CreditCard") {
        await payment.getByRole("combobox", { name: "Franquicia de la propina", exact: true }).click();
        await page.getByRole("option", { name: "Visa", exact: true }).click();
        await payment.getByRole("textbox", { name: "Aprobación de la propina", exact: true }).fill("PROPINA-1");
      } else if (tip === "Transfer") {
        await payment.getByRole("textbox", { name: "Referencia de la propina", exact: true }).fill("PROPINA-TR-1");
      }
      if (consumption === "Cash") {
        await payment.getByRole("textbox", { name: /Efectivo entregado/ }).fill("50000");
        await expect(payment.getByLabel("Estado del pago")).toContainText(tip === "Cash" ? "9.000" : "10.000");
      } else if (tip === "Cash") {
        const tender = payment.getByRole("textbox", { name: "Efectivo entregado para la propina" });
        await tender.fill("500");
        await expect(payment.getByRole("button", { name: "Cobrar y emitir documento" })).toBeDisabled();
        await tender.fill("2000");
        await expect(payment.getByLabel("Estado del pago")).toContainText("Cambio a entregar");
        await expect(payment.getByLabel("Estado del pago")).toContainText("1.000");
      }
      await expect(payment.getByRole("button", { name: "Cobrar y emitir documento" })).toBeEnabled();
      await payment.getByRole("button", { name: "Cobrar y emitir documento" }).click();
      await expect(page.getByRole("heading", { name: "¿A qué mesa atendemos?" })).toBeVisible();
      expect(scenario.collections).toHaveLength(1);
      expect(scenario.collections[0].payments).toMatchObject([{ methodCode: consumption, amount: 40_000 }]);
      expect(scenario.collections[0].tipPayment).toMatchObject({ methodCode: tip, amount: 1_000 });
      expect(scenario.collectionReads()).toBe(1);
    });
  }
}


for (const [price, rounded, adjustment] of [[7546, 7500, -46], [7550, 7600, 50]]) {
  test(`el cobro parcial de ${price} se lleva a ${rounded} antes de agregar propina`, async ({ page }) => {
    const scenario = await prepareRestaurant(page, { firstQuantity: 1, firstPrice: price });
    await page.goto("/pos/restaurant");
    await page.getByRole("button", { name: /M-01/ }).click();
    await page.getByRole("button", { name: "Facturar mesa" }).click();
    const payment = page.getByRole("dialog", { name: "Cobrar mesa" });
    await payment.getByRole("button", { name: "Elegir productos" }).click();
    await payment.getByRole("button", { name: "Marcar Sopa" }).click();
    await expect(payment.getByRole("textbox", { name: /Efectivo entregado/ })).toHaveValue(rounded.toLocaleString("es-CO"));
    await payment.getByLabel("Propina voluntaria recibida").fill("1000");
    await expect(payment.getByRole("textbox", { name: /Efectivo entregado/ })).toHaveValue((rounded + 1000).toLocaleString("es-CO"));
    await payment.getByRole("button", { name: "Guardar este pago" }).click();
    await expect(payment.getByRole("textbox", { name: /Efectivo entregado/ })).toHaveValue("20.000");
    expect(scenario.collections[0].payments).toMatchObject([{ amount: price, roundingAdjustment: adjustment }]);
  });
}


test("una incorporación pagada al final del mismo producto permite quitar la anterior sin pagar", async ({ page }) => {
  const scenario = await prepareRestaurant(page, { firstQuantity: 1, secondPrice: 10_000, status: "Direct" });
  scenario.additions[1].productId = scenario.additions[0].productId;
  scenario.additions[1].description = "Sopa";
  await page.goto("/pos/restaurant");
  await page.getByRole("button", { name: /M-01/ }).click();
  await page.getByRole("button", { name: "Facturar mesa" }).click();
  const payment = page.getByRole("dialog", { name: "Cobrar mesa" });
  await payment.getByRole("button", { name: "Elegir productos" }).click();
  await payment.getByRole("button", { name: "Marcar Sopa", exact: true }).last().click();
  await payment.getByRole("button", { name: "Guardar este pago" }).click();
  await payment.getByRole("button", { name: "Cerrar cobro" }).click();
  await page.getByRole("button", { name: "Quitar uno de Sopa", exact: true }).click();
  await expect.poll(() => scenario.additions[0].status).toBe("Cancelled");
  expect(scenario.additions[1].status).toBe("Direct");
  await page.getByRole("button", { name: "Quitar uno de Sopa", exact: true }).click();
  await expect(page.getByText(/Este producto tiene un pago registrado/)).toBeVisible();
  expect(scenario.additions[1].status).toBe("Direct");
});


test("el estado de pago mantiene tamaño y fondo al faltar, sobrar o recibir el valor exacto", async ({ page }) => {
  await prepareRestaurant(page);
  for (const viewport of [{ width: 320, height: 740 }, { width: 390, height: 844 },
    { width: 768, height: 1024 }, { width: 1440, height: 1000 }]) {
    await page.setViewportSize(viewport);
    await page.goto("/pos/restaurant");
    await page.getByRole("button", { name: /M-01/ }).click();
    const accountTab = page.getByRole("button", { name: /^Cuenta ·/ });
    if (await accountTab.isVisible()) await accountTab.click();
    await page.getByRole("button", { name: "Facturar mesa" }).click();
    const payment = page.getByRole("dialog", { name: "Cobrar mesa" });
    const status = payment.getByLabel("Estado del pago");
    await expect(status).toHaveText("Pago completo");
    const exact = await status.evaluate(element => ({ height: element.getBoundingClientRect().height,
      background: getComputedStyle(element).backgroundColor }));
    expect(exact.height).toBeGreaterThanOrEqual(84);
    await expect(payment.getByText("Imprimir al finalizar", { exact: true })).toHaveCount(0);
    await expect(payment.getByRole("group", { name: "Imprimir al finalizar" }).getByRole("button")).toHaveCount(5);
    const cash = payment.getByRole("textbox", { name: /Efectivo entregado/ });
    await cash.fill("1000");
    await expect(status).toContainText("Faltan");
    await expect(status).toContainText("39.000");
    const missing = await status.evaluate(element => ({ height: element.getBoundingClientRect().height,
      background: getComputedStyle(element).backgroundColor,
      amountFont: parseFloat(getComputedStyle(element.querySelector("p:last-child")!).fontSize) }));
    expect(missing.background).toBe(exact.background);
    expect(Math.abs(missing.height - exact.height)).toBeLessThanOrEqual(1);
    expect(missing.amountFont).toBeGreaterThanOrEqual(24);
    await expect(payment.locator("footer").getByText(/por asignar a los medios/)).toHaveCount(0);
    await cash.fill("50000");
    await expect(status).toContainText("Cambio a entregar");
    await expect(status).toContainText("10.000");
    await cash.fill("40000");
    await expect(status).toHaveText("Pago completo");
    await status.scrollIntoViewIfNeeded();
    await payment.screenshot({ path: `../.codex-build/restaurant-status-${viewport.width}.png` });
    await expectNoHorizontalOverflow(page.locator("html"));
  }
});


test("permite elegir el segundo datáfono y conserva su identidad al emitir", async ({ page }) => {
  const scenario = await prepareRestaurant(page, { secondDebit: true });
  await page.goto("/pos/restaurant");
  await page.getByRole("button", { name: /M-01/ }).click();
  await page.getByRole("button", { name: "Facturar mesa" }).click();
  const payment = page.getByRole("dialog", { name: "Cobrar mesa" });
  await payment.getByRole("combobox", { name: "Medio de pago", exact: true }).click();
  await page.getByRole("option", { name: "Tarjeta débito", exact: true }).click();
  await payment.getByRole("combobox", { name: "Forma de pago", exact: true }).click();
  await expect(page.getByRole("option", { name: "Tarjeta débito · FORM-DebitCard" })).toBeVisible();
  await page.getByRole("option", { name: "Segundo datáfono · SECOND-DEBIT" }).click();
  await payment.getByRole("combobox", { name: "Franquicia", exact: true }).click();
  await page.getByRole("option", { name: "Visa", exact: true }).click();
  await payment.getByRole("textbox", { name: "Número de aprobación", exact: true }).fill("NATIVE-SECOND");
  await payment.getByRole("button", { name: "Cobrar y emitir documento" }).click();
  await expect(page.getByRole("heading", { name: "¿A qué mesa atendemos?" })).toBeVisible();
  expect(scenario.collections[0].payments[0]).toMatchObject({ paymentMethodId: "10000000-0000-0000-0000-000000000005",
    paymentMethodVersion: 1, paymentMethodCode: "SECOND-DEBIT", accountId: "account-second-debit", approvalNumber: "NATIVE-SECOND" });
});
