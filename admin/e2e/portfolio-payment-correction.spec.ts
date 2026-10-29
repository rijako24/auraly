import { expect, test } from "@playwright/test";

for (const direction of ["receivables", "payables"] as const) {
  test(`${direction} muestra una sola acción de anulación y oculta la acción con saldo revertido`, async ({ page, baseURL }) => {
    const tenantId = "11111111-1111-1111-1111-111111111111";
    const businessId = "22222222-2222-2222-2222-222222222222";
    const paymentId = "33333333-3333-3333-3333-333333333333";
    const partyId = "44444444-4444-4444-4444-444444444444";
    const accountId = "55555555-5555-5555-5555-555555555555";
    const permissions = ["accounting.read", "accounting.manual.create", "accounting.manual.send",
      "receivables.read", "receivables.payments.create", "payables.read", "payables.payments.create"];
    const user = { userId: "66666666-6666-6666-6666-666666666666", tenantId, tenantKey: "@auraly",
      username: "correction-e2e", firstName: "Prueba", lastName: "Cartera", roles: [], permissions };
    await page.context().addCookies([{ name: "auth_token", value: "correction-e2e", url: baseURL!, httpOnly: true, sameSite: "Lax" }]);
    await page.addInitScript(({ user, businessId }) => {
      localStorage.setItem("selected_tenant_id", user.tenantId);
      localStorage.setItem("selected_business_id", businessId);
      localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
    }, { user, businessId });

    const captures: string[] = [];
    let sessionCalls = 0;
    let annulled = false;
    await page.route("**/api/**", async route => {
      const url = new URL(route.request().url());
      const path = url.pathname;
      let body: unknown = [];
      if (path === "/api/auth/me") body = user;
      else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Auraly" }];
      else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Sede" }];
      else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, roles: [], permissions };
      else if (path.endsWith("/reference-options/payment-method")) body = [{ id: "77777777-7777-7777-7777-777777777777", code: "Cash", label: "Efectivo", sortOrder: 1 }];
      else if (path.endsWith("/reference-options/accounting-manual-concept")) body = [{ id: "concept", code: "MANUAL_VOUCHER", label: "Comprobante manual" }];
      else if (path.endsWith("/reference-options/accounting-voucher-type")) body = [{ id: "accounting", code: "Accounting", label: "Comprobante contable" }];
      else if (path.endsWith("/receivable-payments") || path.endsWith("/payable-payments")) body = {
        items: [{ paymentId, paidAt: "2026-10-28T12:00:00Z", documentNumber: "RCC-100",
          currencyCode: "COP", totalAmount: 10000, status: "Processed", netAppliedAmount: annulled ? 0 : 10000,
          appliedDocumentCount: 1, payments: [{ methodCode: "Cash" }],
          applications: [{ receivableId: paymentId, payableId: paymentId, documentNumber: "FAC-1", amount: 10000 }],
          customerName: "Cliente A", supplierName: "Proveedor A" }],
        page: 1, pageSize: 20, totalCount: 1, totalPages: 1,
      };
      else if (path.endsWith("/payment-correction-capture")) {
        captures.push(url.searchParams.toString());
        const payable = direction === "payables";
        body = { description: "Corrección de RCC-100", reference: "RCC-100", mainPartyId: partyId,
          mainPartyName: "Tercero A", mainPartyIdentification: "900123456", lines: [
            { value: { accountId, partyId, costCenterId: null, description: "Reabrir FAC-1",
              debit: payable ? 0 : 10000, credit: payable ? 10000 : 0,
              existingPosition: { subledgerKind: payable ? "Payable" : "Receivable", subledgerId: paymentId,
                reversesTransactionId: "88888888-8888-8888-8888-888888888888" } },
              accountCode: "130505", accountName: "Cartera", partyName: "Tercero A" },
            { value: { accountId, partyId, costCenterId: null, description: "Reversar efectivo",
              debit: payable ? 10000 : 0, credit: payable ? 0 : 10000 },
              accountCode: "110505", accountName: "Caja", partyName: "Tercero A" },
          ] };
      }
      else if (path.endsWith("/work-sessions/current")) { sessionCalls++; body = { workSessionId: "99999999-9999-9999-9999-999999999999" }; }
      else if (path.endsWith("/suppliers") || path.endsWith("/customers") ||
        path.endsWith("/payables") || path.endsWith("/receivables")) body = {
        items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0,
        totalOutstanding: 0, totalOverdue: 0, totalInvoiceCount: 0, totalSupplierCredit: 0,
      };
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
    });

    await page.goto(`/dashboard/${direction}`);
    await page.getByRole("tab", { name: direction === "receivables" ? "Recaudos" : "Pagos" }).click();
    const row = page.getByRole("row", { name: /RCC-100/ });
    const action = direction === "receivables" ? "Anular recaudo" : "Anular pago";
    await expect(row.getByRole("button", { name: action })).toBeVisible();
    await expect(row.getByRole("button", { name: "Registrar devolución" })).toHaveCount(0);
    await row.getByRole("button", { name: action }).click();
    await expect(page.getByRole("dialog").getByRole("heading", {
      name: action,
    })).toBeVisible();
    await expect(page.getByRole("dialog").locator('input[value="Reabrir FAC-1"]')).toBeVisible();
    expect(sessionCalls).toBe(0);
    expect(captures).toEqual([`subledgerKind=${direction === "payables" ? "Payable" : "Receivable"}&paymentId=${paymentId}`]);
    annulled = true;
    await page.reload();
    await page.getByRole("tab", { name: direction === "receivables" ? "Recaudos" : "Pagos" }).click();
    const annulledRow = page.getByRole("row", { name: /RCC-100/ });
    await expect(annulledRow).toContainText("Aplicación revertida");
    await expect(annulledRow.getByRole("button", { name: action })).toHaveCount(0);
    expect(sessionCalls).toBe(0);
  });
}
