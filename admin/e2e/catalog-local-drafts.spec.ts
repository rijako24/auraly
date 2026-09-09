import { expect, test, type Locator, type Page, type Route } from "@playwright/test";

const tenantId = "11111111-1111-1111-1111-111111111111";
const businessId = "22222222-2222-2222-2222-222222222222";
const userId = "33333333-3333-3333-3333-333333333333";
const productId = "44444444-4444-4444-4444-444444444444";
const partyId = "55555555-5555-5555-5555-555555555555";
const taxProfileId = "66666666-6666-6666-6666-666666666666";
const permissions = ["dashboard.read", "products.read", "products.create", "products.update", "parties.read", "parties.update", "users.create"];

test.use({ serviceWorkers: "block" });

const json = (route: Route, body: unknown) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });

async function authenticate(page: Page) {
  await page.context().addCookies([{ name: "auth_token", value: "e2e", url: "http://127.0.0.1:3000", httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ tenant, business, user, granted }) => {
    localStorage.setItem("selected_tenant_id", tenant);
    localStorage.setItem("selected_business_id", business);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user: { userId: user, tenantId: tenant, tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test", firstName: "Prueba", lastName: "E2E", avatarUrl: null, roles: ["Administrator"], permissions: granted } }, version: 0 }));
  }, { tenant: tenantId, business: businessId, user: userId, granted: permissions });
}

async function mockApi(page: Page) {
  await page.route("**/api/**", route => {
    const url = new URL(route.request().url());
    const path = url.pathname;
    if (path.endsWith("/auth/me")) return json(route, { userId, tenantId, tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test", firstName: "Prueba", lastName: "E2E", avatarUrl: null, roles: ["Administrator"], permissions });
    if (path.endsWith("/execution-context/tenants")) return json(route, [{ tenantId, name: "Auraly" }]);
    if (path.endsWith("/execution-context/businesses")) return json(route, [{ tenantId, businessId, name: "Auraly" }]);
    if (path.endsWith("/execution-context/access")) return json(route, { tenantId, businessId, roles: ["Administrator"], permissions });
    if (path.endsWith(`/businesses/${businessId}/products`)) return json(route, { items: [{ productId, businessId, productCode: "PRD-001", sku: "REF-ORIGINAL", reference: "REF-ORIGINAL", name: "Producto base", description: "Producto para prueba", unitPrice: 12_000, currency: "COP", manageStock: true, isActive: true }], page: 1, pageSize: 25, totalCount: 1, totalPages: 1 });
    if (path.endsWith(`/businesses/${businessId}/product-categories`)) return json(route, []);
    if (path.endsWith("/commerce/v1/product-brands")) return json(route, []);
    if (path.endsWith("/commerce/v1/product-units")) return json(route, [{ productUnitId: "unit-1", code: "EA", name: "Unidad", symbol: "und", allowsFractionalQuantity: false, decimalPlaces: 0, isActive: true }]);
    if (path.endsWith("/commerce/v1/tax-profiles")) return json(route, [{ taxProfileId, businessId, code: "IVA19", dianTaxCode: "01", name: "IVA 19 %", rate: 19, isActive: true }]);
    if (path.endsWith(`/commerce/v1/products/${productId}/merchandising`)) return json(route, { productId, productCategoryId: null, productBrandId: null, baseUnitCode: "EA", unitGrossWeightKg: 0.75, manageInventory: true, allowsFractionalSale: false, isWeighable: false, scale: null, barcodes: [], link: null, linkedProducts: [], conversionMaximumLossPercent: null });
    if (path.endsWith(`/commerce/v1/products/${productId}/tax-configuration`)) return json(route, { productId, salesTaxProfileId: taxProfileId, purchaseTaxProfileId: taxProfileId, purchaseTaxTreatment: "DeductibleInputVat" });
    if (path.endsWith(`/commerce/v1/pricing/products/${productId}/context`)) return json(route, { productId, productName: "Producto base", preparedSalePrice: 12_000, publicSalePrice: 12_000, costBasisAmount: 10_000, costBasisOrigin: "Manual", currentMarginPercent: 20, salesTaxRate: 19, roundingIncrement: 1, roundingMode: "Nearest" });
    if (path.endsWith(`/commerce/v1/products/${productId}`)) return json(route, { productId, businessId, productCode: "PRD-001", reference: "REF-ORIGINAL", name: "Producto base", description: "Producto para prueba", isActive: true, barcodes: [], prices: [{ amount: 12_000, currencyCode: "COP", costBasisAmount: 10_000, targetMarginPercent: 20 }], suppliers: [], salesTaxProfileId: taxProfileId, purchaseTaxProfileId: taxProfileId, purchaseTaxTreatment: "DeductibleInputVat", baseUnitCode: "EA", manageInventory: true, isWeighable: false, unitGrossWeightKg: 0.75 });
    if (path.includes(`/businesses/${businessId}/products/${productId}/images`)) return json(route, []);
    if (path.includes(`/businesses/${businessId}/products/${productId}/aliases`) || path.includes(`/businesses/${businessId}/products/${productId}/search-terms`)) return json(route, []);
    if (path.endsWith("/commerce/v1/parties")) return json(route, { items: [{ partyId, partyType: "NaturalPerson", identificationTypeCode: "CC", identification: "100000001", verificationDigit: null, displayName: "Tercero base", legalName: null, firstName: "Tercero", lastName: "Base", email: "tercero@example.test", phone: "3000000000", roles: ["Seller"], primarySiteName: null, cityName: null, isActive: true, completionStatus: "Complete", rowVersion: "AAAA", customerId: null, supplierId: null, sellerId: "seller-1", carrierId: null, employeeId: null, userId: null, supplierPurchaseEvidencePolicy: null, supplierDefaultPaymentDueDays: null }], page: 1, pageSize: 25, totalCount: 1, totalPages: 1 });
    if (path.endsWith(`/commerce/v1/parties/${partyId}`)) return json(route, { partyId, partyType: "NaturalPerson", identificationCountryId: null, identificationTypeCode: "CC", identification: "100000001", verificationDigit: null, displayName: "Tercero base", legalName: null, firstName: "Tercero", lastName: "Base", email: "tercero@example.test", phone: "3000000000", roles: ["Seller"], primarySite: null, sites: [], customer: null, supplier: null, seller: { sellerId: "seller-1", code: "VEN-1", defaultCommissionPercent: 5, commissionBasis: "SaleAfterTax", commissionTrigger: "Sale", isActive: true }, carrier: null, employee: null, user: null, rowVersion: "AAAA" });
    if (path.endsWith(`/commerce/v1/parties/${partyId}/seller-access`)) return json(route, null);
    if (path.endsWith("/roles")) return json(route, { items: [{ roleId: "role-1", name: "Administrador", description: "Acceso", isActive: true }], page: 1, pageSize: 500, totalCount: 1, totalPages: 1 });
    if (path.includes("/commerce/v1/reference-options/")) return json(route, []);
    if (path.endsWith("/commerce/v1/masters/geography/countries")) return json(route, []);
    return json(route, []);
  });
}

type StoredDraft = { key: string; value: Record<string, unknown> };
const pixel = {
  name: "producto-local.png",
  mimeType: "image/png",
  buffer: Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=", "base64"),
};

async function catalogDrafts(page: Page) {
  return page.evaluate(async () => {
    const database = await new Promise<IDBDatabase>((resolve, reject) => {
      const request = indexedDB.open("auraly-catalog-work", 1);
      request.onupgradeneeded = () => request.result.createObjectStore("catalog-drafts", { keyPath: "key" });
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
    const drafts = await new Promise<StoredDraft[]>((resolve, reject) => {
      const request = database.transaction("catalog-drafts", "readonly").objectStore("catalog-drafts").getAll();
      request.onsuccess = () => resolve(request.result as StoredDraft[]);
      request.onerror = () => reject(request.error);
    });
    database.close();
    return drafts;
  });
}

async function waitForDraft(page: Page, prefix: string) {
  await expect.poll(async () => (await catalogDrafts(page)).some(item => item.key.startsWith(prefix))).toBe(true);
  return (await catalogDrafts(page)).find(item => item.key.startsWith(prefix))!;
}

async function expectDraftRemoved(page: Page, prefix: string) {
  await expect.poll(async () => (await catalogDrafts(page)).some(item => item.key.startsWith(prefix))).toBe(false);
}

function field(scope: Locator, label: string) {
  return scope.getByText(label, { exact: true }).locator("..");
}

test("producto recupera creación y edición; cancelar elimina el borrador", async ({ page }) => {
  test.setTimeout(150_000);
  await mockApi(page);
  await authenticate(page);
  await page.goto("/dashboard/products");

  await page.getByRole("button", { name: "Nuevo producto" }).click();
  let dialog = page.getByRole("dialog", { name: "Crear producto" });
  await dialog.getByPlaceholder("Nombre claro para venta y búsqueda").fill("Producto recuperable local");
  await dialog.getByPlaceholder("Referencia del fabricante").fill("LOCAL-CREATE-01");
  await dialog.getByLabel("Peso del producto en kilogramos").fill("1.275");
  await dialog.locator('input[type="file"]').setInputFiles(pixel);
  const created = await waitForDraft(page, "product-create:");
  expect(JSON.stringify(created.value)).not.toContain("createdAt");

  await dialog.getByRole("button", { name: "Close", exact: true }).click();
  await expect(dialog).toBeHidden();
  await page.getByRole("button", { name: "Nuevo producto" }).click();
  dialog = page.getByRole("dialog", { name: "Crear producto" });
  await expect(dialog.getByPlaceholder("Nombre claro para venta y búsqueda")).toHaveValue("Producto recuperable local");
  await expect(dialog.getByLabel("Peso del producto en kilogramos")).toHaveValue("1.275");

  await page.reload();
  await page.getByRole("button", { name: "Nuevo producto" }).click();
  dialog = page.getByRole("dialog", { name: "Crear producto" });
  await expect(dialog.getByPlaceholder("Nombre claro para venta y búsqueda")).toHaveValue("Producto recuperable local");
  await expect(dialog.getByPlaceholder("Referencia del fabricante")).toHaveValue("LOCAL-CREATE-01");
  await expect(dialog.getByLabel("Peso del producto en kilogramos")).toHaveValue("1.275");
  await expect(dialog.locator('img[alt="producto-local.png"]').first()).toBeVisible();
  await dialog.getByRole("button", { name: "Cancelar" }).click();
  await expectDraftRemoved(page, "product-create:");

  const firstEdit = page.getByRole("button", { name: "Editar" }).first();
  await expect(firstEdit).toBeVisible({ timeout: 20_000 });
  await firstEdit.click();
  dialog = page.getByRole("dialog");
  const reference = dialog.locator("#product-reference");
  const originalReference = await reference.inputValue();
  await reference.fill("LOCAL-EDIT-01");
  await dialog.locator('input[type="file"]').setInputFiles(pixel);
  await waitForDraft(page, "product-edit:");

  await dialog.getByRole("button", { name: "Close", exact: true }).click();
  await page.getByRole("button", { name: "Editar" }).first().click();
  dialog = page.getByRole("dialog");
  await expect(dialog.locator("#product-reference")).toHaveValue("LOCAL-EDIT-01");

  await page.reload();
  await page.getByRole("button", { name: "Editar" }).first().click();
  dialog = page.getByRole("dialog");
  await expect(dialog.locator("#product-reference")).toHaveValue("LOCAL-EDIT-01");
  await expect(dialog.locator('img[alt="producto-local.png"]').first()).toBeVisible();
  await dialog.getByRole("button", { name: "Cancelar" }).click();
  await expectDraftRemoved(page, "product-edit:");
  await page.getByRole("button", { name: "Editar" }).first().click();
  await expect(page.getByRole("dialog").locator("#product-reference")).toHaveValue(originalReference);
  await page.getByRole("dialog").getByRole("button", { name: "Cancelar" }).click();
});

test("tercero recupera creación y edición sin persistir claves", async ({ page }) => {
  test.setTimeout(150_000);
  await mockApi(page);
  await authenticate(page);
  await page.goto("/dashboard/parties");

  await page.getByRole("button", { name: "Nuevo tercero" }).click();
  let dialog = page.getByRole("dialog");
  await dialog.getByRole("button", { name: /^Usuario/ }).click();
  await field(dialog, "Nombre visible").locator("input").fill("Usuario recuperable local");
  await field(dialog, "Contraseña de acceso y modo sin conexión POS").locator("input").fill("ClaveTemporal123!");
  const created = await waitForDraft(page, "party-create:");
  const serialized = JSON.stringify(created.value);
  expect(serialized).not.toContain("ClaveTemporal123!");
  expect(serialized).not.toContain("createdAt");

  await dialog.getByRole("button", { name: "Close", exact: true }).click();
  await page.getByRole("button", { name: "Nuevo tercero" }).click();
  dialog = page.getByRole("dialog");
  await expect(field(dialog, "Nombre visible").locator("input")).toHaveValue("Usuario recuperable local");

  await page.reload();
  await page.getByRole("button", { name: "Nuevo tercero" }).click();
  dialog = page.getByRole("dialog");
  await expect(field(dialog, "Nombre visible").locator("input")).toHaveValue("Usuario recuperable local");
  await expect(field(dialog, "Contraseña de acceso y modo sin conexión POS").locator("input")).toHaveValue("");
  await dialog.getByRole("button", { name: "Cancelar" }).click();
  await expectDraftRemoved(page, "party-create:");

  const firstEdit = page.getByRole("button", { name: "Editar" }).first();
  await expect(firstEdit).toBeVisible({ timeout: 20_000 });
  await firstEdit.click();
  dialog = page.getByRole("dialog");
  const name = field(dialog, "Nombre visible").locator("input");
  const originalName = await name.inputValue();
  await name.fill("Tercero recuperado desde IndexedDB");
  await waitForDraft(page, "party-edit:");

  await dialog.getByRole("button", { name: "Close", exact: true }).click();
  await page.getByRole("button", { name: "Editar" }).first().click();
  dialog = page.getByRole("dialog");
  await expect(field(dialog, "Nombre visible").locator("input")).toHaveValue("Tercero recuperado desde IndexedDB");

  await page.reload();
  await page.getByRole("button", { name: "Editar" }).first().click();
  dialog = page.getByRole("dialog");
  await expect(field(dialog, "Nombre visible").locator("input")).toHaveValue("Tercero recuperado desde IndexedDB");
  await dialog.getByRole("button", { name: "Cancelar" }).click();
  await expectDraftRemoved(page, "party-edit:");
  await dialog.getByRole("button", { name: "Cerrar" }).click();
  await page.getByRole("button", { name: "Editar" }).first().click();
  await expect(field(page.getByRole("dialog"), "Nombre visible").locator("input")).toHaveValue(originalName);
  await page.getByRole("dialog").getByRole("button", { name: "Cancelar" }).click();
});
