import { expect, test } from "@playwright/test";
import { openExpenses, businessId, accountId, supplierId, conceptId } from "./support/expense-workspace";

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

test("captura de gasto se puede leer y editar en móvil", async ({ page, baseURL }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const { dialog } = await openExpenses(page, baseURL!, false);
  expect(await dialog.evaluate(element => element.getBoundingClientRect().right <= window.innerWidth)).toBe(true);
  await expect(dialog.locator("span").filter({ hasText: "Concepto / descripción" })).toBeVisible();
  await expect(dialog.getByRole("button", { name: "Editar gasto 1" })).toBeVisible();
  await dialog.getByRole("button", { name: "Editar gasto 1" }).click();
  const editing = page.getByRole("dialog", { name: "Editar gasto" });
  await expect(editing.getByLabel("Base antes de IVA")).toBeVisible();
  expect(await editing.evaluate(element => element.getBoundingClientRect().right <= window.innerWidth)).toBe(true);
});

test("historial de gastos presenta cada documento sin desplazamiento horizontal en móvil", async ({ page, baseURL }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const { dialog } = await openExpenses(page, baseURL!, false);
  await dialog.getByRole("button", { name: "Close", exact: true }).click();
  await page.route("**/api/commerce/v1/expenses*", async route => {
    if (!new URL(route.request().url()).pathname.endsWith("/expenses")) return route.fallback();
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({
      items: [{ expenseId: "88888888-8888-8888-8888-888888888888", documentNumber: "G-100",
        supplierDocumentNumber: "FV-100", supplierName: "Proveedor de prueba", conceptName: "Servicios",
        grossAmount: 120000, withholdingAmount: 3000, netPayable: 117000,
        outstandingAmount: 117000, payableStatus: "Open", status: "Processed", chargeReturned: false }],
      page: 1, pageSize: 25, totalCount: 1, totalPages: 1,
      grossTotal: 120000, withholdingTotal: 3000, netPayableTotal: 117000,
    }) });
  });
  await page.getByPlaceholder("Documento, proveedor o concepto").fill("G-100");
  const row = page.getByRole("button", { name: "Ver gasto G-100" });
  await expect(row).toBeVisible();
  await expect(row.getByText("Saldo por pagar")).toBeVisible();
  expect(await row.evaluate(element => element.getBoundingClientRect().right <= window.innerWidth)).toBe(true);
});

test("agregar retención manual funciona sin reglas configuradas", async ({ page, baseURL }) => {
  const { dialog, state } = await openExpenses(page, baseURL!, false, {manageWithholdings:true,rules:[]});
  const add = dialog.getByRole("button", {name:"Agregar retención"});
  await expect(add).toBeVisible();
  await expect(add).toBeEnabled();
  await add.click();
  const manual = page.getByRole("dialog", {name:"Agregar retención manual"});
  await expect(manual).toBeVisible();
  await manual.getByRole("combobox", {name:"Tipo de retención"}).click();
  await page.getByRole("option", {name:"ReteICA"}).click();
  await manual.getByLabel("Concepto o nombre").fill("ReteICA puntual");
  await manual.getByLabel("Base de retención").fill("10000");
  await manual.getByLabel("Tarifa %").fill("2.5");
  await manual.getByLabel("Jurisdicción").fill("11001");
  await manual.getByRole("combobox", {name:"Seleccionar cuenta contable"}).click();
  await page.getByRole("option", {name:/236570/}).click();
  await manual.getByLabel("Motivo").fill("Revisión contable");
  await manual.getByRole("button", {name:"Aplicar retención"}).click();
  await expect.poll(() => state.preview.length).toBe(2);
  expect((state.preview.at(-1)!.withholdingAdjustments as Array<{action:string;accountId:string}>)[0]).toMatchObject({action:"Manual",accountId:"88888888-8888-8888-8888-888888888888"});
});

