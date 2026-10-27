import { expect, test, type Page, type Route } from "@playwright/test";

const tenantId = "11111111-1111-1111-1111-111111111111";
const businessId = "22222222-2222-2222-2222-222222222222";
const permissions = ["accounting.read", "accounting.configure", "accounting.activate", "parties.read", "catalog.update", "commerce.taxation.withholdings.view", "commerce.taxation.withholdings.manage"];

async function authenticate(page: Page) {
  await page.context().addCookies([{ name: "auth_token", value: "e2e", url: process.env.AURALY_E2E_BASE_URL ?? "http://127.0.0.1:3000", httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ tenant, business, granted }) => {
    localStorage.setItem("selected_tenant_id", tenant);
    localStorage.setItem("selected_business_id", business);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user: { userId: "33333333-3333-3333-3333-333333333333", tenantId: tenant, tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test", firstName: "Prueba", lastName: "E2E", avatarUrl: null, roles: ["Administrator"], permissions: granted } }, version: 0 }));
  }, { tenant: tenantId, business: businessId, granted: permissions });
}

async function json(route: Route, body: unknown) {
  await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
}

test("concilia una obligación anterior, conserva su identidad y exige guardar antes de aprobar", async ({ page }) => {
  const accountId = "44444444-4444-4444-4444-444444444444";
  const equityId = "55555555-5555-5555-5555-555555555555";
  const partyId = "66666666-6666-6666-6666-666666666666";
  const positionId = "77777777-7777-7777-7777-777777777777";
  let saved: Record<string, unknown> | null = null;
  let openingReads = 0;
  let positionRequest = "";
  await page.route("**/api/auth/me", route => json(route, { userId: "33333333-3333-3333-3333-333333333333", tenantId, username: "e2e", roles: ["Administrator"], permissions }));
  await page.route("**/api/execution-context/tenants", route => json(route, [{ tenantId, name: "Auraly" }]));
  await page.route("**/api/execution-context/businesses", route => json(route, [{ tenantId, businessId, name: "Auraly" }]));
  await page.route("**/api/execution-context/access", route => json(route, { tenantId, businessId, roles: ["Administrator"], permissions }));
  await page.route("**/api/commerce/v1/reference-options**", route => json(route, []));
  await page.route("**/api/commerce/v1/parties**", route => json(route, { items: [
    { partyId, displayName: "Proveedor histórico", identification: "900100200", roles: ["Supplier"], isActive: true },
  ], page: 1, pageSize: 30, totalCount: 1, totalPages: 1 }));
  await page.route("**/api/commerce/v1/accounting/**", async route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith("/accounts")) return json(route, [
      { accountId, code: "22050501", name: "Proveedores", accountType: "Liability", allowsPosting: true, requiresParty: true, isActive: true, semantics: { financialFunction: "Payable" } },
      { accountId: equityId, code: "310505", name: "Capital", accountType: "Equity", allowsPosting: true, requiresParty: false, isActive: true },
    ]);
    if (path.endsWith("/readiness")) return json(route, { status: "Disabled", blockingIssues: [], canEditOpeningBalances: true });
    if (path.endsWith("/opening-balances")) {
      if (route.request().method() === "GET") { openingReads++; return json(route, saved); }
      const request = route.request().postDataJSON();
      saved = { ...request, status: "Draft", rowVersion: "AQID", debitTotal: 1500, creditTotal: 1500,
        lines: request.lines.map((line: Record<string, unknown>, index: number) => ({ ...line, lineNumber: index + 1,
          existingPositionDocumentNumber: line.existingPosition ? "HIST-1500" : null })) };
      return json(route, saved);
    }
    if (path.endsWith("/open-positions")) {
      positionRequest = route.request().url();
      return json(route, { items: [{ subledgerKind: "Payable", subledgerId: positionId, partyId,
        documentNumber: "HIST-1500", partyName: "Proveedor histórico", outstandingAmount: 1500,
        availableAmount: 1500, openingEntryId: null }], page: 1, pageSize: 10, totalCount: 1, totalOutstandingAmount: 1500 });
    }
    return json(route, []);
  });
  await authenticate(page);
  await page.goto("/dashboard/accounting");
  await page.getByRole("button", { name: "Saldos iniciales" }).click();
  const rows = page.locator("tbody tr");
  await rows.nth(0).getByRole("combobox").first().click();
  await page.getByRole("option", { name: /22050501.*Proveedores/ }).click();
  const positionPicker = page.getByRole("combobox", { name: "Vincular obligación anterior en partida 1" });
  await expect(positionPicker).toBeDisabled();
  expect(positionRequest).toBe("");
  await rows.nth(0).getByRole("combobox", { name: "Seleccionar tercero" }).click();
  await page.getByRole("option", { name: /Proveedor histórico/ }).click();
  await positionPicker.click();
  await page.getByRole("option", { name: /HIST-1500/ }).click();
  await expect(rows.nth(0).getByRole("spinbutton").nth(1)).toHaveValue("1500");
  const query = new URL(positionRequest).searchParams;
  expect(query.get("partyId")).toBe(partyId);
  expect(query.get("subledgerKind")).toBe("Payable");
  expect(Number(query.get("pageSize"))).toBeLessThanOrEqual(100);
  await rows.nth(1).getByRole("combobox").first().click();
  await page.getByRole("option", { name: /310505.*Capital/ }).click();
  await rows.nth(1).getByRole("spinbutton").first().fill("1500");
  const approve = page.getByRole("button", { name: "Aprobar saldos" });
  await expect(approve).toBeDisabled();
  await page.getByRole("button", { name: "Guardar borrador" }).click();
  await expect(approve).toBeEnabled();
  expect(openingReads).toBe(1);
  const savedLines = saved!.lines as Array<Record<string, unknown>>;
  expect(savedLines[0].existingPosition).toEqual({ subledgerKind: "Payable", subledgerId: positionId });
  const stableId = savedLines[0].lineId;
  expect(stableId).toEqual(expect.any(String));
  await rows.nth(0).getByRole("textbox").fill("Conciliación revisada");
  await expect(approve).toBeDisabled();
  await page.getByRole("button", { name: "Guardar borrador" }).click();
  await expect(approve).toBeEnabled();
  expect((saved!.lines as Array<Record<string, unknown>>)[0].lineId).toBe(stableId);
  await page.reload();
  await page.getByRole("button", { name: "Saldos iniciales" }).click();
  await expect(page.getByRole("combobox", { name: "Vincular obligación anterior en partida 1" })).toContainText("HIST-1500");
  expect(openingReads).toBe(2);
});

