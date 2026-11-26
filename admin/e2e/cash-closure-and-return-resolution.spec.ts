import { readFile } from "node:fs/promises";
import { unzipSync, strFromU8 } from "fflate";
import { paymentCatalogFixture } from "./support/payment-catalog";
import { expect, test, type Page, type Route } from "@playwright/test";
import { login as sharedLogin } from "./support/auth";
import type { WorkSessionClosurePage } from "../src/services/api/work-session-differences";

const tenantId = "11111111-1111-1111-1111-111111111111";
const businessId = "22222222-2222-2222-2222-222222222222";
const userId = "33333333-3333-3333-3333-333333333333";
const closureId = "44444444-4444-4444-4444-444444444444";
const saleId = "55555555-5555-5555-5555-555555555555";
const permissions = ["dashboard.read", "work-sessions.differences.read", "work-sessions.closures.reconcile","sales.payments.correct", "sales.returns.read", "sales.returns.create", "sales.returns.confirm", "invoice-charges.read", "invoice-charges.configure"];

const json = (route: Route, body: unknown) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
const cashClosurePage = (route: Route, body: WorkSessionClosurePage) => json(route, {
  ...body,
  items: body.items.map(cashClosure => ({
    closureId: cashClosure.workSessionClosureId, type: "Cash", cashClosure, dispatchClosure: null,
  })),
});

