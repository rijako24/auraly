import { expect, test } from "@playwright/test";
import { randomUUID } from "node:crypto";
import type { AccountingOpenPosition, SaveVoucherDraft } from "../src/services/api/accounting";
import { financialVoucherScenario, installFinancialVoucherContext } from "./support/financial-voucher";

test("reabrir y guardar una captura financiera conserva su versión, vínculos y evidencia sin otro GET", async ({ page, baseURL }) => {
  const scenario = financialVoucherScenario();
  const { documentId } = scenario;
  let draft = scenario.draft;
  const originalLines = structuredClone(draft.lines.map(line => line.value));
  let reads = 0, writes = 0, hiddenCatalogReads = 0;
  await installFinancialVoucherContext(page, baseURL!, scenario);
  await page.route("**/api/**", async route => {
    const request = route.request(), path = new URL(request.url()).pathname;
    if (path.endsWith("/reference-options/accounting-document-type")) hiddenCatalogReads++;
    if (!path.endsWith(`/accounting/manual/drafts/${documentId}`)) { await route.fallback(); return; }
    if (request.method() === "GET") reads++;
    else if (request.method() === "PUT") {
      writes++;
      const value = request.postDataJSON() as SaveVoucherDraft;
      expect(value.payloadVersion).toBe(4);
      expect(value.voucherTypeCode).toBe("Disbursement");
      expect(value.lines).toEqual(originalLines);
      draft = { ...draft, description: value.description, rowVersion: "AAAAAAAAAAI=" };
    }
    await route.fulfill({ status: 200, json: draft });
  });
  await page.goto(`/dashboard/financial-traceability?document=${documentId}`);
  await expect(page.getByRole("button", { name: "Contabilizar", exact: true })).toBeEnabled();
  await expect(page.getByLabel("Descripción", { exact: true })).toHaveValue(draft.description);
  await page.getByLabel("Descripción", { exact: true }).fill("Abono con descripción corregida");
  await page.getByRole("button", { name: "Guardar comprobante", exact: true }).click();
  await expect(page.getByRole("button", { name: "Contabilizar", exact: true })).toBeEnabled();
  await expect(page.getByLabel("Descripción", { exact: true })).toHaveValue("Abono con descripción corregida");
  expect(reads).toBe(1);
  expect(writes).toBe(1);
  expect(hiddenCatalogReads).toBe(0);
});

test("las cuentas no disponibles de una captura no disparan consultas por partida", async ({ page, baseURL }) => {
  const scenario = financialVoucherScenario();
  let draft = { ...scenario.draft, canSend: false, lines: Array.from({ length: 50 }, (_, index) => ({
    ...scenario.draft.lines[0], accountCode: null, accountName: null, validationError: "Cuenta no disponible en esta sede",
    value: { ...scenario.draft.lines[0].value, lineId: randomUUID(), accountId: randomUUID(),
      existingPosition: null, debit: index % 2 ? 0 : 1, credit: index % 2 ? 1 : 0 },
  })) };
  let lookups = 0, writes = 0;
  await installFinancialVoucherContext(page, baseURL!, scenario);
  await page.route("**/api/**", async route => {
    const request = route.request(), path = new URL(request.url()).pathname;
    if (path.endsWith("/accounting/account-options")) {
      lookups++; await route.fulfill({ json: { items: [], page: 1, pageSize: 1, totalPages: 0, totalCount: 0 } }); return;
    }
    if (!path.endsWith(`/accounting/manual/drafts/${draft.documentId}`)) { await route.fallback(); return; }
    if (request.method() === "PUT") { writes++; draft = { ...draft, description: request.postDataJSON().description, rowVersion: "AAAAAAAAAAI=" }; }
    await route.fulfill({ json: draft });
  });
  await page.goto(`/dashboard/financial-traceability?document=${draft.documentId}`);
  await expect(page.getByText(/Revisa las partidas marcadas/)).toBeVisible();
  await page.getByLabel("Descripción", { exact: true }).fill("Revisar las cuentas del comprobante");
  await page.getByRole("button", { name: "Guardar comprobante", exact: true }).click();
  await expect(page.getByRole("button", { name: "Guardar comprobante", exact: true })).toBeDisabled();
  expect(writes).toBe(1); expect(lookups).toBe(0);
});

