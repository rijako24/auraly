import { expect, test } from "@playwright/test";

for (const direction of ["receivables", "payables"] as const) {
  test(`${direction} abre e imprime el comprobante contable del pago seleccionado`, async ({ page, baseURL }) => {
    const tenantId = "11111111-1111-1111-1111-111111111111";
    const businessId = "22222222-2222-2222-2222-222222222222";
    const paymentId = "33333333-3333-3333-3333-333333333333";
    const sourceDocumentType = direction === "receivables" ? "ReceivablePayment" : "PayablePayment";
    const user = { userId: "44444444-4444-4444-4444-444444444444", tenantId,
      tenantKey: "@auraly", username: "voucher-e2e", firstName: "Prueba", lastName: "Cartera",
      roles: [], permissions: ["receivables.read", "payables.read", "accounting.read"] };
    await page.context().addCookies([{ name: "auth_token", value: "voucher-e2e", url: baseURL!, httpOnly: true, sameSite: "Lax" }]);
    await page.addInitScript(({ user, businessId }) => {
      localStorage.setItem("selected_tenant_id", user.tenantId);
      localStorage.setItem("selected_business_id", businessId);
      localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
    }, { user, businessId });

    const accountingRequests: string[] = [];
    await page.route("**/api/**", async route => {
      const path = new URL(route.request().url()).pathname;
      let body: unknown = [];
      if (path === "/api/auth/me") body = user;
      else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Auraly" }];
      else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Sede de prueba" }];
      else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, roles: [], permissions: user.permissions };
      else if (path.endsWith("/receivable-payments") || path.endsWith("/payable-payments")) body = {
        items: [{ paymentId, paidAt: "2026-10-08T12:00:00Z", documentNumber: "RC-100",
          currencyCode: "COP", totalAmount: 10000, status: "Processed", appliedDocumentCount: 1,
          payments: [{ methodCode: "Cash" }], applications: [{ receivableId: paymentId,
            payableId: paymentId, documentNumber: "FAC-1", amount: 10000 }],
          customerName: "Cliente A", supplierName: "Proveedor A" }],
        page: 1, pageSize: 20, totalCount: 1, totalPages: 1,
      };
      else if (path.endsWith(`/accounting/postings/by-document/${paymentId}`)) {
        accountingRequests.push(path);
        body = { sourceDocumentId: paymentId, sourceDocumentType, status: "Posted",
          errorCode: null, errorMessage: null, entryId: "55555555-5555-5555-5555-555555555555" };
      } else if (path.endsWith(`/accounting/entries/by-document/${paymentId}`)) {
        accountingRequests.push(path);
        body = { entryId: "55555555-5555-5555-5555-555555555555", entryNumber: "CC-100",
          sourceDocumentId: paymentId, sourceDocumentType, occurredAt: "2026-10-08T12:00:00Z",
          postedAt: "2026-10-08T12:00:01Z", description: "Recaudo aplicado", debitTotal: 10000,
          creditTotal: 10000, lines: [{ lineNumber: 1, accountCode: "110505", accountName: "Caja",
            debit: 10000, credit: 0, partyId: null, partyIdentification: null, partyName: null,
            costCenterId: null, costCenterCode: null, costCenterName: null, description: "Efectivo" }] };
      } else if (path.endsWith("/suppliers") || path.endsWith("/customers") ||
        path.endsWith("/payables") || path.endsWith("/receivables")) body = {
        items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0,
        totalOutstanding: 0, totalOverdue: 0, totalInvoiceCount: 0, totalSupplierCredit: 0,
      };
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
    });

    await page.goto(`/dashboard/${direction}`);
    await page.getByRole("tab", { name: direction === "receivables" ? "Recaudos" : "Pagos" }).click();
    await page.getByRole("button", { name: "Ver e imprimir" }).click();
    const dialog = page.getByRole("dialog");
    await expect(dialog.getByText("Comprobante contable CC-100")).toBeVisible();
    await dialog.getByRole("button", { name: "Abrir reporte" }).click();
    await expect(dialog.getByRole("button", { name: "Imprimir" })).toBeVisible();
    await page.evaluate(() => {
      (window as Window & { printCalls?: number }).printCalls = 0;
      window.print = () => {
        const tracked = window as Window & { printCalls?: number };
        tracked.printCalls = (tracked.printCalls ?? 0) + 1;
      };
    });
    await dialog.getByRole("button", { name: "Imprimir" }).click();
    expect(await page.evaluate(() => (window as Window & { printCalls?: number }).printCalls)).toBe(1);
    expect(accountingRequests).toEqual([
      `/api/commerce/v1/accounting/postings/by-document/${paymentId}`,
      `/api/commerce/v1/accounting/entries/by-document/${paymentId}`,
    ]);
  });
}
