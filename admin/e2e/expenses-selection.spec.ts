import { expect, test } from "@playwright/test";

const tenantId = "11111111-1111-1111-1111-111111111111";
const businessId = "22222222-2222-2222-2222-222222222222";
const supplierId = "44444444-4444-4444-4444-444444444444";
const conceptId = "55555555-5555-5555-5555-555555555555";

test("gastos: seleccionar proveedor y concepto conserva la selección y envía sus IDs", async ({ page }) => {
  const user = { userId: "33333333-3333-3333-3333-333333333333", tenantId,
    tenantKey: "@expenses-test", username: "expenses-test", firstName: "Prueba", lastName: "Gastos",
    roles: [], permissions: ["expenses.read", "expenses.create", "expenses.configure", "suppliers.read"] };
  await page.context().addCookies([{ name: "auth_token", value: "expenses-test",
    url: process.env.AURALY_E2E_BASE_URL ?? "http://127.0.0.1:3000", httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ user, businessId }) => {
    localStorage.setItem("selected_tenant_id", user.tenantId);
    localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
  }, { user, businessId });
  let confirmation: Record<string, unknown> | undefined;
  let supplierQueries = 0;
  const supplierSearches: string[] = [];
  await page.route("**/api/**", async route => {
    const url = new URL(route.request().url());
    const path = url.pathname;
    let body: unknown = [];
    if (path === "/api/auth/me") body = user;
    else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Pruebas" }];
    else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Sede pruebas" }];
    else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, roles: [], permissions: user.permissions };
    else if (path.endsWith("/expenses/options")) body = {
      concepts: [{ conceptId, businessId, code: "DOM", name: "Domicilios", expenseAccountId: "account", expenseAccountCode: "513550", expenseAccountName: "Transporte", defaultCostCenterId: null, defaultCostCenterName: null, withholdingConceptCode: null, isActive: true }],
      suppliers: [], expenseAccounts: [{ accountId: "account", code: "513550", name: "Transporte" }], costCenters: [],
      purchaseEvidenceTypes: [{ code: "SupplierElectronicInvoice", label: "Factura electrónica", description: "Factura del proveedor" }],
    };
    else if (path.endsWith("/expenses/confirm")) { confirmation = route.request().postDataJSON(); body = { expenseId: confirmation?.expenseId }; }
    else if (path.endsWith("/expenses")) body = { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0, grossTotal: 0, withholdingTotal: 0, netPayableTotal: 0 };
    else if (path.endsWith("/parties/role-options")) {
      supplierQueries++;
      supplierSearches.push(url.searchParams.get("search") ?? "");
      expect(url.searchParams.get("role")).toBe("Supplier");
      body = { items: [{ role: "Supplier", roleId: supplierId, partyId: supplierId, displayName: "Domiciliario de prueba", identification: "PRUEBA-001" }], page: 1, totalPages: 1, totalCount: 1 };
    }
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
  });
  await page.goto("/dashboard/expenses");
  await page.getByRole("button", { name: "Nuevo gasto" }).click();
  const dialog = page.getByRole("dialog", { name: "Registrar gasto" });
  const supplier = dialog.getByRole("combobox", { name: "Seleccionar supplier" });
  await supplier.click();
  const search = page.getByPlaceholder("Buscar por nombre o identificación…");
  await search.fill("Domici");
  await expect(search).toHaveValue("Domici");
  await expect.poll(() => supplierSearches).toContain("Domici");
  await page.getByRole("option", { name: /Domiciliario de prueba/ }).click();
  await expect(supplier).toContainText("Domiciliario de prueba");
  await expect(supplier).toHaveAttribute("aria-expanded", "false");
  await dialog.getByRole("combobox").filter({ hasText: "Selecciona" }).click();
  await page.getByRole("option", { name: "Domicilios", exact: true }).click();
  await expect(dialog).toContainText("513550 · Transporte");
  await dialog.getByText("Número de factura electrónica", { exact: true }).locator("..").locator("input").fill("PRUEBA-001");
  await dialog.getByText("Base antes de IVA", { exact: true }).locator("..").locator("input").fill("5000");
  await dialog.getByRole("button", { name: "Confirmar gasto" }).click();
  await expect(dialog).not.toBeVisible();
  expect(confirmation).toMatchObject({ supplierId, conceptId, businessId, taxExclusiveAmount: 5000 });
  expect(supplierQueries).toBeGreaterThanOrEqual(2);
});

