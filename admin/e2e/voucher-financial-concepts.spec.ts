import { expect, test } from "@playwright/test";
import type { SaveVoucherDraft } from "../src/services/api/accounting";
import type { FinancialConcept, SaveFinancialConcept } from "../src/services/api/financial-concepts";
import { financialVoucherScenario, installFinancialVoucherContext } from "./support/financial-voucher";

const conceptId = "aaaaaaaa-1111-1111-1111-111111111111";
function definition(supplier = false, advance = true): FinancialConcept {
  return { financialConceptId: conceptId, code: "CONCEPTO", name: advance ? "Anticipo elegido" : "Contrapartida elegida",
    accountId: "aaaaaaaa-2222-2222-2222-222222222222", accountCode: advance ? supplier ? "13300501" : "28050501" : supplier ? "51959501" : "42959501",
    accountName: advance ? "Anticipos" : "Cuenta general", financialFunction: advance ? supplier ? "SupplierAdvance" : "CustomerAdvance" : "General",
    sideOptionId: supplier ? "debit" : "credit", sideCode: supplier ? "Debit" : "Credit", sideLabel: supplier ? "Débito" : "Crédito",
    useForReceipts: !supplier, useForSupplierPayments: supplier, useForVouchers: true, isActive: true, sortOrder: 10, rowVersion: "AAAAAAAAAAE=" };
}