test("la regla automática no se duplica y permite ajustar su retención", async ({ page, baseURL }) => {
  const rule = {ruleId:"rule",businessId,version:1,code:"RF",name:"Retefuente servicios",kind:"IncomeTax",
    direction:"Purchase",moment:"Accrual",baseKind:"TaxExclusiveAmount",conceptCode:null,jurisdictionCode:null,
    rate:2.5,minimumBase:0,requiredResponsibilities:[],effectiveFrom:"2026-01-01",effectiveTo:null,isActive:true,appliesAutomatically:true,defaultAccountId:null};
  const { dialog } = await openExpenses(page, baseURL!, false, {manageWithholdings:true,rules:[rule]});
  await expect(dialog.getByRole("button", {name:"Agregar retención"})).toBeEnabled();
  await dialog.getByRole("button", {name:"Editar Retefuente servicios"}).click();
  await expect(page.getByRole("dialog", {name:"Ajustar retención"})).toBeVisible();
});

test("sin reglas de compra permite abrir retención puntual", async ({ page, baseURL }) => {
  const { dialog } = await openExpenses(page, baseURL!, false, {manageWithholdings:true,rules:[]});
  await expect(dialog.getByRole("button", {name:"Agregar retención"})).toBeVisible();
  await expect(dialog.getByRole("button", {name:"Agregar retención"})).toBeEnabled();
});

test("guardar como regla es opcional y no activa el cálculo automático", async ({ page, baseURL }) => {
  const { dialog, state } = await openExpenses(page, baseURL!, false, {manageWithholdings:true,rules:[]});
  await dialog.getByRole("button", {name:"Agregar retención"}).click();
  const manual = page.getByRole("dialog", {name:"Agregar retención manual"});
  await manual.getByRole("combobox", {name:"Tipo de retención"}).click();
  await page.getByRole("option", {name:"ReteICA"}).click();
  await manual.getByLabel("Concepto o nombre").fill("ReteICA local");
  await manual.getByLabel("Base de retención").fill("10000");
  await manual.getByLabel("Tarifa %").fill("1");
  await manual.getByLabel("Jurisdicción").fill("11001");
  await manual.getByRole("combobox", {name:"Seleccionar cuenta contable"}).click();
  await page.getByRole("option", {name:/236570/}).click();
  await manual.getByLabel("Motivo").fill("Aplicación contable puntual");
  const checkbox = manual.getByRole("checkbox", {name:"Guardar como regla para usarla después"});
  await expect(checkbox).not.toBeChecked();
  await checkbox.click();
  await manual.getByLabel("Código de la regla").fill("ICA-LOCAL");
  await manual.getByRole("button", {name:"Aplicar retención"}).click();
  await expect.poll(() => state.savedRules.length).toBe(1);
  expect(state.savedRules[0]).toMatchObject({code:"ICA-LOCAL",appliesAutomatically:false,
    defaultAccountId:"88888888-8888-8888-8888-888888888888"});
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
  await adding.getByLabel("Concepto de gasto").click();
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

test("conceptos con cuenta automática, fechas Auraly y cálculo obligatorio antes de confirmar", async ({ page, baseURL }, info) => {
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
  await expect(second.getByRole("combobox", { name: "Seleccionar cuenta contable" })).toHaveCount(0);
  await second.getByLabel("Concepto de gasto").click();
  await page.getByRole("option", { name: "Servicio frecuente" }).click();
  await second.getByLabel("Descripción de la línea").fill("Segundo servicio por concepto");
  await second.getByLabel("Base antes de IVA").fill("80000"); await second.getByLabel("Base antes de IVA").blur();
  await page.screenshot({ path: info.outputPath("gasto-linea.png"), fullPage: true });
  await second.getByRole("button", { name: "Agregar a la grilla" }).click();
  await expect(second).toHaveCount(0);
  await expect(dialog.getByText("Retefuente servicios", { exact: true })).toBeVisible();
  await expect(dialog.getByRole("button", { name: "Confirmar gasto", exact: true })).toBeEnabled();
  const calculated = state.preview.at(-1)!;
  expect(calculated.conceptId).toBeNull();
  expect((calculated.lines as { conceptId: string|null }[]).map(line => line.conceptId)).toEqual([conceptId, conceptId]);
  expect((calculated.lines as { expenseAccountId: string|null }[]).map(line => line.expenseAccountId)).toEqual([null, null]);
  expect(state.accountReads).toBe(0);
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
