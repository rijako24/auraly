import { expect, test, type Page, type Route } from "@playwright/test";

const tenantId = "11111111-1111-1111-1111-111111111111";
const businessId = "22222222-2222-2222-2222-222222222222";
const userId = "33333333-3333-3333-3333-333333333333";
const closureId = "44444444-4444-4444-4444-444444444444";
const saleId = "55555555-5555-5555-5555-555555555555";
const permissions = ["dashboard.read", "work-sessions.differences.read", "work-sessions.closures.reconcile", "sales.returns.read", "sales.returns.create", "sales.returns.confirm", "invoice-charges.read", "invoice-charges.configure"];

const json = (route: Route, body: unknown) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });

async function authenticate(page: Page) {
  await page.context().addCookies([{ name: "auth_token", value: "e2e", url: process.env.AURALY_E2E_BASE_URL ?? "http://127.0.0.1:3000", httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ tenant, business, user, granted }) => {
    localStorage.setItem("selected_tenant_id", tenant);
    localStorage.setItem("selected_business_id", business);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user: { userId: user, tenantId: tenant, tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test", firstName: "Prueba", lastName: "E2E", avatarUrl: null, roles: ["Administrator"], permissions: granted } }, version: 0 }));
  }, { tenant: tenantId, business: businessId, user: userId, granted: permissions });
  await page.route("**/api/auth/me", route => json(route, { userId, tenantId, tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test", firstName: "Prueba", lastName: "E2E", avatarUrl: null, roles: ["Administrator"], permissions }));
  await page.route("**/api/execution-context/tenants", route => json(route, [{ tenantId, name: "Auraly" }]));
  await page.route("**/api/execution-context/businesses", route => json(route, [{ tenantId, businessId, name: "Auraly" }]));
  await page.route("**/api/execution-context/access", route => json(route, { tenantId, businessId, roles: ["Administrator"], permissions }));
}

test("el cierre muestra motivos y observaciones sin números de egreso", async ({ page }) => {
  await authenticate(page);
  let snapshotReads = 0;
  await page.route("**/api/commerce/v1/work-sessions/*/closure", route => {
    snapshotReads++;
    return json(route, { invoiceCharges: [
      { documentId: saleId, documentNumber: "FV-101", appliedChargeId: "charge-1", chargeId: "delivery", code: "DOMICILIO", name: "Domicilio", supplierName: "Domiciliario prueba", amount: 5000, invoicedAmount: 5000, expenseAmount: 0, withholdingAmount: 0, payments: [{ paymentNumber: 1, paymentMethodCode: "Cash", amount: 5000 }] },
      { documentId: saleId, documentNumber: "FV-101", appliedChargeId: "charge-2", chargeId: "exhausted", code: "AGOTADOS", name: "Agotados", supplierName: "Domiciliario prueba", amount: 6500, invoicedAmount: 0, expenseAmount: 6500, withholdingAmount: 0, payments: [] },
    ] });
  });
  await page.route("**/api/commerce/v1/work-sessions/closures?**", route => json(route, { items: [{
    workSessionClosureId: closureId, workSessionId: crypto.randomUUID(), businessId, businessName: "Auraly", warehouseId: crypto.randomUUID(), warehouseName: "Principal", userId, userName: "Cajero", openedAt: "2026-08-31T08:00:00-05:00", closedAt: "2026-08-31T18:00:00-05:00", salesCount: 2, creditSalesCount: 0, returnCount: 1, totalSales: 150000, totalRefunds: 20000, netAmount: 130000, reconciliationStatus: "Pending", accountingStatus: "AccountingDisabled", paymentTotals: [{ paymentMethodCode: "Cash", salesAmount: 150000, refundAmount: 20000, otherAmount: 0, netAmount: 130000, countedAmount: 130000, difference: 0, requiresCount: true }],
  }], page: 1, pageSize: 50, totalItems: 1 }));
  await page.route(`**/api/commerce/v1/work-sessions/closures/${closureId}/payment-verifications`, route => json(route, [
    movement("sale-1", "Sale", "SalesInvoice", "FV-101", 100000),
    movement("sale-2", "Sale", "SalesReceipt", "POS-202", 50000),
    movement("return-1", "Refund", "SalesReturn", "DVT-1", -20000),
    { ...movement("in-1", "CashIn", "CashMovement", "ING-999", 10000), reasonName: "Base adicional", notes: "Cambio para comenzar el turno" },
    { ...movement("out-1", "CashOut", "CashMovement", "EGR-888", -10000), reasonName: "Consignación", notes: "Entrega en banco" },
  ]));
  await page.route("**/api/commerce/v1/reference-options/cash-reconciliation-reason", route => json(route, []));

  await page.goto("/dashboard/cash-differences");
  await page.getByRole("button", { name: "Conciliar" }).click();
  const dialog = page.getByRole("dialog", { name: "Conciliar cierre" });
  const cash = dialog.locator("section").filter({ hasText: "Efectivo" }).first();
  await expect(cash.getByText("Domicilio", { exact: true })).toBeVisible();
  await expect(cash.getByText("Agotados", { exact: true })).toHaveCount(0);
  await expect(dialog.getByText("Agotados", { exact: true })).toBeVisible();
  await cash.getByRole("button", { name: /Entradas de dinero/ }).click();
  await cash.getByRole("button", { name: /Salidas de dinero/ }).click();
  await expect(cash.getByText("Base adicional", { exact: true })).toBeVisible();
  await expect(cash.getByText("Consignación", { exact: true })).toBeVisible();
  await expect(cash.getByText("Cambio para comenzar el turno", { exact: true })).toBeVisible();
  await expect(cash.getByText("Entrega en banco", { exact: true })).toBeVisible();
  await expect(cash.getByText("ING-999", { exact: true })).toHaveCount(0);
  await expect(cash.getByText("EGR-888", { exact: true })).toHaveCount(0);
  await expect(cash.getByRole("button", { name: "Verificado", exact: true })).toHaveCount(2);
  await cash.getByRole("button", { name: "Verificado", exact: true }).first().click();
  await cash.getByRole("button", { name: "Verificado", exact: true }).last().click();
  await expect(cash.getByText(/Total efectivo confirmado:/)).toContainText("130.000");
  expect(snapshotReads).toBe(1);
});

test("la devolución ofrece destinos independientes y enlaza la reversión de tarjeta", async ({ page }) => {
  await authenticate(page);
  await page.route("**/api/commerce/v1/pos/settlement-configuration**", route => json(route, { isAccountingEnabled: false, bankAccounts: [] }));
  await page.route("**/api/commerce/v1/sales-returns/sales?**", route => json(route, { items: [{ documentId: saleId, documentNumber: "FV-900", fiscalNumber: "SETT-900", cufe: "CUFE", issuedAt: "2026-08-30T10:00:00-05:00", customerId: crypto.randomUUID(), customerName: "Cliente crédito", customerIdentification: "900123", warehouseId: crypto.randomUUID(), warehouseName: "Principal", totalAmount: 119000, returnedAmount: 0, hasAvailableQuantity: true, fiscalStatus: "Accepted" }], page: 1, pageSize: 25, totalCount: 1, totalPages: 1 }));
  await page.route(`**/api/commerce/v1/sales-returns/sales/${saleId}**`, route => json(route, {
    documentId: saleId, documentNumber: "FV-900", fiscalNumber: "SETT-900", cufe: "CUFE", issuedAt: "2026-08-30T10:00:00-05:00", customerId: crypto.randomUUID(), customerName: "Cliente crédito", customerIdentification: "900123", warehouseId: crypto.randomUUID(), warehouseName: "Principal", totalAmount: 119000, returnedAmount: 0, receivableOutstanding: 90000, fiscalStatus: "Accepted", payments: [{ paymentNumber: 1, methodCode: "CreditCard", originalAmount: 29000, refundedAmount: 0, availableAmount: 29000, cardFranchiseCode: "Visa", approvalNumber: "APP-900" }], lines: [{ originalLineNumber: 1, productId: crypto.randomUUID(), productCode: "P-1", reference: null, description: "Producto", soldQuantity: 1, returnedQuantity: 0, availableQuantity: 1, unitPrice: 100000, discountAmount: 0, taxCode: "01", taxRate: 19, untaxedAmount: 100000, taxAmount: 19000, lineTotal: 119000, barcodes: "" }],
  }));
  await page.route("**/api/commerce/v1/reference-options/sales-return-resolution-method", route => json(route, [
    { id: "cash", code: "Cash", label: "Efectivo", description: null, sortOrder: 10 },
    { id: "credit", code: "CustomerCredit", label: "Abono a cartera", description: null, sortOrder: 20 },
    { id: "transfer", code: "Transfer", label: "Transferencia", description: null, sortOrder: 30 },
    { id: "debit", code: "DebitCard", label: "Tarjeta débito", description: null, sortOrder: 40 },
    { id: "credit-card", code: "CreditCard", label: "Tarjeta crédito", description: null, sortOrder: 50 },
  ]));
  await page.route("**/api/commerce/v1/accounting/bank-accounts**", route => json(route, [{ bankAccountId: crypto.randomUUID(), displayName: "Cuenta principal", bankName: "Banco", accountNumber: "1234", accountTypeName: "Ahorros", isPrimary: true, isActive: true, rowVersion: "AQ==" }]));
  await page.route("**/api/commerce/v1/reference-options/sales-return-scope", route => json(route, [{ id: "partial", code: "Partial", label: "Parcial", description: null, sortOrder: 10 }]));
  await page.route("**/api/commerce/v1/reasons?**", route => json(route, []));

  await page.goto("/dashboard/sales-returns");
  await page.getByText("FV-900", { exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Nueva devolución" });
  const resolution = dialog.getByText("Cómo devolver el valor", { exact: true }).locator("..");
  await expect(resolution.getByRole("combobox")).toContainText("Abono a cartera");
  await resolution.getByRole("combobox").click();
  await expect(page.getByRole("option", { name: "Abono a cartera" })).toBeVisible();
  await expect(page.getByRole("option", { name: "Efectivo" })).toBeVisible();
  await expect(page.getByRole("option", { name: "Tarjeta crédito" })).toBeVisible();
  await expect(page.getByRole("option", { name: "Transferencia" })).toBeVisible();
  await page.getByRole("option", { name: "Tarjeta crédito" }).click();
  const originalPayment = dialog.getByText("Pago de tarjeta por reversar", { exact: true }).locator("..");
  await originalPayment.getByRole("combobox").click();
  await expect(page.getByRole("option", { name: /Visa · APP-900/ })).toBeVisible();
  await page.getByRole("option", { name: /Visa · APP-900/ }).click();
  const quantity = dialog.getByRole("spinbutton", { name: "Cantidad a devolver de Producto" });
  const productRow = quantity.locator("..");
  await quantity.fill("0.5");
  await expect(productRow).toContainText("119.000");
  await expect(productRow).toContainText("59.500");
  await expect(dialog.getByText("Valor estimado").locator("../..")).toContainText("59.500");
});

test("el modal de periféricos cubre el viewport completo desde el body", async ({ page }) => {
  await authenticate(page);
  await page.route("**/api/commerce/v1/pos/workspace/options", route => json(route, []));
  await page.route("**/api/commerce/v1/routes?**", route => json(route, { items: [], page: 1, pageSize: 100, totalCount: 0, totalPages: 0 }));
  await page.route("**/api/commerce/v1/orders?**", route => json(route, { items: [], page: 1, pageSize: 20, totalCount: 0, hasMore: false }));
  await page.route("**/api/commerce/v1/pos/installer", route => json(route, { downloadUrl: "/auraly-installer.exe", version: "1.0.0", sha256: "test", tenantPreconfigured: false }));

  await page.goto("/dashboard/orders");
  await page.evaluate(() => document.documentElement.classList.add("dark"));
  await page.getByRole("button", { name: "Configurar plantillas e impresoras" }).click();

  const backdrop = page.getByTestId("peripherals-dialog-backdrop");
  const dialog = page.getByRole("dialog", { name: "Periféricos" });
  await expect(dialog).toBeVisible();
  await expect(dialog).toHaveCSS("color", "rgb(2, 6, 23)");
  await expect(dialog.getByText("Formato").first()).toHaveCSS("color", "rgb(2, 6, 23)");
  await expect(dialog.getByRole("button", { name: "Cancelar" })).toHaveCSS("color", "rgb(2, 6, 23)");
  expect(await backdrop.evaluate(element => element.parentElement === document.body)).toBe(true);
  await expect(backdrop).toHaveCSS("position", "fixed");
  expect(await backdrop.boundingBox()).toEqual({ x: 0, y: 0, width: 1440, height: 1000 });
});

function movement(key: string, movementType: "Sale" | "Refund" | "CashIn" | "CashOut", sourceDocumentType: "SalesInvoice" | "SalesReceipt" | "SalesReturn" | "CashMovement", documentNumber: string, amount: number) {
  return { verificationKey: key, paymentMethodCode: "Cash", movementType, sourceDocumentType, sourceId: crypto.randomUUID(), documentNumber, sourceNumber: 1, amount, reference: null, cardFranchiseCode: null, approvalNumber: null, occurredAt: "2026-08-31T12:00:00-05:00" };
}


test("historial de cargos pagina y consulta sin cargar pestañas ocultas", async ({ page }) => {
  await authenticate(page);
  let configurationReads = 0, historyReads = 0;
  await page.route("**/api/commerce/v1/reference-options/invoice-charge-*", route => json(route, []));
  await page.route("**/api/commerce/v1/invoice-charges?**", route => {
    configurationReads++;
    return json(route, { items: [], totalCount: 0, page: 1, pageSize: 25, totalPages: 0 });
  });
  await page.route("**/api/commerce/v1/invoice-charges/history?**", route => {
    historyReads++;
    return json(route, { items: [{ documentId: saleId, documentNumber: "FV-CARGO-1", issuedAt: "2026-09-19T10:00:00-05:00",
      workSessionId: closureId, expenseDocumentNumber: "GAS-123", expenseStatus: "Processed", payableBalance: 5000,
      charge: { appliedChargeId: "one", name: "Domicilio", supplier: { name: "Domiciliario prueba" }, amount: 5000,
        invoicedAmount: 5000, expenseAmount: 0 } }], totalCount: 1, page: 1, pageSize: 25, invoicedTotal: 5000, expenseTotal: 0 });
  });
  await page.goto("/dashboard/invoice-charges");
  await expect(page.getByRole("heading", { name: "Cargos de facturación", exact: true })).toBeVisible();
  await expect(page.getByText("Todavía no hay cargos", { exact: true })).toBeVisible();
  expect(configurationReads).toBe(1); expect(historyReads).toBe(0);
  await page.getByRole("tab", { name: "Consulta de cargos" }).click();
  await expect(page.getByRole("cell", { name: /^FV-CARGO-1/ })).toBeVisible();
  await expect(page.getByText("GAS-123", { exact: true })).toBeVisible();
  await page.screenshot({ path: test.info().outputPath("consulta-cargos.png"), fullPage: true });
  expect(configurationReads).toBe(1); expect(historyReads).toBe(1);
  await page.getByRole("button", { name: "Actualizar", exact: true }).click();
  await expect.poll(() => historyReads).toBe(2);
  expect(configurationReads).toBe(1);
  await page.getByLabel("Hasta", { exact: true }).fill("2027-12-31");
  await expect(page.getByRole("main").getByRole("alert")).toContainText("31 días");
  expect(historyReads).toBe(2);
});

test("configurar un cargo conserva concepto y proveedor y reutiliza el resultado al editar", async ({ page }) => {
  await authenticate(page);
  const supplierId = crypto.randomUUID(), conceptId = crypto.randomUUID(), taxId = crypto.randomUUID();
  let items: Record<string, unknown>[] = [], reads = 0, writes = 0, optionsReads = 0;
  const calculation = [{ code: "Manual", label: "Valor manual" }, { code: "Ranges", label: "Por rangos" }, { code: "Fixed", label: "Valor fijo" }, { code: "Percentage", label: "Porcentaje" }];
  const inclusion = [{ code: "Never", label: "No incluir en factura" }, { code: "Always", label: "Siempre en factura" }, { code: "UpToInvoiceAmount", label: "Hasta un importe de factura" }];
  await page.route("**/api/commerce/v1/reference-options/invoice-charge-calculation", route => json(route, calculation));
  await page.route("**/api/commerce/v1/reference-options/invoice-charge-inclusion", route => json(route, inclusion));
  await page.route("**/api/commerce/v1/invoice-charges?**", route => {
    reads++;
    expect(new URL(route.request().url()).searchParams.get("pageSize")).toBe("20");
    return json(route, { items, page: 1, pageSize: 20, totalCount: items.length, totalPages: 1 });
  });
  await page.route("**/api/commerce/v1/invoice-charges/options", route => {
    optionsReads++;
    return json(route, { concepts: [{ conceptId, name: "Domicilios", expenseAccountCode: "519595", expenseAccountName: "Servicios", defaultCostCenterName: "Operación" }], taxProfiles: [{ taxProfileId: taxId, name: "IVA 0", rate: 0 }] });
  });
  await page.route("**/api/commerce/v1/parties/role-options?**", route => json(route, { items: [{ role: "Supplier", roleId: supplierId, partyId: supplierId, displayName: "Domiciliario prueba", identification: "TEST" }], page: 1, totalPages: 1, totalCount: 1 }));
  await page.route("**/api/commerce/v1/invoice-charges/*", async route => {
    if (route.request().method() !== "PUT") return route.fallback();
    const input = route.request().postDataJSON();
    writes++;
    expect(input).toMatchObject({ expenseConceptId: conceptId, supplierIds: [supplierId], salesTaxProfileId: taxId, purchaseTaxProfileId: taxId });
    if (writes === 1) expect(input).toMatchObject({ expectedVersion: 0, calculationMode: "Manual", value: 5000, inclusionMode: "Never" });
    else expect(input).toMatchObject({ expectedVersion: 1, calculationMode: "Ranges", invoiceAmountLimit: 80000, inclusionMode: "UpToInvoiceAmount", ranges: [{ fromInclusive: 0, toExclusive: 400000, calculationMode: "Fixed", value: 5000 }, { fromInclusive: 400000, toExclusive: null, calculationMode: "Percentage", value: 2 }] });
    const saved = { ...input, businessId, version: writes, expenseConceptName: "Domicilios", expenseAccountCode: "519595", expenseAccountName: "Servicios", costCenterName: "Operación", suppliers: [{ supplierId, name: "Domiciliario prueba", identification: "TEST", isActive: true }] };
    items = [saved];
    await json(route, saved);
  });
  await page.goto("/dashboard/invoice-charges");
  await expect(page.getByRole("main").getByRole("combobox")).toContainText("20");
  expect(optionsReads).toBe(0);
  await page.getByRole("button", { name: "Crear cargo", exact: true }).click();
  let dialog = page.getByRole("dialog");
  await dialog.getByLabel("Nombre", { exact: true }).fill("Agotados");
  await dialog.getByLabel("Código", { exact: true }).fill("AGOTADOS");
  await dialog.getByLabel("Modalidad", { exact: true }).click();
  await page.getByRole("option", { name: "Valor manual", exact: true }).click();
  await dialog.getByLabel("Valor sugerido (COP)").fill("5000");
  await dialog.getByLabel("Aplicación del cargo", { exact: true }).click();
  await page.getByRole("option", { name: "No incluir en factura", exact: true }).click();
  await dialog.getByLabel("Concepto de gasto", { exact: true }).click();
  await page.getByRole("option", { name: "Domicilios", exact: true }).click();
  for (const label of ["Impuesto al facturar", "Impuesto del costo del proveedor"]) {
    await dialog.getByLabel(label, { exact: true }).click();
    await page.getByRole("option", { name: "IVA 0", exact: true }).click();
  }
  await dialog.getByRole("combobox", { name: "Seleccionar supplier" }).click();
  await page.getByRole("option", { name: /Domiciliario prueba/ }).click();
  await dialog.getByRole("button", { name: "Guardar cargo" }).click();
  await expect(dialog).not.toBeVisible();
  await expect(page.getByRole("cell", { name: /^Agotados/ })).toBeVisible();
  expect(reads).toBe(2);
  await page.getByRole("button", { name: "Editar Agotados" }).click();
  dialog = page.getByRole("dialog");
  await expect(dialog).toContainText("519595 · Servicios");
  await expect(dialog).toContainText("Domiciliario prueba");
  await dialog.getByLabel("Nombre", { exact: true }).fill("Domicilio");
  await dialog.getByLabel("Modalidad", { exact: true }).click();
  await page.getByRole("option", { name: "Por rangos", exact: true }).click();
  await dialog.getByRole("button", { name: "Agregar rango" }).click();
  await dialog.locator("#range-to-0").fill("400000");
  await dialog.locator("#range-mode-0").click();
  await page.getByRole("option", { name: "Valor fijo", exact: true }).click();
  await dialog.locator("#range-value-0").fill("5000");
  await dialog.getByRole("button", { name: "Agregar rango" }).click();
  await expect(dialog.locator("#range-from-1")).toHaveValue("400000");
  await dialog.locator("#range-mode-1").click();
  await page.getByRole("option", { name: "Porcentaje", exact: true }).click();
  await dialog.locator("#range-value-1").fill("2");
  await dialog.getByLabel("Aplicación del cargo", { exact: true }).click();
  await page.getByRole("option", { name: "Hasta un importe de factura", exact: true }).click();
  await dialog.getByLabel("Incluir hasta un importe de factura de (COP)").fill("80000");
  await page.screenshot({ path: test.info().outputPath("configuracion-cargo.png"), fullPage: true });
  await dialog.getByRole("button", { name: "Guardar cargo" }).click();
  await expect(dialog).not.toBeVisible();
  await expect(page.getByRole("cell", { name: /^Domicilio/ }).first()).toBeVisible();
  expect(reads).toBe(2); expect(writes).toBe(2);
});
