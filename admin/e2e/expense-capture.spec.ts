import { expect, test, type Page } from "@playwright/test";

const tenantId = "11111111-1111-1111-1111-111111111111", businessId = "22222222-2222-2222-2222-222222222222";
const accountId = "44444444-4444-4444-4444-444444444444", supplierId = "55555555-5555-5555-5555-555555555555";
const conceptId = "66666666-6666-6666-6666-666666666666";
async function openExpenses(page: Page, baseURL: string, enterSupplierInvoice = true,
  behavior: {holdPreview?: boolean; manageWithholdings?: boolean; rules?: object[]} = {}) {
  const user = { userId: "33333333-3333-3333-3333-333333333333", tenantId, tenantKey: "@expense",
    username: "accountant", firstName: "Prueba", lastName: "Gastos", roles: [], permissions: ["expenses.read", "expenses.create", "expenses.configure", "expenses.cancel",
      ...(behavior.manageWithholdings ? ["commerce.taxation.withholdings.manage"] : [])] };
  await page.context().addCookies([{ name: "auth_token", value: "expenses", url: baseURL, httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ user, businessId }) => {
    localStorage.setItem("selected_tenant_id", user.tenantId); localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
  }, { user, businessId });
  const state = { preview: [] as Record<string, unknown>[], confirm: [] as Record<string, unknown>[], list: 0, accountReads: 0, conflict: false, canConfirm: true };
  let releasePreview = () => {};
  const previewGate = new Promise<void>(resolve => { releasePreview = resolve; });
  await page.route("**/api/**", async route => {
    const request = route.request(), path = new URL(request.url()).pathname;
    let body: unknown = [], status = 200;
    if (path === "/api/auth/me") body = user;
    else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Empresa" }];
    else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Sede" }];
    else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, permissions: user.permissions, roles: [] };
    else if (path.endsWith("/accounting/readiness")) body = { status: "Ready", blockingIssues: [] };
    else if (path.endsWith("/expenses/options")) body = { concepts: [{ conceptId, businessId, code: "SERV", name: "Servicio frecuente", expenseAccountId: accountId,
      expenseAccountCode: "519595", expenseAccountName: "Servicios", defaultCostCenterId: null, defaultCostCenterName: null, withholdingConceptCode: "SERVICIOS", isActive: true }],
      suppliers: [], expenseAccounts: [], costCenters: [], taxes: [{ taxProfileId: "77777777-7777-7777-7777-777777777777", name: "IVA", rate: 19 }],
      taxTreatments: [{ code: "DeductibleInputVat", label: "IVA descontable" }, { code: "CapitalizedCost", label: "Mayor valor del gasto" }],
      withholdingConceptCodes: ["SERVICIOS"], purchaseEvidenceTypes: [{ code: "SupplierElectronicInvoice", label: "Factura electrónica del proveedor" }, { code: "InternalReceiptVoucher", label: "Comprobante interno" }] };
    else if (path.endsWith("/parties/role-options")) body = { items: [{ partyId: supplierId, roleId: supplierId, role: "Supplier", displayName: "Proveedor de prueba", identification: "900123456", supplierPurchaseEvidencePolicy: null, supplierDefaultPaymentDueDays: 30, supplierId }], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 };
    else if (path.endsWith("/parties/site-options")) body = { items: [{ partySiteId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", siteName: "Sede principal", displayName: "Proveedor de prueba", identification: "900123456" }], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 };
    else if (path.endsWith("/taxation/withholding-rules")) body = behavior.rules ?? [];
    else if (path.endsWith("/accounting/account-options")) { state.accountReads++; body = { items: [{ accountId: "99999999-9999-9999-9999-999999999999", code: "519596", name: "Servicios varios", accountType: "Expense", allowsPosting: true, isActive: true }], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 }; }
    else if (path.endsWith("/expenses/preview")) {
      if (behavior.holdPreview) await previewGate;
      const value = request.postDataJSON(); state.preview.push(value);
      const base = value.lines.reduce((sum: number, line: { taxExclusiveAmount: number }) => sum + line.taxExclusiveAmount, 0);
      body = { lines: value.lines.map((line: object, index: number) => ({ ...line, lineNumber: index + 1, accountCode: "519595", accountName: "Servicios", vatAmount: 0, taxRate: 0 })),
        calculationHash: `review-${state.preview.length}`, canConfirm: state.canConfirm,
        diagnostics: state.canConfirm ? [] : ["Falta el perfil tributario del proveedor."],
        withholding: { grossAmount: base, withholdingTotal: base * .025, netAmount: base * .975,
          lines: [{ ruleId: "rule", ruleVersion: 1, ruleCode: "RF", name: "Retefuente servicios", taxableBase: base, rate: 2.5, amount: base * .025 }] } };
    } else if (path.endsWith("/expenses/confirm")) {
      state.confirm.push(request.postDataJSON());
      if (state.conflict) { status = 409; body = { detail: "El cálculo cambió. Recalcula las retenciones." }; }
      else { status = 202; body = { expenseId: request.postDataJSON().expenseId, status: "Accepted" }; }
    } else if (path.endsWith("/expenses")) { state.list++; body = { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0, grossTotal: 0, withholdingTotal: 0, netPayableTotal: 0 }; }
    await route.fulfill({ status, contentType: "application/json", body: JSON.stringify(body) });
  });
  await page.goto("/dashboard/expenses");
  await page.getByRole("button", { name: "Nuevo gasto", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Registrar gasto" });
  await dialog.getByRole("combobox", { name: "Seleccionar supplier" }).click();
  await page.getByRole("option", { name: /Proveedor de prueba/ }).click();
  await dialog.getByRole("combobox", { name: "Sede del proveedor" }).click();
  await page.getByRole("option", { name: /Sede principal/ }).click();
  if (enterSupplierInvoice) await dialog.getByLabel("Número de factura del proveedor").fill("FV-100");
  await dialog.getByRole("button", { name: "Agregar gasto", exact: true }).click();
  const line = page.getByRole("dialog", { name: "Agregar gasto", exact: true });
  await expect(line.getByRole("button", { name: "Agregar a la grilla" })).toBeDisabled();
  await expect(line.getByLabel("Gasto frecuente (opcional)")).toContainText("Seleccionar gasto frecuente");
  await expect(line.getByRole("button", { name: "Quitar selección de gasto frecuente" })).toHaveCount(0);
  await line.getByLabel("Gasto frecuente (opcional)").click();
  await page.getByRole("option", { name: "Servicio frecuente" }).click();
  await line.getByRole("button", { name: "Quitar selección de gasto frecuente" }).click();
  await expect(line.getByLabel("Gasto frecuente (opcional)")).toContainText("Seleccionar gasto frecuente");
  await expect(line.getByRole("combobox", { name: "Seleccionar cuenta contable" })).toContainText("Seleccionar cuenta");
  await line.getByLabel("Gasto frecuente (opcional)").click();
  await page.getByRole("option", { name: "Servicio frecuente" }).click();
  await line.getByLabel("Base antes de IVA").fill("120000");
  await line.getByLabel("Base antes de IVA").blur();
  await line.getByRole("button", { name: "Agregar a la grilla" }).click();
  await expect(line).toHaveCount(0);
  return { state, dialog, releasePreview };
}

test("cerrar gasto durante el cálculo libera la página y permite abrirlo otra vez", async ({ page, baseURL }) => {
  const { dialog, releasePreview } = await openExpenses(page, baseURL!, false, {holdPreview:true});
  try {
    await expect(dialog.getByText("Calculando retenciones…")).toBeVisible();
    await dialog.getByRole("button", { name: "Close", exact: true }).click();
    await expect(dialog).toHaveCount(0);
    await page.getByRole("button", { name: "Nuevo gasto", exact: true }).click();
    await expect(page.getByRole("dialog", { name: "Registrar gasto" })).toBeVisible();
  } finally {
    releasePreview();
  }
});

test("agregar retención está disponible sin responsabilidades del proveedor", async ({ page, baseURL }) => {
  const rule = {ruleId:"manual-rule",businessId,version:1,code:"RET-MAN",name:"Retención manual",kind:"IncomeTax",
    direction:"Purchase",moment:"Accrual",baseKind:"TaxExclusiveAmount",conceptCode:null,jurisdictionCode:null,
    rate:2.5,minimumBase:0,requiredResponsibilities:[],effectiveFrom:"2026-01-01",effectiveTo:null,isActive:true};
  const { dialog } = await openExpenses(page, baseURL!, false, {manageWithholdings:true,rules:[rule]});
  const add = dialog.getByRole("button", {name:"Agregar retención"});
  await expect(add).toBeVisible();
  await expect(add).toBeEnabled();
  await add.click();
  await expect(page.getByRole("dialog", {name:"Agregar retención"})).toBeVisible();
});

test("sin reglas de compra muestra el botón deshabilitado y la causa", async ({ page, baseURL }) => {
  const { dialog } = await openExpenses(page, baseURL!, false, {manageWithholdings:true,rules:[]});
  await expect(dialog.getByRole("button", {name:"Agregar retención"})).toBeVisible();
  await expect(dialog.getByRole("button", {name:"Agregar retención"})).toBeDisabled();
  await expect(dialog.getByText("No hay reglas de retención de compra vigentes para esta sede.", {exact:false})).toBeVisible();
});

test("la retención se recalcula al agregar, editar y quitar gastos antes de completar la factura", async ({ page, baseURL }) => {
  const { state, dialog } = await openExpenses(page, baseURL!, false);
  await expect.poll(() => state.preview.length).toBe(1);
  await expect(dialog.getByRole("region", { name: "Cálculo del gasto" })).toContainText("Retefuente servicios");
  await expect(dialog.getByRole("button", { name: "Confirmar gasto", exact: true })).toBeDisabled();

  await dialog.getByRole("button", { name: "Editar gasto 1" }).click();
  const editing = page.getByRole("dialog", { name: "Editar gasto" });
  await editing.getByLabel("Base antes de IVA").fill("150000");
  await editing.getByLabel("Base antes de IVA").blur();
  await editing.getByRole("button", { name: "Guardar cambios" }).click();
  await expect.poll(() => state.preview.length).toBe(2);
  expect((state.preview.at(-1)!.lines as Array<{ taxExclusiveAmount: number }>)[0].taxExclusiveAmount).toBe(150000);
  await expect(dialog.getByRole("region", { name: "Cálculo del gasto" })).toContainText("150.000");

  await dialog.getByRole("button", { name: "Agregar gasto", exact: true }).click();
  const adding = page.getByRole("dialog", { name: "Agregar gasto", exact: true });
  await adding.getByLabel("Gasto frecuente (opcional)").click();
  await page.getByRole("option", { name: "Servicio frecuente" }).click();
  await adding.getByLabel("Base antes de IVA").fill("50000");
  await adding.getByLabel("Base antes de IVA").blur();
  await adding.getByRole("button", { name: "Agregar a la grilla" }).click();
  await expect.poll(() => state.preview.length).toBe(3);

  await dialog.getByRole("button", { name: "Quitar gasto 1" }).click();
  await expect.poll(() => state.preview.length).toBe(4);
  expect((state.preview.at(-1)!.lines as Array<{ taxExclusiveAmount: number }>)[0].taxExclusiveAmount).toBe(50000);
  await expect(dialog.getByRole("region", { name: "Cálculo del gasto" })).toContainText("50.000");
  await dialog.getByRole("button", { name: "Quitar gasto 1" }).click();
  await expect(dialog.getByRole("region", { name: "Cálculo del gasto" })).toHaveCount(0);
  await expect(dialog.getByRole("button", { name: "Confirmar gasto", exact: true })).toBeDisabled();
  expect(state.preview).toHaveLength(4);
});

test("gasto frecuente, cuenta directa, fechas Auraly y cálculo obligatorio antes de confirmar", async ({ page, baseURL }, info) => {
  const { state, dialog } = await openExpenses(page, baseURL!);
  await expect.poll(() => state.preview.length).toBe(1);
  await expect(dialog.getByRole("button", { name: "Confirmar gasto", exact: true })).toBeEnabled();
  await expect(dialog.locator('input[type="date"]')).toHaveCount(0);
  await dialog.getByLabel("Fecha de emisión").click();
  await expect(page.getByRole("button", { name: "Mes anterior" })).toBeVisible();
  await page.getByRole("button", { name: "Elegir hoy", exact: true }).click();
  await dialog.getByLabel("Fecha de emisión").click();
  await page.getByRole("button", { name: "Mes anterior" }).focus();
  await page.keyboard.press("Escape");
  await expect(dialog.getByLabel("Número de factura del proveedor")).toHaveValue("FV-100");
  await dialog.getByRole("button", { name: "Agregar gasto", exact: true }).click();
  const second = page.getByRole("dialog", { name: "Agregar gasto", exact: true });
  await second.getByRole("combobox", { name: "Seleccionar cuenta contable" }).click();
  await page.getByPlaceholder("Buscar por código o nombre…").fill("serv");
  await page.getByRole("option", { name: /519596/ }).click();
  await second.getByLabel("Descripción de la línea").fill("Cuenta directa sin plantilla");
  await second.getByLabel("Base antes de IVA").fill("80000"); await second.getByLabel("Base antes de IVA").blur();
  await page.screenshot({ path: info.outputPath("gasto-linea.png"), fullPage: true });
  await second.getByRole("button", { name: "Agregar a la grilla" }).click();
  await expect(second).toHaveCount(0);
  await expect(dialog.getByText("Retefuente servicios", { exact: true })).toBeVisible();
  await expect(dialog.getByRole("button", { name: "Confirmar gasto", exact: true })).toBeEnabled();
  const calculated = state.preview.at(-1)!;
  expect(calculated.conceptId).toBeNull();
  expect((calculated.lines as { conceptId: string|null }[]).map(line => line.conceptId)).toEqual([conceptId, null]);
  expect(calculated.withholdingJurisdictionCode).toBeNull();
  await page.setViewportSize({ width: 1440, height: 1300 });
  await dialog.evaluate(element => { element.scrollTop = 0; });
  await page.screenshot({ path: info.outputPath("gasto-calculo.png"), fullPage: true });
  await page.setViewportSize({ width: 1440, height: 1000 });
  const accountReads = state.accountReads;
  await dialog.getByRole("button", { name: "Editar gasto 2", exact: true }).click();
  const editing = page.getByRole("dialog", { name: "Editar gasto", exact: true });
  await expect(editing.getByLabel("Base antes de IVA")).toHaveValue("80 000");
  await editing.getByLabel("Base antes de IVA").fill("90000"); await editing.getByLabel("Base antes de IVA").blur();
  await editing.getByRole("button", { name: "Cancelar", exact: true }).click();
  await expect(dialog.getByRole("button", { name: "Confirmar gasto", exact: true })).toBeEnabled();
  await dialog.getByRole("button", { name: "Editar gasto 2", exact: true }).click();
  await expect(editing.getByLabel("Base antes de IVA")).toHaveValue("80 000");
  await editing.getByLabel("Base antes de IVA").fill("90000"); await editing.getByLabel("Base antes de IVA").blur();
  const previewsBeforeSave = state.preview.length;
  await editing.getByRole("button", { name: "Guardar cambios" }).click();
  expect(state.accountReads).toBe(accountReads);
  await expect.poll(() => state.preview.length).toBe(previewsBeforeSave + 1);
  await expect(dialog.getByRole("button", { name: "Confirmar gasto", exact: true })).toBeEnabled();
  const reads = state.list;
  await dialog.getByRole("button", { name: "Confirmar gasto", exact: true }).click();
  await expect(dialog).toHaveCount(0);
  expect(state.confirm).toHaveLength(1); expect(state.confirm[0].calculationHash).toBe(`review-${state.preview.length}`);
  await expect.poll(() => state.list).toBe(reads + 1);
});

test("perfil incompleto y cálculo desactualizado requieren un cálculo vigente", async ({ page, baseURL }) => {
  const { state, dialog } = await openExpenses(page, baseURL!);
  await expect.poll(() => state.preview.length).toBe(1);
  state.canConfirm = false;
  await dialog.getByRole("button", { name: "Editar gasto 1" }).click();
  await page.getByRole("dialog", { name: "Editar gasto" }).getByLabel("Descripción de la línea").fill("Cambio tributario");
  await page.getByRole("dialog", { name: "Editar gasto" }).getByRole("button", { name: "Guardar cambios" }).click();
  await expect(dialog.getByText("Falta el perfil tributario del proveedor.")).toBeVisible();
  await expect(dialog.getByRole("button", { name: "Confirmar gasto", exact: true })).toBeDisabled();
  state.canConfirm = true; state.conflict = true;
  await dialog.getByRole("button", { name: "Editar gasto 1" }).click();
  await page.getByRole("dialog", { name: "Editar gasto" }).getByLabel("Descripción de la línea").fill("Cambio tributario corregido");
  await page.getByRole("dialog", { name: "Editar gasto" }).getByRole("button", { name: "Guardar cambios" }).click();
  await expect(dialog.getByRole("button", { name: "Confirmar gasto", exact: true })).toBeEnabled();
  await dialog.getByRole("button", { name: "Confirmar gasto", exact: true }).click();
  await expect(dialog.getByRole("alert")).toContainText("El cálculo cambió");
  expect(state.confirm).toHaveLength(1);
  await expect(dialog.getByLabel("Número de factura del proveedor")).toHaveValue("FV-100");
});

test("cancelar una línea nueva no la agrega y quitar la última impide confirmar", async ({ page, baseURL }) => {
  const { dialog, state } = await openExpenses(page, baseURL!);
  await dialog.getByRole("button", { name: "Agregar gasto", exact: true }).click();
  const adding = page.getByRole("dialog", { name: "Agregar gasto", exact: true });
  await adding.getByLabel("Descripción de la línea").fill("Sin guardar");
  await adding.getByRole("button", { name: "Cancelar", exact: true }).click();
  await expect(dialog.getByRole("button", { name: "Editar gasto 2", exact: true })).toHaveCount(0);
  await expect(dialog.getByRole("button", { name: "Revisar cálculo" })).toHaveCount(0);
  await expect(dialog.getByRole("button", { name: "Confirmar gasto", exact: true })).toBeEnabled();
  await dialog.getByRole("button", { name: "Quitar gasto 1", exact: true }).click();
  await expect(dialog.getByText("Agrega el primer gasto para continuar.")).toBeVisible();
  await expect(dialog.getByRole("button", { name: "Revisar cálculo" })).toHaveCount(0);
  await expect(dialog.getByRole("button", { name: "Confirmar gasto", exact: true })).toBeDisabled();
  expect(state.confirm).toHaveLength(0);
});

test("valida moneda y fechas, bloquea el envío y conserva la captura ante un error", async ({ page, baseURL }) => {
  const { dialog } = await openExpenses(page, baseURL!);
  await dialog.getByRole("button", { name: "Editar gasto 1", exact: true }).click();
  const editing = page.getByRole("dialog", { name: "Editar gasto", exact: true });
  const amount = editing.getByLabel("Base antes de IVA");
  await expect(amount).toHaveValue("120 000");
  await amount.fill("0"); await amount.blur();
  await expect(editing.getByRole("button", { name: "Guardar cambios" })).toBeDisabled();
  await amount.fill("120000"); await amount.blur();
  await editing.getByRole("button", { name: "Guardar cambios" }).click();
  await dialog.getByLabel("Número de factura del proveedor").fill("");
  await expect(dialog.getByRole("button", { name: "Revisar cálculo" })).toHaveCount(0);
  await dialog.getByLabel("Número de factura del proveedor").fill("FV-100");
  await dialog.getByLabel("Fecha de emisión").click();
  await page.getByRole("button", { name: "Mes siguiente" }).click();
  const next = new Date(); next.setDate(1); next.setMonth(next.getMonth() + 1);
  const nextLabel = `1 de ${new Intl.DateTimeFormat("es-CO", { month: "long" }).format(next)} de ${next.getFullYear()}`;
  await page.getByRole("button", { name: nextLabel, exact: true }).click();
  await dialog.getByLabel("Fecha de vencimiento").click();
  await expect(page.getByRole("button", { name: "Elegir hoy", exact: true })).toBeDisabled();
  await page.getByRole("button", { name: "Mes anterior" }).focus(); await page.keyboard.press("Escape");
  await dialog.getByLabel("Fecha de emisión").click();
  await page.getByRole("button", { name: "Elegir hoy", exact: true }).click();
  await expect(dialog.getByRole("button", { name: "Confirmar gasto", exact: true })).toBeEnabled();
  let release!: () => void;
  const pending = new Promise<void>(resolve => { release = resolve; });
  let sends = 0;
  await page.route("**/expenses/confirm", async route => {
    sends++; await pending;
    await route.fulfill({ status: 503, contentType: "application/json", body: JSON.stringify({ detail: "No fue posible guardar el gasto." }) });
  });
  try {
    await dialog.getByRole("button", { name: "Confirmar gasto", exact: true }).click();
    await expect(dialog.getByRole("button", { name: "Confirmando gasto…" })).toBeDisabled();
    await expect(dialog.getByRole("button", { name: "Editar gasto 1", exact: true })).toBeDisabled();
    await page.keyboard.press("Escape");
    await expect(dialog).toBeVisible();
  } finally { release(); }
  await expect(dialog.getByRole("alert")).toContainText("No fue posible guardar");
  await expect(dialog.getByLabel("Número de factura del proveedor")).toHaveValue("FV-100");
  await dialog.getByRole("button", { name: "Editar gasto 1", exact: true }).click();
  await expect(amount).toHaveValue("120 000");
  expect(sends).toBe(1);
});

test("una anulación refresca una vez el detalle y la página filtrada con el estado real", async ({ page, baseURL }) => {
  const { dialog } = await openExpenses(page, baseURL!);
  await dialog.getByRole("button", { name: "Close", exact: true }).click();
  let cancelled = false, reads = 0, details = 0;
  const expenseId = "88888888-8888-8888-8888-888888888888";
  const item = { expenseId, documentNumber: "G-100", supplierDocumentNumber: "FV-100", supplierId,
    supplierName: "Proveedor de prueba", conceptId, conceptName: "Servicio frecuente", issuedAt: "2026-09-30T12:00:00-05:00",
    dueDate: "2026-10-30T12:00:00-05:00", grossAmount: 120000, withholdingAmount: 3000, netPayable: 117000,
    outstandingAmount: 117000, payableStatus: "Open", status: "Processed", currencyCode: "COP", purchaseEvidenceType: "SupplierElectronicInvoice", chargeReturned: false };
  await page.route("**/api/commerce/v1/**", async route => {
    const url = new URL(route.request().url()), path = url.pathname;
    let body: unknown;
    if (path.endsWith("/reference-options/expense-status")) body = [{ id: "processed", code: "Processed", label: "Procesado" }];
    else if (path.endsWith("/reference-options/expense-cancellation-reason")) body = [{ id: conceptId, code: "ERROR", label: "Error en el registro" }];
    else if (path.endsWith(`/expenses/${expenseId}/cancel`)) {
      cancelled = true; body = { expenseId, cancellationId: conceptId, accountingJobId: accountId, hasFiscalAdjustment: false, idempotentReplay: false };
    } else if (path.endsWith(`/expenses/${expenseId}`)) {
      details++; body = { ...item, status: cancelled ? "Cancelled" : "Processed", description: "Gasto de prueba", taxExclusiveAmount: 120000, vatAmount: 0,
        cancellationId: cancelled ? conceptId : null, cancellationReason: cancelled ? "Error en el registro" : null,
        payable: { payableId: supplierId, status: cancelled ? "Cancelled" : "Open", originalAmount: 117000, outstandingAmount: cancelled ? 0 : 117000 } };
    } else if (path.endsWith("/expenses")) {
      reads++; const items = cancelled && url.searchParams.get("status") === "Processed" ? [] : [item];
      body = { items, page: 1, pageSize: 25, totalCount: items.length, totalPages: items.length, grossTotal: items.length * 120000, withholdingTotal: items.length * 3000, netPayableTotal: items.length * 117000 };
    } else { await route.fallback(); return; }
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
  });
  await page.getByRole("button", { name: "Filtros", exact: true }).click();
  await page.getByRole("combobox").filter({ has: page.getByText("Todos los estados", { exact: true }) }).first().click();
  await page.getByRole("option", { name: "Procesado", exact: true }).click();
  await page.getByRole("button", { name: "Ver gasto G-100" }).click();
  await page.getByRole("button", { name: "Anular gasto", exact: true }).click();
  const cancellation = page.getByRole("dialog", { name: "Anular gasto", exact: true });
  await cancellation.getByRole("combobox").click();
  await page.getByRole("option", { name: "Error en el registro" }).click();
  const beforeReads = reads, beforeDetails = details;
  await cancellation.getByRole("button", { name: "Confirmar anulación" }).click();
  await expect(cancellation).toHaveCount(0);
  const detail = page.getByRole("dialog", { name: "G-100", exact: true });
  await expect(detail.getByText("Cancelado", { exact: true })).toBeVisible();
  await expect.poll(() => reads).toBe(beforeReads + 1);
  expect(details).toBe(beforeDetails + 1);
  await detail.getByRole("button", { name: "Cerrar", exact: true }).click();
  await expect(page.getByRole("button", { name: "Ver gasto G-100" })).toHaveCount(0);
});
