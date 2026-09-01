import { expect, test, type Page, type Route } from "@playwright/test";

const tenantId = "11111111-1111-1111-1111-111111111111";
const businessId = "22222222-2222-2222-2222-222222222222";
const userId = "33333333-3333-3333-3333-333333333333";
const closureId = "44444444-4444-4444-4444-444444444444";
const saleId = "55555555-5555-5555-5555-555555555555";
const permissions = ["dashboard.read", "work-sessions.differences.read", "work-sessions.closures.reconcile", "sales.returns.read", "sales.returns.create", "sales.returns.confirm"];

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

test("el efectivo despliega su trazabilidad sin conciliar cada movimiento", async ({ page }) => {
  await authenticate(page);
  await page.route("**/api/commerce/v1/work-sessions/closures?**", route => json(route, { items: [{
    workSessionClosureId: closureId, workSessionId: crypto.randomUUID(), businessId, businessName: "Auraly", warehouseId: crypto.randomUUID(), warehouseName: "Principal", userId, userName: "Cajero", openedAt: "2026-08-31T08:00:00-05:00", closedAt: "2026-08-31T18:00:00-05:00", salesCount: 2, creditSalesCount: 0, returnCount: 1, totalSales: 150000, totalRefunds: 20000, netAmount: 130000, reconciliationStatus: "Pending", accountingStatus: "AccountingDisabled", paymentTotals: [{ paymentMethodCode: "Cash", salesAmount: 150000, refundAmount: 20000, otherAmount: 0, netAmount: 130000, countedAmount: 130000, difference: 0, requiresCount: true }],
  }], page: 1, pageSize: 50, totalItems: 1 }));
  await page.route(`**/api/commerce/v1/work-sessions/closures/${closureId}/payment-verifications`, route => json(route, [
    movement("sale-1", "Sale", "SalesInvoice", "FV-101", 100000),
    movement("sale-2", "Sale", "SalesReceipt", "POS-202", 50000),
    movement("return-1", "Refund", "SalesReturn", "DVT-1", -20000),
    movement("in-1", "CashIn", "CashMovement", "Base adicional", 10000),
    movement("out-1", "CashOut", "CashMovement", "Consignación", -10000),
  ]));
  await page.route("**/api/commerce/v1/reference-options/cash-reconciliation-reason", route => json(route, []));

  await page.goto("/dashboard/cash-differences");
  await page.getByRole("button", { name: "Conciliar" }).click();
  const dialog = page.getByRole("dialog", { name: "Conciliar cierre" });
  const cash = dialog.locator("section").filter({ hasText: "Efectivo" }).first();
  await cash.getByRole("button", { name: /Ver trazabilidad/ }).click();
  await expect(cash.getByText(/Factura electrónica FV-101/)).toBeVisible();
  await expect(cash.getByText(/Comprobante POS-202/)).toBeVisible();
  await expect(cash.getByText(/Devolución DVT-1/)).toBeVisible();
  await expect(cash.getByText(/Entrada Base adicional/)).toBeVisible();
  await expect(cash.getByText(/Salida Consignación/)).toBeVisible();
  await expect(cash.getByText(/Neto trazado/)).toContainText("130.000");
  await expect(cash.getByRole("button", { name: "Verificado" })).toHaveCount(0);
  await expect(cash.getByText("Total verificado", { exact: true })).toBeVisible();
});

test("la devolución ofrece destinos independientes y enlaza la reversión de tarjeta", async ({ page }) => {
  await authenticate(page);
  await page.route("**/api/commerce/v1/sales-returns/sales?**", route => json(route, { items: [{ documentId: saleId, documentNumber: "FV-900", fiscalNumber: "SETT-900", cufe: "CUFE", issuedAt: "2026-08-30T10:00:00-05:00", customerId: crypto.randomUUID(), customerName: "Cliente crédito", customerIdentification: "900123", warehouseId: crypto.randomUUID(), warehouseName: "Principal", totalAmount: 119000, returnedAmount: 0, hasAvailableQuantity: true, fiscalStatus: "Accepted" }], page: 1, pageSize: 25, totalCount: 1, totalPages: 1 }));
  await page.route(`**/api/commerce/v1/sales-returns/sales/${saleId}`, route => json(route, {
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
});

function movement(key: string, movementType: "Sale" | "Refund" | "CashIn" | "CashOut", sourceDocumentType: "SalesInvoice" | "SalesReceipt" | "SalesReturn" | "CashMovement", documentNumber: string, amount: number) {
  return { verificationKey: key, paymentMethodCode: "Cash", movementType, sourceDocumentType, sourceId: crypto.randomUUID(), documentNumber, sourceNumber: 1, amount, reference: null, cardFranchiseCode: null, approvalNumber: null, occurredAt: "2026-08-31T12:00:00-05:00" };
}
