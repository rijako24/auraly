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
    };
    else if (path.endsWith("/expenses/confirm")) { confirmation = route.request().postDataJSON(); body = { expenseId: confirmation?.expenseId }; }
    else if (path.endsWith("/expenses")) body = { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0, grossTotal: 0, withholdingTotal: 0, netPayableTotal: 0 };
    else if (path.endsWith("/parties/role-options")) {
      supplierQueries++;
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
  await page.getByRole("option", { name: /Domiciliario de prueba/ }).click();
  await expect(supplier).toContainText("Domiciliario de prueba");
  await expect(supplier).toHaveAttribute("aria-expanded", "false");
  await dialog.getByRole("combobox").filter({ hasText: "Selecciona" }).click();
  await page.getByRole("option", { name: "Domicilios", exact: true }).click();
  await expect(dialog).toContainText("513550 · Transporte");
  await dialog.getByLabel("Factura o soporte", { exact: true }).fill("PRUEBA-001");
  await dialog.getByLabel("Base antes de IVA", { exact: true }).fill("5000");
  await dialog.getByRole("button", { name: "Confirmar gasto" }).click();
  await expect(dialog).not.toBeVisible();
  expect(confirmation).toMatchObject({ supplierId, conceptId, businessId, taxExclusiveAmount: 5000 });
  expect(supplierQueries).toBe(1);
});