test("saldos iniciales busca cualquier tercero activo sin filtrar por rol", async ({ page }) => {
  let partyRequest = "";
  await page.route("**/api/auth/me", route => json(route, { userId: "33333333-3333-3333-3333-333333333333", tenantId, tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test", firstName: "Prueba", lastName: "E2E", avatarUrl: null, roles: ["Administrator"], permissions }));
  await page.route("**/api/execution-context/tenants", route => json(route, [{ tenantId, name: "Auraly" }]));
  await page.route("**/api/execution-context/businesses", route => json(route, [{ tenantId, businessId, name: "Auraly" }]));
  await page.route("**/api/execution-context/access", route => json(route, { tenantId, businessId, roles: ["Administrator"], permissions }));
  await page.route("**/api/commerce/v1/reference-options**", route => json(route, []));
  await page.route("**/api/commerce/v1/accounting/**", async route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith("/accounts")) return json(route, [
      { accountId: "44444444-4444-4444-4444-444444444444", code: "130505", name: "Cuentas por cobrar", accountType: "Asset", allowsPosting: true, requiresParty: true, isActive: true },
      { accountId: "55555555-5555-5555-5555-555555555555", code: "310505", name: "Capital", accountType: "Equity", allowsPosting: true, requiresParty: false, isActive: true },
    ]);
    if (path.endsWith("/cost-centers")) return json(route, []);
    if (path.endsWith("/periods")) return json(route, []);
    if (path.endsWith("/mappings")) return json(route, []);
    if (path.endsWith("/category-definitions")) return json(route, []);
    if (path.endsWith("/readiness")) return json(route, { status: "Disabled", blockingIssues: ["Configura los saldos iniciales."], effectiveFrom: null, openingBalanceMode: null, canEditOpeningBalances: true });
    if (path.endsWith("/opening-balances")) return json(route, null);
    return json(route, []);
  });
  await page.route("**/api/commerce/v1/parties**", async route => {
    partyRequest = route.request().url();
    return json(route, { items: [
      { partyId: "66666666-6666-6666-6666-666666666666", displayName: "Proveedor Andino", identification: "900100200", email: null, roles: ["Supplier"], isActive: true },
      { partyId: "77777777-7777-7777-7777-777777777777", displayName: "Acreedor Logístico", identification: "901300400", email: null, roles: ["Carrier"], isActive: true },
      { partyId: "88888888-8888-8888-8888-888888888888", displayName: "Cliente Contado", identification: "10203040", email: null, roles: ["Customer"], isActive: true },
    ], page: 1, pageSize: 30, totalCount: 3, totalPages: 1 });
  });

  await authenticate(page);
  await page.goto("/dashboard/accounting");
  await page.getByRole("button", { name: "Saldos iniciales" }).click();
  await page.getByRole("combobox", { name: "Seleccionar tercero" }).first().click();

  await expect(page.getByText("Proveedor Andino")).toBeVisible();
  await expect(page.getByText("Acreedor Logístico")).toBeVisible();
  await expect(page.getByText("Cliente Contado")).toBeVisible();
  const url = new URL(partyRequest);
  expect(url.searchParams.get("role")).toBe("Any");
  expect(Number(url.searchParams.get("pageSize"))).toBeLessThanOrEqual(100);
});

test("limpia el tipo de cuenta bancaria y expone la creación de reglas de retención", async ({ page }) => {
  const bankTypeId = "99999999-9999-9999-9999-999999999999";
  const bankAccountId = "44444444-4444-4444-4444-444444444444";
  let savedBankAccount: Record<string, unknown> | null = null;

  await page.route("**/api/auth/me", route => json(route, { userId: "33333333-3333-3333-3333-333333333333", tenantId, tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test", firstName: "Prueba", lastName: "E2E", avatarUrl: null, roles: ["Administrator"], permissions }));
  await page.route("**/api/execution-context/tenants", route => json(route, [{ tenantId, name: "Auraly" }]));
  await page.route("**/api/execution-context/businesses", route => json(route, [{ tenantId, businessId, name: "Auraly" }]));
  await page.route("**/api/execution-context/access", route => json(route, { tenantId, businessId, roles: ["Administrator"], permissions }));
  await page.route("**/api/commerce/v1/reference-options/**", route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith("/bank-account-type")) return json(route, [{ id: bankTypeId, code: "Savings", label: "Ahorros", description: null, sortOrder: 1 }]);
    if (path.endsWith("/accounting-withholding-kind")) return json(route, [{ id: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", code: "IncomeTax", label: "ReteFuente", description: null, sortOrder: 1 }]);
    return json(route, []);
  });
  await page.route("**/api/commerce/v1/accounting/**", async route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith("/accounts")) return json(route, [{ accountId: bankAccountId, code: "111005", name: "Bancos", accountType: "Asset", allowsPosting: true, requiresParty: false, isActive: true }]);
    if (path.endsWith("/bank-accounts") && route.request().method() === "GET") return json(route, savedBankAccount ? [savedBankAccount] : []);
    if (path.includes("/bank-accounts/") && route.request().method() === "PUT") {
      expect(route.request().postDataJSON()).not.toHaveProperty("accountingAccountId");
      savedBankAccount = { ...route.request().postDataJSON(), accountingAccountId:null, accountingAccountCode:null, accountingAccountName:null, auxiliaryCount:0, accountTypeCode: "Savings", accountTypeName: "Ahorros", currencyCode: "COP", rowVersion: "AQID" };
      return json(route, savedBankAccount);
    }
    if (path.endsWith("/readiness")) return json(route, { status: "Ready", functionalCurrencyCode: "COP", effectiveFrom: "2026-01-01", openingBalanceMode: "ZeroDeclared", activatedAt: "2026-01-01T00:00:00Z", blockingIssues: [], canEditOpeningBalances: false });
    if (path.endsWith("/opening-balances")) return json(route, null);
    return json(route, []);
  });
  let ruleRequests = 0;
  await page.route("**/api/commerce/v1/taxation/withholding-rules**", route => {
    ruleRequests++;
    return json(route, [{ ruleId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", businessId, version: 1, code: "RF-COMPRA", name: "Retefuente compras", kind: "IncomeTax", direction: "Purchase", moment: "Accrual", baseKind: "TaxExclusiveAmount", conceptCode: null, jurisdictionCode: null, rate: 2.5, minimumBase: 100000, requiredResponsibilities: [], effectiveFrom: "2026-01-01", effectiveTo: null, isActive: true }]);
  });
  let appliedRequest = "";
  let appliedRequests = 0;
  await page.route("**/api/commerce/v1/taxation/withholdings/applied?**", route => {
    appliedRequests++;
    appliedRequest = route.request().url();
    return json(route, { from: "2026-10-01", to: "2026-10-31", page: 1, pageSize: 25,
      totalCount: 2, incomeTaxTotal: 500, vatTotal: 0, industryCommerceTotal: 0,
      items: [{ documentId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", documentType: "GoodsReceiptCostDocument",
        lineNumber: 1, recognizedAt: "2026-10-03T12:00:00-05:00", documentNumber: "FLETE-42",
        supplierName: "Transportes Andinos", supplierIdentification: "900100200", kind: "IncomeTax",
        name: "Retefuente flete", ruleCode: "RF-FLETE", jurisdictionCode: null,
        taxableBase: 10000, rate: 2.5, amount: 250, isManual: false,
        documentTypeLabel: "Factura adicional configurada", kindLabel: "Retención configurada" },
        { documentId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaab", documentType: "ManualAccountingVoucher",
        lineNumber: 1, recognizedAt: "2026-10-03T12:00:00-05:00", documentNumber: "DS-43",
        supplierName: "Servicios del tercero", supplierIdentification: "900100201", kind: "IncomeTax",
        name: "Retención del soporte", ruleCode: "RF-SOPORTE", jurisdictionCode: null,
        taxableBase: 10000, rate: 2.5, amount: 250, isManual: false,
        documentTypeLabel: "Soporte manual configurado", kindLabel: "Retención configurada" }] });
  });

  await authenticate(page);
  await page.goto("/dashboard/accounting");
  await expect(page.getByRole("link", { name: "Reglas de retención" })).toHaveCount(0);
  await page.getByRole("button", { name: "Cuentas bancarias" }).click();
  const bankForm = page.getByRole("heading", { name: "Nueva cuenta bancaria" }).locator("../..");
  await bankForm.getByText("Banco", { exact: true }).locator("..").locator("input").fill("Banco E2E");
  await bankForm.getByText("Nombre para mostrar", { exact: true }).locator("..").locator("input").fill("Cuenta principal E2E");
  await bankForm.getByText("Tipo de cuenta", { exact: true }).locator("..").getByRole("combobox").click();
  await page.getByRole("option", { name: "Ahorros" }).click();
  await bankForm.getByText("Número de cuenta", { exact: true }).locator("..").locator("input").fill("123456789");
  await expect(bankForm.getByText("Cuenta auxiliar del PUC", { exact: true })).toHaveCount(0);
  await bankForm.getByRole("button", { name: "Guardar cuenta bancaria" }).click();

  await expect.poll(() => savedBankAccount?.accountTypeOptionId).toBe(bankTypeId);
  await expect(bankForm.getByText("Tipo de cuenta", { exact: true }).locator("..").getByRole("combobox")).toContainText("Seleccionar tipo");
  expect(savedBankAccount).not.toHaveProperty("accountingAccountId",expect.any(String));
  await expect(page.getByRole("row").filter({hasText:"Cuenta principal E2E"})).toContainText("0 auxiliares vinculados");

  await page.getByRole("button", { name: "Retenciones" }).click();
  await expect(page.getByRole("heading", { name: "Reglas de retención" })).toBeVisible();
  await expect(page.getByText("RF-COMPRA · Retefuente compras")).toBeVisible();
  expect(ruleRequests).toBe(1);
  expect(appliedRequest).toBe("");
  await page.getByRole("tab", { name: "Retenciones aplicadas" }).click();
  await expect(page.getByRole("heading", { name: "Retenciones practicadas" })).toBeVisible();
  await expect(page.getByText("FLETE-42")).toBeVisible();
  await expect(page.getByText("Transportes Andinos")).toBeVisible();
  await expect(page.getByText("DS-43")).toBeVisible();
  await expect(page.getByText("Soporte manual configurado")).toBeVisible();
  await expect(page.getByText("Factura adicional configurada")).toBeVisible();
  expect(appliedRequests).toBe(1);
  expect(new URL(appliedRequest).searchParams.get("pageSize")).toBe("25");
  await expect(page.getByRole("heading", { name: "Reglas de retención" })).toHaveCount(0);
  expect(ruleRequests).toBe(1);
  await page.getByRole("tab", { name: "Configuración" }).click();
  await expect(page.getByRole("heading", { name: "Reglas de retención" })).toBeVisible();
  await expect(page.getByText("FLETE-42")).toHaveCount(0);
  expect(appliedRequests).toBe(1);
  expect(ruleRequests).toBe(1);
  await page.getByRole("button", { name: "Nueva regla" }).click();
  await expect(page.getByRole("heading", { name: "Nueva regla de retención" })).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(page.getByRole("heading", { name: "Responsabilidades tributarias" })).toBeVisible();
  await page.getByRole("button", { name: "Nueva responsabilidad" }).click();
  await expect(page.getByRole("heading", { name: "Nueva responsabilidad tributaria" })).toBeVisible();
});