test("el egreso prepara la partida del descuento financiero con la cuenta configurada sin releer el documento", async ({ page, baseURL }) => {
  const scenario = financialVoucherScenario();
  const draft = { ...scenario.draft, lines: [{ ...scenario.draft.lines[1],
    value: { ...scenario.draft.lines[1].value, credit: 90 } }] };
  let positionReads = 0;
  const position: AccountingOpenPosition = {
    subledgerKind: "Payable", subledgerId: scenario.positionId, partyId: scenario.draft.mainPartyId!,
    partyName: "Proveedor de prueba", partyIdentification: "900123456",
    sourceDocumentId: randomUUID(), sourceDocumentType: "GoodsReceipt", documentNumber: "COMPRA-DESC-1",
    occurredAt: "2026-10-22T12:00:00+00:00", dueDate: null, currencyCode: "COP",
    originalAmount: 100, outstandingAmount: 100, pendingPaymentAmount: 0, pendingAdjustmentAmount: 0,
    availableAmount: 100, openingEntryId: randomUUID(), openingEntryLineNumber: 1,
    accountId: scenario.draft.lines[0].value.accountId, accountCode: "22050501", accountName: "Proveedor",
    accountIsActive: true, openingDebit: 0, openingCredit: 100, openingLinkIssue: null,
    counterpartyRoleId: null, status: "Open", financialDiscountEligibleAmount: 10,
    financialDiscountAnticipated: false, financialDiscountDecided: false, financialDiscountPendingReturn: false,
    financialDiscountEarnedAccountId: "edededed-eded-eded-eded-edededededed",
    financialDiscountEarnedAccountCode: "42104001", financialDiscountEarnedAccountName: "Descuento obtenido",
  };
  await installFinancialVoucherContext(page, baseURL!, scenario);
  await page.route("**/api/**", async route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith("/reference-options/accounting-subledger-kind")) {
      await route.fulfill({ json: [{ id: "payable", code: "Payable", label: "Cuenta por pagar" }] }); return;
    }
    if (path.endsWith("/accounting/open-positions")) {
      positionReads++;
      const query = new URL(route.request().url()).searchParams;
      expect(query.get("effectiveOn")).toBe("2026-10-23");
      await route.fulfill({ json: { items: [position], page: 1, pageSize: 10, totalCount: 1, totalOutstandingAmount: 100 } }); return;
    }
    if (path.endsWith(`/accounting/manual/drafts/${scenario.documentId}`)) {
      await route.fulfill({ json: draft }); return;
    }
    await route.fallback();
  });
  await page.goto(`/dashboard/financial-traceability?document=${scenario.documentId}`);
  expect(positionReads).toBe(0);
  await page.getByRole("button", { name: "Ver documentos", exact: true }).click();
  const grid = page.getByRole("region", { name: "Documentos pendientes del tercero" });
  const row = grid.getByRole("row", { name: "Documento COMPRA-DESC-1" });
  await expect(row).toBeVisible();
  await grid.getByRole("combobox", { name: "Decisión del descuento financiero de COMPRA-DESC-1" }).click();
  await page.getByRole("option", { name: "Sí lo obtuve" }).click();
  await expect(grid).toContainText(/Salida de dinero requerida:.*90/);
  await row.getByRole("button", { name: "Agregar" }).click();
  await expect(page.getByRole("row", { name: "Partida 3", exact: true })).toContainText("42104001");
  expect(positionReads).toBe(1);
});

