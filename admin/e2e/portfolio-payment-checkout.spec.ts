import { expect, test } from "@playwright/test";

test("abono usa la caja de pago del POS y refresca la cartera al confirmar", async ({ page, baseURL }) => {
  const tenantId = "11111111-1111-1111-1111-111111111111";
  const businessId = "22222222-2222-2222-2222-222222222222";
  const customerId = "33333333-3333-3333-3333-333333333333";
  const receivableId = "44444444-4444-4444-4444-444444444444";
  const user = { userId: "55555555-5555-5555-5555-555555555555", tenantId,
    tenantKey: "@portfolio-test", username: "portfolio-test", firstName: "Prueba", lastName: "Cartera",
    roles: [], permissions: ["receivables.read", "receivables.payments.create"] };
  await page.context().addCookies([{ name: "auth_token", value: "portfolio-test", url: baseURL!, httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ user, businessId }) => {
    localStorage.setItem("selected_tenant_id", user.tenantId);
    localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
  }, { user, businessId });

  let paid = false;
  let customerReads = 0;
  let paymentInvoiceReads = 0;
  let confirmation: { payments: Array<{ methodCode: string; amount: number; cardFranchiseCode: string; approvalNumber: string }> } | null = null;
  const invoice = { receivableId, customerId, customerName: "Cliente prueba", partySiteId: null,
    partySiteName: null, documentNumber: "FV-001", currencyCode: "COP", originalAmount: 10450.45,
    outstandingAmount: paid ? 0 : 10450.45, dueDate: "2026-10-01T12:00:00-05:00", status: "Open",
    isOverdue: false, createdAt: "2026-09-23T12:00:00-05:00" };
  await page.route("**/api/**", async route => {
    const url = new URL(route.request().url());
    const path = url.pathname;
    let body: unknown = [];
    if (path === "/api/auth/me") body = user;
    else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Pruebas" }];
    else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Sede pruebas" }];
    else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, roles: [], permissions: user.permissions };
    else if (path.endsWith("/receivables/customers")) {
      customerReads++;
      body = { items: [{ customerId, customerName: "Cliente prueba", identification: "1001", invoiceCount: 1,
        originalAmount: 10450.45, paidAmount: paid ? 10450.45 : 0, outstandingAmount: paid ? 0 : 10450.45,
        overdueAmount: 0 }], page: 1, pageSize: 20, totalCount: 1, totalPages: 1,
        totalOutstanding: paid ? 0 : 10450.45, totalOverdue: 0, totalInvoiceCount: 1 };
    } else if (path.endsWith("/receivables")) {
      if (url.searchParams.get("outstandingOnly") === "true") paymentInvoiceReads++;
      body = { items: paid ? [] : [invoice], page: 1, pageSize: 20, totalCount: paid ? 0 : 1,
        totalPages: paid ? 0 : 1, totalOutstanding: paid ? 0 : 10450.45, totalOverdue: 0 };
    }
    else if (path.endsWith("/receivable-payments")) body = { items: paid ? [{ paymentId: "66666666-6666-6666-6666-666666666666",
      documentNumber: "RCC-001", paidAt: "2026-09-23T12:00:00-05:00", currencyCode: "COP", totalAmount: 10450.45,
      status: "Accepted", appliedDocumentCount: 1, payments: [{ methodCode: "DebitCard" }],
      applications: [{ receivableId, documentNumber: "FV-001", amount: 10450.45 }], customerId,
      customerName: "Cliente prueba" }] : [], page: 1, pageSize: 20, totalCount: paid ? 1 : 0, totalPages: paid ? 1 : 0 };
    else if (path.endsWith("/reference-options/payment-method")) body = [
      { id: "cash", code: "Cash", label: "Efectivo", sortOrder: 1 },
      { id: "debit", code: "DebitCard", label: "Tarjeta débito", sortOrder: 2 },
      { id: "transfer", code: "Transfer", label: "Transferencia", sortOrder: 3 },
    ];
    else if (path.endsWith("/reference-options/card-franchise")) body = [{ id: "visa", code: "Visa", label: "Visa", sortOrder: 1 }];
    else if (path.endsWith("/pos/settlement-configuration")) body = { isAccountingEnabled: false, bankAccounts: [] };
    else if (path.endsWith("/parties/role-options")) body = { items: [], page: 1, totalPages: 0, totalCount: 0 };
    else if (path.endsWith("/work-sessions/current")) body = { workSessionId: "77777777-7777-7777-7777-777777777777" };
    else if (path.endsWith("/receivable-payments/confirm")) {
      confirmation = route.request().postDataJSON();
      paid = true;
      body = { paymentId: "66666666-6666-6666-6666-666666666666", documentNumber: "RCC-001",
        accountingJobId: "88888888-8888-8888-8888-888888888888", status: "Accepted", idempotentReplay: false };
    }
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
  });

  await page.goto("/dashboard/receivables");
  await page.getByRole("row", { name: /Cliente prueba/ }).click();
  await page.getByRole("button", { name: "Ir a pagar" }).click();
  const checkout = page.getByRole("dialog", { name: "Abono a cartera" });
  await expect(checkout.getByText("Recibo de abono a cartera")).toBeVisible();
  await expect(checkout.getByRole("button", { name: "Confirmar pago" })).toBeEnabled();
  await page.keyboard.press("F2");
  const card = page.getByRole("dialog", { name: "Datos de la tarjeta" });
  await expect(card).toBeVisible();
  await card.getByPlaceholder("Aprobación del datáfono").fill("AP-001");
  await card.getByRole("button", { name: /Guardar datos/ }).click();
  await checkout.getByRole("button", { name: "Confirmar pago" }).click();
  await expect(checkout).not.toBeVisible();
  expect(confirmation).toMatchObject({ payments: [{ methodCode: "DebitCard", amount: 10450.45,
    cardFranchiseCode: "Visa", approvalNumber: "AP-001" }] });
  await expect.poll(() => customerReads).toBeGreaterThan(1);
  await expect(page.getByLabel("Resumen de cartera")).toContainText("$ 0");
  const readsAfterPayment = paymentInvoiceReads;
  await page.getByRole("row", { name: /Cliente prueba/ }).click();
  await expect(page.getByText("Este tercero no tiene facturas pendientes.")).toBeVisible();
  expect(paymentInvoiceReads).toBe(readsAfterPayment + 1);
});

