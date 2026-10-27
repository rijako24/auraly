import { expect, test, type Page } from "@playwright/test";

const tenantId = "11111111-1111-1111-1111-111111111111";
const businessId = "22222222-2222-2222-2222-222222222222";

async function openSubscription(page: Page, baseURL: string, existingOrder: boolean) {
  const user = { userId: "33333333-3333-3333-3333-333333333333", tenantId, tenantKey: "@continuity",
    username: "administrator", firstName: "Prueba", lastName: "Suscripción", roles: [], permissions: ["tenant.profile.read"] };
  await page.context().addCookies([{ name: "auth_token", value: "continuity", url: baseURL, httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ user, businessId }) => {
    localStorage.setItem("selected_tenant_id", user.tenantId);
    localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
  }, { user, businessId });
  const state = { subscriptionReads: 0, catalogReads: 0, orderReads: 0, checkouts: 0, confirmations: 0,
    paid: false, quotes: [] as Record<string, unknown>[], saves: [] as Record<string, unknown>[] };
  let releaseOrder = () => {};
  const orderGate = new Promise<void>(resolve => { releaseOrder = resolve; });
  const plan = { planId: "plan", code: "company", name: "Empresa", monthlyPriceCop: 100_000,
    salesTaxRate: 19, annualDiscountRate: .12, includedFullUsers: 2, includedSellerUsers: 0,
    includedPosDevices: 1, includedDianDocuments: 1_000, includedPayrollEmployees: 5,
    isRecommended: true, isCustom: false, features: ["Operación comercial"] };
  const quote = { planCode: plan.code, planName: plan.name, billingPeriod: "Monthly",
    monthlySubtotalCop: 100_000, periods: 1, grossPeriodAmountCop: 100_000, discountRate: 0,
    discountAmountCop: 0, taxAmountCop: 19_000, payableAmountCop: 119_000, monthlyEquivalentCop: 119_000,
    fullUserLimit: 2, sellerUserLimit: 0, posDeviceLimit: 1, dianDocumentMonthlyLimit: 1_750,
    payrollEmployeeLimit: 25, lines: [{ code: "dian_document_pack", quantity: 3 }, { code: "payroll_employee_pack", quantity: 4 }] };
  const order = { renewalOrderId: "renewal", revision: 1, status: "Draft", isCurrent: true,
    targetPeriodStart: "2026-10-01T00:00:00Z", targetPeriodEnd: "2026-11-01T00:00:00Z",
    dueAt: "2026-10-01T00:00:00Z", quote,
    usage: { fullUsers: 1, sellerUsers: 0, posDevices: 1, payrollEmployees: 0 } };
  await page.route("https://checkout.wompi.co/**", route => route.fulfill({ contentType: "application/javascript",
    body: "window.WidgetCheckout = class { open(callback) { callback({transaction:{id:'mock-paid'}}); } };" }));
  await page.route("**/api/**", async route => {
    const request = route.request(), path = new URL(request.url()).pathname;
    let body: unknown = [];
    if (path === "/api/auth/me") body = user;
    else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Empresa" }];
    else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Sede" }];
    else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, permissions: user.permissions, roles: [] };
    else if (path === "/api/tenant-commercial/subscription") {
      state.subscriptionReads++;
      body = { subscriptionId: "subscription", planCode: plan.code, planName: plan.name, billingPeriod: "Monthly",
        status: state.paid ? "Active" : "PastDue", currentPeriodStart: "2026-09-01T00:00:00Z", currentPeriodEnd: "2026-10-01T00:00:00Z",
        fullUserLimit: 2, sellerUserLimit: 0, posDeviceLimit: 1, dianDocumentMonthlyLimit: 1_500,
        dianDocumentsUsed: state.paid ? 0 : 1_500, payrollEmployeeLimit: 15, hasDianDocumentQuota: state.paid };
    } else if (path === "/api/tenant-commercial/catalog") {
      state.catalogReads++;
      body = { plans: [plan], addOns: [
        { addOnId: "documents", code: "dian_document_pack", name: "Paquete documentos DIAN", unitLabel: "250 documentos", unitSize: 250, monthlyUnitPriceCop: 5_000, salesTaxRate: 19 },
        { addOnId: "payroll", code: "payroll_employee_pack", name: "Paquete empleados de nómina", unitLabel: "5 empleados", unitSize: 5, monthlyUnitPriceCop: 10_000, salesTaxRate: 19 },
      ] };
    } else if (path === "/api/tenant-commercial/subscription/renewal-order") {
      if (request.method() === "GET") { state.orderReads++; await orderGate; body = existingOrder ? { ...order, status: state.paid ? "Activated" : order.status } : null; }
      else {
        state.saves.push(request.postDataJSON());
        body = { ...order, revision: 2, quote: { ...quote, monthlySubtotalCop: 110_000,
          grossPeriodAmountCop: 110_000, taxAmountCop: 20_900, payableAmountCop: 130_900, monthlyEquivalentCop: 130_900 } };
      }
    } else if (path === "/api/tenant-commercial/subscription/renewal-order/checkout") {
      state.checkouts++;
      body = { renewalOrderId: "renewal", widget: { currency: "COP", amountInCents: 11_900_000,
        reference: "mock-renewal", publicKey: "mock-key", integritySignature: "mock-signature" } };
    } else if (path === "/api/tenant-commercial/subscription/renewal-orders/renewal/confirm") {
      state.confirmations++; state.paid = true;
      body = { documentId: "receipt", documentNumber: "TEST-1", issuedAt: "2026-10-01T00:00:00Z",
        periodStart: order.targetPeriodStart, periodEnd: order.targetPeriodEnd, billingPeriod: "Monthly",
        currencyCode: "COP", subtotal: 100_000, taxAmount: 19_000, totalAmount: 119_000,
        paymentMethod: "Mock", paymentReference: "mock-paid", cufe: null, fiscalStatus: "Pending", lines: [] };
    } else if (path === "/api/tenant-commercial/quote") {
      state.quotes.push(request.postDataJSON()); body = quote;
    } else if (path.endsWith("/reference-options/tenant-subscription-status")) body = [
      { id: "past-due", code: "PastDue", label: "Pendiente de pago", isActive: true, sortOrder: 10 },
    ];
    else if (path.includes("/dashboard/") && path.endsWith("/subscription")) body = null;
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
  });
  await page.goto("/dashboard/subscription");
  await expect.poll(() => state.orderReads).toBe(1);
  await expect(page.getByText("Tienes una renovación pendiente de pago.")).toBeVisible();
  await expect(page.getByText("No tienes documentos DIAN disponibles.", { exact: true })).toBeVisible();
  expect(state.quotes).toHaveLength(0);
  releaseOrder();
  return state;
}

