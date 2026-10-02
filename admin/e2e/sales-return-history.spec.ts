import { expect, test } from "@playwright/test";

const tenantId = "11111111-1111-1111-1111-111111111111";
const businessId = "22222222-2222-2222-2222-222222222222";
const returnId = "44444444-4444-4444-4444-444444444444";
const customerId = "55555555-5555-5555-5555-555555555555";

for (const { format, mode } of [
  { format: "Receipt", mode: "browser" }, { format: "HalfLetter", mode: "browser" },
  { format: "Receipt", mode: "installed-online" }, { format: "Receipt", mode: "prepared" },
]) {
  test(`historial: filtros remotos y reimpresión directa en ${format} (${mode})`, async ({ page, baseURL }) => {
    const user = { userId: "33333333-3333-3333-3333-333333333333", tenantId,
      tenantKey: "@returns", username: "returns", firstName: "Prueba", lastName: "Devoluciones",
      roles: [], permissions: ["sales.returns.read", "sales.returns.create"] };
    await page.context().addCookies([{ name: "auth_token", value: "returns", url: baseURL!, httpOnly: true, sameSite: "Lax" }]);
    await page.addInitScript(({ user, businessId, format, mode }) => {
      localStorage.setItem("selected_tenant_id", user.tenantId);
      localStorage.setItem("selected_business_id", businessId);
      localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
      localStorage.setItem("auraly.printing.configuration.v1", JSON.stringify({ posOutputFormat: format, receiptPaperWidthMillimeters: 58 }));
      if (mode !== "browser") sessionStorage.setItem("auraly.pos.edge-token", "print-test");
    }, { user, businessId, format, mode });
    const item = { returnId, documentNumber: "DVT-30", originalDocumentNumber: "FV-200", originalDocumentId: crypto.randomUUID(), customerName: "Cliente histórico", returnedAt: "2026-09-30T12:00:00-05:00", totalAmount: 8000, economicResolution: "Refund", status: "Processed", fiscalStatus: "Pending", reasonCode: "Return" };
    const receipt = { documentId: returnId, documentType: "SalesReturn", documentNumber: "DVT-30", lines: [{ description: "Producto devuelto", quantity: 1, total: 8000 }], payableAmount: 8000 };
    let saleReads = 0;
    const historyQueries: URL[] = [];
    const rendered: Record<string, unknown>[] = [];
    const edgePrinted: Record<string, unknown>[] = [];
    let brandingReads = 0;
    const mutations: string[] = [];
    await page.route("**/edge/v1/**", async route => {
      const path = new URL(route.request().url()).pathname;
      if (path.endsWith("/print/receipt")) {
        edgePrinted.push(route.request().postDataJSON());
        await route.fulfill({ status: 204 });
        return;
      }
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({
        status: mode === "prepared" ? "Ready" : "EnrollmentRequired", identityReady: mode === "prepared", businessId,
      }) });
    });
    await page.route("**/api/**", async route => {
      const request = route.request(), url = new URL(request.url()), path = url.pathname;
      let body: unknown = [];
      if (path === "/api/auth/me") body = user;
      else if (path.endsWith("/tenants/branding/print")) { brandingReads++; body = { tenantId, displayName: "Empresa", legalName: "Empresa", logoUrl: null, nit: "123", verificationDigit: null }; }
      else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Empresa" }];
      else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Sede" }];
      else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, permissions: user.permissions, roles: [] };
      else if (path.endsWith("/sales-returns/sales")) { saleReads++; body = { items: [], page: 1, pageSize: Number(url.searchParams.get("pageSize") ?? 20), totalCount: 0, totalPages: 0 }; }
      else if (path.endsWith("/sales-returns")) { historyQueries.push(url); body = { items: [item], page: 1, pageSize: Number(url.searchParams.get("pageSize") ?? 20), totalCount: 1, totalPages: 1 }; }
      else if (path.endsWith(`/sales-returns/${returnId}`)) body = { ...item, receipt, customerIdentification: "123", warehouseId: crypto.randomUUID(), warehouseName: "Principal", businessName: "Sede", reasonDescription: "Devolución parcial", untaxedAmount: 8000, taxAmount: 0, roundingAmount: 0 };
      else if (path.endsWith("/parties/role-options")) body = { items: [{ partyId: customerId, roleId: customerId, role: "Customer", displayName: "Cliente remoto", identification: "123" }], page: 1, pageSize: 10, totalPages: 1, totalCount: 1 };
      else if (path.endsWith("/sales/receipts/render")) { rendered.push(request.postDataJSON()); body = { html: "<html><body>Copia de prueba</body></html>" }; }
      if (request.method() !== "GET" && !path.endsWith("/sales/receipts/render") && path.includes("/commerce/")) mutations.push(path);
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
    });
    await page.goto("/dashboard/sales-returns");
    await expect(page.getByRole("tab", { name: "Nueva devolución" })).toBeVisible();
    expect(brandingReads).toBe(0);
    expect(historyQueries).toHaveLength(0);
    await page.getByRole("tab", { name: "Devoluciones realizadas" }).click();
    await expect(page.getByText("DVT-30", { exact: true })).toBeVisible();
    const previousSaleReads = saleReads;
    await page.getByRole("combobox", { name: "Seleccionar customer" }).click();
    await page.getByPlaceholder("Buscar por nombre o identificación…").fill("Cliente");
    await page.getByRole("option", { name: /Cliente remoto/ }).click();
    await expect.poll(() => historyQueries.at(-1)?.searchParams.get("customerId")).toBe(customerId);
    expect(saleReads).toBe(previousSaleReads);
    await expect(page.getByRole("button", { name: "Reimprimir", exact: true })).toBeEnabled();
    const brandingBeforePrint = brandingReads;
    expect(brandingBeforePrint).toBe(0);
    await page.getByRole("button", { name: "Reimprimir", exact: true }).click();
    if (mode === "browser") {
      await expect.poll(() => rendered.length).toBe(1);
      expect(rendered[0].format).toBe(format);
      expect(rendered[0].paperWidthMillimeters).toBe(format === "Receipt" ? 58 : 80);
    } else {
      await expect.poll(() => edgePrinted.length).toBe(1);
      expect(edgePrinted[0].documentType).toBe("SalesReturn");
      expect(edgePrinted[0].companyLogoSource).toBe(mode === "prepared" ? null : "");
    }
    expect(brandingReads).toBe(mode === "prepared" ? 0 : 1);
    await expect(page.getByRole("dialog")).toHaveCount(0);
    await page.getByRole("button", { name: "Ver detalle", exact: true }).click();
    await expect(page.getByRole("dialog").getByText("Producto devuelto")).toBeVisible();
    expect(mutations).toEqual([]);
    await page.screenshot({ path: test.info().outputPath(`history-${format}.png`), fullPage: true });
  });
}
