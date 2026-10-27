import { expect, test } from "@playwright/test";

for (const acceptedStatus of ["Accepted", "Processed"] as const) {
for (const failureStatus of [null, 409, 503] as const) {
test(`abono conserva captura y distingue rechazo de resultado incierto (${failureStatus ?? "aceptado"}, ${acceptedStatus})`, async ({ page, baseURL }) => {
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
  let accepted = false;
  let customerReads = 0;
  let paymentInvoiceReads = 0;
  const delayPaymentMethods = acceptedStatus === "Accepted" && failureStatus === null;
  let releasePaymentMethods: (() => void) | undefined;
  const paymentMethodsReady = delayPaymentMethods
    ? new Promise<void>(resolve => { releasePaymentMethods = resolve; }) : Promise.resolve();
  let printed: { format: string; receipt: { direction: string; documentNumber: string; companyName: string } } | null = null;
  let confirmation: { payments: Array<{ methodCode: string; amount: number; cardFranchiseCode: string; approvalNumber: string }> } | null = null;
  const attempts: Array<{ paymentId: string; key: string | undefined }> = [];
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
    else if (path.endsWith("/receivable-payments")) body = { items: accepted ? [{ paymentId: "66666666-6666-6666-6666-666666666666",
      documentNumber: "RCC-001", paidAt: "2026-09-23T12:00:00-05:00", currencyCode: "COP", totalAmount: 10450.45,
      status: acceptedStatus, appliedDocumentCount: 1, payments: [{ methodCode: "DebitCard" }],
      applications: [{ receivableId, documentNumber: "FV-001", amount: 10450.45 }], customerId,
      customerName: "Cliente prueba" }] : [], page: 1, pageSize: 20, totalCount: accepted ? 1 : 0, totalPages: accepted ? 1 : 0 };
    else if (path.endsWith("/reference-options/payment-method")) {
      await paymentMethodsReady;
      body = [
      { id: "cash", code: "Cash", label: "Efectivo", sortOrder: 1 },
      { id: "debit", code: "DebitCard", label: "Tarjeta configurada", sortOrder: 2 },
      { id: "transfer", code: "Transfer", label: "Transferencia", sortOrder: 3 },
      ];
    }
    else if (path.endsWith("/reference-options/card-franchise")) body = [{ id: "visa", code: "Visa", label: "Visa", sortOrder: 1 }];
    else if (path.endsWith("/pos/settlement-configuration")) body = { isAccountingEnabled: false, bankAccounts: [] };
    else if (path.endsWith("/parties/role-options")) body = { items: [], page: 1, totalPages: 0, totalCount: 0 };
    else if (path.endsWith("/work-sessions/current")) body = { workSessionId: "77777777-7777-7777-7777-777777777777" };
    else if (path.endsWith(`/businesses/${businessId}`)) body = { businessId, name: "Sede pruebas", address: "Calle 1", phone: "3001234567" };
    else if (path.endsWith("/portfolio-payments/receipt/render")) {
      printed = route.request().postDataJSON();
      body = { html: "<!doctype html><html><body>Abono registrado</body></html>" };
    }
    else if (path.endsWith("/receivable-payments/confirm")) {
      confirmation = route.request().postDataJSON();
      attempts.push({ paymentId: route.request().postDataJSON().paymentId, key: route.request().headers()["idempotency-key"] });
      if (failureStatus && attempts.length === 1) {
        await route.fulfill({ status: failureStatus, contentType: "application/problem+json",
          body: JSON.stringify({ status: failureStatus, detail: failureStatus === 409
            ? "Una factura ya no tiene saldo disponible para ese abono."
            : "No se recibió confirmación." }) });
        return;
      }
      accepted = true;
      paid = acceptedStatus === "Processed";
      body = { paymentId: "66666666-6666-6666-6666-666666666666", documentNumber: "RCC-001",
        accountingJobId: "88888888-8888-8888-8888-888888888888", status: acceptedStatus, idempotentReplay: false };
    }
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
  });

  await page.goto("/dashboard/receivables");
  await page.getByRole("row", { name: /Cliente prueba/ }).click();
  await expect(page.getByLabel("Abono para FV-001")).toBeVisible();
  const readsBeforeCancel = paymentInvoiceReads;
  await page.getByRole("dialog", { name: "Abono a cartera" }).getByRole("button", { name: "Cancelar" }).click();
  await expect(page.getByRole("dialog", { name: "Abono a cartera" })).not.toBeVisible();
  await page.getByRole("row", { name: /Cliente prueba/ }).click();
  await expect(page.getByLabel("Abono para FV-001")).toBeVisible();
  await expect.poll(() => paymentInvoiceReads).toBe(readsBeforeCancel + 1);
  await page.getByRole("button", { name: "Ir a pagar" }).click();
  const checkout = page.getByRole("dialog", { name: "Abono a cartera" });
  await expect(checkout.getByText("Recibo de abono a cartera")).toBeVisible();
  if (delayPaymentMethods) {
    try {
      await expect(checkout.getByRole("button", { name: "Confirmar pago" })).toBeDisabled();
      await expect(checkout.getByRole("button", { name: "Sin imprimir", exact: true })).toBeDisabled();
      await expect(checkout.getByRole("button", { name: "Media carta", exact: true })).toBeDisabled();
      expect(attempts).toHaveLength(0);
    } finally { releasePaymentMethods?.(); }
  }
  await expect(checkout.getByRole("button", { name: "Confirmar pago" })).toBeEnabled();
  await page.keyboard.press("F2");
  const card = page.getByRole("dialog", { name: "Datos de la tarjeta" });
  await expect(card).toBeVisible();
  await card.getByPlaceholder("Aprobación del datáfono").fill("AP-001");
  await card.getByRole("button", { name: /Guardar datos/ }).click();
  await checkout.getByRole("button", { name: "Media carta", exact: true }).click();
  if (failureStatus) {
    const alert = checkout.getByRole("alert");
    await expect(alert).toContainText(failureStatus === 409 ? "El pago no se realizó" : "No se pudo confirmar el pago");
    await expect(alert).toContainText(failureStatus === 409 ? "saldo disponible" : "no podemos asegurar si quedó registrado");
    await expect(checkout).toBeVisible();
    expect(printed).toBeNull();
    expect(paid).toBe(false);
    const firstAttempt = attempts[0];
    await checkout.getByRole("button", { name: "Media carta", exact: true }).click();
    await expect.poll(() => attempts.length).toBe(2);
    expect(attempts[1]).toEqual(firstAttempt);
  }
  await expect(checkout).not.toBeVisible();
  await expect.poll(() => printed).not.toBeNull();
  expect(printed).toMatchObject({ format: "HalfLetter", receipt: { direction: "Receivable", status: acceptedStatus, documentNumber: "RCC-001", companyName: "Sede pruebas",
    payments: [{ methodName: "Tarjeta configurada", amount: 10450.45 }] } });
  expect(confirmation).toMatchObject({ payments: [{ methodCode: "DebitCard", amount: 10450.45,
    cardFranchiseCode: "Visa", approvalNumber: "AP-001" }] });
  await expect.poll(() => customerReads).toBeGreaterThan(1);
  if (acceptedStatus === "Accepted") {
    await expect(page.getByText(/RCC-001 recibido y pendiente de aplicar/)).toBeVisible();
    await expect(page.getByLabel("Resumen de cartera")).toContainText("10.450");
    expect(paid).toBe(false);
    return;
  }
  await expect(page.getByText("Pago aplicado: RCC-001.")).toBeVisible();
  await expect(page.getByLabel("Resumen de cartera")).toContainText("$ 0");
  const readsAfterPayment = paymentInvoiceReads;
  await page.getByRole("row", { name: /Cliente prueba/ }).click();
  await expect(page.getByText("Este tercero no tiene facturas pendientes.")).toBeVisible();
  expect(paymentInvoiceReads).toBe(readsAfterPayment + 1);
});
}
}

