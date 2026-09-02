import { expect, test, type Page, type Route } from "@playwright/test";

const tenantId = "11111111-1111-1111-1111-111111111111";
const businessId = "22222222-2222-2222-2222-222222222222";
const userId = "33333333-3333-3333-3333-333333333333";
const supplierId = "44444444-4444-4444-4444-444444444444";
const permissions = ["dashboard.read", "purchasing.goods-receipts.read", "purchasing.goods-receipts.create", "purchasing.goods-receipts.confirm"];

const json = (route: Route, body: unknown) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });

async function authenticate(page: Page) {
  await page.context().addCookies([{ name: "auth_token", value: "e2e", url: "http://127.0.0.1:3000", httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ tenant, business, user, granted }) => {
    localStorage.setItem("selected_tenant_id", tenant);
    localStorage.setItem("selected_business_id", business);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user: {
      userId: user, tenantId: tenant, tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test",
      firstName: "Prueba", lastName: "E2E", avatarUrl: null, roles: ["Administrator"], permissions: granted,
    } }, version: 0 }));
  }, { tenant: tenantId, business: businessId, user: userId, granted: permissions });
}

test("emisión y vencimiento se pueden seleccionar y conservan el vencimiento propuesto", async ({ page }) => {
  await page.route("**/api/auth/me", route => json(route, { userId, tenantId, tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test", firstName: "Prueba", lastName: "E2E", avatarUrl: null, roles: ["Administrator"], permissions }));
  await page.route("**/api/execution-context/tenants", route => json(route, [{ tenantId, name: "Auraly" }]));
  await page.route("**/api/execution-context/businesses", route => json(route, [{ tenantId, businessId, name: "Auraly" }]));
  await page.route("**/api/execution-context/access", route => json(route, { tenantId, businessId, roles: ["Administrator"], permissions }));
  await page.route("**/api/commerce/v1/goods-receipts?**", route => json(route, { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 }));
  await page.route("**/api/commerce/v1/goods-receipts/options", route => json(route, {
    warehouses: [{ warehouseId: "55555555-5555-5555-5555-555555555555", code: "PPL", name: "Principal" }],
    suppliers: [], purchaseEvidenceTypes: [{ code: "InternalReceiptVoucher", label: "Comprobante interno" }],
    withholdingConcepts: [], withholdingJurisdictions: [],
  }));
  await page.route("**/api/commerce/v1/purchase-orders?**", route => json(route, { items: [], page: 1, pageSize: 100, totalCount: 0, totalPages: 0 }));
  await page.route("**/api/tenant-commercial/subscription", route => json(route, { dianDocumentMonthlyLimit: 100, dianDocumentsUsed: 0 }));
  await page.route("**/api/commerce/v1/parties?**", route => json(route, {
    items: [{ partyId: supplierId, supplierId, displayName: "Proveedor Andino", identification: "900100200", email: null,
      roles: ["Supplier"], isActive: true, supplierPurchaseEvidencePolicy: "InternalReceiptVoucher", supplierDefaultPaymentDueDays: 30 }],
    page: 1, pageSize: 10, totalCount: 1, totalPages: 1,
  }));

  await authenticate(page);
  await page.goto("/dashboard/purchasing/goods-receipts");
  await page.getByRole("button", { name: "Nueva entrada" }).click();
  const dialog = page.getByRole("dialog", { name: /Recepción de compra/ });
  await dialog.getByRole("combobox", { name: "Seleccionar supplier" }).click();
  await page.getByRole("option", { name: /Proveedor Andino/ }).click();

  const issue = dialog.getByText("Fecha de emisión", { exact: true }).locator("..").getByRole("button").first();
  const initialIssue = await issue.textContent();
  await issue.click();
  await page.locator('[data-radix-popper-content-wrapper]:visible button[aria-label*=" de "]:not(:disabled)').first().click();
  await expect(issue).not.toHaveText(initialIssue ?? "");

  const due = dialog.getByText("Fecha de vencimiento", { exact: true }).locator("..").getByRole("button").first();
  const proposedDue = await due.textContent();
  await expect(due).toBeEnabled();
  await due.click();
  await page.locator('[data-radix-popper-content-wrapper]:visible button[aria-label*=" de "]:not(:disabled)').first().click();
  await expect(due).not.toHaveText(proposedDue ?? "");
});