async function authenticate(page: Page) {
  await page.context().addCookies([{ name: "auth_token", value: "e2e", url: process.env.AURALY_E2E_BASE_URL ?? "http://127.0.0.1:3000", httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ tenant, business, user, granted }) => {
    localStorage.setItem("selected_tenant_id", tenant);
    localStorage.setItem("selected_business_id", business);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user: { userId: user, tenantId: tenant, tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test", firstName: "Prueba", lastName: "E2E", avatarUrl: null, roles: ["Administrator"], permissions: granted } }, version: 0 }));
  }, { tenant: tenantId, business: businessId, user: userId, granted: permissions });
  await page.route("**/api/auth/me", route => json(route, { userId, tenantId, tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test", firstName: "Prueba", lastName: "E2E", avatarUrl: null, roles: ["Administrator"], permissions }));
  await page.route("**/api/execution-context/tenants", route => json(route, [{ tenantId, name: "Auraly" }]));
  await page.route("**/api/execution-context/businesses", route => json(route, [{ tenantId, businessId, name: "Auraly", timeZone: "America/Bogota" }]));
  await page.route("**/api/execution-context/access", route => json(route, { tenantId, businessId, roles: ["Administrator"], permissions }));
  await page.route("**/reference-options/payment-method**", route => json(route, [
    { id: "cash", code: "Cash", label: "Efectivo", sortOrder: 1 },
    { id: "debit", code: "DebitCard", label: "Tarjeta débito", sortOrder: 2 },
    { id: "credit", code: "CreditCard", label: "Tarjeta crédito", sortOrder: 3 },
    { id: "transfer", code: "Transfer", label: "Transferencia", sortOrder: 4 },
  ]));
  await page.route("**/reference-options/card-franchise**", route => json(route, [
    { id: "visa", code: "Visa", label: "Visa", sortOrder: 1 },
    { id: "mastercard", code: "Mastercard", label: "Mastercard", sortOrder: 2 },
  ]));
  await page.route(/\/api\/commerce\/v1\/payment-methods\/?(?:\?.*)?$/, route => {
    const method = new URL(route.request().url()).searchParams.get("methodCode");
    const items = paymentCatalogFixture().forms.filter(form => !method || form.methodCode === method);
    return json(route, { items, page: 1, pageSize: 10, totalCount: items.length });
  });
}

test("el cierre muestra motivos y observaciones sin números de egreso", async ({ page }) => {
  await authenticate(page);
  let validationRequests = 0;
  await page.route(`**/api/commerce/v1/work-sessions/closures/${closureId}/payment-corrections/validate`, route => {
    validationRequests++;
    expect(route.request().postDataJSON()).toMatchObject([{ amount: 100000,
      tenderMethodCode: "DebitCard", paymentMethodVersion: 1,
      paymentMethodId: paymentCatalogFixture().forms.find(form => form.methodCode === "DebitCard")!.paymentMethodId,
    }]);
    if (validationRequests === 1) return route.fulfill({ status: 400, contentType: "application/problem+json",
      body: JSON.stringify({ detail: "Falta la configuración contable del medio de pago corregido." }) });
    return json(route, { valid: true });
  });
  let chargeReads = 0;
  let fullSnapshotReads = 0;
  page.on("request", request => { if (/\/work-sessions\/[^/]+\/closure$/.test(request.url())) fullSnapshotReads++; });
  await page.route("**/api/commerce/v1/work-sessions/closures/*/invoice-charges/page?**", route => {
    chargeReads++;
    const method = new URL(route.request().url()).searchParams.get("paymentMethodCode");
    const charges = [
      { documentId: saleId, documentNumber: "FV-101", appliedChargeId: "charge-1", chargeId: "delivery", code: "DOMICILIO", name: "Domicilio", supplierName: "Domiciliario prueba", amount: 5000, invoicedAmount: 5000, expenseAmount: 0, withholdingAmount: 0, payments: [{ paymentNumber: 1, paymentMethodCode: "Cash", amount: 5000 }] },
      { documentId: saleId, documentNumber: "FV-101", appliedChargeId: "charge-2", chargeId: "exhausted", code: "AGOTADOS", name: "Agotados", supplierName: "Domiciliario prueba", amount: 6500, invoicedAmount: 0, expenseAmount: 6500, withholdingAmount: 0, payments: [] },
    ];
    return json(route, { items: method ? charges.filter(charge => charge.payments.some(payment => payment.paymentMethodCode === method)) : charges,
      page: 1, pageSize: 50, totalItems: method ? 1 : 2, totalAmount: method ? 5000 : 11500,
      closureSummary: { count: 2, invoicedAmount: 5000, expenseAmount: 6500,
        paymentMethods: [{ paymentMethodCode: "Cash", count: 1, amount: 5000 }] } });
  });
  await page.route("**/api/commerce/v1/work-sessions/closures/inbox?**", route => cashClosurePage(route, { items: [{
    workSessionClosureId: closureId, workSessionId: crypto.randomUUID(), businessId, businessName: "Auraly", warehouseId: crypto.randomUUID(), warehouseName: "Principal", userId, userName: "Cajero", openedAt: "2026-08-31T08:00:00-05:00", closedAt: "2026-08-31T18:00:00-05:00", salesCount: 3, creditSalesCount: 0, returnCount: 1, totalSales: 159000, totalRefunds: 20000, netAmount: 139000, expectedCash: 135000, reconciliationStatus: "Pending", accountingStatus: "AccountingDisabled", paymentTotals: [{ paymentMethodCode: "Cash", salesAmount: 150000, refundAmount: 20000, otherAmount: 0, netAmount: 130000, countedAmount: 130000, difference: 0, requiresCount: true }, { paymentMethodCode: "Card", salesAmount: 0, refundAmount: 0, otherAmount: 0, netAmount: 0, countedAmount: 0, difference: 0, requiresCount: true }, { paymentMethodCode: "Transfer", salesAmount: 9000, refundAmount: 0, otherAmount: 0, netAmount: 8000, countedAmount: 8000, difference: 0, requiresCount: true }],
  }], page: 1, pageSize: 50, totalItems: 1 }));
  const allMovements = [
    movement("sale-1", "Sale", "SalesInvoice", "FV-101", 100000),
    movement("sale-2", "Sale", "SalesReceipt", "POS-202", 50000),
    movement("return-1", "Refund", "SalesReturn", "DVT-1", -20000),
    { ...movement("sale-transfer", "Sale", "SalesInvoice", "FV-TRANSFER", 9000), paymentMethodCode: "Transfer" },
    { ...movement("in-1", "CashIn", "CashMovement", "ING-999", 10000), reasonName: "Base adicional", notes: "Cambio para comenzar el turno" },
    { ...movement("out-1", "CashOut", "CashMovement", "EGR-888", -10000), reasonName: "Consignación", notes: "Entrega en banco" },
    { ...movement("collection-1", "CashIn", "CashMovement", "RCC-102", 5000), movementType: "ReceivablePayment" as const, counterpartyName: "Cliente de prueba" },
    { ...movement("supplier-payment-1", "CashOut", "CashMovement", "PGC-103", -5000), movementType: "PayablePayment" as const, counterpartyName: "Proveedor de prueba" },
    { ...movement("bank-in", "CashIn", "ManualAccountingVoucher", "ASI-BANCO-IN", 500), paymentMethodCode: "Transfer", reference: "BANCO-ENTRADA", reasonName: "Entrada bancaria del comprobante", counterpartyName: "Tercero banco" },
    { ...movement("bank-out", "CashOut", "ManualAccountingVoucher", "ASI-BANCO-OUT", -500), paymentMethodCode: "Transfer", reference: "BANCO-SALIDA", reasonName: "Salida bancaria del comprobante", counterpartyName: "Tercero banco" },
  ];
  await page.route(`**/api/commerce/v1/work-sessions/closures/${closureId}/payment-verifications/page?**`, route => {
    const url = new URL(route.request().url());
    const method = url.searchParams.get("paymentMethodCode");
    const movementType = url.searchParams.get("movementType");
    const pageNumber = Number(url.searchParams.get("page") ?? "1");
    const pageSize = Number(url.searchParams.get("pageSize") ?? "100");
    const filtered = allMovements.filter(item => (!method || item.paymentMethodCode === method) &&
      (!movementType || item.movementType === movementType));
    const groups = [...new Set(allMovements.map(item => `${item.paymentMethodCode}:${item.movementType}`))].map(key => {
      const [paymentMethodCode, movementType] = key.split(":");
      const inGroup = allMovements.filter(item => item.paymentMethodCode === paymentMethodCode && item.movementType === movementType);
      return { paymentMethodCode, movementType, count: inGroup.length, totalAmount: inGroup.reduce((sum, item) => sum + item.amount, 0) };
    });
    return json(route, { items: filtered.slice((pageNumber - 1) * pageSize, pageNumber * pageSize),
      groups, page: pageNumber, pageSize, totalItems: filtered.length });
  });
  await page.route("**/api/commerce/v1/reference-options/cash-reconciliation-reason", route => json(route, []));

  await page.goto("/dashboard/cash-differences");
  await page.getByRole("button", { name: "Conciliar" }).click();
  const dialog = page.getByRole("dialog", { name: "Conciliar cierre" });
  const cash = dialog.locator("section").filter({ hasText: "Efectivo" }).first();
  await expect(cash.getByText(/Esperado.*135.000/)).toBeVisible();
  await expect(cash.getByText(/Faltante.*5.000/)).toBeVisible();
  await expect(dialog.getByText(/Faltante.*8.000/).first()).toBeVisible();
  await expect(dialog.getByRole("button", { name: "Cruzar diferencias" })).toHaveCount(0);
  await expect(dialog.getByText("Agotados", { exact: true })).toBeVisible();
  await cash.getByRole("button", { name: "Ver cargos de Efectivo", exact: true }).click();
  await expect(cash.getByText("Domicilio", { exact: true })).toBeVisible();
  await expect(cash.getByText("Agotados", { exact: true })).toHaveCount(0);
  await cash.getByRole("button", { name: /Facturas/ }).click();
  await cash.getByRole("button", { name: /Devoluciones/ }).click();
  await expect(cash.getByText("FV-101", { exact: true })).toBeVisible();
  await expect(cash.getByText("DVT-1", { exact: true })).toBeVisible();
  await expect(cash.getByRole("button", { name: "No encontrado", exact: true })).toHaveCount(0);
  await cash.getByRole("button", { name: "Corregir", exact: true }).first().click();
  const correction = page.getByRole("dialog", { name: "Corregir medio de recaudo" });
  await expect(correction).toBeVisible();
  await correction.getByRole("combobox").first().click();
  await expect(page.getByRole("option", { name: "Tarjeta débito" })).toBeVisible({ timeout: 10_000 });
  await page.getByRole("option", { name: "Tarjeta débito" }).click();
  await expect(correction.getByRole("combobox", { name: "Forma de pago del recaudo", exact: true })).toContainText("Tarjeta débito");
  await expect(correction.getByText("Franquicia", { exact: true })).toBeVisible();
  await expect(correction.getByRole("button", { name: "Aplicar" })).toBeDisabled();
  await correction.getByRole("combobox").last().click();
  await page.getByRole("option", { name: "Visa" }).click();
  await correction.getByLabel("Número de aprobación", { exact: true }).fill("AP-123");
  await correction.getByLabel("Motivo de la corrección").fill("Datáfono débito");
  await expect(correction.getByRole("button", { name: "Aplicar" })).toBeEnabled();
  await correction.getByRole("button", { name: "Aplicar" }).click();
  await expect(correction.getByRole("alert")).toContainText("Falta la configuración contable");
  await expect(correction).toBeVisible();
  await correction.getByRole("button", { name: "Aplicar" }).click();
  await expect(correction).not.toBeVisible();
  expect(validationRequests).toBe(2);
  await expect(cash.getByText("FV-101", { exact: true })).toHaveCount(0);
  const card = dialog.locator("section").filter({ hasText: "Tarjeta" }).first();
  await expect(card.getByText("FV-101", { exact: true })).toBeVisible();
  await expect(dialog).toBeVisible();
  await cash.getByRole("button", { name: /Entradas de dinero/ }).click();
  await cash.getByRole("button", { name: /Salidas de dinero/ }).click();
  await expect(cash.getByText("Base adicional", { exact: true })).toBeVisible();
  await expect(cash.getByText("Consignación", { exact: true })).toBeVisible();
  await expect(cash.getByText("Cambio para comenzar el turno", { exact: true })).toBeVisible();
  await expect(cash.getByText("Entrega en banco", { exact: true })).toBeVisible();
  await expect(cash.getByText("ING-999", { exact: true })).toHaveCount(0);
  await expect(cash.getByText("EGR-888", { exact: true })).toHaveCount(0);
  await expect(cash.getByRole("button", { name: "Verificado", exact: true })).toHaveCount(2);
  const transfer = dialog.locator("section").filter({ hasText: "Transferencia" }).first();
  await transfer.getByRole("button", { name: /Facturas y comprobantes/ }).click();
  await expect(transfer.getByText("FV-TRANSFER", { exact: true })).toBeVisible();
  await expect(transfer.getByRole("button", { name: "Verificado", exact: true })).toHaveCount(1);
  await expect(transfer.getByRole("button", { name: "No encontrado", exact: true })).toHaveCount(1);
  await expect(transfer.getByRole("button", { name: "Corregir", exact: true })).toHaveCount(1);
  await expect(transfer.getByText(/Total confirmado:/)).toContainText("0");
  await transfer.getByRole("button", { name: "No encontrado", exact: true }).click();
  await expect(transfer.getByText(/Total confirmado:/)).toContainText("0");
  await transfer.getByRole("button", { name: "Verificado", exact: true }).click();
  await expect(transfer.getByText(/Total confirmado:/)).toContainText("9.000");
  await expect(transfer.getByText(/Sobrante.*1.000/).first()).toBeVisible();
  await cash.getByRole("button", { name: "Verificado", exact: true }).first().click();
  await cash.getByRole("button", { name: "Verificado", exact: true }).last().click();
  await expect(cash.getByText(/Total confirmado:/)).toContainText("130.000");
  await cash.getByRole("button", { name: /Abonos a cartera/ }).click();
  await cash.getByRole("button", { name: /Pagos a proveedores/ }).click();
  await expect(cash.getByText("Cliente de prueba")).toBeVisible();
  await expect(cash.getByText("Proveedor de prueba")).toBeVisible();
  await expect(cash.getByText("Comprobante: RCC-102", { exact: false })).toBeVisible();
  await expect(cash.getByText("Comprobante: PGC-103", { exact: false })).toBeVisible();
  await transfer.getByRole("button", { name: /Entradas de dinero/ }).click();
  await transfer.getByRole("button", { name: /Salidas de dinero/ }).click();
  await expect(transfer.getByText(/Comprobante: ASI-BANCO-IN/)).toBeVisible();
  await expect(transfer.getByText(/Comprobante: ASI-BANCO-OUT/)).toBeVisible();
  const bankOut = transfer.locator(".grid").filter({ hasText: "Comprobante: ASI-BANCO-OUT" }).last();
  await bankOut.getByRole("button", { name: "Corregir", exact: true }).click();
  await expect(correction.getByLabel("Valor del recaudo", { exact: true })).toBeDisabled();
  await expect(correction.getByText("La corrección del medio conserva el importe del recaudo.", { exact: true })).toBeVisible();
  await correction.getByRole("combobox").first().click();
  await expect(page.getByRole("option", { name: "Transferencia", exact: true })).toBeVisible();
  await expect(page.getByRole("option", { name: "Efectivo", exact: true })).toBeVisible();
  await page.getByRole("option", { name: "Transferencia", exact: true }).click();
  await correction.getByRole("button", { name: "Cerrar", exact: true }).click();
  const bankIn = transfer.locator(".grid").filter({ hasText: "Comprobante: ASI-BANCO-IN" }).last();
  await bankIn.getByRole("button", { name: "Verificado", exact: true }).click();
  await expect(transfer.getByText(/Total confirmado:/)).toContainText("9.500");
  await bankOut.getByRole("button", { name: "Verificado", exact: true }).click();
  await expect(transfer.getByText(/Total confirmado:/)).toContainText("9.000");
  expect(chargeReads).toBe(2);
  expect(fullSnapshotReads).toBe(0);
});

test("cargos del cierre pagina, conserva totales y vuelve desde caché sin descargar el cierre", async ({ page }) => {
  await authenticate(page);
  const charges = Array.from({ length: 51 }, (_, index) => ({ documentId: saleId,
    documentNumber: `FV-${index + 1}`, appliedChargeId: `charge-${index + 1}`, chargeId: "delivery",
    code: "DELIVERY", name: `Cargo ${index + 1}`, supplierName: "Proveedor de prueba", amount: 1000,
    invoicedAmount: 0, expenseAmount: 1000, withholdingAmount: 0, payments: [] }));
  let chargeReads = 0;
  let fullSnapshotReads = 0;
  page.on("request", request => { if (/\/work-sessions\/[^/]+\/closure$/.test(request.url())) fullSnapshotReads++; });
  await page.route("**/api/commerce/v1/work-sessions/closures/*/invoice-charges/page?**", route => {
    chargeReads++;
    if (chargeReads === 1) return route.fulfill({ status: 500, contentType: "application/problem+json",
      body: JSON.stringify({ detail: "Lectura no disponible" }) });
    const query = new URL(route.request().url()).searchParams;
    expect(query.get("pageSize")).toBe("50");
    const requestedPage = Number(query.get("page"));
    return json(route, { items: charges.slice((requestedPage - 1) * 50, requestedPage * 50),
      page: requestedPage, pageSize: 50, totalItems: 51, totalAmount: 51000,
      closureSummary: { count: 51, invoicedAmount: 0, expenseAmount: 51000, paymentMethods: [] } });
  });
  await page.route("**/api/commerce/v1/work-sessions/closures/inbox?**", route => cashClosurePage(route, {
    items: [{ workSessionClosureId: closureId, workSessionId: crypto.randomUUID(), businessId,
      businessName: "Auraly", warehouseId: null, warehouseName: null, userId, userName: "Cajero",
      openedAt: "2026-10-08T08:00:00-05:00", closedAt: "2026-10-08T18:00:00-05:00",
      salesCount: 0, creditSalesCount: 0, returnCount: 0, totalSales: 0, totalRefunds: 0, netAmount: 0,
      expectedCash: 0, reconciliationStatus: "Pending", accountingStatus: "AccountingDisabled", paymentTotals: [] }],
    page: 1, pageSize: 50, totalItems: 1,
  }));
  await page.route(`**/api/commerce/v1/work-sessions/closures/${closureId}/payment-verifications/page?**`, route =>
    json(route, { items: [], groups: [], page: 1, pageSize: 1, totalItems: 0 }));
  await page.route("**/api/commerce/v1/reference-options/cash-reconciliation-reason", route => json(route, []));
  await page.goto("/dashboard/cash-differences");
  await page.getByRole("button", { name: "Conciliar", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Conciliar cierre" });
  await expect(dialog.getByText("No fue posible cargar los cargos.")).toBeVisible();
  expect(chargeReads).toBe(1);
  await dialog.getByRole("button", { name: "Reintentar", exact: true }).click();
  const summary = dialog.locator("section").filter({ has: page.getByRole("heading", { name: "Cargos de facturación", exact: true }) });
  await expect(summary.getByText("Cargo 50", { exact: true })).toBeVisible();
  await expect(summary.getByText("Cargo 51", { exact: true })).toHaveCount(0);
  await expect(summary.getByText(/51 cargos · Total/)).toContainText("51.000");
  await summary.getByRole("button", { name: "Siguiente", exact: true }).click();
  await expect(summary.getByText("Cargo 51", { exact: true })).toBeVisible();
  await expect(summary.getByText("Cargo 50", { exact: true })).toHaveCount(0);
  await expect(summary.getByText(/51 cargos · Total/)).toContainText("51.000");
  await summary.getByRole("button", { name: "Anterior", exact: true }).click();
  await expect(summary.getByText("Cargo 50", { exact: true })).toBeVisible();
  expect(chargeReads).toBe(3);
  expect(fullSnapshotReads).toBe(0);
});

test("transferencia sin comprobantes muestra el faltante frente al valor reportado", async ({ page }) => {
  await authenticate(page);
  await page.route("**/api/commerce/v1/work-sessions/closures/*/invoice-charges/page?**", route =>
    json(route, { items: [], page: 1, pageSize: 50, totalItems: 0, totalAmount: 0,
      closureSummary: { count: 0, invoicedAmount: 0, expenseAmount: 0, paymentMethods: [] } }));
  await page.route("**/api/commerce/v1/work-sessions/closures/inbox?**", route => cashClosurePage(route, {
    items: [{ workSessionClosureId: closureId, workSessionId: crypto.randomUUID(), businessId,
      businessName: "Auraly", warehouseId: null, warehouseName: "Principal", userId,
      userName: "Cajero", openedAt: "2026-10-08T08:00:00-05:00",
      closedAt: "2026-10-08T18:00:00-05:00", salesCount: 0, creditSalesCount: 0,
      returnCount: 0, totalSales: 0, totalRefunds: 0, netAmount: 0,
      expectedCash: 0, reconciliationStatus: "Pending", accountingStatus: "NotRequired",
      paymentTotals: [{ paymentMethodCode: "Transfer", salesAmount: 0,
        refundAmount: 0, otherAmount: 0, netAmount: 0, countedAmount: 5000,
        difference: 5000, requiresCount: true }] }],
    page: 1, pageSize: 50, totalItems: 1,
  }));
  await page.route(`**/api/commerce/v1/work-sessions/closures/${closureId}/payment-verifications/page?**`,
    route => json(route, { items: [], groups: [], page: 1, pageSize: 1, totalItems: 0 }));
  await page.route("**/api/commerce/v1/reference-options/cash-reconciliation-reason",
    route => json(route, []));
  await page.goto("/dashboard/cash-differences");
  await page.getByRole("button", { name: "Conciliar" }).click();
  const dialog = page.getByRole("dialog", { name: "Conciliar cierre" });
  const transfer = dialog.locator("section").filter({ hasText: "Transferencia" }).first();
  await expect(transfer.getByText(/Por explicar frente al conteo:/)).toBeVisible();
  await expect(transfer.getByText(/Faltante.*5.000/)).toBeVisible();
  await expect(transfer.getByText(/Total confirmado:/)).toContainText("0");
  await transfer.getByRole("checkbox").check();
  await expect(transfer.getByRole("checkbox")).toBeChecked();
});

test("el detalle de efectivo carga 100 facturas por página sin descargarlas al abrir", async ({ page }) => {
  await authenticate(page);
  const workSessionId = crypto.randomUUID();
  const invoices = Array.from({ length: 101 }, (_, index) =>
    movement(`sale-${index + 1}`, "Sale", "SalesInvoice", `FV-${String(index + 1).padStart(3, "0")}`, 1000));
  const requests: string[] = [];
  await page.route("**/api/commerce/v1/work-sessions/closures/*/invoice-charges/page?**", route => json(route,
    { items: [], page: 1, pageSize: 50, totalItems: 0, totalAmount: 0,
      closureSummary: { count: 0, invoicedAmount: 0, expenseAmount: 0, paymentMethods: [] } }));
  await page.route("**/api/commerce/v1/work-sessions/closures/inbox?**", route => cashClosurePage(route, {
    items: [{ workSessionClosureId: closureId, workSessionId, businessId, businessName: "Auraly",
      warehouseId: crypto.randomUUID(), warehouseName: "Principal", userId, userName: "Cajero",
      openedAt: "2026-08-31T08:00:00-05:00", closedAt: "2026-08-31T18:00:00-05:00",
      salesCount: 101, creditSalesCount: 0, returnCount: 0, totalSales: 101000,
      totalRefunds: 0, netAmount: 101000, reconciliationStatus: "Pending",
      accountingStatus: "AccountingDisabled", paymentTotals: [{ paymentMethodCode: "Cash",
        salesAmount: 101000, refundAmount: 0, otherAmount: 0, netAmount: 101000,
        countedAmount: 101000, difference: 0, requiresCount: true }] }],
    page: 1, pageSize: 50, totalItems: 1,
  }));
  await page.route(`**/api/commerce/v1/work-sessions/closures/${closureId}/payment-verifications/page?**`, route => {
    const url = new URL(route.request().url());
    requests.push(url.search);
    const filtered = url.searchParams.get("movementType") === "Sale" ? invoices :
      url.searchParams.has("movementType") ? [] : invoices;
    const currentPage = Number(url.searchParams.get("page"));
    const pageSize = Number(url.searchParams.get("pageSize"));
    return json(route, { items: filtered.slice((currentPage - 1) * pageSize, currentPage * pageSize),
      groups: [{ paymentMethodCode: "Cash", movementType: "Sale", count: 101, totalAmount: 101000 }],
      page: currentPage, pageSize, totalItems: filtered.length });
  });
  await page.route("**/api/commerce/v1/reference-options/cash-reconciliation-reason", route => json(route, []));
  await page.goto("/dashboard/cash-differences");
  await page.getByRole("button", { name: "Conciliar" }).click();
  const dialog = page.getByRole("dialog", { name: "Conciliar cierre" });
  await expect(dialog.getByText("101 registros · Total")).toBeVisible();
  expect(requests).toHaveLength(1);
  expect(requests[0]).toContain("pageSize=1");
  await dialog.getByRole("button", { name: /Facturas y comprobantes/ }).click();
  await expect(dialog.getByText("FV-100", { exact: true })).toBeAttached();
  await dialog.getByText("FV-100", { exact: true }).scrollIntoViewIfNeeded();
  await expect(dialog.getByText("FV-101", { exact: true })).toBeAttached();
  expect(requests).toHaveLength(3);
  expect(requests[1]).toContain("pageSize=100");
  expect(requests[2]).toContain("page=2");
});

test("la devolución ofrece destinos independientes y enlaza la reversión de tarjeta", async ({ page }) => {
  await authenticate(page);
  const webWorkSessionId = "77777777-7777-7777-7777-777777777777";
  let workSessionOpens = 0;
  let confirmedReturn: Record<string, unknown> | undefined;
  await page.route("**/api/commerce/v1/work-sessions/current", route => {
    workSessionOpens += 1;
    expect(route.request().postDataJSON()).toMatchObject({ businessId, warehouseId: null, deviceId: null });
    return json(route, { workSessionId: webWorkSessionId });
  });
  await page.route("**/api/commerce/v1/sales-returns/confirm", route => {
    confirmedReturn = route.request().postDataJSON();
    return json(route, { returnId: confirmedReturn?.returnId, documentNumber: "DEV-900" });
  });
  await page.route("**/api/commerce/v1/pos/settlement-configuration**", route => json(route, { isAccountingEnabled: false, bankAccounts: [], paymentMethods: paymentCatalogFixture() }));
  await page.route("**/api/commerce/v1/sales-returns/sales?**", route => json(route, { items: [{ documentId: saleId, documentNumber: "FV-900", fiscalNumber: "SETT-900", cufe: "CUFE", issuedAt: "2026-08-30T10:00:00-05:00", customerId: crypto.randomUUID(), customerName: "Cliente crédito", customerIdentification: "900123", warehouseId: crypto.randomUUID(), warehouseName: "Principal", totalAmount: 119000, returnedAmount: 0, hasAvailableQuantity: true, fiscalStatus: "Accepted" }], page: 1, pageSize: 25, totalCount: 1, totalPages: 1 }));
  await page.route(`**/api/commerce/v1/sales-returns/sales/${saleId}**`, route => json(route, {
    documentId: saleId, documentNumber: "FV-900", fiscalNumber: "SETT-900", cufe: "CUFE", issuedAt: "2026-08-30T10:00:00-05:00", customerId: crypto.randomUUID(), customerName: "Cliente crédito", customerIdentification: "900123", warehouseId: crypto.randomUUID(), warehouseName: "Principal", totalAmount: 119000, returnedAmount: 0, originalUnrounded: 119000, originalRounding: 0, unroundedOutstanding: 119000, remainingRounding: 0, receivableOutstanding: 90000, fiscalStatus: "Accepted", payments: [{ paymentNumber: 1, methodCode: "CreditCard", originalAmount: 29000, refundedAmount: 0, availableAmount: 29000, cardFranchiseCode: "Visa", approvalNumber: "APP-900" }], charges: [
      { appliedChargeId: crypto.randomUUID(), code: "BILLED", name: "Domicilio", amount: 5000, invoicedAmount: 5000, expenseAmount: 0, isReturned: false, expenseStatus: "Cancelled" },
      { appliedChargeId: crypto.randomUUID(), code: "ABSORBED", name: "Mensajería", amount: 2000, invoicedAmount: 0, expenseAmount: 2000, isReturned: false, expenseStatus: "Cancelled" },
    ], lines: [{ originalLineNumber: 1, productId: crypto.randomUUID(), productCode: "P-1", reference: null, description: "Producto", soldQuantity: 1, returnedQuantity: 0, availableQuantity: 1, unitPrice: 100000, discountAmount: 0, taxCode: "01", taxRate: 19, untaxedAmount: 100000, taxAmount: 19000, lineTotal: 119000, barcodes: "" }],
  }));
  await page.route("**/api/commerce/v1/reference-options/sales-return-resolution-method", route => json(route, [
    { id: "cash", code: "Cash", label: "Efectivo", description: null, sortOrder: 10 },
    { id: "credit", code: "CustomerCredit", label: "Abono a cartera", description: null, sortOrder: 20 },
    { id: "transfer", code: "Transfer", label: "Transferencia", description: null, sortOrder: 30 },
    { id: "debit", code: "DebitCard", label: "Tarjeta débito", description: null, sortOrder: 40 },
    { id: "credit-card", code: "CreditCard", label: "Tarjeta crédito", description: null, sortOrder: 50 },
  ]));
  await page.route("**/api/commerce/v1/accounting/bank-accounts**", route => json(route, [{ bankAccountId: crypto.randomUUID(), displayName: "Cuenta principal", bankName: "Banco", accountNumber: "1234", accountTypeName: "Ahorros", isPrimary: true, isActive: true, rowVersion: "AQ==" }]));
  await page.route("**/api/commerce/v1/reference-options/sales-return-scope", route => json(route, [{ id: "partial", code: "Partial", label: "Parcial", description: null, sortOrder: 10 }]));
  await page.route("**/api/commerce/v1/reasons?**", route => json(route, [{ inventoryReasonId: "88888888-8888-8888-8888-888888888888", code: "CUSTOMER_REQUEST", name: "Solicitud del cliente", isActive: true }]));

  await page.goto("/dashboard/sales-returns");
  await page.getByText("FV-900", { exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Nueva devolución" });
  await expect(dialog).toContainText("Gasto anulado; cobro disponible");
  await expect(dialog).toContainText("Gasto anulado");
  const resolution = dialog.getByText("Cómo devolver el valor", { exact: true }).locator("..");
  await expect(resolution.getByRole("combobox")).toContainText("Abono a cartera");
  await resolution.getByRole("combobox").click();
  await expect(page.getByRole("option", { name: "Abono a cartera" })).toBeVisible();
  await expect(page.getByRole("option", { name: "Efectivo" })).toBeVisible();
  await expect(page.getByRole("option", { name: "Tarjeta crédito" })).toBeVisible();
  await expect(page.getByRole("option", { name: "Transferencia" })).toBeVisible();
  await page.getByRole("option", { name: "Tarjeta crédito" }).click();
  const originalPayment = dialog.getByText("Pago de tarjeta por reversar", { exact: true }).locator("..");
  await originalPayment.getByRole("combobox").click();
  await expect(page.getByRole("option", { name: /Visa · APP-900/ })).toBeVisible();
  await page.getByRole("option", { name: /Visa · APP-900/ }).click();
  const quantity = dialog.getByRole("spinbutton", { name: "Cantidad a devolver de Producto" });
  const productRow = quantity.locator("..");
  await quantity.fill("0.5");
  await expect(productRow).toContainText("119.000");
  await expect(productRow).toContainText("59.500");
  await expect(dialog.getByText("Valor estimado").locator("../..")).toContainText("59.500");
  await resolution.getByRole("combobox").click();
  await page.getByRole("option", { name: "Abono a cartera" }).click();
  await dialog.getByText("Motivo", { exact: true }).locator("..").getByRole("combobox").click();
  await page.getByRole("option", { name: "Solicitud del cliente" }).click();
  await dialog.getByRole("button", { name: "Confirmar devolución" }).click();
  await expect(dialog).not.toBeVisible();
  expect(workSessionOpens).toBe(1);
  expect(confirmedReturn).toMatchObject({ businessId, workSessionId: webWorkSessionId, originalDocumentId: saleId });
});

test("el modal de periféricos cubre el viewport completo desde el body", async ({ page }) => {
  test.setTimeout(150_000);
  if (process.env.AURALY_E2E_TENANT_KEY) await sharedLogin(page, undefined, 90_000);
  else await authenticate(page);
  await page.route("**/api/commerce/v1/pos/workspace/options", route => json(route, []));
  await page.route("**/api/commerce/v1/routes?**", route => json(route, { items: [], page: 1, pageSize: 100, totalCount: 0, totalPages: 0 }));
  await page.route("**/api/commerce/v1/orders?**", route => json(route, { items: [], page: 1, pageSize: 20, totalCount: 0, hasMore: false }));
  await page.route("**/api/commerce/v1/pos/installer", route => json(route, { downloadUrl: "/auraly-installer.exe", version: "1.0.0", sha256: "test", tenantPreconfigured: false }));

  await page.goto("/dashboard/orders");
  await expect(page.getByRole("button", { name: "Configurar plantillas e impresoras" })).toBeVisible();
  await page.evaluate(() => document.documentElement.classList.add("dark"));
  await page.getByRole("button", { name: "Configurar plantillas e impresoras" }).click();

  const backdrop = page.getByTestId("peripherals-dialog-backdrop");
  const dialog = page.getByRole("dialog", { name: "Periféricos" });
  await expect(dialog).toBeVisible();
  await expect(dialog).toHaveCSS("color", "rgb(2, 6, 23)");
  await expect(dialog.getByText("Media oficio", { exact: true }).first()).toBeVisible();
  await expect(dialog.getByRole("button", { name: "Cancelar" })).toHaveCSS("color", "rgb(2, 6, 23)");
  expect(await backdrop.evaluate(element => element.parentElement === document.body)).toBe(true);
  await expect(backdrop).toHaveCSS("position", "fixed");
  expect(await backdrop.boundingBox()).toEqual({ x: 0, y: 0, ...page.viewportSize()! });
  const invoices = dialog.getByRole("heading", { name: "Facturas" }).locator("../..");
  const orders = dialog.getByRole("heading", { name: "Pedidos" }).locator("../..");
  await invoices.getByRole("checkbox", { name: "Usar media oficio por defecto en facturas" }).check();
  await orders.getByRole("checkbox", { name: "Usar carta por defecto en pedidos" }).check();
  await expect(invoices.getByRole("checkbox", { checked: true })).toHaveCount(1);
  await expect(orders.getByRole("checkbox", { checked: true })).toHaveCount(1);
  await expect(dialog.locator('input[type="radio"]')).toHaveCount(0);
  await dialog.getByRole("button", { name: "Guardar" }).click();
  const formats = await page.evaluate(() => {
    const configuration = JSON.parse(localStorage.getItem("auraly.printing.configuration.v1") ?? "{}");
    return [configuration.posOutputFormat, configuration.orderOutputFormat];
  });
  expect(formats).toEqual(["HalfLegal", "Letter"]);
});

function movement(key: string, movementType: "Sale" | "Refund" | "CashIn" | "CashOut", sourceDocumentType: "SalesInvoice" | "SalesReceipt" | "SalesReturn" | "CashMovement" | "ManualAccountingVoucher", documentNumber: string, amount: number) {
  return { verificationKey: key, paymentMethodCode: "Cash", movementType, sourceDocumentType, sourceId: crypto.randomUUID(), documentNumber, sourceNumber: 1, amount, reference: null, cardFranchiseCode: null, approvalNumber: null, occurredAt: "2026-08-31T12:00:00-05:00" };
}


test("historial de cargos pagina y consulta sin cargar pestañas ocultas", async ({ page }) => {
  await authenticate(page);
  let configurationReads = 0, historyReads = 0;
  await page.route("**/api/commerce/v1/reference-options/invoice-charge-*", route => json(route, []));
  await page.route("**/api/commerce/v1/invoice-charges?**", route => {
    configurationReads++;
    return json(route, { items: [], totalCount: 0, page: 1, pageSize: 25, totalPages: 0 });
  });
  await page.route("**/api/commerce/v1/invoice-charges/history?**", route => {
    historyReads++;
    return json(route, { items: [{ documentId: saleId, documentNumber: "FV-CARGO-1", customerName: "Cliente de prueba", issuedAt: "2026-09-19T10:00:00-05:00",
      workSessionId: closureId, invoiceTotal:90000.25,expenseCreatedByName:"Operador de caja",fiscalNumber:"FVL100",expenseId:saleId,expenseAcceptedAt:"2026-09-19T11:05:00-05:00",expenseIssuedAt:"2026-09-19T10:00:00-05:00",expenseGrossAmount:5000,expenseFiscalNumber:"DS100",expenseDocumentNumber: "GAS-123", expenseStatus: "Processed", payableBalance: 5000,
      charge: { appliedChargeId: "one", name: "Domicilio", supplier: { name: "Domiciliario prueba" }, amount: 5000,
        invoicedAmount: 5000, expenseAmount: 0 } }, { documentId:saleId, documentNumber:"FV-CARGO-1",invoiceTotal:90000.25,expenseCreatedByName:"Operador de caja", fiscalNumber:"FVL100",customerName:"Cliente de prueba",issuedAt:"2026-09-19T10:00:00-05:00",workSessionId:closureId,expenseId:closureId,expenseDocumentNumber:"GAS-124",expenseFiscalNumber:null,expenseAcceptedAt:"2026-09-19T11:06:00-05:00",expenseDueDate:"2026-09-25T10:00:00-05:00",expenseGrossAmount:6500,expenseWithholdingAmount:650,expenseNetPayable:5850,expenseStatus:"Cancelled",payableBalance:0,charge:{appliedChargeId:"two",name:"Agotados",supplier:{name:"Proveedor con nombre largo de servicios logísticos y transporte regional",identification:"901234567"},amount:6500,invoicedAmount:0,expenseAmount:6500}}], totalCount: 2, totalDocuments:1, page: 1, pageSize: 20, invoicedTotal: 5000, expenseTotal: 6500, generatedExpenseTotal: 11500 });
  });
  await page.goto("/dashboard/invoice-charges");
  await expect(page.getByRole("heading", { name: "Cargos de facturación", exact: true })).toBeVisible();
  await expect(page.getByText("Todavía no hay cargos", { exact: true })).toBeVisible();
  expect(configurationReads).toBe(1); expect(historyReads).toBe(0);
  await page.getByRole("tab", { name: "Consulta de cargos" }).click();
  const trigger=page.getByRole("button",{name:"Ver cargos de FV-CARGO-1",exact:true});
  await expect(trigger).toBeVisible();await expect(trigger).toContainText("Cliente de prueba");
  await expect(trigger).toContainText("2 cargos");await expect(trigger).toContainText("2 gastos generados");
  await expect(page.getByRole("region", { name: "Gastos de FV-CARGO-1", exact: true })).toHaveCount(0);
  const card = await page.getByText("Cargos en el periodo").boundingBox();
  const filter = await page.getByText("Desde", { exact: true }).boundingBox();
  expect(card!.y).toBeLessThan(filter!.y);
  await expect(page.getByText("DIAN FVL100",{exact:true})).toBeVisible();
  let expenseReads=0;page.on("request",request=>{if(new URL(request.url()).pathname.includes("/expenses/"))expenseReads++;});
  await page.getByRole("button",{name:"No mostrar de nuevo",exact:true}).click();
  for(const width of [1366,1920,390]){
    await page.setViewportSize({width,height:width===1366?768:844});
    const before=performance.now();await trigger.focus();await page.keyboard.press("Enter");
    const region=page.getByRole("region",{name:"Gastos de FV-CARGO-1",exact:true});
    const expense=region.getByRole("row",{name:"Gasto Domicilio",exact:true});
    await expect(expense).toBeVisible();await expect(trigger).toHaveAttribute("aria-expanded","true");
    expect(performance.now()-before).toBeLessThan(1500);
    await expect(page.getByText("DIAN FVL100",{exact:true})).toHaveCount(1);
    await expect(trigger).toContainText("Total factura");await expect(trigger).toContainText("90.000,25");
    await expect(expense).toContainText("Operador de caja");
    await expect(expense.getByTestId("charge-customer-amount")).toContainText("5.000");
    await expect(expense.getByTestId("charge-company-amount")).toContainText("$ 0");

    await expect(expense.getByText("DIAN DS100",{exact:true})).toBeVisible();
    await expect(expense).toContainText("11:05");
    await expect(expense.getByRole("cell").nth(5)).toContainText("5.000");
    const cancelled=region.getByRole("row",{name:"Gasto Agotados",exact:true});
    await expect(region.getByRole("row")).toHaveCount(3);
    await expect(expense.getByRole("cell")).toHaveCount(7);
    await expect(cancelled).toContainText("Proveedor con nombre largo de servicios logísticos y transporte regional");
    await expect(cancelled).toContainText("Cancelado");
    await expect(cancelled.getByRole("cell").nth(5)).toContainText("Por pagar: $ 0");
    await expect(region.getByRole("columnheader",{name:"Asumido por el cliente",exact:true})).toHaveCount(1);
    await expect(region.getByText("Vencimiento",{exact:true})).toHaveCount(0);
    expect(await page.getByRole("main").evaluate(element=>element.scrollWidth<=element.clientWidth+1)).toBe(true);
    const cells=await expense.getByRole("cell").evaluateAll(elements=>elements.map(element=>element.getBoundingClientRect().top));
    expect(Math.max(...cells)-Math.min(...cells)).toBeLessThanOrEqual(1);
    expect((await expense.boundingBox())!.height).toBeLessThan(160);
    expect((await cancelled.boundingBox())!.height).toBeLessThan(160);
    await trigger.scrollIntoViewIfNeeded();
    await page.screenshot({path:test.info().outputPath(`charge-expense-${width}.png`)});
    await region.screenshot({path:test.info().outputPath(`charge-expense-rows-${width}.png`)});
    if(width===390){
      await region.focus();await page.keyboard.press("End");
      await region.evaluate(element=>{element.scrollLeft=element.scrollWidth;});
      await expect(cancelled.getByText("Cancelado",{exact:true})).toBeInViewport();
      await region.screenshot({path:test.info().outputPath("charge-expense-rows-mobile-amounts.png")});
      await region.evaluate(element=>{element.scrollLeft=0;});
    }
    await trigger.click();await expect(expense).toBeHidden();
  }
  expect(expenseReads).toBe(0);expect(historyReads).toBe(1);
  await page.screenshot({ path: test.info().outputPath("consulta-cargos.png"), fullPage: true });
  expect(configurationReads).toBe(1); expect(historyReads).toBe(1);
  await page.getByRole("button", { name: "Actualizar", exact: true }).click();
  await expect.poll(() => historyReads).toBe(2);
  expect(configurationReads).toBe(1);
  await page.getByLabel("Desde", { exact: true }).click();
  await page.getByRole("button", { name: "Mes anterior" }).click();
  await page.getByRole("button", { name: "Mes anterior" }).click();
  await page.getByRole("button", { name: /^1 de / }).first().click();
  await expect(page.getByRole("main").getByRole("alert")).toContainText("31 días");
  expect(historyReads).toBe(2);
});

test("consulta de cargos busca y pagina en el servidor", async ({ page }) => {
  await authenticate(page);
  const requests: Array<{ page: string | null; pageSize: string | null; search: string | null }> = [];
  await page.route("**/api/commerce/v1/reference-options/invoice-charge-*", route => json(route, []));
  await page.route("**/api/commerce/v1/invoice-charges?**", route =>
    json(route, { items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 0 }));
  await page.route("**/api/commerce/v1/invoice-charges/history?**", route => {
    const url = new URL(route.request().url());
    const requested = { page: url.searchParams.get("page"), pageSize: url.searchParams.get("pageSize"), search: url.searchParams.get("search") };
    requests.push(requested);
    const filtered = requested.search === "Domicilio";
    const second = requested.page === "2";
    const items=Array.from({length:filtered||second?1:20},(_,index)=>({
      documentId:second?closureId:`invoice-${index}`,documentNumber:second?"FV-CARGO-21":`FV-CARGO-${index+1}`,
      customerName:"Cliente de prueba",invoiceTotal:105000,expenseCreatedByName:"Operador de caja",issuedAt:"2026-09-19T10:00:00-05:00",workSessionId:closureId,
      expenseId:crypto.randomUUID(),expenseDocumentNumber:`GAS-${index+1}`,expenseStatus:"Accepted",payableBalance:null,expenseGrossAmount:5000,
      charge:{appliedChargeId:`charge-${second?21:index}`,name:"Domicilio",supplier:{name:"Proveedor de prueba"},amount:5000,invoicedAmount:5000,expenseAmount:0}
    }));
    return json(route, { items,
      totalCount: filtered ? 1 : 21, totalDocuments:filtered ? 1 : 21, page: second ? 2 : 1, pageSize: 20,
      invoicedTotal: filtered ? 5000 : 105000, expenseTotal: 0, generatedExpenseTotal: filtered ? 5000 : 105000 });
  });
  await page.goto("/dashboard/invoice-charges");
  await page.getByRole("tab", { name: "Consulta de cargos" }).click();
  await expect(page.getByRole("button", { name: "Ver cargos de FV-CARGO-1", exact:true })).toBeVisible();
  await expect(page.getByText("21", { exact: true }).first()).toBeVisible();
  await expect(page.getByRole("button",{name:/^Ver cargos de FV-CARGO-/})).toHaveCount(20);
  await page.getByRole("button",{name:"No mostrar de nuevo",exact:true}).click();
  const first=page.getByRole("button",{name:"Ver cargos de FV-CARGO-1",exact:true});
  await first.click();
  await expect(page.getByRole("row",{name:"Gasto Domicilio",exact:true})).toContainText("Aceptado");
  await expect(page.getByRole("row",{name:"Gasto Domicilio",exact:true})).toContainText("Sin cuenta registrada");
  await first.click();
  for(const width of [1366,390]){
    await page.setViewportSize({width,height:768});
    expect(await page.getByRole("main").evaluate(element=>element.scrollWidth<=element.clientWidth+1)).toBe(true);
    await page.getByRole("button",{name:"Ver cargos de FV-CARGO-1",exact:true}).scrollIntoViewIfNeeded();
    await page.screenshot({path:test.info().outputPath(`charge-full-page-${width}.png`)});
    await page.getByRole("button",{name:"Ver cargos de FV-CARGO-20",exact:true}).scrollIntoViewIfNeeded();
    await page.screenshot({path:test.info().outputPath(`charge-full-page-bottom-${width}.png`)});
  }
  await page.getByRole("button", { name: "Siguiente página" }).click();
  await expect(page.getByRole("button", { name: "Ver cargos de FV-CARGO-21", exact:true })).toBeVisible();
  await page.getByPlaceholder("Cargo, proveedor o factura").fill("Domicilio");
  await expect(page.getByRole("button", { name: "Ver cargos de FV-CARGO-1", exact:true })).toBeVisible();
  await expect.poll(() => requests.at(-1)).toEqual({ page: "1", pageSize: "20", search: "Domicilio" });
  await expect(page.getByRole("button", { name: "Siguiente página" })).toBeDisabled();
});

test("consulta de cargos muestra carga vacío y error sin consultas adicionales al desplegar",async({page})=>{
  await authenticate(page);
  await page.route("**/api/commerce/v1/reference-options/invoice-charge-*",route=>json(route,[]));
  await page.route("**/api/commerce/v1/invoice-charges?**",route=>json(route,{items:[],totalCount:0,page:1,pageSize:20,totalPages:0}));
  let fail=false;let pending:Route|undefined;
  await page.route("**/api/commerce/v1/invoice-charges/history?**",async route=>{pending=route;});
  await page.goto("/dashboard/invoice-charges");await page.getByRole("tab",{name:"Consulta de cargos"}).click();
  await expect(page.getByRole("status")).toContainText("Consultando cargos");
  await expect.poll(()=>Boolean(pending)).toBe(true);
  await json(pending!,{items:[],totalCount:0,totalDocuments:0,page:1,pageSize:20,invoicedTotal:0,expenseTotal:0,generatedExpenseTotal:0});
  await expect(page.getByText("No hay cargos en este periodo.",{exact:true})).toBeVisible();
  await expect(page.getByRole("button",{name:"Siguiente página"})).toHaveCount(0);
  await page.unroute("**/api/commerce/v1/invoice-charges/history?**");
  await page.route("**/api/commerce/v1/invoice-charges/history?**",route=>fail?route.fulfill({status:503,contentType:"application/json",body:JSON.stringify({detail:"Servicio no disponible"})}):json(route,{items:[],totalCount:0,totalDocuments:0,page:1,pageSize:20,invoicedTotal:0,expenseTotal:0,generatedExpenseTotal:0}));
  fail=true;await page.getByRole("button",{name:"Actualizar",exact:true}).click();
  await expect(page.getByRole("main").getByRole("alert")).toContainText("No fue posible consultar los cargos");
  fail=false;await page.getByRole("button",{name:"Actualizar",exact:true}).click();
  await expect(page.getByRole("main").getByRole("alert")).toHaveCount(0);
  await expect(page.getByText("No hay cargos en este periodo.",{exact:true})).toBeVisible();
});

test("consulta de cargos filtra identidades y exporta el conjunto completo sin recargar la lista", async ({ page }) => {
  test.setTimeout(120_000);
  await authenticate(page);
  const legalName = "DISTRIBUCIONES DE PRODUCTOS Y SERVICIOS DEL NORTE Y DEL CARIBE SOCIEDAD POR ACCIONES SIMPLIFICADA";
  const logo = `data:image/png;base64,${(await readFile("public/brand/auraly-mark.png")).toString("base64")}`;
  let historyReads = 0, brandingReads = 0, failExport = false;
  const optionsReads: string[] = [], exports: URL[] = [];
  const items = [0, 1, 2].map(index => ({ documentId: index < 2 ? saleId : closureId,
    documentNumber: index < 2 ? "CVI00-00000001" : "CVI00-00000002", fiscalNumber: index < 2 ? "FVL100" : "FVL101",
    invoiceTotal: index < 2 ? 90000.25 : 60000, customerName: "Cliente de prueba", issuedAt: "2026-09-19T10:00:00-05:00",
    workSessionId: closureId, expenseId: `expense-${index}`, expenseDocumentNumber: `GAS-${index + 1}`,
    expenseCreatedByName: "Operador de caja", expenseAcceptedAt: "2026-09-19T11:05:00-05:00", expenseStatus: "Processed",
    expenseGrossAmount: 5000, payableBalance: 5000,
    charge: { appliedChargeId: `charge-${index}`, name: "Domicilio", supplier: { name: "Domiciliario prueba" },
      amount: 5000, invoicedAmount: index === 1 ? 0 : 5000, expenseAmount: index === 1 ? 5000 : 0 } }));
  const totals = { totalCount: 3, totalDocuments: 2, invoicedTotal: 10000, expenseTotal: 5000, generatedExpenseTotal: 15000 };
  await page.route("**/api/commerce/v1/invoice-charges?**", route => json(route, { items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 0 }));
  await page.route("**/api/commerce/v1/reference-options/invoice-charge-*", route => json(route, []));
  await page.route("**/api/commerce/v1/invoice-charges/history?**", route => {
    historyReads++; return json(route, { items: items.slice(0, 2), ...totals, page: 1, pageSize: 20 });
  });
  await page.route("**/api/commerce/v1/invoice-charges/history/export?**", route => {
    exports.push(new URL(route.request().url()));
    return failExport ? route.fulfill({ status: 400, contentType: "application/problem+json", body: JSON.stringify({ detail: "El reporte supera 5.000 cargos. Reduce el rango de fechas." }) })
      : json(route, { items, ...totals, page: 1, pageSize: 5000 });
  });
  await page.route("**/api/commerce/v1/parties/role-options?**", route => {
    const url = new URL(route.request().url()), role = url.searchParams.get("role")!;
    optionsReads.push(role); expect(url.searchParams.get("includeInactive")).toBe("true");
    return json(route, { items: [{ role, roleId: role === "User" ? userId : saleId, partyId: saleId,
      displayName: role === "User" ? "Operador de caja" : "Cliente de prueba", identification: "900123456" }], page: 1, pageSize: 10, totalPages: 1, totalCount: 1 });
  });
  await page.route("**/api/tenants/branding/print", route => {
    brandingReads++; return json(route, { tenantId, displayName: "Empresa", legalName, logoUrl: logo, nit: "900100200" });
  });
  await page.goto("/dashboard/invoice-charges");
  await page.getByRole("tab", { name: "Consulta de cargos" }).click();
  await expect(page.getByRole("button", { name: "Ver cargos de CVI00-00000001", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "No mostrar de nuevo", exact: true }).click();
  await expect(page.getByText("Gastos generados", { exact: true }).locator("..")).toContainText("15.000");
  await page.getByRole("button", { name: "Filtros", exact: true }).click();
  expect(optionsReads).toEqual([]);
  await page.getByRole("combobox", { name: "Filtrar por usuario que generó el gasto" }).click();
  await page.getByRole("option", { name: /Operador de caja/ }).click();
  await expect.poll(() => historyReads).toBe(2);
  await page.getByRole("combobox", { name: "Filtrar cargos por cliente" }).click();
  await page.getByRole("option", { name: /Cliente de prueba/ }).click();
  await expect.poll(() => historyReads).toBe(3);
  expect(optionsReads).toEqual(["User", "Customer"]);
  await page.getByPlaceholder("Cargo, proveedor o factura").fill("FVL10");
  await expect.poll(() => historyReads).toBe(4);
  for (const mode of ["detail", "summary"]) {
    if (mode === "summary") {
      await page.getByRole("combobox", { name: "Tipo de reporte" }).click();
      await page.getByRole("option", { name: "Consolidado por factura" }).click();
    }
    await page.getByRole("button", { name: "Generar reporte", exact: true }).click();
    const title = mode === "detail" ? "Cargos por factura — detallado" : "Cargos por factura — consolidado";
    await expect(page.getByRole("heading", { name: title, exact: true })).toHaveCount(1);
    const report = page.locator("#auraly-report-print-area");
    await expect(report.getByText(legalName, { exact: true })).toHaveCount(1);
    await expect(report).toContainText("FVL101");
    await expect(report).toContainText("Totales del filtro");
    await expect(report).toContainText("15.000");
    const image = report.getByRole("img", { name: `Logo de ${legalName}`, exact: true });
    await expect(image).toBeVisible(); await expect(image).toHaveCSS("object-fit", "contain");
    const box = await image.locator("..").boundingBox(); expect(box!.width).toBe(box!.height);
    for (const width of [1366, 390]) {
      await page.setViewportSize({ width, height: 768 });
      expect(await page.getByRole("main").evaluate(element => element.scrollWidth <= element.clientWidth + 1)).toBe(true);
      await page.screenshot({ path: test.info().outputPath(`charge-report-${mode}-${width}.png`), fullPage: true });
    }
    const spreadsheet = page.waitForEvent("download"); await page.getByRole("button", { name: "Exportar Excel", exact: true }).click();
    const excel = await spreadsheet; const file = test.info().outputPath(`charge-report-${mode}.xlsx`); await excel.saveAs(file);
    const xml = Object.values(unzipSync(await readFile(file))).map(value => strFromU8(value)).join("\n");
    expect(xml).toContain("FVL101"); expect(xml).toContain("15000");
    const pdfDownload = page.waitForEvent("download"); await page.getByRole("button", { name: "Descargar PDF", exact: true }).click();
    const pdf = await pdfDownload; const pdfPath = test.info().outputPath(`charge-report-${mode}.pdf`); await pdf.saveAs(pdfPath);
    expect((await readFile(pdfPath)).subarray(0, 4).toString()).toBe("%PDF");
    await page.evaluate(() => { window.print = () => { document.documentElement.dataset.printCalled = "true"; }; });
    await page.getByRole("button", { name: "Imprimir", exact: true }).click();
    await expect(page.locator("html")).toHaveAttribute("data-print-called", "true");
    await page.emulateMedia({ media: "print" });
    await page.pdf({ path: test.info().outputPath(`charge-report-${mode}-browser-print.pdf`), format: "A4", landscape: true });
    await page.emulateMedia({ media: "screen" });
    await page.getByRole("button", { name: "Volver", exact: true }).click();
    await expect(page.getByRole("button", { name: "Ver cargos de CVI00-00000001", exact: true })).toBeVisible();
    expect(historyReads).toBe(4); expect(optionsReads).toEqual(["User", "Customer"]);
  }
  expect(exports).toHaveLength(2); expect(brandingReads).toBe(1);
  for (const url of exports) {
    expect(url.searchParams.get("createdByUserId")).toBe(userId);
    expect(url.searchParams.get("customerId")).toBe(saleId);
    expect(url.searchParams.get("search")).toBe("FVL10");
  }
  failExport = true;
  await page.getByRole("button", { name: "Generar reporte", exact: true }).click();
  await expect(page.getByRole("main").getByRole("alert")).toContainText("5.000");
  await expect(page.getByRole("heading", { name: /Cargos por factura/ })).toHaveCount(0);
  expect(historyReads).toBe(4);
});

test("configurar un cargo conserva concepto y proveedor y reutiliza el resultado al editar", async ({ page }) => {
  await authenticate(page);
  const supplierId = crypto.randomUUID(), conceptId = crypto.randomUUID(), taxId = crypto.randomUUID();
  let items: Record<string, unknown>[] = [], reads = 0, writes = 0, optionsReads = 0;
  const calculation = [{ code: "Manual", label: "Valor manual" }, { code: "Ranges", label: "Por rangos" }, { code: "Fixed", label: "Valor fijo" }, { code: "Percentage", label: "Porcentaje" }];
  const inclusion = [{ code: "Never", label: "No incluir en factura" }, { code: "Always", label: "Siempre en factura" }, { code: "UpToInvoiceAmount", label: "Hasta un importe de factura" }];
  await page.route("**/api/commerce/v1/reference-options/invoice-charge-calculation", route => json(route, calculation));
  await page.route("**/api/commerce/v1/reference-options/invoice-charge-inclusion", route => json(route, inclusion));
  await page.route("**/api/commerce/v1/invoice-charges?**", route => {
    reads++;
    expect(new URL(route.request().url()).searchParams.get("pageSize")).toBe("20");
    return json(route, { items, page: 1, pageSize: 20, totalCount: items.length, totalPages: 1 });
  });
  await page.route("**/api/commerce/v1/invoice-charges/options", route => {
    optionsReads++;
    return json(route, { concepts: [{ conceptId, name: "Domicilios", expenseAccountCode: "519595", expenseAccountName: "Servicios", defaultCostCenterName: "Operación" }], taxProfiles: [{ taxProfileId: taxId, name: "IVA 0", rate: 0 }] });
  });
  await page.route("**/api/commerce/v1/parties/role-options?**", route => json(route, { items: [{ role: "Supplier", roleId: supplierId, partyId: supplierId, displayName: "Domiciliario prueba", identification: "TEST" }], page: 1, totalPages: 1, totalCount: 1 }));
  await page.route("**/api/commerce/v1/invoice-charges/*", async route => {
    if (route.request().method() !== "PUT") return route.fallback();
    const input = route.request().postDataJSON();
    writes++;
    expect(input).toMatchObject({ expenseConceptId: conceptId, supplierIds: [supplierId], salesTaxProfileId: taxId, purchaseTaxProfileId: taxId });
    if (writes === 1) expect(input).toMatchObject({ expectedVersion: 0, calculationMode: "Manual", value: 5000, inclusionMode: "Never" });
    else expect(input).toMatchObject({ expectedVersion: 1, calculationMode: "Ranges", invoiceAmountLimit: 80000, inclusionMode: "UpToInvoiceAmount", ranges: [{ fromInclusive: 0, toExclusive: 400000, calculationMode: "Fixed", value: 5000 }, { fromInclusive: 400000, toExclusive: null, calculationMode: "Percentage", value: 2 }] });
    const saved = { ...input, businessId, version: writes, expenseConceptName: "Domicilios", expenseAccountCode: "519595", expenseAccountName: "Servicios", costCenterName: "Operación", suppliers: [{ supplierId, name: "Domiciliario prueba", identification: "TEST", isActive: true }] };
    items = [saved];
    await json(route, saved);
  });
  await page.goto("/dashboard/invoice-charges");
  await expect(page.getByRole("main").getByRole("combobox")).toContainText("20");
  expect(optionsReads).toBe(0);
  await page.getByRole("button", { name: "Crear cargo", exact: true }).click();
  let dialog = page.getByRole("dialog");
  await dialog.getByLabel("Nombre", { exact: true }).fill("Agotados");
  await dialog.getByLabel("Código", { exact: true }).fill("AGOTADOS");
  await dialog.getByLabel("Modalidad", { exact: true }).click();
  await page.getByRole("option", { name: "Valor manual", exact: true }).click();
  await dialog.getByLabel("Valor sugerido (COP)").fill("5000");
  await dialog.getByLabel("Aplicación del cargo", { exact: true }).click();
  await page.getByRole("option", { name: "No incluir en factura", exact: true }).click();
  await dialog.getByLabel("Concepto de gasto", { exact: true }).click();
  await page.getByRole("option", { name: "Domicilios", exact: true }).click();
  for (const label of ["Impuesto al facturar", "Impuesto del costo del proveedor"]) {
    await dialog.getByLabel(label, { exact: true }).click();
    await page.getByRole("option", { name: "IVA 0", exact: true }).click();
  }
  await dialog.getByRole("combobox", { name: "Seleccionar proveedor" }).click();
  await page.getByRole("option", { name: /Domiciliario prueba/ }).click();
  await dialog.getByRole("button", { name: "Guardar cargo" }).click();
  await expect(dialog).not.toBeVisible();
  await expect(page.getByRole("cell", { name: /^Agotados/ })).toBeVisible();
  expect(reads).toBe(2);
  await page.getByRole("button", { name: "Editar Agotados" }).click();
  dialog = page.getByRole("dialog");
  await expect(dialog).toContainText("519595 · Servicios");
  await expect(dialog).toContainText("Domiciliario prueba");
  await dialog.getByLabel("Nombre", { exact: true }).fill("Domicilio");
  await dialog.getByLabel("Modalidad", { exact: true }).click();
  await page.getByRole("option", { name: "Por rangos", exact: true }).click();
  await dialog.getByRole("button", { name: "Agregar rango" }).click();
  await dialog.locator("#range-to-0").fill("400000");
  await dialog.locator("#range-mode-0").click();
  await page.getByRole("option", { name: "Valor fijo", exact: true }).click();
  await dialog.locator("#range-value-0").fill("5000");
  await dialog.getByRole("button", { name: "Agregar rango" }).click();
  await expect(dialog.locator("#range-from-1")).toHaveValue("400000");
  await dialog.locator("#range-mode-1").click();
  await page.getByRole("option", { name: "Porcentaje", exact: true }).click();
  await dialog.locator("#range-value-1").fill("2");
  await dialog.getByLabel("Aplicación del cargo", { exact: true }).click();
  await page.getByRole("option", { name: "Hasta un importe de factura", exact: true }).click();
  await dialog.getByLabel("Incluir hasta un importe de factura de (COP)").fill("80000");
  await page.screenshot({ path: test.info().outputPath("configuracion-cargo.png"), fullPage: true });
  await dialog.getByRole("button", { name: "Guardar cargo" }).click();
  await expect(dialog).not.toBeVisible();
  await expect(page.getByRole("cell", { name: /^Domicilio/ }).first()).toBeVisible();
  expect(reads).toBe(2); expect(writes).toBe(2);
});