for (const sample of [
  { kind: "Payable", label: "Cuenta por pagar", debit: 0, credit: 500 },
  { kind: "Receivable", label: "Cuenta por cobrar", debit: 500, credit: 0 },
  { kind: "SupplierCredit", label: "Saldo a favor con proveedor", debit: 500, credit: 0 },
  { kind: "CustomerCredit", label: "Saldo a favor del cliente", debit: 0, credit: 500 },
] as const) {
  test(`agregar ${sample.kind} captura el abono contra su cuenta original y conserva la respuesta sin refetch`, async ({ page, baseURL }) => {
    if (sample.kind === "CustomerCredit") await page.setViewportSize({ width: 390, height: 844 });
    const scenario = financialVoucherScenario();
    let draft = scenario.draft;
    let positionReads = 0, draftReads = 0, writes = 0;
    let failKinds = sample.kind === "Payable";
    const position: AccountingOpenPosition = {
      subledgerKind: sample.kind, subledgerId: "88888888-8888-8888-8888-888888888888", partyId: draft.mainPartyId!,
      partyName: "Proveedor de prueba", partyIdentification: "900123456", sourceDocumentId: "cccccccc-cccc-cccc-cccc-cccccccccccc",
      sourceDocumentType: "Expense", documentNumber: "DOC-PENDIENTE-250", occurredAt: draft.occurredAt,
      dueDate: null, currencyCode: "COP", originalAmount: 500, outstandingAmount: 500, pendingPaymentAmount: 0,
      pendingAdjustmentAmount: 0, availableAmount: 500, openingEntryId: "dddddddd-dddd-dddd-dddd-dddddddddddd",
      openingEntryLineNumber: 1, accountId: "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee", accountCode: "22050599",
      accountName: "Auxiliar original", accountIsActive: true, openingDebit: sample.debit, openingCredit: sample.credit,
      openingLinkIssue: null, counterpartyRoleId: null, status: "Pending",
    };
    await installFinancialVoucherContext(page, baseURL!, scenario);
    await page.route("**/api/**", async route => {
      const request = route.request(), url = new URL(request.url()), path = url.pathname;
      if (path.endsWith("/reference-options/accounting-subledger-kind")) {
        if (failKinds) { await route.fulfill({ status: 503, json: { detail: "Catálogo temporalmente no disponible" } }); return; }
        await route.fulfill({ json: [{ id: "payable", code: "Payable", label: "Cuenta por pagar" },
          { id: sample.kind, code: sample.kind, label: sample.label }].filter((item, index, all) => all.findIndex(other => other.code === item.code) === index) }); return;
      }
      if (path.endsWith("/accounting/open-positions")) {
        expect(url.searchParams.get("partyId")).toBe(draft.mainPartyId);
        expect(url.searchParams.get("page")).toBe("1");
        if (url.searchParams.get("subledgerKind") !== sample.kind)
          { await route.fulfill({ json: { items: [], page: 1, pageSize: 10, totalCount: 0, totalOutstandingAmount: 0 } }); return; }
        positionReads++;
        await route.fulfill({ json: { items: [position], page: 1, pageSize: 10, totalCount: 1, totalOutstandingAmount: 500 } }); return;
      }
      if (!path.endsWith(`/accounting/manual/drafts/${draft.documentId}`)) { await route.fallback(); return; }
      if (request.method() === "GET") draftReads++;
      else if (request.method() === "PUT") {
        writes++;
        const command = request.postDataJSON() as SaveVoucherDraft;
        const line = command.lines.at(-1)!;
        expect(line.existingPosition).toEqual({ subledgerKind: sample.kind, subledgerId: position.subledgerId });
        expect(line.accountId).toBe(position.accountId);
        expect(line.partyId).toBe(position.partyId);
        expect(line.debit).toBe(sample.credit > 0 ? 300 : 0);
        expect(line.credit).toBe(sample.debit > 0 ? 300 : 0);
        draft = { ...draft, rowVersion: "AAAAAAAAAAI=", lines: command.lines.map(value => ({ value,
          accountCode: position.accountCode, accountName: position.accountName, partyName: position.partyName,
          partyIdentification: position.partyIdentification, costCenterName: null })) };
      }
      await route.fulfill({ json: draft });
    });
    await page.goto(`/dashboard/financial-traceability?document=${draft.documentId}`);
    expect(positionReads).toBe(0);
    await page.getByRole("button", { name: "Ver documentos", exact: true }).click();
    const grid = page.getByRole("region", { name: "Documentos pendientes del tercero" });
    await grid.getByText("Filtros", { exact: true }).click();
    if (failKinds) {
      await expect(grid.getByRole("button", { name: "Reintentar tipos de saldo", exact: true })).toBeVisible();
      failKinds = false;
      await grid.getByRole("button", { name: "Reintentar tipos de saldo", exact: true }).click();
      await expect(grid.getByRole("combobox", { name: "Tipo de saldo pendiente" })).toContainText("Cuenta por pagar");
    }
    if (sample.kind !== "Payable") {
      await grid.getByRole("combobox", { name: "Tipo de saldo pendiente" }).click();
      await page.getByRole("option", { name: sample.label, exact: true }).click();
    }
    const row = grid.getByRole("row", { name: "Documento DOC-PENDIENTE-250" });
    await expect(row).toContainText("22050599 · Auxiliar original");
    await row.getByLabel("Abono a DOC-PENDIENTE-250").fill("600");
    await expect(row.getByRole("button", { name: "Agregar" })).toBeDisabled();
    await row.getByLabel("Abono a DOC-PENDIENTE-250").fill("250");
    await row.getByRole("button", { name: "Agregar" }).click();
    await expect(page.getByRole("row", { name: "Partida 3", exact: true })).toContainText("22050599");
    await expect(page.getByRole("row", { name: "Partida 3", exact: true }).getByRole("combobox", { name: "Seleccionar cuenta contable", exact: true })).toBeDisabled();
    await expect(row.getByLabel("Abono a DOC-PENDIENTE-250")).toHaveValue(/250/);
    await row.getByLabel("Abono a DOC-PENDIENTE-250").fill("300");
    await row.getByRole("button", { name: "Actualizar" }).click();
    await expect(page.getByRole("row", { name: "Partida 4", exact: true })).toHaveCount(0);
    await page.getByRole("button", { name: "Guardar comprobante", exact: true }).click();
    await expect(page.getByRole("button", { name: "Guardar comprobante", exact: true })).toBeDisabled();
    expect(positionReads).toBe(1); expect(draftReads).toBe(1); expect(writes).toBe(1);
  });
}

