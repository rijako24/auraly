import { expect, test } from "@playwright/test";

for (const direction of ["receivables", "payables"] as const) {
  test(`${direction} consulta el informe por página sin traer toda la cartera`, async ({ page, baseURL }) => {
    const tenantId = "11111111-1111-1111-1111-111111111111";
    const businessId = "22222222-2222-2222-2222-222222222222";
    const user = { userId: "33333333-3333-3333-3333-333333333333", tenantId,
      tenantKey: "@auraly", username: "report-e2e", firstName: "Prueba", lastName: "Informes",
      roles: [], permissions: ["receivables.read", "payables.read"] };
    await page.context().addCookies([{ name: "auth_token", value: "report-e2e", url: baseURL!, httpOnly: true, sameSite: "Lax" }]);
    await page.addInitScript(({ user, businessId }) => {
      localStorage.setItem("selected_tenant_id", user.tenantId);
      localStorage.setItem("selected_business_id", businessId);
      localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
    }, { user, businessId });
    const reportRequests: URL[] = [];
    const printRequests: URL[] = [];
    await page.route("**/api/**", async route => {
      const url = new URL(route.request().url());
      const path = url.pathname;
      let body: unknown = [];
      if (path === "/api/auth/me") body = user;
      else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Auraly" }];
      else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Auraly" }];
      else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, roles: [], permissions: user.permissions };
      else if (path.endsWith(`/${direction}/report/print`)) {
        printRequests.push(url);
        return route.fulfill({ status: 200, contentType: "text/html",
          body: "<!doctype html><html lang=\"es\"><body><h1>Informe completo</h1></body></html>" });
      } else if (path.endsWith(`/${direction}/report`)) {
        reportRequests.push(url);
        const currentPage = Number(url.searchParams.get("page"));
        const isPayable = direction === "payables";
        body = {
          items: [{ ...(isPayable ? { supplierId: "sup-1", supplierName: "Proveedor A", payableId: `pay-${currentPage}` }
            : { customerId: "cus-1", customerName: "Cliente A", receivableId: `rec-${currentPage}` }),
            identification: "900100001", partySiteId: "site-1", partySiteName: "Centro", currencyCode: "COP",
            invoiceCount: 1, originalAmount: 10000, paidAmount: 2500, outstandingAmount: 7500, otherImpact: 0,
            overdueAmount: 0, notDueAmount: 7500, overdue1To30Amount: 0,
            overdue31To60Amount: 0, overdue61To90Amount: 0, overdueOver90Amount: 0,
            documentNumber: `FAC-${currentPage}`, issuedAt: "2026-10-01T12:00:00Z",
            dueDate: "2026-10-20T12:00:00Z", applications: [{ documentNumber: "AB-1", appliedAt: "2026-10-05T12:00:00Z", amount: 2500 }] }],
          page: currentPage, pageSize: 50, totalCount: 51, totalPages: 2,
          ...(isPayable ? { currencyTotals: [{ currencyCode: "COP", invoiceCount: 51,
            originalAmount: 510000, paidAmount: 127500, outstandingAmount: 382500, overdueAmount: 0, otherImpact: 0,
            notDueAmount: 382500, overdue1To30Amount: 0, overdue31To60Amount: 0,
            overdue61To90Amount: 0, overdueOver90Amount: 0 }] }
            : { totalInvoiceCount: 51, totalOriginal: 510000, totalPaid: 127500,
              totalOutstanding: 382500, totalOverdue: 0, totalOtherImpact: 0,
              totalNotDue: 382500, totalOverdue1To30: 0, totalOverdue31To60: 0,
              totalOverdue61To90: 0, totalOverdueOver90: 0 }),
        };
      } else if (path.endsWith(`/${direction}/customers`) || path.endsWith(`/${direction}/suppliers`)) {
        body = { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0,
          totalOutstanding: 0, totalOverdue: 0 };
      } else if (path.endsWith(`/${direction}`) || path.endsWith("/receivable-payments") || path.endsWith("/payable-payments")) {
        body = { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 };
      } else if (path.endsWith("/portfolio/parties/site-options")) {
        body = { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 };
      }
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
    });

    await page.goto(`/dashboard/${direction}`);
    await page.getByText("Filtros", { exact: true }).click();
    const filteredList = page.waitForResponse(response => response.url().includes(`/api/commerce/v1/${direction}`)
      && new URL(response.url()).searchParams.get("search") === "FAC");
    await page.getByPlaceholder("Número de documento o identificación").fill("FAC");
    await filteredList;
    await page.getByRole("button", { name: "Reportes" }).click();
    const dialog = page.getByRole("dialog", { name: direction === "receivables"
      ? "Informe de cuentas por cobrar" : "Informe de cuentas por pagar" });
    await expect(dialog.getByText("FAC-1", { exact: true })).toBeVisible();
    await expect(dialog.getByText(/AB-1/)).toBeVisible();
    await expect(dialog.getByRole("heading", { name: direction === "receivables" ? "Cliente A" : "Proveedor A" })).toBeVisible();
    const document = dialog.locator("article").filter({ hasText: "FAC-1" });
    await expect(document.getByText("Saldo pendiente")).toBeVisible();
    await expect(document.getByText(/AB-1/)).toBeVisible();
    await expect(document.getByText("Al día", { exact: true })).toBeVisible();
    await expect(dialog.getByRole("region", { name: "Edades de cartera" })).toBeVisible();
    await page.setViewportSize({ width: 390, height: 844 });
    await expect(document).toBeVisible();
    expect(await document.evaluate(element => element.getBoundingClientRect().right <= window.innerWidth)).toBe(true);
    await page.setViewportSize({ width: 1440, height: 1000 });
    expect(reportRequests).toHaveLength(1);
    expect(reportRequests[0].searchParams.get("pageSize")).toBe("50");
    expect(reportRequests[0].searchParams.get("search")).toBe("FAC");
    expect(reportRequests[0].searchParams.has("sortDirection")).toBe(false);
    await dialog.getByRole("button", { name: "Siguiente" }).click();
    await expect(dialog.getByText("FAC-2", { exact: true })).toBeVisible();
    expect(reportRequests).toHaveLength(2);
    expect(reportRequests[1].searchParams.get("page")).toBe("2");
    await dialog.getByRole("button", { name: /Consolidado/ }).click();
    await expect.poll(() => reportRequests.length).toBe(3);
    const summaryRequest = reportRequests.at(-1)!;
    expect(summaryRequest.searchParams.get("page")).toBe("1");
    expect(summaryRequest.searchParams.get("sortBy")).toBeNull();
    expect(summaryRequest.searchParams.has("sortDirection")).toBe(false);
    expect(summaryRequest.searchParams.get("consolidated")).toBe("true");
    let popups = 0;
    page.on("popup", () => popups++);
    await dialog.getByRole("button", { name: "Imprimir informe completo" }).click();
    await expect(dialog.frameLocator('iframe[title="Impresión del informe"]').getByRole("heading", { name: "Informe completo" })).toHaveText("Informe completo");
    expect(popups).toBe(0);
    expect(printRequests).toHaveLength(1);
    expect(printRequests[0].searchParams.has("pageSize")).toBe(false);
    await dialog.getByRole("button", { name: "Detallado" }).click();
    await dialog.getByRole("combobox", { name: "Ordenar documentos por" }).click();
    await page.getByRole("option", { name: "Valor original" }).click();
    await expect.poll(() => reportRequests.at(-1)?.searchParams.get("sortBy")).toBe("originalAmount");
    await dialog.getByRole("button", { name: "Ascendente" }).click();
    await expect.poll(() => reportRequests.at(-1)?.searchParams.get("sortDirection")).toBe("desc");
  });
}
