import { expect, test } from "@playwright/test";

test("factura, imprime y avanza el contador pedido por pedido en una instalación", async ({ page, baseURL }) => {
  const tenantId = "11111111-1111-1111-1111-111111111111";
  const businessId = "22222222-2222-2222-2222-222222222222";
  const warehouseId = "33333333-3333-3333-3333-333333333333";
  const userId = "44444444-4444-4444-4444-444444444444";
  const workSessionId = "55555555-5555-5555-5555-555555555555";
  const orders = Array.from({ length: 3 }, (_, index) => ({
    orderId: `aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa${index + 1}`,
    orderNumber: `PED-00${index + 1}`,
    status: "Available",
    source: 1,
    customerName: `Cliente ${index + 1}`,
    customerIdentification: `90000000${index + 1}`,
    customerPhone: null,
    currency: "COP",
    total: 11_900,
    lineCount: 1,
    createdAt: "2026-09-16T08:00:00-05:00",
    canInvoice: true,
    invoiceDocumentId: null,
    claim: null,
    customerId: `bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb${index + 1}`,
    partySiteId: `cccccccc-cccc-cccc-cccc-ccccccccccc${index + 1}`,
    partySiteName: "Principal",
  }));
  const user = {
    userId,
    tenantId,
    tenantKey: "TEST",
    username: "admin",
    firstName: "Admin",
    lastName: "Prueba",
    roles: ["ADMINISTRATOR"],
    permissions: ["orders.invoice", "sales.create"],
  };
  const workspace = {
    businessId,
    businessName: "Sede prueba",
    warehouseId,
    warehouseCode: "B01",
    warehouseName: "Principal",
    warehouseAllowsNegativeStockSales: true,
    hasActiveEdgeEnrollment: true,
    fiscalReadyForOnlineSales: true,
    fiscalReadyForEnrollment: true,
    hasDianDocumentQuota: true,
    fiscalWarningMessages: [],
  };
  const completed = new Set<string>();
  const sequence: string[] = [];
  const invoiceRequests: Array<{
    orderIds: string[];
    charge?: {
      chargeId: string;
      chargeVersion: number;
      supplierId: string;
      manualAmount: number | null;
    } | null;
  }> = [];
  const supplierId = "ffffffff-ffff-ffff-ffff-ffffffffffff";
  const chargeId = "99999999-9999-9999-9999-999999999999";

  await page.context().addCookies([{ name: "auth_token", value: "e2e", url: baseURL!, httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ tenantId, businessId, user }) => {
    localStorage.setItem("selected_tenant_id", tenantId);
    localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
  }, { tenantId, businessId, user });

  await page.route("http://127.0.0.1:47831/**", async (route) => {
    const url = new URL(route.request().url());
    if (url.pathname === "/edge/v1/print/receipt") {
      const receipt = route.request().postDataJSON() as { documentNumber: string };
      sequence.push(`print:start:${receipt.documentNumber}`);
      await new Promise((resolve) => setTimeout(resolve, 250));
      sequence.push(`print:end:${receipt.documentNumber}`);
    } else if (url.pathname === "/edge/v1/cash-drawer/open") {
      sequence.push("drawer");
    }
    await route.fulfill({ status: 204, headers: { "access-control-allow-origin": "*" } });
  });

  await page.route("**/api/**", async (route) => {
    const url = new URL(route.request().url());
    const path = url.pathname;
    let body: unknown = [];
    if (path === "/api/auth/me") body = user;
    else if (path === "/api/execution-context/tenants") body = [{ tenantId, name: "Tenant prueba" }];
    else if (path === "/api/execution-context/businesses") body = [{ businessId, tenantId, name: "Sede prueba" }];
    else if (path === "/api/execution-context/access") body = {
      tenantId,
      businessId,
      roles: user.roles,
      permissions: user.permissions,
    };
    else if (path.endsWith("/pos/workspace/options")) body = [workspace];
    else if (path.endsWith("/pos/workspace/select")) body = workspace;
    else if (path.endsWith("/work-sessions/current")) body = { workSessionId };
    else if (path.endsWith("/routes")) body = { items: [], page: 1, pageSize: 100, totalCount: 0, totalPages: 0 };
    else if (path.endsWith("/tenants/branding")) body = { tenantId, displayName: "Empresa prueba", legalName: null, logoUrl: null };
    else if (path.endsWith("/invoice-charges")) body = {
      items: [{
        chargeId,
        businessId,
        version: 1,
        code: "DOMICILIO",
        name: "Domicilio",
        calculationMode: "Fixed",
        value: 6000,
        inclusionMode: "UpToInvoiceAmount",
        invoiceAmountLimit: 80000,
        isActive: true,
        suppliers: [{
          supplierId,
          name: "Domiciliario prueba",
          identification: "900999999",
          defaultPaymentDueDays: 0,
          isActive: true,
        }],
      }],
      page: 1,
      pageSize: 100,
      totalPages: 1,
      totalCount: 1,
    };
    else if (path.endsWith("/orders/print-batch") && route.request().method() === "POST") {
      const request = route.request().postDataJSON() as { orderIds: string[] };
      body = orders
        .filter((order) => request.orderIds.includes(order.orderId))
        .map((order) => ({
          orderId: order.orderId,
          businessId,
          orderNumber: order.orderNumber,
          createdAt: order.createdAt,
          customerName: order.customerName,
          customerIdentification: order.customerIdentification,
          currency: order.currency,
          total: order.total,
          lines: [{
            productCode: `SKU-${order.orderNumber}`,
            productName: `Producto ${order.orderNumber}`,
            quantity: 1,
            unitPrice: order.total,
            discountAmount: 0,
            lineTotal: order.total,
          }],
        }));
    }
    else if (path.endsWith("/orders") && route.request().method() === "GET") {
      const items = orders.filter((order) => !completed.has(order.orderId));
      body = { items, page: 1, pageSize: 20, totalCount: items.length, hasMore: false };
    } else if (path.endsWith("/orders/invoice") && route.request().method() === "POST") {
      const request = route.request().postDataJSON() as (typeof invoiceRequests)[number];
      invoiceRequests.push(request);
      expect(request.orderIds).toHaveLength(1);
      const order = orders.find((candidate) => candidate.orderId === request.orderIds[0])!;
      sequence.push(`invoice:start:${order.orderNumber}`);
      await new Promise((resolve) => setTimeout(resolve, 250));
      completed.add(order.orderId);
      sequence.push(`invoice:end:${order.orderNumber}`);
      body = {
        operationId: `dddddddd-dddd-dddd-dddd-ddddddddddd${completed.size}`,
        status: "Completed",
        requestedCount: 1,
        completedCount: 1,
        failedCount: 0,
        isReplay: false,
        results: [{
          orderId: order.orderId,
          orderNumber: order.orderNumber,
          status: "Invoiced",
          documentId: `eeeeeeee-eeee-eeee-eeee-eeeeeeeeeee${completed.size}`,
          documentNumber: `FV-00${completed.size}`,
          error: null,
          receipt: {
            documentId: `eeeeeeee-eeee-eeee-eeee-eeeeeeeeeee${completed.size}`,
            documentNumber: `FV-00${completed.size}`,
            fiscalNumber: `FV-00${completed.size}`,
            cufe: null,
            qrPayload: null,
            issuedAt: "2026-09-16T09:00:00-05:00",
            customerName: order.customerName,
            customerIdentification: order.customerIdentification,
            untaxedAmount: 10_000,
            taxAmount: 1_900,
            payableAmount: 11_900,
            lines: [],
            payments: [],
          },
        }],
      };
    }
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
  });

  await page.goto("/dashboard/orders#edgeToken=edge-test-token");
  await expect(page.getByText("PED-001")).toBeVisible();
  await page.getByRole("checkbox", { name: "Seleccionar disponibles" }).click();
  await page.getByRole("button", { name: "Imprimir (3)" }).click();
  await expect.poll(() => sequence).toEqual([
    "print:start:PED-001",
    "print:end:PED-001",
    "print:start:PED-002",
    "print:end:PED-002",
    "print:start:PED-003",
    "print:end:PED-003",
  ]);
  sequence.length = 0;
  await expect(page.getByRole("button", { name: "Facturar en efectivo (3)" })).toBeEnabled();
  await page.getByRole("button", { name: "Facturar en efectivo (3)" }).click();

  await expect(page.getByText("1/3", { exact: true })).toBeVisible({ timeout: 10_000 });
  await expect(page.getByText("2/3", { exact: true })).toBeVisible({ timeout: 10_000 });
  await expect(page.getByText("3/3", { exact: true })).toBeVisible({ timeout: 10_000 });
  await expect.poll(() => sequence).toEqual([
    "invoice:start:PED-001",
    "invoice:end:PED-001",
    "print:start:FV-001",
    "print:end:FV-001",
    "invoice:start:PED-002",
    "invoice:end:PED-002",
    "print:start:FV-002",
    "print:end:FV-002",
    "invoice:start:PED-003",
    "invoice:end:PED-003",
    "print:start:FV-003",
    "print:end:FV-003",
    "drawer",
  ]);
  expect(invoiceRequests).toHaveLength(3);
  expect(invoiceRequests.every((request) => request.charge == null)).toBe(true);

  orders.push(...Array.from({ length: 2 }, (_, index) => ({
    ...orders[index],
    orderId: `aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa${index + 4}`,
    orderNumber: `PED-00${index + 4}`,
    customerName: `Cliente ${index + 4}`,
    customerIdentification: `90000000${index + 4}`,
    customerId: `bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb${index + 4}`,
    partySiteId: `cccccccc-cccc-cccc-cccc-ccccccccccc${index + 4}`,
  })));
  sequence.length = 0;
  await page.reload();
  await expect(page.getByText("PED-004")).toBeVisible();
  await page.getByRole("checkbox", { name: "Seleccionar disponibles" }).click();
  const chargeButton = page.getByRole("button", { name: "Facturar 2 pedidos con cargo" });
  await expect(chargeButton).toBeVisible();
  const chargeButtonBox = await chargeButton.boundingBox();
  expect(chargeButtonBox?.width).toBeLessThanOrEqual(44);
  await chargeButton.click();
  const chargeDialog = page.getByRole("dialog", { name: "Facturar pedidos con cargo" });
  await chargeDialog.getByRole("button", { name: /Domicilio/ }).click();
  await chargeDialog.getByRole("button", { name: "Facturar 2 pedidos con cargo" }).click();
  await expect(page.getByText("2/2", { exact: true })).toBeVisible({ timeout: 10_000 });
  await expect.poll(() => invoiceRequests.length).toBe(5);
  expect(invoiceRequests.slice(3)).toEqual([
    expect.objectContaining({
      orderIds: [orders[3].orderId],
      charge: { chargeId, chargeVersion: 1, supplierId, manualAmount: null },
    }),
    expect.objectContaining({
      orderIds: [orders[4].orderId],
      charge: { chargeId, chargeVersion: 1, supplierId, manualAmount: null },
    }),
  ]);
});