test("La renovación espera la orden existente, conserva sus cantidades y no vuelve a consultar después de guardar", async ({ page, baseURL }) => {
  const state = await openSubscription(page, baseURL!, true);
  await expect(page.getByLabel("Paquete documentos DIAN · 250 documentos")).toHaveValue("3");
  await expect(page.getByLabel("Paquete empleados de nómina · 5 empleados")).toHaveValue("4");
  await page.getByRole("button", { name: "Actualizar orden", exact: true }).click();
  await expect(page.getByText("Revisión 2", { exact: true })).toBeVisible();
  await expect(page.getByText(/^\$\s*130\.900$/)).toBeVisible();
  expect(state.saves).toHaveLength(1);
  expect(state.saves[0]).toMatchObject({ dianDocumentPacks: 3, payrollEmployeePacks: 4 });
  expect(state.subscriptionReads).toBe(1);
  expect(state.catalogReads).toBe(1);
  expect(state.orderReads).toBe(1);
  expect(state.quotes).toHaveLength(1);
});

test("Sin orden previa, la propuesta usa el tamaño de los paquetes del catálogo", async ({ page, baseURL }) => {
  const state = await openSubscription(page, baseURL!, false);
  await expect(page.getByLabel("Paquete documentos DIAN · 250 documentos")).toHaveValue("2");
  await expect(page.getByLabel("Paquete empleados de nómina · 5 empleados")).toHaveValue("2");
  await expect.poll(() => state.quotes.length).toBe(1);
  expect(state.quotes[0]).toMatchObject({ dianDocumentPacks: 2, payrollEmployeePacks: 2 });
});

test("Confirmar el pago actualiza cupo y orden con una lectura de cada recurso", async ({ page, baseURL }) => {
  const state = await openSubscription(page, baseURL!, true);
  const pay = page.getByRole("button", { name: "Pagar renovación", exact: true });
  await expect(pay).toBeEnabled();
  await pay.click();
  await expect(page.getByRole("heading", { name: "Pago confirmado", exact: true })).toBeVisible();
  await expect.poll(() => [state.subscriptionReads, state.orderReads]).toEqual([2, 2]);
  await expect(page.getByText("No tienes documentos DIAN disponibles.", { exact: true })).toHaveCount(0);
  await expect(page.getByText("Tienes una renovación pendiente de pago.", { exact: true })).toHaveCount(0);
  expect(state.checkouts).toBe(1);
  expect(state.confirmations).toBe(1);
  expect(state.catalogReads).toBe(1);
  expect(state.quotes).toHaveLength(1);
});