for (const supplier of [false, true]) for (const kind of ["advance", "general", "invoiceAndGeneral", "invoiceReduction"]) test(`${supplier ? "egreso" : "recibo"}: agrega concepto ${kind} sólo por su valor explícito y conserva el resultado`, async ({ page, baseURL }, testInfo) => {
  const advance = kind === "advance", reduction = kind === "invoiceReduction", withInvoice = kind === "invoiceAndGeneral" || reduction;
  const money = reduction ? 216696.06 : withInvoice ? 216800 : 100, conceptAmount = withInvoice ? 51.97 : 100;
  const scenario = financialVoucherScenario(), concept = definition(supplier, advance);
  if (reduction) Object.assign(concept, { name: "Reducción elegida", sideCode: supplier ? "Credit" : "Debit", sideLabel: supplier ? "Crédito" : "Débito" });
  scenario.draft.voucherTypeCode = supplier ? "Disbursement" : "CashReceipt";
  const treasury = scenario.draft.lines[1];
  treasury.value = { ...treasury.value, debit: supplier ? 0 : money, credit: supplier ? money : 0,
    paymentEvidence: { tenderedAmount: money } };
  if (withInvoice) {
    scenario.user.permissions.push("receivables.payments.create");
    const invoice = scenario.draft.lines[0];
    invoice.value = { ...invoice.value, debit: supplier ? 216748.03 : 0, credit: supplier ? 0 : 216748.03,
      existingPosition: { subledgerKind: supplier ? "Payable" : "Receivable", subledgerId: scenario.positionId } };
    invoice.accountCode = supplier ? "22050501" : "13050501";
    invoice.accountName = supplier ? "Proveedor" : "Cliente";
  } else scenario.draft.lines = [treasury];
  let current = scenario.draft, reads = 0, conceptReads = 0, writes = 0, sends = 0;
  await installFinancialVoucherContext(page, baseURL!, scenario);
  await page.route("**/api/**", async route => {
    const request = route.request(), url = new URL(request.url()), path = url.pathname;
    if (path.endsWith("/financial-concepts")) {
      conceptReads++; expect(url.searchParams.get("usage")).toBe(supplier ? "SupplierPayments" : "Receipts");
      await route.fulfill({ json: { items: [concept], page: 1, pageSize: 10, totalCount: 1 } }); return;
    }
    if (path.endsWith(`/accounting/manual/drafts/${scenario.documentId}`)) {
      if (request.method() === "GET") reads++;
      else {
        writes++; const capture = request.postDataJSON() as SaveVoucherDraft;
        const selected = capture.lines.find(line => line.financialConceptId === conceptId)!;
        expect(capture.lines).toHaveLength(withInvoice ? 3 : 2); expect(selected.accountId).toBe(concept.accountId);
        expect(selected.debit).toBe(supplier !== reduction ? conceptAmount : 0); expect(selected.credit).toBe(supplier !== reduction ? 0 : conceptAmount);
        expect(selected.financialConceptTargetLineId ?? null).toBe(reduction ? scenario.draft.lines[0].value.lineId : null);
        if (withInvoice) {
          expect(capture.lines[0].existingPosition?.subledgerId).toBe(scenario.positionId);
          expect(capture.lines[0].debit + capture.lines[0].credit).toBe(216748.03);
          expect(capture.lines[1].debit + capture.lines[1].credit).toBe(money);
        }
        expect(capture.lines.every(line => line.financialGroupId === scenario.documentId)).toBe(true);
        current = { ...current, ...capture, rowVersion: "AAAAAAAAAAI=", lines: capture.lines.map(value => ({ value,
          accountCode: value.financialConceptId ? concept.accountCode : scenario.draft.lines.find(line => line.value.lineId === value.lineId)?.accountCode ?? null,
          accountName: value.financialConceptId ? concept.accountName : scenario.draft.lines.find(line => line.value.lineId === value.lineId)?.accountName ?? null,
          partyName: "Tercero", partyIdentification: null, costCenterName: null,
          financialConceptCode: value.financialConceptId ? concept.code : null, financialConceptName: value.financialConceptId ? concept.name : null,
          financialConceptSideCode: value.financialConceptId ? concept.sideCode : null })) };
      }
      await route.fulfill({ json: current }); return;
    }
    if (path.endsWith(`/accounting/manual/drafts/${scenario.documentId}/send`)) {
      sends++; current = { ...current, sentAt: "2026-11-10T12:00:00Z", status: "Posted", canEdit: false, canSend: false,
        row: { ...current.row, status: "Posted" }, financialEffects: { positions: advance ? [{}] : [], payments: advance || withInvoice ? [{}] : [],
          directMovements: advance || withInvoice ? [] : [{}], creditApplications: [] } };
      await route.fulfill({ json: current }); return;
    }
    await route.fallback();
  });
  await page.goto(`${supplier ? "/dashboard/financial-traceability" : "/dashboard/cash-receipts"}?document=${scenario.documentId}`);
  await expect(page.getByRole("button", { name: "Agregar concepto", exact: true })).toBeDisabled();
  expect(conceptReads).toBe(0);
  await page.getByRole("combobox", { name: "Concepto financiero", exact: true }).click();
  await page.getByRole("option", { name: new RegExp(concept.name) }).click();
  await expect(page.getByText(`Cuenta: ${concept.accountCode} · ${concept.accountName} · ${concept.sideLabel}`, { exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Agregar concepto", exact: true })).toBeDisabled();
  await page.getByRole("textbox", { name: "Valor del concepto financiero", exact: true }).fill(String(conceptAmount));
  if (reduction) {
    await page.getByRole("button", { name: "Agregar concepto", exact: true }).click();
    await expect(page.getByText("Selecciona la partida del documento que reduce este concepto.", { exact: true })).toBeVisible();
    await page.getByRole("combobox", { name: "Documento que ajusta el concepto", exact: true }).click();
    await expect(page.getByRole("option", { name: "Sin documento vinculado", exact: true })).toBeFocused();
    await page.keyboard.press("End");
    await expect(page.getByRole("option").filter({ hasText: "FACT-1" })).toBeFocused();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("combobox", { name: "Documento que ajusta el concepto", exact: true })).toContainText("FACT-1");
  }
  await page.getByRole("button", { name: "Agregar concepto", exact: true }).click();
  const lineNumber = withInvoice ? 3 : 2;
  const row = page.getByRole("row", { name: `Partida ${lineNumber}`, exact: true });
  await expect(row.getByRole("combobox", { name: "Seleccionar cuenta contable", exact: true })).toBeDisabled();
  await expect(row.getByRole("button", { name: `Intercambiar débito y crédito de la partida ${lineNumber}`, exact: true })).toBeDisabled();
  await expect(row.getByRole("textbox", { name: supplier !== reduction ? "Crédito de la partida" : "Débito de la partida", exact: true })).toBeDisabled();
  if (reduction) {
    await expect(page.getByRole("button", { name: "Eliminar partida 1", exact: true })).toBeDisabled();
    await page.getByRole("button", { name: "Detalles de la partida 3", exact: true }).click();
    await expect(page.getByRole("combobox", { name: "Documento ajustado", exact: true })).toContainText("FACT-1");
    await expect(page.getByRole("combobox", { name: "Grupo de la partida", exact: true })).toBeDisabled();
    await page.getByRole("button", { name: "Cerrar detalles", exact: true }).click();
  }
  await page.getByRole("button", { name: "Guardar borrador", exact: true }).click();
  await expect.poll(() => writes).toBe(1);
  await page.getByRole("button", { name: "Guardar comprobante", exact: true }).click();
  await expect.poll(() => sends).toBe(1);
  await expect(page.getByRole("region", { name: "Agregar concepto financiero", exact: true })).toHaveCount(0);
  await expect(page.getByText(`${concept.code} · ${concept.name}`, { exact: true })).toBeVisible();
  if (withInvoice) {
    const amount = page.getByRole("row", { name: "Partida 1", exact: true })
      .getByRole("textbox", { name: supplier ? "Débito de la partida" : "Crédito de la partida", exact: true });
    await expect(amount).toHaveValue("216 748.03");
    await amount.scrollIntoViewIfNeeded();
    const measured = await amount.evaluate(element => {
      const input = element as HTMLInputElement, style = getComputedStyle(input);
      const context = document.createElement("canvas").getContext("2d")!;
      context.font = `${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
      return { text: context.measureText(input.value).width,
        available: input.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight) };
    });
    expect(measured.available, "El importe completo debe poder leerse sin recortar sus decimales").toBeGreaterThanOrEqual(measured.text);
  }
  expect(reads).toBe(1); expect(writes).toBe(1); expect(conceptReads).toBe(1);
  await page.screenshot({ path: testInfo.outputPath("financial-concept-in-voucher.png"), fullPage: true });
});

test("500 partidas con conceptos extensos conservan filas uniformes y permiten consultar la última", async ({ page, baseURL }) => {
  const scenario = financialVoucherScenario(), concept = { ...definition(false, false), name: "Concepto financiero extenso ".repeat(6).slice(0, 160) };
  const cash = scenario.draft.lines[1];
  cash.value = { ...cash.value, debit: 499, credit: 0, financialGroupId: scenario.documentId, paymentEvidence: { tenderedAmount: 499 } };
  scenario.draft = { ...scenario.draft, voucherTypeCode: "CashReceipt", row: { ...scenario.draft.row, debitTotal: 499, creditTotal: 499 },
    lines: [cash, ...Array.from({ length: 499 }, (_, index) => ({
      value: { accountId: concept.accountId, partyId: scenario.draft.mainPartyId ?? null, costCenterId: null,
        description: "Contrapartida explícita", debit: 0, credit: 1,
        lineId: `aaaaaaaa-1111-4111-8111-${String(index + 1).padStart(12, "0")}`,
        financialGroupId: scenario.documentId, financialConceptId: conceptId },
      accountCode: concept.accountCode, accountName: concept.accountName, partyName: "Tercero", partyIdentification: null,
      costCenterName: null, financialConceptCode: concept.code, financialConceptName: concept.name, financialConceptSideCode: concept.sideCode,
    }))] };
  let reads = 0;
  await installFinancialVoucherContext(page, baseURL!, scenario);
  await page.route(`**/accounting/manual/drafts/${scenario.documentId}`, async route => {
    expect(route.request().method()).toBe("GET"); reads++; await route.fulfill({ json: scenario.draft });
  });
  await page.goto(`/dashboard/cash-receipts?document=${scenario.documentId}`);
  const first = page.getByRole("row", { name: "Partida 1", exact: true });
  const second = page.getByRole("row", { name: "Partida 2", exact: true });
  await expect(second).toBeVisible();
  expect((await second.boundingBox())!.height, "Las filas virtualizadas deben conservar su altura aunque el concepto sea largo")
    .toBe((await first.boundingBox())!.height);
  const table = page.getByRole("table").filter({ has: page.getByRole("columnheader", { name: "Cuenta PUC", exact: true }) });
  await table.evaluate(element => { element.parentElement!.scrollTop = element.parentElement!.scrollHeight; });
  const last = page.getByRole("row", { name: "Partida 500", exact: true });
  await expect(last).toBeVisible();
  await last.scrollIntoViewIfNeeded();
  await expect(last).toBeInViewport();
  await expect(last.getByTitle(`${concept.code} · ${concept.name}`, { exact: true })).toHaveCount(1);
  expect(await page.getByRole("row", { name: /^Partida \d+$/ }).count()).toBeLessThanOrEqual(24);
  expect(reads).toBe(1);
});

test("agregar un concepto conserva los datos de una partida incompleta", async ({ page, baseURL }) => {
  const scenario = financialVoucherScenario(), concept = definition(false, false);
  scenario.draft.voucherTypeCode = "CashReceipt";
  const partial = scenario.draft.lines[0];
  partial.value = { ...partial.value, accountId: null, description: "", debit: 0, credit: 0,
    existingPosition: null, paymentEvidence: null, financialGroupId: null, reference: "Referencia por completar" };
  partial.accountCode = null; partial.accountName = null; scenario.draft.lines = [partial];
  let writes = 0;
  await installFinancialVoucherContext(page, baseURL!, scenario);
  await page.route("**/api/**", async route => {
    const request = route.request(), path = new URL(request.url()).pathname;
    if (path.endsWith("/financial-concepts")) {
      await route.fulfill({ json: { items: [concept], page: 1, pageSize: 10, totalCount: 1 } }); return;
    }
    if (path.endsWith(`/accounting/manual/drafts/${scenario.documentId}`) && request.method() === "GET") {
      await route.fulfill({ json: scenario.draft }); return;
    }
    if (path.endsWith(`/accounting/manual/drafts/${scenario.documentId}`) && request.method() === "PUT") {
      writes++; const capture = request.postDataJSON() as SaveVoucherDraft;
      expect(capture.lines).toHaveLength(2);
      expect(capture.lines[0]).toMatchObject({ lineId: partial.value.lineId, accountId: null,
        partyId: partial.value.partyId, reference: "Referencia por completar", description: "Partida pendiente" });
      await route.fulfill({ json: { ...scenario.draft, ...capture, lines: capture.lines.map(value => ({ ...partial, value })) } }); return;
    }
    await route.fallback();
  });
  await page.goto(`/dashboard/cash-receipts?document=${scenario.documentId}`);
  await page.getByRole("combobox", { name: "Concepto financiero", exact: true }).click();
  await page.getByRole("option", { name: new RegExp(concept.name) }).click();
  await page.getByRole("textbox", { name: "Valor del concepto financiero", exact: true }).fill("100");
  await page.getByRole("button", { name: "Agregar concepto", exact: true }).click();
  await expect(page.getByRole("row", { name: "Partida 2", exact: true })).toBeVisible();
  await page.getByRole("row", { name: "Partida 1", exact: true }).getByLabel("Descripción de la partida", { exact: true }).fill("Partida pendiente");
  await page.getByRole("button", { name: "Guardar borrador", exact: true }).click();
  await expect.poll(() => writes).toBe(1);
});

test("la bandeja filtra por el concepto de las partidas, incluso inactivo, mediante su identidad", async ({ page, baseURL }) => {
  const scenario = financialVoucherScenario(), concept = { ...definition(), isActive: false };
  const queries: URL[] = []; let options = 0;
  await installFinancialVoucherContext(page, baseURL!, scenario);
  await page.route("**/api/**", async route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith("/financial-concepts")) {
      options++; expect(url.searchParams.get("usage")).toBe("Filter"); expect(url.searchParams.has("isActive")).toBe(false);
      await route.fulfill({ json: { items: [concept], page: 1, pageSize: 10, totalCount: 1 } }); return;
    }
    if (url.pathname.endsWith("/accounting/documents")) {
      queries.push(url); const items = url.searchParams.get("financialConceptId") === conceptId ? [{ ...scenario.draft.row, sourceDocumentNumber: "RC-CONCEPTO", voucherTypeCode: "CashReceipt" }] : [];
      await route.fulfill({ json: { items, page: 1, pageSize: 25, totalCount: items.length, totalPages: items.length } }); return;
    }
    await route.fallback();
  });
  await page.goto("/dashboard/cash-receipts");
  await expect(page.getByText("No hay documentos para estos filtros.", { exact: true })).toBeVisible(); expect(options).toBe(0);
  await page.getByRole("combobox", { name: "Concepto financiero", exact: true }).click();
  await page.getByRole("option", { name: /Anticipo elegido/ }).click();
  await expect(page.getByText("RC-CONCEPTO", { exact: true })).toBeVisible();
  expect(queries).toHaveLength(2); expect(queries[1].searchParams.get("financialConceptId")).toBe(conceptId);
  expect(queries[1].searchParams.get("voucherTypeCode")).toBe("CashReceipt");
  await page.getByRole("button", { name: "Quitar selección de Concepto financiero", exact: true }).click();
  await expect(page.getByText("No hay documentos para estos filtros.", { exact: true })).toBeVisible();
  // The unchanged unfiltered page is already authoritative in this journey.
  expect(queries).toHaveLength(2); expect(options).toBe(1);
});

test("el maestro guarda la cuenta y naturaleza y reutiliza la página devuelta sin otro GET", async ({ page, baseURL }, testInfo) => {
  const scenario = financialVoucherScenario(), concept = definition();
  scenario.user.permissions.push("catalog.update");
  let reads = 0, writes = 0;
  await installFinancialVoucherContext(page, baseURL!, scenario);
  await page.route("**/api/**", async route => {
    const request = route.request(), path = new URL(request.url()).pathname;
    if (path.endsWith("/reference-options/accounting-balance-side")) {
      await route.fulfill({ json: [{ id: "debit", code: "Debit", label: "Débito" }, { id: "credit", code: "Credit", label: "Crédito" }] }); return;
    }
    if (path.endsWith("/accounting/account-options")) {
      await route.fulfill({ json: { items: [{ accountId: concept.accountId, code: concept.accountCode, name: concept.accountName }], page: 1, pageSize: 10, totalCount: 1, totalPages: 1 } }); return;
    }
    if (path.endsWith("/financial-concepts")) {
      reads++; await route.fulfill({ json: { items: [], page: 1, pageSize: 50, totalCount: 0 } }); return;
    }
    if (path.includes("/financial-concepts/") && request.method() === "PUT") {
      writes++; const capture = request.postDataJSON() as SaveFinancialConcept;
      expect(capture).toMatchObject({ code: "ANT", name: "Anticipo elegido", accountId: concept.accountId, sideOptionId: "credit", useForReceipts: true });
      const saved = { ...concept, ...capture, rowVersion: "AAAAAAAAAAI=" };
      await route.fulfill({ json: { concept: saved, page: { items: [saved, { ...concept, financialConceptId: "concurrent", code: "OTRO", name: "Concepto concurrente" }], page: 1, pageSize: 50, totalCount: 2 } } }); return;
    }
    await route.fallback();
  });
  await page.goto("/dashboard/settings/masters");
  await page.getByRole("button", { name: /Conceptos financieros/ }).click();
  await expect(page.getByText("No hay conceptos financieros para este filtro.", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Nuevo concepto", exact: true }).click();
  await page.getByLabel("Código", { exact: true }).fill("ANT"); await page.getByLabel("Nombre", { exact: true }).fill("Anticipo elegido");
  await page.getByRole("combobox", { name: "Seleccionar cuenta contable", exact: true }).click();
  await page.getByPlaceholder("Buscar por código o nombre…", { exact: true }).fill(concept.accountCode);
  await page.getByRole("option", { name: new RegExp(concept.accountCode) }).click();
  await page.getByRole("combobox", { name: "Naturaleza", exact: true }).click(); await page.getByRole("option", { name: "Crédito", exact: true }).click();
  await page.getByRole("checkbox", { name: "Recibos de caja", exact: true }).check();
  await page.getByRole("button", { name: "Guardar concepto", exact: true }).click();
  await expect(page.getByText("Concepto concurrente", { exact: true })).toBeVisible();
  await expect(page.getByText("Página 1 · 2 conceptos", { exact: true })).toBeVisible();
  expect(reads).toBe(1); expect(writes).toBe(1);
  await page.screenshot({ path: testInfo.outputPath("financial-concept-master.png"), fullPage: true });
});
