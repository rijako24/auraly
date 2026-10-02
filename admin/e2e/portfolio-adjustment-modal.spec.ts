import { expect, test } from "@playwright/test";

const tenantId = "11111111-1111-1111-1111-111111111111";
const businessId = "22222222-2222-2222-2222-222222222222";
const partyId = "33333333-3333-3333-3333-333333333333";
const invoiceId = "44444444-4444-4444-4444-444444444444";
const user = {
  userId: "55555555-5555-5555-5555-555555555555", tenantId,
  tenantKey: "@adjustment-test", username: "adjustment-test", firstName: "Prueba", lastName: "Cartera",
  roles: [], permissions: ["payables.read", "receivables.read", "accounting.manual.create"],
};

for (const direction of ["payables", "receivables"] as const) {
  test(`ajuste de cartera abre modal con contexto desde ${direction}`, async ({ page, baseURL }) => {
    const payable = direction === "payables";
    let partyListReads = 0;
    const partyRole = payable ? "Supplier" : "Customer";
    const partyName = payable ? "Proveedor de prueba" : "Cliente de prueba";
    const documentNumber = payable ? "GTO00-20" : "FV00-20";
    const invoice = {
      documentNumber, currencyCode: "COP", originalAmount: 5000, outstandingAmount: 3500,
      dueDate: "2026-09-25T12:00:00-05:00", status: "Open", isOverdue: false,
      createdAt: "2026-09-24T12:00:00-05:00",
      ...(payable ? { payableId: invoiceId, supplierId: partyId, supplierName: partyName }
        : { receivableId: invoiceId, customerId: partyId, customerName: partyName, partySiteId: null, partySiteName: null, paidAmount: 1500 }),
    };
    const detail = {
      ...invoice, transactions: [], sourceDocumentId: invoiceId, sourceDocumentType: "Expense",
      ...(payable ? { supplierIdentification: "1001", expenseConceptName: null, expenseDescription: "", sourceInvoiceNumber: null, goodsReceiptId: null }
        : { customerIdentification: "1001" }),
    };

    await page.context().addCookies([{ name: "auth_token", value: "adjustment-test", url: baseURL!, httpOnly: true, sameSite: "Lax" }]);
    await page.addInitScript(({ user, businessId }) => {
      localStorage.setItem("selected_tenant_id", user.tenantId);
      localStorage.setItem("selected_business_id", businessId);
      localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
    }, { user, businessId });

    await page.route("**/api/**", async route => {
      const url = new URL(route.request().url());
      const path = url.pathname;
      let body: unknown = [];
      if (path === "/api/auth/me") body = user;
      else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Pruebas" }];
      else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Sede pruebas" }];
      else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, roles: [], permissions: user.permissions };
      else if (path.endsWith("/parties/role-options")) body = { items: [{ partyId, roleId: partyId, role: partyRole,
        displayName: partyName, identification: "1001", supplierPurchaseEvidencePolicy: null,
        supplierDefaultPaymentDueDays: null }], page: 1, totalPages: 1, totalCount: 1 };
      else if (path.endsWith(`/${direction}/${invoiceId}`)) body = detail;
      else if (path.endsWith(`/${direction}`)) body = { items: [invoice], page: 1, pageSize: 20,
        totalCount: 1, totalPages: 1, totalOutstanding: 3500, totalOverdue: 0, totalInvoiceCount: 1 };
      else if (path.endsWith("/payables/suppliers") || path.endsWith("/receivables/customers")) {
        partyListReads++;
        body = {
        items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0,
        totalOutstanding: 3500, totalOverdue: 0, totalInvoiceCount: 1,
        };
      }
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
    });

    await page.goto(`/dashboard/${direction}`);
    await page.getByRole("button", { name: "Ajuste de cartera", exact: true }).click();
    const modal = page.getByRole("dialog", { name: "Ajuste de cartera" });
    await expect(modal.getByRole("heading", { name: "Ajuste de cartera", level: 2 })).toBeVisible();
    await expect(modal.getByLabel("Valor del ajuste")).toBeDisabled();
    await modal.getByRole("combobox", { name: `Seleccionar ${partyRole.toLowerCase()}` }).click();
    await page.getByRole("option", { name: new RegExp(partyName) }).click();
    await modal.getByRole("combobox", { name: "Seleccionar factura para ajuste de cartera" }).click();
    await page.getByRole("option", { name: new RegExp(documentNumber) }).click();
    await expect(modal.getByText(`${partyName} · 1001`)).toBeVisible();
    await expect(modal.getByText(documentNumber, { exact: true }).last()).toBeVisible();
    await expect(modal.getByText("$ 3.500").first()).toBeVisible();
    await expect(modal.getByLabel("Valor del ajuste")).toBeEnabled();
    await expect(modal.getByRole("button", { name: "Close" })).toBeVisible();
    await modal.getByRole("button", { name: "Cerrar" }).click();
    await expect(modal).toHaveCount(0);
    await expect(page).toHaveURL(new RegExp(`/dashboard/${direction}$`));

    await page.getByRole("tab", { name: "Facturas" }).click();
    await page.getByRole("row", { name: new RegExp(documentNumber) }).click();
    expect(partyListReads).toBe(1);
    await page.getByRole("dialog", { name: documentNumber }).getByRole("button", { name: "Ajuste de cartera" }).click();
    await expect(modal.getByRole("combobox", { name: "Seleccionar factura para ajuste de cartera" })).toContainText(documentNumber);
    await expect(modal.getByText(`${partyName} · 1001`)).toBeVisible();
    await expect(modal.getByText("$ 3.500").first()).toBeVisible();
    await modal.getByRole("button", { name: "Close" }).click();
    await expect(modal).toHaveCount(0);
  });
}