for (const supplierStatus of ["Accepted", "Processed"] as const) {
for (const financialDiscount of [false, true]) {
test(`pago a proveedor muestra cuatro medios, usa F1-F4 y captura tarjeta (${supplierStatus}, descuento ${financialDiscount})`, async ({ page, baseURL }) => {
  const tenantId = "11111111-1111-1111-1111-111111111111";
  const businessId = "22222222-2222-2222-2222-222222222222";
  const supplierId = "33333333-3333-3333-3333-333333333333";
  const partySiteId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
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
  let supplierReads = 0;
  let printed: { format: string; receipt: { direction: string; documentNumber: string; companyName: string } } | null = null;
  let confirmation: { payments: Array<{ methodCode: string; amount: number; cardFranchiseCode: string; approvalNumber: string }>;
    allocations: Array<{ payableId: string; amount: number; financialDiscountEarned: boolean | null }> } | null = null;
  await page.route("**/api/**", async route => {
    const url = new URL(route.request().url());
    const path = url.pathname;
    let body: unknown = [];
    if (path === "/api/auth/me") body = user;
    else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Pruebas" }];
    else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Sede pruebas" }];
    else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, roles: [], permissions: user.permissions };
    else if (path.endsWith("/payables/suppliers")) {
      supplierReads++;
      body = { items: [{ supplierId, supplierName: "Proveedor prueba", identification: "1001", partySiteId, partySiteName: "Sede principal", invoiceCount: 1,
        originalAmount: 12400, paidAmount: paid ? 12400 : 0, outstandingAmount: paid ? 0 : 12400, overdueAmount: 0 }], page: 1,
        pageSize: 20, totalCount: 1, totalPages: 1, totalOutstanding: paid ? 0 : 12400, totalOverdue: 0, totalInvoiceCount: 1 };
    }
    else if (path.endsWith("/payables")) {
      if (url.searchParams.get("outstandingOnly") === "true") paymentInvoiceReads++;
      body = { items: paid ? [] : [{ payableId, supplierId, supplierName: "Proveedor prueba", partySiteId, partySiteName: "Sede principal",
        documentNumber: "FC-001", currencyCode: "COP", originalAmount: 12400, outstandingAmount: 12400,
        dueDate: "2026-10-01T12:00:00-05:00", status: "Open", isOverdue: false,
        ...(financialDiscount ? { financialDiscountEligibleAmount: 2400, financialDiscountAnticipated: false,
          financialDiscountPendingReturn: false } : {}),
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
    else if (path.endsWith("/parties/site-options")) body = { items: [{ partyId: supplierId, roleId: supplierId, partySiteId, displayName: "Proveedor prueba", identification: "1001", siteName: "Sede principal", isPrimary: true, supplierPurchaseEvidencePolicy: null, supplierDefaultPaymentDueDays: 30 }], page: 1, totalPages: 1, totalCount: 1 };
    else if (path.endsWith("/work-sessions/current")) body = { workSessionId: "77777777-7777-7777-7777-777777777777" };
    else if (path.endsWith(`/businesses/${businessId}`)) body = { businessId, name: "Sede pruebas", address: "Calle 1", phone: "3001234567" };
    else if (path.endsWith("/portfolio-payments/receipt/render")) {
      printed = route.request().postDataJSON();
      body = { html: "<!doctype html><html><body>Pago registrado</body></html>" };
    }
    else if (path.endsWith("/payable-payments/confirm")) {
      confirmation = route.request().postDataJSON();
      paid = supplierStatus === "Processed";
      body = { paymentId: "66666666-6666-6666-6666-666666666666", documentNumber: "PGP-001",
        accountingJobId: "88888888-8888-8888-8888-888888888888", status: supplierStatus, idempotentReplay: false };
    }
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
  });

  await page.goto("/dashboard/payables");
  await page.getByRole("row", { name: /Proveedor prueba/ }).click();
  await expect(page.getByRole("dialog", { name: "Pago a proveedores" }).getByRole("combobox", { name: "Seleccionar proveedor y sede" })).toContainText("Proveedor prueba · Sede principal");
  if (financialDiscount) {
    await page.getByRole("combobox", { name: "Descuento financiero para FC-001" }).click();
    await page.getByRole("option", { name: "Sí lo obtuve" }).click();
    await expect(page.getByLabel("Abono para FC-001")).toHaveValue("10 000");
  }
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
  await checkout.getByRole("button", { name: "Oficio", exact: true }).click();
  await expect(checkout).not.toBeVisible();
  await expect.poll(() => printed).not.toBeNull();
  expect(printed).toMatchObject({ format: "HalfLegal", receipt: { direction: "Payable", status: supplierStatus, documentNumber: "PGP-001", companyName: "Sede pruebas" } });
  expect(confirmation).toMatchObject({ payments: [{ methodCode: "CreditCard", amount: financialDiscount ? 10000 : 12400,
    cardFranchiseCode: "Visa", approvalNumber: "CREDIT-001" }],
    allocations: [{ payableId, amount: financialDiscount ? 10000 : 12400,
      financialDiscountEarned: financialDiscount ? true : null }] });
  await expect.poll(() => supplierReads).toBeGreaterThan(1);
  if (supplierStatus === "Accepted") {
    await expect(page.getByText(/PGP-001 recibido y pendiente de aplicar/)).toBeVisible();
    await expect(page.getByLabel("Resumen de cartera")).toContainText("12.400");
    expect(paid).toBe(false);
    return;
  }
  await expect(page.getByLabel("Resumen de cartera")).toContainText("$ 0");
  const readsAfterPayment = paymentInvoiceReads;
  await page.getByRole("row", { name: /Proveedor prueba/ }).click();
  await expect(page.getByText("Este tercero no tiene facturas pendientes.")).toBeVisible();
  expect(paymentInvoiceReads).toBe(readsAfterPayment + 1);
});
}
}
