import { expect, test } from "@playwright/test";

for (const scenario of [
  { direction: "receivable", shortcut: "Control+f", role: "Customer", dialog: "Abono a cartera", invoicePath: "/pos/receivables", confirmationPath: "/pos/receivable-payments/confirm", documentNumber: "ABO-001" },
  { direction: "payable", shortcut: "Control+g", role: "Supplier", dialog: "Pago a proveedores", invoicePath: "/pos/payables", confirmationPath: "/pos/payable-payments/confirm", documentNumber: "PGP-001" },
] as const) for (const installed of [false, true]) {
  test(`${scenario.dialog} en POS ${installed ? "instalado en línea" : "web"} despacha su tirilla`, async ({ page, baseURL }) => {
    const tenantId = "11111111-1111-1111-1111-111111111111";
    const businessId = "22222222-2222-2222-2222-222222222222";
    const warehouseId = "33333333-3333-3333-3333-333333333333";
    const partyId = "44444444-4444-4444-4444-444444444444";
    const invoiceId = "55555555-5555-5555-5555-555555555555";
    const paymentId = "66666666-6666-6666-6666-666666666666";
    const permissions = ["pos.sales.create", "pos.receivables.payments.create", "pos.payables.payments.create"];
    const user = { userId: "77777777-7777-7777-7777-777777777777", tenantId, tenantKey: "TEST",
      username: "cajero", firstName: "Laura", lastName: "Prueba", roles: [], permissions };
    const workspace = { businessId, warehouseId, businessName: "Sede prueba", warehouseName: "Principal",
      warehouseCode: "B01", warehouseAllowsNegativeStockSales: true, hasActiveEdgeEnrollment: false };
    await page.context().addCookies([{ name: "auth_token", value: "e2e", url: baseURL!, httpOnly: true, sameSite: "Lax" }]);
    await page.addInitScript(({ tenantId, businessId, warehouseId, user, installed }) => {
      localStorage.setItem("selected_tenant_id", tenantId);
      localStorage.setItem("selected_business_id", businessId);
      localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
      localStorage.setItem(`auraly.pos.sales-workspace:${tenantId}:${user.userId}`, `${businessId}:${warehouseId}`);
      if (installed) sessionStorage.setItem("auraly.pos.edge-token", "e2e-edge");
    }, { tenantId, businessId, warehouseId, user, installed });

    let renderBody: { receipt: { direction: string; documentNumber: string; nit: string; responsibleName: string; allocations: Array<{ documentNumber: string }> } } | null = null;
    let localPrint: { direction: string; documentNumber: string; partyIdentification: string; allocations: Array<{ documentNumber: string }> } | null = null;
    let localPrintCount = 0;
    const edgePaths: string[] = [];
    const renderGate: { release?: () => void } = {};
    let confirmCount = 0;
    let paymentDetailReads = 0;
    if (installed) await page.route("http://127.0.0.1:47831/edge/v1/**", async route => {
      const path = new URL(route.request().url()).pathname;
      edgePaths.push(`${route.request().method()} ${path}`);
      const headers = {
        "Access-Control-Allow-Origin": new URL(baseURL!).origin,
        "Access-Control-Allow-Credentials": "true",
        "Access-Control-Allow-Headers": "Content-Type,X-Auraly-Edge-Session,X-Auraly-User-Session",
        "Access-Control-Allow-Methods": "GET,POST,OPTIONS",
      };
      if (route.request().method() === "OPTIONS")
        return route.fulfill({ status: 204, headers });
      if (path.endsWith("/health"))
        return route.fulfill({ status: 200, headers, contentType: "application/json",
          body: JSON.stringify({ status: "EnrollmentRequired", identityReady: false }) });
      if (path.endsWith("/print/portfolio-payment")) {
        localPrintCount++;
        localPrint = route.request().postDataJSON();
        return route.fulfill({ status: 204, headers });
      }
      return route.fulfill({ status: 404, headers });
    });
    await page.route("**/api/**", async route => {
      const path = new URL(route.request().url()).pathname;
      let body: unknown = [];
      if (path === "/api/auth/me") body = user;
      else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Pruebas" }];
      else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Sede prueba" }];
      else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, roles: [], permissions };
      else if (path.endsWith("/workspace/bootstrap")) body = { tenantId, tenantName: "Empresa prueba", userId: user.userId,
        userDisplayName: "Laura Prueba", options: [workspace], canEnrollPosDevice: false,
        activeEnrolledDeviceCount: 0, maximumEnrolledDevices: 0 };
      else if (path.endsWith("/workspace/options")) body = [workspace];
      else if (path.endsWith("/workspace/select")) body = workspace;
      else if (path.endsWith("/work-sessions/current")) body = { workSessionId: "88888888-8888-8888-8888-888888888888" };
      else if (path.endsWith("/drafts/active")) body = { draftId: "draft", ...workspace,
        userId: user.userId, workSessionId: "88888888-8888-8888-8888-888888888888",
        status: "Active", version: 1, lines: [], charges: [], untaxedAmount: 0, taxAmount: 0, payableAmount: 0 };
      else if (path.endsWith("/parties/role-options")) body = { items: [{ role: scenario.role, roleId: partyId,
        partyId, displayName: "Tercero prueba", identification: "1001" }], page: 1, totalPages: 1, totalCount: 1 };
      else if (path.endsWith(scenario.invoicePath)) body = { items: [{
        ...(scenario.direction === "receivable" ? { receivableId: invoiceId } : { payableId: invoiceId }),
        documentNumber: "FV-001", dueDate: "2026-10-01T12:00:00-05:00", outstandingAmount: 125000,
        currencyCode: "COP", isOverdue: false }], page: 1, pageSize: 20, totalPages: 1, totalCount: 1 };
      else if (path.endsWith("/reference-options/payment-method")) body = [{ id: "cash", code: "Cash", label: "Efectivo", sortOrder: 1 }];
      else if (path.endsWith("/pos/settlement-configuration")) body = { isAccountingEnabled: false, bankAccounts: [] };
      else if (path.endsWith(scenario.confirmationPath)) { confirmCount++; body = { paymentId, documentNumber: scenario.documentNumber,
        accountingJobId: "99999999-9999-9999-9999-999999999999", status: "Accepted",
        idempotentReplay: false }; }
      else if (path.endsWith(`/${paymentId}`)) paymentDetailReads++;
      else if (path.endsWith("/tenants/branding")) body = { tenantId, displayName: "Empresa prueba", legalName: "Empresa prueba SAS", nit: "900123456", verificationDigit: "7", logoUrl: null };
      else if (path.endsWith(`/businesses/${businessId}`)) body = { businessId, name: "Sede prueba", address: "Calle 1", phone: "3001234567" };
      else if (path.endsWith("/portfolio-payments/receipt/render")) {
        renderBody = route.request().postDataJSON();
        await new Promise<void>(resolve => { renderGate.release = resolve; });
        body = { html: "<!doctype html><html><body>Comprobante POS</body></html>" };
      }
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
    });

    await page.goto("/pos");
    if (installed) expect(await page.evaluate(() => sessionStorage.getItem("auraly.pos.edge-token"))).toBe("e2e-edge");
    await expect(page.locator("#pos-scanner")).toBeEnabled();
    const dialog = page.getByRole("dialog", { name: scenario.dialog });
    await expect(async () => {
      await page.keyboard.press(scenario.shortcut);
      await expect(dialog).toBeVisible({ timeout: 500 });
    }).toPass({ timeout: 10_000 });
    await dialog.getByRole("combobox", { name: `Seleccionar ${scenario.role.toLowerCase()}` }).click();
    await page.getByRole("option", { name: /Tercero prueba/ }).click();
    await expect(dialog.getByText("FV-001")).toBeVisible();
    await dialog.getByRole("button", { name: "Ir a pagar" }).click();
    await dialog.getByRole("button", { name: "Confirmar pago" }).click();

    await expect(dialog).not.toBeVisible();
    if (installed) await expect.poll(() => localPrint, { message: JSON.stringify({ edgePaths, renderBody }) }).not.toBeNull();
    else await expect.poll(() => renderBody).not.toBeNull();
    expect(confirmCount).toBe(1);
    expect(paymentDetailReads).toBe(0);
    expect(installed ? localPrint : renderBody!.receipt).toMatchObject({
      direction: scenario.direction === "receivable" ? "Receivable" : "Payable",
      documentNumber: scenario.documentNumber,
    });
    if (!installed) {
      expect(renderBody!.receipt).toMatchObject({
        nit: "900123456", responsibleName: "Laura Prueba",
        allocations: [{ documentNumber: "FV-001" }],
      });
      renderGate.release?.();
    } else {
      expect(localPrint).toMatchObject({ partyIdentification: "1001",
        allocations: [{ documentNumber: "FV-001" }] });
      expect(localPrintCount).toBe(1);
      expect(renderBody).toBeNull();
    }
  });
}
