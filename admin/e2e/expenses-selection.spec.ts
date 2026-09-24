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

test("gastos: filtros, origen, saldo y pago abren una sola ventana", async ({ page }) => {
  const expenseId = "66666666-6666-6666-6666-666666666666";
  const payableId = "77777777-7777-7777-7777-777777777777";
  const invoiceId = "88888888-8888-8888-8888-888888888888";
  const user = { userId: "33333333-3333-3333-3333-333333333333", tenantId,
    tenantKey: "@expenses-test", username: "expenses-test", firstName: "Prueba", lastName: "Gastos",
    roles: [], permissions: ["expenses.read", "expenses.cancel", "payables.payments.create", "suppliers.read"] };
  await page.context().addCookies([{ name: "auth_token", value: "expenses-test",
    url: process.env.AURALY_E2E_BASE_URL ?? "http://127.0.0.1:3000", httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ user, businessId }) => {
    localStorage.setItem("selected_tenant_id", user.tenantId);
    localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
  }, { user, businessId });
  const expense = { expenseId, documentNumber: "GTO00-00000020", supplierDocumentNumber: null,
    supplierId, supplierName: "ANDERSON ATENCIO", conceptId, conceptName: "Transporte y mensajería",
    issuedAt: "2026-09-22T12:00:00-05:00", dueDate: "2026-09-22T12:00:00-05:00",
    grossAmount: 5000, withholdingAmount: 0, netPayable: 5000, currencyCode: "COP",
    status: "Processed", evidenceUrl: null, purchaseEvidenceType: "InternalReceiptVoucher",
    payableStatus: "Open", outstandingAmount: 5000, chargeReturned: false };
  const payable = { payableId, supplierId, supplierName: expense.supplierName,
    documentNumber: expense.documentNumber, currencyCode: "COP", originalAmount: 5000,
    outstandingAmount: 5000, dueDate: expense.dueDate, status: "Open", isOverdue: false,
    createdAt: expense.issuedAt };
  const expenseQueries: URL[] = [];
  await page.route("**/api/**", async route => {
    const url = new URL(route.request().url());
    const path = url.pathname;
    let body: unknown = [];
    if (path === "/api/auth/me") body = user;
    else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Pruebas" }];
    else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Sede pruebas" }];
    else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, roles: [], permissions: user.permissions };
    else if (path.endsWith("/expenses/options")) body = { concepts: [], suppliers: [], expenseAccounts: [], costCenters: [], purchaseEvidenceTypes: [] };
    else if (path.endsWith(`/expenses/${expenseId}`)) body = { ...expense,
      description: "Domicilios agotados", taxExclusiveAmount: 5000, vatAmount: 0,
      fiscalNumber: null, fiscalStatus: null, payable: { payableId, status: "Open", originalAmount: 5000, outstandingAmount: 5000 },
      cancellationId: null, cancellationReason: null, adjustmentFiscalNumber: null, adjustmentFiscalStatus: null,
      sourceInvoiceId: invoiceId, sourceInvoiceNumber: "VTA00-00000190" };
    else if (path.endsWith("/expenses")) {
      expenseQueries.push(url);
      body = { items: [expense], page: 1, pageSize: 25, totalCount: 1, totalPages: 1,
        grossTotal: 5000, withholdingTotal: 0, netPayableTotal: 5000 };
    } else if (path.endsWith("/payables")) body = { items: [payable], page: 1, pageSize: 20,
      totalCount: 1, totalPages: 1, totalOutstanding: 5000, totalOverdue: 0 };
    else if (path.endsWith("/parties/role-options")) body = { items: [{ role: "Supplier", roleId: supplierId,
      partyId: supplierId, displayName: expense.supplierName, identification: "PRUEBA-003" }],
      page: 1, totalPages: 1, totalCount: 1 };
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
  });
  await page.goto("/dashboard/expenses");
  const expenseRow = page.getByRole("button", { name: "Ver gasto GTO00-00000020" });
  await expect(expenseRow).toContainText("$ 5.000");
  await expect(expenseRow).toContainText("Abierto");
  await page.getByRole("button", { name: "Filtros" }).click();
  await expect(page.getByRole("button", { name: "Filtros" })).toHaveAttribute("aria-expanded", "true");
  await expect(page.locator('input[type="date"]')).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Fecha inicial" })).toBeVisible();
  await page.getByRole("combobox", { name: "Seleccionar supplier" }).click();
  await page.getByRole("option", { name: /ANDERSON ATENCIO/ }).click();
  await expect(page.getByRole("button", { name: "Filtros 1" })).toBeVisible();
  await page.getByRole("button", { name: "Quitar selección de Seleccionar supplier" }).click();
  await expect(page.getByRole("button", { name: "Filtros" })).toBeVisible();
  await page.getByText("Estado de la cuenta por pagar").locator("..").getByRole("combobox").click();
  await page.getByRole("option", { name: "Pagada", exact: true }).click();
  await expect.poll(() => expenseQueries.some(url => url.searchParams.get("payableStatus") === "Paid")).toBe(true);
  await page.getByRole("button", { name: "Limpiar" }).click();
  await expect.poll(() => expenseQueries.at(-1)?.searchParams.get("payableStatus")).toBe(null);
  await expenseRow.click();
  const detail = page.getByRole("dialog", { name: "GTO00-00000020" });
  await expect(detail).toContainText("Cargo de la factura de venta VTA00-00000190");
  await expect(detail.getByRole("button", { name: "Anular gasto" })).toBeVisible();
  await detail.getByRole("button", { name: "Pagar" }).click();
  await expect(detail).not.toBeVisible();
  const payment = page.getByRole("dialog", { name: "Pago a proveedores" });
  await expect(payment).toBeVisible();
  await expect(page.getByRole("dialog")).toHaveCount(1);
  await expect(payment.getByText("GTO00-00000020")).toBeVisible();
  await expect(payment.getByLabel("Abono para GTO00-00000020")).toHaveValue("5 000");
  await payment.getByRole("button", { name: "Ir a pagar" }).click();
  await expect(page.getByRole("dialog", { name: "Pago a proveedores" })).toBeVisible();
  await expect(page.getByRole("dialog")).toHaveCount(1);
});