test("gastos: el tipo de documento respeta la política fiscal del proveedor", async ({ page }) => {
  const user = { userId: "33333333-3333-3333-3333-333333333333", tenantId,
    tenantKey: "@expenses-test", username: "expenses-test", firstName: "Prueba", lastName: "Gastos",
    roles: [], permissions: ["expenses.read", "expenses.create", "suppliers.read"] };
  await page.context().addCookies([{ name: "auth_token", value: "expenses-test",
    url: process.env.AURALY_E2E_BASE_URL ?? "http://127.0.0.1:3000", httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ user, businessId }) => {
    localStorage.setItem("selected_tenant_id", user.tenantId);
    localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
  }, { user, businessId });
  let evidenceType: string | undefined;
  await page.route("**/api/**", async route => {
    const path = new URL(route.request().url()).pathname;
    let body: unknown = [];
    if (path === "/api/auth/me") body = user;
    else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Pruebas" }];
    else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Sede pruebas" }];
    else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, roles: [], permissions: user.permissions };
    else if (path.endsWith("/expenses/options")) body = {
      concepts: [{ conceptId, businessId, code: "DOM", name: "Domicilios", expenseAccountId: "account",
        expenseAccountCode: "513550", expenseAccountName: "Transporte", defaultCostCenterId: null,
        defaultCostCenterName: null, withholdingConceptCode: null, isActive: true }],
      suppliers: [], expenseAccounts: [], costCenters: [], purchaseEvidenceTypes: [
        { code: "SupplierElectronicInvoice", label: "Factura electrónica" },
        { code: "BuyerElectronicSupportDocument", label: "Documento soporte" },
        { code: "InternalReceiptVoucher", label: "Comprobante interno" },
      ],
    };
    else if (path.endsWith("/expenses/confirm")) {
      evidenceType = (route.request().postDataJSON() as { purchaseEvidenceType: string }).purchaseEvidenceType;
      body = { expenseId: "66666666-6666-6666-6666-666666666666" };
    } else if (path.endsWith("/expenses")) body = { items: [], page: 1, pageSize: 25,
      totalCount: 0, totalPages: 0, grossTotal: 0, withholdingTotal: 0, netPayableTotal: 0 };
    else if (path.endsWith("/parties/role-options")) body = { items: [{ role: "Supplier", roleId: supplierId,
      partyId: supplierId, displayName: "Proveedor QA Codex", identification: "PRUEBA-002",
      supplierPurchaseEvidencePolicy: "InternalReceiptVoucher" }], page: 1, totalPages: 1, totalCount: 1 };
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
  });
  await page.goto("/dashboard/expenses");
  await page.getByRole("button", { name: "Nuevo gasto" }).click();
  const dialog = page.getByRole("dialog", { name: "Registrar gasto" });
  await dialog.getByRole("combobox", { name: "Seleccionar supplier" }).click();
  await page.getByRole("option", { name: /Proveedor QA Codex/ }).click();
  await expect(dialog.getByRole("combobox").filter({ hasText: "Comprobante interno" })).toBeVisible();
  await dialog.getByText("Concepto", { exact: true }).locator("..").getByRole("combobox").click();
  await page.getByRole("option", { name: "Domicilios", exact: true }).click();
  await dialog.getByRole("combobox").filter({ hasText: "Comprobante interno" }).click();
  await expect(page.getByRole("option", { name: "Documento soporte" })).toHaveCount(0);
  await page.getByRole("option", { name: "Comprobante interno" }).click();
  await dialog.getByText("Base antes de IVA", { exact: true }).locator("..").locator("input").fill("1000");
  await dialog.getByRole("button", { name: "Confirmar gasto" }).click();
  await expect(dialog).not.toBeVisible();
  expect(evidenceType).toBe("InternalReceiptVoucher");
});
