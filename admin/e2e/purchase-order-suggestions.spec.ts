import { expect, test, type Page, type Route } from "@playwright/test";

const tenantId = "11111111-1111-1111-1111-111111111111";
const businessId = "22222222-2222-2222-2222-222222222222";
const userId = "33333333-3333-3333-3333-333333333333";
const supplierId = "44444444-4444-4444-4444-444444444444";
const warehouseId = "55555555-5555-5555-5555-555555555555";
const productId = "66666666-6666-6666-6666-666666666666";
const permissions = ["dashboard.read", "purchasing.purchase-orders.read", "purchasing.purchase-orders.create"];

const json = (route: Route, body: unknown) => route.fulfill({
  status: 200,
  contentType: "application/json",
  body: JSON.stringify(body),
});

async function authenticate(page: Page) {
  await page.context().addCookies([{
    name: "auth_token",
    value: "e2e",
    url: process.env.AURALY_E2E_BASE_URL ?? "http://127.0.0.1:3000",
    httpOnly: true,
    sameSite: "Lax",
  }]);
  await page.addInitScript(({ tenant, business, user, granted }) => {
    localStorage.setItem("selected_tenant_id", tenant);
    localStorage.setItem("selected_business_id", business);
    localStorage.setItem("auth-state", JSON.stringify({ state: {
      isAuthenticated: true,
      user: { userId: user, tenantId: tenant, tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test", firstName: "Prueba", lastName: "E2E", avatarUrl: null, roles: ["Administrator"], permissions: granted },
    }, version: 0 }));
  }, { tenant: tenantId, business: businessId, user: userId, granted: permissions });
}

test("la orden precarga y explica el sugerido semanal y permite recalcularlo", async ({ page }) => {
  const partyRequests: URL[] = [];
  const productSearches: string[] = [];
  await page.route("**/api/auth/me", route => json(route, { userId, tenantId, tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test", firstName: "Prueba", lastName: "E2E", avatarUrl: null, roles: ["Administrator"], permissions }));
  await page.route("**/api/execution-context/tenants", route => json(route, [{ tenantId, name: "Auraly" }]));
  await page.route("**/api/execution-context/businesses", route => json(route, [{ tenantId, businessId, name: "Auraly" }]));
  await page.route("**/api/execution-context/access", route => json(route, { tenantId, businessId, roles: ["Administrator"], permissions }));
  await page.route("**/api/commerce/v1/purchase-orders?**", route => json(route, { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 }));
  await page.route("**/api/commerce/v1/goods-receipts/options", route => json(route, {
    warehouses: [{ warehouseId, code: "PPL", name: "Principal" }],
    suppliers: [], purchaseEvidenceTypes: [], withholdingConcepts: [], withholdingJurisdictions: [],
  }));
  await page.route("**/api/commerce/v1/parties?**", route => {
    partyRequests.push(new URL(route.request().url()));
    return json(route, {
    items: [{ partyId: supplierId, supplierId, displayName: "Proveedor Andino", identification: "900100200", email: null, roles: ["Supplier"], isActive: true }],
    page: 1, pageSize: 10, totalCount: 1, totalPages: 1,
    });
  });
  await page.route("**/api/commerce/v1/goods-receipts/products?**", route => {
    productSearches.push(new URL(route.request().url()).searchParams.get("search") ?? "");
    return json(route, {
    items: [{ productId, productCode: "PRD-001", reference: null, name: "Café tostado", supplierProductCode: null, latestUnitCost: 20_000, averageUnitCost: 19_000, taxCode: "01", taxRate: 19, taxTreatment: "DeductibleInputVat", barcodes: [], baseUnitCode: "94", isAssociated: true, purchasePresentationName: "Caja", unitsPerPresentation: 6, isPrimary: true }],
    page: 1, pageSize: 50, totalCount: 1, totalPages: 1,
    });
  });
  await page.route("**/api/commerce/v1/purchase-orders/suggestions", async route => {
    const request = route.request().postDataJSON() as { targetCoverageDays: number };
    const presentations = request.targetCoverageDays === 7 ? 2 : 4;
    return json(route, [{ productId, targetCoverageDays: request.targetCoverageDays, rotation30Days: 60, rotation90Days: 120, dailyDemand90Days: 1.33, forecastDailyDemand: 1.8, currentStock: 1, incomingQuantity: 0, presentationName: "Caja", unitsPerPresentation: 6, suggestedQuantity: presentations * 6, suggestedPresentationQuantity: presentations, rotationCalculatedAt: "2026-08-31T12:00:00Z" }]);
  });

  await authenticate(page);
  await page.goto("/dashboard/purchasing/purchase-orders");
  await page.getByRole("button", { name: "Nueva orden" }).click();
  const dialog = page.getByRole("dialog", { name: "Nueva orden de compra" });

  await expect(dialog.locator('input[type="datetime-local"]')).toHaveCount(0);
  expect(partyRequests).toHaveLength(0);
  await dialog.getByRole("combobox", { name: "Seleccionar supplier" }).click();
  await page.getByRole("option", { name: /Proveedor Andino/ }).click();
  expect(partyRequests).toHaveLength(1);
  expect(partyRequests[0].searchParams.get("role")).toBe("Supplier");
  expect(partyRequests[0].searchParams.get("pageSize")).toBe("10");
  await dialog.getByText("Bodega", { exact: true }).locator("..").getByRole("combobox").click();
  await page.getByRole("option", { name: /Principal/ }).click();
  await dialog.getByTestId("supplier-product-picker-search").click();
  await expect(dialog.getByText("Escribe al menos una letra, código o referencia para buscar.")).toBeVisible();
  await expect.poll(() => productSearches).toEqual([]);
  await dialog.getByTestId("supplier-product-picker-search").fill("C");
  await page.getByRole("option", { name: /Café tostado/ }).click();

  const quantity = dialog.locator("tbody tr").first().locator('input[type="number"]').first();
  await expect(quantity).toHaveValue("2");
  await dialog.getByText("Ver sugerido", { exact: true }).click();
  await expect(dialog.getByText(/rotación de 30 días \(60\).*90 días \(120\)/)).toBeVisible();
  await expect(dialog.locator("tbody tr").first().locator("td").nth(5).locator("input")).toHaveValue(/20\s000/);
  expect(await dialog.getByTestId("purchase-order-lines").evaluate(
    element => element.scrollWidth <= element.clientWidth,
  )).toBe(true);

  await dialog.getByText("Días sugeridos", { exact: true }).locator("..").locator("input").fill("14");
  await expect(dialog.getByText("Sugerido · 14 días")).toBeVisible();
  await expect(quantity).toHaveValue("2");
  await dialog.getByRole("button", { name: "Aplicar todos los sugeridos" }).click();
  await expect(quantity).toHaveValue("4");
  await expect.poll(() => page.evaluate(({ key }) => {
    const stored = localStorage.getItem(key);
    return stored ? (JSON.parse(stored) as { lines: unknown[] }).lines.length : 0;
  }, { key: `auraly.purchase-order.entry.${businessId}` })).toBe(1);

  await dialog.getByRole("button", { name: "Cerrar", exact: true }).click();
  await page.getByRole("button", { name: "Nueva orden" }).click();
  const restored = page.getByRole("dialog", { name: "Nueva orden de compra" });
  await expect(restored.locator("tbody tr").first()).toContainText("Café tostado");
  await expect(restored.locator("tbody tr").first().locator('input[type="number"]')).toHaveValue("4");
});
