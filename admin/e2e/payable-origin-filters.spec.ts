import { expect, test } from "@playwright/test";

test("cartera muestra origen del gasto, filtra concepto y ubica filtros bajo el resumen", async ({ page, baseURL }) => {
  const tenantId = "11111111-1111-1111-1111-111111111111";
  const businessId = "22222222-2222-2222-2222-222222222222";
  const conceptId = "33333333-3333-3333-3333-333333333333";
  const payableId = "44444444-4444-4444-4444-444444444444";
  const user = { userId: "55555555-5555-5555-5555-555555555555", tenantId,
    tenantKey: "@portfolio-test", username: "portfolio-test", firstName: "Prueba", lastName: "Cartera",
    roles: [], permissions: ["payables.read", "receivables.read", "purchasing.goods-receipts.read"] };
  await page.context().addCookies([{ name: "auth_token", value: "portfolio-test", url: baseURL!, httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ user, businessId }) => {
    localStorage.setItem("selected_tenant_id", user.tenantId);
    localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
  }, { user, businessId });

  let conceptFilter = "";
  let conceptPageSize = "";
  let receiptMode = false;
  const portfolioRequests: Array<string | null> = [];
  await page.route("**/api/**", async route => {
    const url = new URL(route.request().url());
    const path = url.pathname;
    let body: unknown = [];
    if (path === "/api/auth/me") body = user;
    else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Pruebas" }];
    else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Sede pruebas" }];
    else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, roles: [], permissions: user.permissions };
    else if (path.endsWith("/payables/suppliers") || path.endsWith("/receivables/customers")) {
      const partyId = url.searchParams.get(path.endsWith("/payables/suppliers") ? "supplierId" : "customerId");
      portfolioRequests.push(partyId);
      if (partyId === "") {
        await route.fulfill({ status: 400, contentType: "application/json", body: JSON.stringify({ title: "Invalid party ID" }) });
        return;
      }
      body = { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0,
        totalOutstanding: 0, totalOverdue: 0, totalInvoiceCount: 0, totalSupplierCredit: 0 };
    }
    else if (path.endsWith("/payables/expense-concepts")) {
      conceptPageSize = url.searchParams.get("pageSize") ?? "";
      body = { items: [{ conceptId, name: "Transporte y mensajería" }],
        page: 1, pageSize: 10, totalCount: 1, totalPages: 1 };
    }
    else if (path.endsWith("/goods-receipts/66666666-6666-6666-6666-666666666666"))
      body = { documentId: "66666666-6666-6666-6666-666666666666", documentNumber: "EMC00-17",
        supplierName: "Proveedor de prueba", warehouseName: "Bodega", status: "Processed",
        receivedAt: "2026-09-24T12:00:00-05:00", purchaseEvidenceType: "SupplierElectronicInvoice",
        supplierInvoiceNumber: "FLETE-20", supplierInvoiceDate: null, createsPayable: true,
        dueDate: "2026-09-25T12:00:00-05:00", currencyCode: "COP", lines: [],
        netAmount: 5000, taxAmount: 0, grandTotal: 5000, functionalNetAmount: 5000,
        functionalTaxAmount: 0, functionalGrandTotal: 5000, withholding: null,
        additionalCostDocuments: [], accountingStatuses: [] };
    else if (path.endsWith("/goods-receipts"))
      body = { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 };
    else if (path.endsWith(`/payables/${payableId}`)) body = { payableId, supplierId: conceptId,
      supplierName: "Proveedor de prueba", supplierIdentification: "1001", sourceDocumentId: payableId,
      sourceDocumentType: receiptMode ? "GoodsReceiptCostDocument" : "Expense",
      documentNumber: receiptMode ? "FLETE-20" : "GTO00-20", currencyCode: "COP",
      originalAmount: 5000, outstandingAmount: 5000, dueDate: "2026-09-25T12:00:00-05:00",
      status: "Open", expenseConceptName: "Transporte y mensajería",
      expenseDescription: "Domicilio · Factura VIA00-190", sourceInvoiceNumber: "VIA00-190",
      goodsReceiptId: receiptMode ? "66666666-6666-6666-6666-666666666666" : null, transactions: [] };
    else if (path.endsWith("/payables")) {
      conceptFilter = url.searchParams.get("conceptId") ?? "";
      body = { items: [{ payableId, supplierId: conceptId, supplierName: "Proveedor de prueba",
        documentNumber: "GTO00-20", currencyCode: "COP", originalAmount: 5000,
        outstandingAmount: 5000, dueDate: "2026-09-25T12:00:00-05:00", status: "Open",
        isOverdue: false, createdAt: "2026-09-24T12:00:00-05:00",
        expenseConceptName: "Transporte y mensajería" }], page: 1, pageSize: 20,
        totalCount: 1, totalPages: 1, totalOutstanding: 5000, totalOverdue: 0 };
    }
    else if (path.endsWith("/payable-payments") || path.endsWith("/receivable-payments"))
      body = { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 };
    else if (path.endsWith("/parties/role-options")) {
      const role = url.searchParams.get("role") ?? "Supplier";
      body = { items: [{ partyId: conceptId, roleId: conceptId, role,
        displayName: "Tercero de prueba", identification: "1001",
        supplierPurchaseEvidencePolicy: null, supplierDefaultPaymentDueDays: null }],
        page: 1, totalPages: 1, totalCount: 1 };
    }
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
  });

  await page.goto("/dashboard/payables");
  await expect(page.getByLabel("Resumen de cartera")).toBeVisible();
  expect(await page.getByLabel("Resumen de cartera").evaluate(node =>
    !!(node.compareDocumentPosition(document.querySelector("details")!) & Node.DOCUMENT_POSITION_FOLLOWING))).toBe(true);
  await page.getByText("Filtros", { exact: true }).click();
  await page.getByRole("combobox", { name: "Seleccionar supplier" }).click();
  await page.getByRole("option", { name: /Tercero de prueba/ }).click();
  await expect.poll(() => portfolioRequests.at(-1)).toBe(conceptId);
  const payableRequestsBeforeClear = portfolioRequests.length;
  await page.getByRole("button", { name: "Quitar selección de Seleccionar supplier" }).click();
  await expect.poll(() => portfolioRequests.at(-1)).toBeNull();
  expect(portfolioRequests.length).toBe(payableRequestsBeforeClear + 1);
  await expect(page.getByText("No fue posible cargar la información.")).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Desde" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Hasta" })).toBeVisible();
  await expect(page.locator('input[type="date"]')).toHaveCount(0);
  await page.getByRole("tab", { name: "Facturas" }).click();
  await page.getByRole("combobox", { name: "Concepto de gasto" }).click();
  await expect.poll(() => conceptPageSize).toBe("10");
  await page.getByRole("option", { name: "Transporte y mensajería" }).click();
  await expect.poll(() => conceptFilter).toBe(conceptId);
  await page.getByRole("row", { name: /GTO00-20/ }).click();
  const detail = page.getByRole("dialog", { name: "GTO00-20" });
  await expect(detail).toContainText("Transporte y mensajería");
  await expect(detail).toContainText("Domicilio · Factura VIA00-190");
  await expect(detail).toContainText("Factura de venta: VIA00-190");
  await detail.getByRole("button", { name: "Cerrar" }).click();
  await page.getByRole("button", { name: "Limpiar filtros" }).click();
  await expect.poll(() => conceptFilter).toBe("");

  await page.goto("/dashboard/receivables");
  await expect(page.getByLabel("Resumen de cartera")).toBeVisible();
  expect(await page.getByLabel("Resumen de cartera").evaluate(node =>
    !!(node.compareDocumentPosition(document.querySelector("details")!) & Node.DOCUMENT_POSITION_FOLLOWING))).toBe(true);
  await page.getByText("Filtros", { exact: true }).click();
  await page.getByRole("combobox", { name: "Seleccionar customer" }).click();
  await page.getByRole("option", { name: /Tercero de prueba/ }).click();
  await expect.poll(() => portfolioRequests.at(-1)).toBe(conceptId);
  const receivableRequestsBeforeClear = portfolioRequests.length;
  await page.getByRole("button", { name: "Quitar selección de Seleccionar customer" }).click();
  await expect.poll(() => portfolioRequests.at(-1)).toBeNull();
  expect(portfolioRequests.length).toBe(receivableRequestsBeforeClear + 1);
  await expect(page.getByText("No fue posible cargar la información.")).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Desde" })).toBeVisible();
  await expect(page.locator('input[type="date"]')).toHaveCount(0);

  receiptMode = true;
  await page.goto("/dashboard/payables");
  await page.getByRole("tab", { name: "Facturas" }).click();
  await page.getByRole("row", { name: /GTO00-20/ }).click();
  await page.getByRole("button", { name: "Ver recepción" }).click();
  await expect(page).toHaveURL(/goods-receipts\?receiptId=66666666-6666-6666-6666-666666666666/);
  await expect(page.getByRole("dialog", { name: "EMC00-17" })).toBeVisible();
});