test("pago a proveedor muestra cuatro medios, usa F1-F4 y captura tarjeta", async ({ page, baseURL }) => {
  const tenantId = "11111111-1111-1111-1111-111111111111";
  const businessId = "22222222-2222-2222-2222-222222222222";
  const supplierId = "33333333-3333-3333-3333-333333333333";
  const payableId = "44444444-4444-4444-4444-444444444444";
  const user = { userId: "55555555-5555-5555-5555-555555555555", tenantId,
    tenantKey: "@portfolio-test", username: "portfolio-test", firstName: "Prueba", lastName: "Proveedor",
    roles: [], permissions: ["payables.read", "payables.payments.create"] };
  await page.context().addCookies([{ name: "auth_token", value: "portfolio-test", url: baseURL!, httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ user, businessId }) => {
    localStorage.setItem("selected_tenant_id", user.tenantId);
    localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
  }, { user, businessId });

  let paid = false;
  let paymentInvoiceReads = 0;
  let confirmation: { payments: Array<{ methodCode: string; amount: number; cardFranchiseCode: string; approvalNumber: string }> } | null = null;
  await page.route("**/api/**", async route => {
    const url = new URL(route.request().url());
    const path = url.pathname;
    let body: unknown = [];
    if (path === "/api/auth/me") body = user;
    else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Pruebas" }];
    else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Sede pruebas" }];
    else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, roles: [], permissions: user.permissions };
    else if (path.endsWith("/payables/suppliers")) body = { items: [{ supplierId, supplierName: "Proveedor prueba", identification: "1001", invoiceCount: 1,
      originalAmount: 12400, paidAmount: 0, outstandingAmount: 12400, overdueAmount: 0 }], page: 1,
      pageSize: 20, totalCount: 1, totalPages: 1, totalOutstanding: 12400, totalOverdue: 0, totalInvoiceCount: 1 };
    else if (path.endsWith("/payables")) {
      if (url.searchParams.get("outstandingOnly") === "true") paymentInvoiceReads++;
      body = { items: paid ? [] : [{ payableId, supplierId, supplierName: "Proveedor prueba",
        documentNumber: "FC-001", currencyCode: "COP", originalAmount: 12400, outstandingAmount: 12400,
        dueDate: "2026-10-01T12:00:00-05:00", status: "Open", isOverdue: false,
        createdAt: "2026-09-23T12:00:00-05:00" }], page: 1, pageSize: 20,
        totalCount: paid ? 0 : 1, totalPages: paid ? 0 : 1,
        totalOutstanding: paid ? 0 : 12400, totalOverdue: 0 };
    }
    else if (path.endsWith("/payable-payments")) body = { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 };
    else if (path.endsWith("/reference-options/payment-method")) body = [
      { id: "cash", code: "Cash", label: "Efectivo", sortOrder: 1 },
      { id: "transfer", code: "Transfer", label: "Transferencia", sortOrder: 2 },
      { id: "debit", code: "DebitCard", label: "Tarjeta débito", sortOrder: 3 },
      { id: "credit", code: "CreditCard", label: "Tarjeta crédito", sortOrder: 4 },
    ];
    else if (path.endsWith("/reference-options/card-franchise")) body = [{ id: "visa", code: "Visa", label: "Visa", sortOrder: 1 }];
    else if (path.endsWith("/pos/settlement-configuration")) body = { isAccountingEnabled: false, bankAccounts: [] };
    else if (path.endsWith("/parties/role-options")) body = { items: [], page: 1, totalPages: 0, totalCount: 0 };
    else if (path.endsWith("/work-sessions/current")) body = { workSessionId: "77777777-7777-7777-7777-777777777777" };
    else if (path.endsWith("/payable-payments/confirm")) {
      confirmation = route.request().postDataJSON();
      paid = true;
      body = { paymentId: "66666666-6666-6666-6666-666666666666", documentNumber: "PGP-001",
        accountingJobId: "88888888-8888-8888-8888-888888888888", status: "Accepted", idempotentReplay: false };
    }
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
  });

  await page.goto("/dashboard/payables");
  await page.getByRole("row", { name: /Proveedor prueba/ }).click();
  await page.getByRole("button", { name: "Ir a pagar" }).click();
  const checkout = page.getByRole("dialog", { name: "Pago a proveedores" });
  const methodButtons = checkout.locator("button", { hasText: /F[1-4]$/ });
  await expect(methodButtons).toHaveCount(4);
  const boxes = await methodButtons.evaluateAll(buttons => buttons.map(button => button.getBoundingClientRect().toJSON()));
  expect(new Set(boxes.map(box => Math.round(box.y)))).toHaveProperty("size", 1);
  await page.keyboard.press("F2");
  const transfer = page.getByRole("dialog", { name: "Datos de la transferencia" });
  await expect(transfer).toBeVisible();
  await transfer.getByRole("button", { name: "Cerrar datos de transferencia" }).click();
  await page.keyboard.press("F1");
  await expect(checkout.getByRole("combobox", { name: "Medio de pago" })).toContainText("Efectivo");
  await page.keyboard.press("F3");
  let card = page.getByRole("dialog", { name: "Datos de la tarjeta" });
  await expect(card).toBeVisible();
  await card.getByRole("button", { name: "Cerrar datos de tarjeta" }).click();
  await page.keyboard.press("F4");
  card = page.getByRole("dialog", { name: "Datos de la tarjeta" });
  await expect(card).toBeVisible();
  await card.getByPlaceholder("Aprobación del datáfono").fill("CREDIT-001");
  await card.getByRole("button", { name: /Guardar datos/ }).click();
  await checkout.getByRole("button", { name: "Confirmar pago" }).click();
  await expect(checkout).not.toBeVisible();
  expect(confirmation).toMatchObject({ payments: [{ methodCode: "CreditCard", amount: 12400,
    cardFranchiseCode: "Visa", approvalNumber: "CREDIT-001" }] });
  const readsAfterPayment = paymentInvoiceReads;
  await page.getByRole("row", { name: /Proveedor prueba/ }).click();
  await expect(page.getByText("Este tercero no tiene facturas pendientes.")).toBeVisible();
  expect(paymentInvoiceReads).toBe(readsAfterPayment + 1);
});