for (const legacy of [false, true]) {
  test(`grupos y evidencia se guardan sin sesión desde ${legacy ? "la captura legacy activada explícitamente" : "el comprobante financiero"}`, async ({ page, baseURL }) => {
    const scenario = financialVoucherScenario();
    let draft = legacy ? { ...scenario.draft, payloadVersion: 2, voucherTypeCode: null,
      lines: scenario.draft.lines.map(line => ({ ...line, value: { ...line.value, existingPosition: null, paymentEvidence: null } })) } : scenario.draft;
    let reads = 0, writes = 0, sessionCalls = 0, positionReads = 0;
    await installFinancialVoucherContext(page, baseURL!, scenario);
    await page.route("**/api/**", async route => {
      const request = route.request(), path = new URL(request.url()).pathname;
      if (path.endsWith("/accounting/open-positions")) {
        positionReads++;
        await route.fulfill({ json: { items: [], page: 1, pageSize: 10, totalCount: 0, totalOutstandingAmount: 0 } }); return;
      }
      if (path.endsWith("/reference-options/card-franchise")) { await route.fulfill({ json: [{ id: "visa", code: "Visa", label: "Visa" }] }); return; }
      if (path.endsWith("/work-sessions/current")) {
        sessionCalls++; await route.fulfill({ status: 500, json: {} }); return;
      }
      if (!path.endsWith(`/accounting/manual/drafts/${draft.documentId}`)) { await route.fallback(); return; }
      if (request.method() === "GET") reads++;
      else if (request.method() === "PUT") {
        writes++;
        const command = request.postDataJSON() as SaveVoucherDraft;
        expect(command.payloadVersion).toBe(4);
        expect(command.workSessionId).toBeNull();
        expect(command.lines[0].financialGroupId).toBeTruthy();
        expect(command.lines[0].financialGroupId).toBe(command.lines[1].financialGroupId);
        expect(command.lines[1].paymentEvidence).toEqual({ cardFranchiseCode: "Visa", approvalNumber: "AP-123",
          notes: "Evidencia conservada", ...(legacy ? {} : { tenderedAmount: 100 }) });
        draft = { ...draft, ...command, rowVersion: "AAAAAAAAAAM=", lines: command.lines.map((value, index) => ({ ...draft.lines[index], value })) };
      }
      await route.fulfill({ json: draft });
    });
    await page.goto(`/dashboard/financial-traceability?document=${draft.documentId}`);
    if (legacy) await page.getByRole("button", { name: "Usar captura financiera", exact: true }).click();
    await page.getByRole("button", { name: "Detalles de la partida 2", exact: true }).click();
    let dialog = page.getByRole("dialog", { name: "Detalles de la partida 2", exact: true });
    await dialog.getByRole("combobox", { name: "Grupo de la partida" }).click();
    await page.getByRole("option", { name: "Crear otro grupo", exact: true }).click();
    await dialog.getByLabel("Autorización del pago", { exact: true }).fill("AP-123");
    await dialog.getByLabel("Observación del pago", { exact: true }).fill("Evidencia conservada");
    await dialog.getByRole("combobox", { name: "Franquicia del pago" }).click();
    await page.getByRole("option", { name: "Visa", exact: true }).click();
    await dialog.getByRole("button", { name: "Cerrar detalles" }).click();
    await page.getByRole("button", { name: "Detalles de la partida 1", exact: true }).click();
    dialog = page.getByRole("dialog", { name: "Detalles de la partida 1", exact: true });
    await dialog.getByRole("combobox", { name: "Grupo de la partida" }).click();
    await page.getByRole("option", { name: "Grupo 1", exact: true }).click();
    await dialog.getByRole("button", { name: "Cerrar detalles" }).click();
    await expect(page.getByRole("button", { name: /sesión de trabajo|cierre de caja/i })).toHaveCount(0);
    await page.getByRole("button", { name: "Guardar comprobante", exact: true }).click();
    await expect(page.getByRole("button", { name: "Guardar comprobante", exact: true })).toBeDisabled();
    expect(reads).toBe(1); expect(writes).toBe(1); expect(sessionCalls).toBe(0); expect(positionReads).toBe(0);
  });
}

test("un comprobante financiero sin tercero principal no se contabiliza desde la vista", async ({ page, baseURL }) => {
  const scenario = financialVoucherScenario();
  const draft = { ...scenario.draft, mainPartyId: null, mainPartyName: null, mainPartyIdentification: null };
  let sends = 0;
  await installFinancialVoucherContext(page, baseURL!, scenario);
  await page.route("**/api/**", async route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith(`/accounting/manual/drafts/${draft.documentId}/send`)) {
      sends++; await route.fulfill({ status: 500, json: {} }); return;
    }
    if (path.endsWith(`/accounting/manual/drafts/${draft.documentId}`)) {
      await route.fulfill({ json: draft }); return;
    }
    await route.fallback();
  });
  await page.goto(`/dashboard/financial-traceability?document=${draft.documentId}`);
  await expect(page.getByText("Obligatorio para contabilizar.", { exact: false })).toBeVisible();
  await page.getByRole("button", { name: "Contabilizar", exact: true }).click();
  await expect(page.getByRole("alert").filter({ hasText: "tercero principal" })).toBeVisible();
  expect(sends).toBe(0);
});
