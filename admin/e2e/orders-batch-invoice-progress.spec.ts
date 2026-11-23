import { expect, test } from "@playwright/test";
import { login } from "./support/auth";

test.use({ timezoneId: "Asia/Tokyo" });

for (const paymentCode of ["Cash", "DebitCard", "CreditCard", "Transfer", "PendingCollection", "Credit"] as const) {
test(`factura e imprime pedidos con evidencia de ${paymentCode} en una instalación`, async ({ page, baseURL }) => {
  test.setTimeout(120_000);
  await page.clock.setFixedTime(new Date("2026-11-17T01:30:00Z"));
  if (process.env.AURALY_E2E_TENANT_KEY) await login(page, undefined, 90_000);
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
    timeZone: "America/Bogota",
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
  let brandingReads = 0;
  let orderReads = 0;
  const longPaymentName = "Medio del lote · Recepción de pedidos y ventas del mostrador principal";
  const invoiceRequests: Array<{
    paymentMethodCode: string; documentType?: string; paymentMethodId?: string; cardFranchiseCode?: string; approvalNumber?: string; paymentReference?: string; bankAccountId?: string;
    orderIds: string[];
    charges?: Array<{
      chargeId: string;
      chargeVersion: number;
      supplierId: string;
      manualAmount: number | null;
    }> | null;
  }> = [];
  const supplierId = "ffffffff-ffff-ffff-ffff-ffffffffffff";
  const chargeId = "99999999-9999-9999-9999-999999999999";

  if (!process.env.AURALY_E2E_TENANT_KEY)
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
    if (path.endsWith("/tenants/branding/print")) {
      brandingReads++;
      await route.fulfill({ status: 500, contentType: "application/json", body: JSON.stringify({ title: "Logo no disponible" }) });
      return;
    }
    let body: unknown = [];
    if (path === "/api/auth/me") body = user;
    else if (path === "/api/execution-context/tenants") body = [{ tenantId, name: "Tenant prueba" }];
    else if (path === "/api/execution-context/businesses") body = [{ businessId, tenantId, name: "Sede prueba", timeZone: "America/Bogota" }];
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
    else if (path.endsWith("/reference-options/card-franchise")) {
      body = [{ id: "visa", code: "Visa", label: "Visa", description: null, sortOrder: 1 }];
    }
    else if (path.endsWith("/payment-methods/")) {
      const creditFilter = url.searchParams.get("methodCode") === "Credit";
      if (creditFilter) expect(url.searchParams.get("excludeMethodCode")).toBeNull();
      else expect(url.searchParams.get("excludeMethodCode")).toBe("Credit");
      if(paymentCode==="PendingCollection")expect(url.searchParams.get("usage")).toBe("OrderReceipts");
      body = { items: [{ paymentMethodId: creditFilter ? "form-credit-native" : "form-cash-native", code: creditFilter ? "CREDIT-BATCH" : "CASH-BATCH", name: longPaymentName,
        methodCode: creditFilter ? "Credit" : paymentCode === "Credit" ? "Cash" : paymentCode,
        bankAccountId: paymentCode === "Transfer" ? "bank-configured" : null, accountCode: "111005", accountName: "Cuenta configurada",
        version: 1, accountId: "account-cash-native", isActive: true, useForSales: true }], page: 1, pageSize: 10, totalCount: 1 };
    }
    else if (path.endsWith("/orders") && route.request().method() === "GET") {
      orderReads += 1;
      const filters = new URL(route.request().url()).searchParams;
      expect(filters.get("createdFrom")).toBe("2026-11-16T05:00:00.000Z");
      expect(filters.get("createdTo")).toBe("2026-11-17T05:00:00.000Z");
      const items = orders.filter((order) => !completed.has(order.orderId));
      body = { items, page: 1, pageSize: 20, totalCount: items.length, hasMore: false };
    } else if (path.endsWith("/orders/invoice/credit-validation") && route.request().method() === "POST") {
      expect(route.request().postDataJSON()).toMatchObject({ paymentMethodCode: "Credit", paymentMethodId: "form-credit-native" });
      body = [];
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
  if(paymentCode==="PendingCollection") await page.getByRole("button",{name:"Comprobante de venta",exact:true}).click();
  const invoiceAction = page.getByRole("button", { name: paymentCode==="PendingCollection"?"Emitir de contado (3)":paymentCode==="Credit"?"Facturar a crédito (3)":"Facturar de contado (3)" });
  const refreshButton = page.getByRole("button", { name: "Actualizar pedidos", exact: true });
  await expect(refreshButton).toBeEnabled();
  const readsBeforeRefresh = orderReads;
  await refreshButton.click();
  await expect(refreshButton).toBeEnabled();
  expect(orderReads).toBe(readsBeforeRefresh + 1);
  await expect(page.getByRole("button", { name: "Efectivo", exact: true })).toHaveCount(0);
  await page.getByRole("button", { name: "Crédito", exact: true }).click();
  await expect(page.getByRole("combobox", { name: "Forma de pago del lote" })).toBeVisible();
  if (paymentCode !== "Credit") await page.getByRole("button", { name: "De contado", exact: true }).click();
  await expect(page.getByRole("combobox", { name: "Forma de pago del lote" })).toBeVisible();
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
  await expect(invoiceAction).toBeEnabled();
  await page.getByRole("combobox", { name: "Forma de pago del lote" }).click();
  const longPaymentOption = page.getByRole("option", { name: /Medio del lote/ });
  await expect(longPaymentOption.getByText(longPaymentName, { exact: true })).toBeVisible();
  const labelLayout = await longPaymentOption.getByText(longPaymentName, { exact: true }).evaluate((element) => ({
    fits: element.scrollWidth <= element.clientWidth && element.scrollHeight <= element.clientHeight,
    whiteSpace: getComputedStyle(element).whiteSpace,
    textOverflow: getComputedStyle(element).textOverflow,
  }));
  expect(labelLayout).toMatchObject({ fits: true, whiteSpace: "normal" });
  expect(labelLayout.textOverflow).not.toBe("ellipsis");
  for (const width of [390, 820, 1440]) {
    await page.setViewportSize({ width, height: 1000 });
    await expect(longPaymentOption).toBeVisible();
    await expect.poll(async () => {
      const bounds = await longPaymentOption.boundingBox();
      return Boolean(bounds && bounds.x >= 0 && bounds.x + bounds.width <= width);
    }).toBe(true);
    const fits = await longPaymentOption.getByText(longPaymentName, { exact: true }).evaluate((element) =>
      element.scrollWidth <= element.clientWidth && element.scrollHeight <= element.clientHeight);
    expect(fits).toBe(true);
  }
  await page.screenshot({ path: "../work/orders-toolbar-review/payment-options.png", fullPage: true });
  await longPaymentOption.click();
  if (paymentCode === "DebitCard" || paymentCode === "CreditCard" || paymentCode === "Transfer") {
    // Closing without saving must not confirm a payment; the next invoice action
    // requests the same missing evidence before any command leaves the browser.
    await page.getByRole("button", { name: paymentCode === "Transfer" ? "Cerrar datos de transferencia" : "Cerrar datos de tarjeta", exact: true }).click();
    await invoiceAction.click();
    expect(invoiceRequests).toHaveLength(0);
    if (paymentCode === "Transfer") {
      const dialog = page.getByRole("dialog", { name: "Datos de la transferencia", exact: true });
      await dialog.getByRole("textbox", { name: "Referencia", exact: true }).fill("TRANSFER-456");
      await dialog.getByRole("button", { name: "Guardar transferencia", exact: true }).click();
    } else {
      const dialog = page.getByRole("dialog", { name: "Datos de la tarjeta", exact: true });
      await dialog.getByRole("combobox").click();
      await expect(page.getByRole("option", { name: "Visa", exact: true })).toBeVisible();
      await page.keyboard.press("Escape");
      await expect(dialog).toBeVisible();
      await dialog.getByRole("combobox").click();
      await page.getByRole("option", { name: "Visa", exact: true }).click();
      await dialog.getByRole("textbox", { name: "Número de aprobación", exact: true }).fill("APPROVAL-456");
      await dialog.getByRole("button", { name: "Guardar datos · Enter", exact: true }).click();
    }
    await expect(page.getByRole("dialog", { name: /Datos de la/ })).toHaveCount(0);
  } else await expect(page.getByRole("dialog", { name: /Datos de la/ })).toHaveCount(0);
  await invoiceAction.click();

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
  expect(invoiceRequests).toEqual(expect.arrayContaining([expect.objectContaining({
    paymentMethodId: paymentCode === "Credit" ? "form-credit-native" : "form-cash-native",
    paymentFormCode: paymentCode === "Credit" ? "CREDIT-BATCH" : "CASH-BATCH" })]));
  expect(invoiceRequests.every(request => request.paymentMethodCode === paymentCode)).toBe(true);
  if(paymentCode==="PendingCollection")expect(invoiceRequests.every(request=>request.documentType==="SalesReceipt")).toBe(true);
  if (paymentCode === "DebitCard" || paymentCode === "CreditCard")
    expect(invoiceRequests.every(request => request.cardFranchiseCode === "Visa" && request.approvalNumber === "APPROVAL-456" && request.paymentReference === null)).toBe(true);
  if (paymentCode === "Transfer")
    expect(invoiceRequests.every(request => request.paymentReference === "TRANSFER-456" && request.bankAccountId === "bank-configured" && request.cardFranchiseCode === undefined)).toBe(true);
  expect(brandingReads).toBe(1);
  expect(invoiceRequests.every((request) => request.charges == null)).toBe(true);

  if (paymentCode !== "Cash") return;

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
  await page.getByRole("combobox", { name: "Forma de pago del lote" }).click();
  await page.getByRole("option", { name: /Medio del lote/ }).click();
  const chargeButton = page.getByRole("button", { name: "Facturar 2 pedidos con cargo" });
  await expect(chargeButton).toBeVisible();
  const chargeButtonBox = await chargeButton.boundingBox();
  expect(chargeButtonBox?.width).toBeLessThanOrEqual(44);
  await chargeButton.click();
  const chargeDialog = page.getByRole("dialog", { name: "Facturar pedidos con cargo" });
  await chargeDialog.getByRole("button", { name: /Domicilio/ }).click();
  await chargeDialog.getByRole("button", { name: "Agregar cargo" }).click();
  await chargeDialog.getByRole("button", { name: "Facturar 2 pedidos con 1 cargo" }).click();
  await expect(page.getByText("2/2", { exact: true })).toBeVisible({ timeout: 10_000 });
  await expect.poll(() => invoiceRequests.length).toBe(5);
  expect(invoiceRequests.slice(3)).toEqual([
    expect.objectContaining({
      orderIds: [orders[3].orderId],
      charges: [{ chargeId, chargeVersion: 1, supplierId, manualAmount: null }],
    }),
    expect.objectContaining({
      orderIds: [orders[4].orderId],
      charges: [{ chargeId, chargeVersion: 1, supplierId, manualAmount: null }],
    }),
  ]);
  expect(brandingReads).toBe(2);
});

}
