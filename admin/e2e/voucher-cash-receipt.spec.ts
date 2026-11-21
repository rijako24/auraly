import { expect, test } from "@playwright/test";
import type { AccountingOpenPosition, SaveVoucherDraft, VoucherDraft } from "../src/services/api/accounting";
import { financialVoucherScenario, installFinancialVoucherContext } from "./support/financial-voucher";

for (const receipt of [false, true]) for (const status of ["Pending", "Posted"] as const) {
  test(`captura nueva ${receipt ? "de recibo" : "contable"} conserva el resultado ${status} al guardar y contabilizar`, async ({ page, baseURL }) => {
    const scenario = financialVoucherScenario();
    const accounts = [
      { accountId: scenario.draft.lines[1].value.accountId, code: "110505", name: "Caja de prueba", accountType: "Asset", allowsPosting: true, requiresParty: false, isActive: true },
      { accountId: scenario.draft.lines[0].value.accountId, code: "280505", name: "Anticipo de prueba", accountType: "Liability", allowsPosting: true, requiresParty: true, isActive: true },
    ];
    await installFinancialVoucherContext(page, baseURL!, scenario);
    let stored: VoucherDraft | undefined;
    let saves = 0, sends = 0, draftReads = 0;
    await page.route("**/api/**", async route => {
      const request = route.request(), path = new URL(request.url()).pathname;
      if (path.endsWith("/accounting/account-options")) {
        await route.fulfill({ json: { items: accounts, page: 1, pageSize: 10, totalCount: 2, totalPages: 1 } });
      } else if (path.endsWith("/parties/role-options")) {
        await route.fulfill({ json: { items: [{ partyId: scenario.draft.mainPartyId, roleId: scenario.draft.mainPartyId,
          role: "Any", displayName: "Tercero de prueba", identification: "900123456" }], page: 1, pageSize: 10, totalCount: 1, totalPages: 1 } });
      } else if (path.endsWith("/reference-options/accounting-document-status")) {
        await route.fulfill({ json: [{ id: "created", code: "Created", label: "Borrador" },
          { id: "pending", code: "Pending", label: "Enviado / pendiente" }, { id: "posted", code: "Posted", label: "Contabilizado" }] });
      } else if (/\/accounting\/manual\/drafts\/[^/]+\/send$/.test(path)) {
        sends++;
        // Let the save-triggered URL transition finish before the posting
        // response. It must not replace the editor with the intermediate draft.
        await new Promise(resolve => setTimeout(resolve, 500));
        stored = { ...stored!, status, sentAt: "2026-11-14T12:00:00Z", canEdit: false, canSend: false,
          rowVersion: "AAAAAAAAAAM=", row: { ...stored!.row, status,
            entryId: status === "Posted" ? "entry-1" : null, entryNumber: status === "Posted" ? "ASI-1" : null } };
        await route.fulfill({ json: stored });
      } else if (/\/accounting\/manual\/drafts\/[^/]+$/.test(path)) {
        if (request.method() === "PUT") {
          saves++;
          const capture = request.postDataJSON() as SaveVoucherDraft;
          stored = { ...scenario.draft, ...capture, status: "Created", sentAt: null, canSend: true,
            rowVersion: "AAAAAAAAAAI=", lines: capture.lines.map(value => {
              const account = accounts.find(item => item.accountId === value.accountId)!;
              return { value, accountCode: account.code, accountName: account.name, partyName: "Tercero de prueba",
                partyIdentification: "900123456", costCenterName: null };
            }), row: { ...scenario.draft.row, sourceDocumentId: capture.documentId, status: "Created", voucherTypeCode: capture.voucherTypeCode } };
        } else draftReads++;
        await route.fulfill({ json: stored });
      } else await route.fallback();
    });
    const basePath = receipt ? "/dashboard/cash-receipts" : "/dashboard/financial-traceability";
    await page.goto(`${basePath}?new=1`);
    await page.getByRole("textbox", { name: "Descripción", exact: true }).fill("Captura nueva de prueba");
    await page.getByRole("combobox", { name: "Seleccionar tercero principal", exact: true }).click();
    await page.getByRole("option", { name: /Tercero de prueba/ }).click();
    for (const [index, account] of accounts.entries()) {
      const row = page.getByRole("row", { name: `Partida ${index + 1}`, exact: true });
      await row.getByRole("combobox", { name: "Seleccionar cuenta contable", exact: true }).click();
      await page.getByPlaceholder("Buscar por código o nombre…").fill(account.code);
      await page.getByRole("option", { name: `${account.code} · ${account.name}`, exact: true }).click();
      await row.getByRole("textbox", { name: "Descripción de la partida", exact: true }).fill(account.name);
      await row.getByRole("textbox", { name: index === 0 ? "Débito de la partida" : "Crédito de la partida", exact: true }).fill("20");
    }
    await page.getByRole("button", { name: "Guardar comprobante", exact: true }).click();
    await expect(page.getByText(status === "Posted" ? "Contabilizado · Documento interno, sin envío a DIAN"
      : "Enviado / pendiente · Documento interno, sin envío a DIAN", { exact: true })).toBeVisible();
    await expect(page).toHaveURL(`${baseURL}${basePath}?document=${stored!.documentId}`);
    await expect(page.getByRole("button", { name: "Guardar comprobante", exact: true })).toHaveCount(0);
    expect(saves).toBe(1); expect(sends).toBe(1); expect(draftReads).toBe(0);
  });
}

for (const { posted, clone } of [{ posted: true, clone: false }, { posted: false, clone: false }, { posted: true, clone: true }]) test(`recibo nativo de cartera conserva su documento y consulta compartida ${clone ? "al clonar" : posted ? "con asiento" : "sin contabilidad activa"}`, async ({ page, baseURL }, testInfo) => {
  const scenario = financialVoucherScenario();
  scenario.user.permissions = clone ? ["accounting.read", "accounting.manual.create"] : ["accounting.read"];
  const debt = scenario.draft.lines[0], cash = scenario.draft.lines[1];
  debt.accountCode = "13050501"; debt.accountName = "Cartera de clientes";
  debt.partyName = "Cliente que pagó";
  debt.value = { ...debt.value, description: "Abono a factura", debit: 0, credit: 100,
    existingPosition: { subledgerKind: "Receivable", subledgerId: scenario.positionId } };
  cash.value = { ...cash.value, description: "Ingreso de caja", debit: 100, credit: 0 };
  await installFinancialVoucherContext(page, baseURL!, scenario);
  let listReads = 0, postingReads = 0, entryReads = 0, draftReads = 0, fiscalReads = 0, writes = 0;
  const status = posted ? "Posted" : "CommercialEffectsApplied";
  const row = { ...scenario.draft.row, sourceDocumentType: "ReceivablePayment", sourceDocumentNumber: "RC-000001",
    hasManualDraft: false, voucherTypeCode: "CashReceipt", status, mainPartyName: "Cliente que pagó",
    entryId: posted ? "entry-1" : null, entryNumber: posted ? "CC-000001" : null };
  await page.route("**/api/**", async route => {
    const request = route.request(), url = new URL(request.url()), path = url.pathname;
    if (!["GET", "HEAD"].includes(request.method())) writes++;
    if (path.includes("/accounting/manual/drafts")) draftReads++;
    if (path.includes("/fiscal/documents")) fiscalReads++;
    if (path.endsWith("/accounting/documents")) {
      listReads++;
      expect(url.searchParams.get("voucherTypeCode")).toBe("CashReceipt");
      expect(url.searchParams.has("documentType")).toBe(false);
      await route.fulfill({ json: { items: [row], page: 1, pageSize: 25, totalCount: 1, totalPages: 1 } });
    } else if (path.endsWith(`/accounting/documents/${scenario.documentId}/clone`)) {
      const command = request.postDataJSON() as { documentId: string; occurredAt: string };
      const copy = { ...scenario.draft, documentId: command.documentId, occurredAt: command.occurredAt,
        payloadVersion: 2, voucherTypeCode: null, clonedFromDocumentId: scenario.documentId,
        lines: scenario.draft.lines.map(line => ({ ...line, value: { ...line.value, existingPosition: null, paymentEvidence: null } })) };
      await route.fulfill({ json: copy });
    } else if (path.endsWith(`/accounting/postings/by-document/${scenario.documentId}`)) {
      postingReads++;
      await route.fulfill({ json: { ...row, errorCode: null, errorMessage: null } });
    } else if (path.endsWith(`/accounting/entries/by-document/${scenario.documentId}`)) {
      entryReads++;
      await route.fulfill({ json: { ...row, description: "Recaudo de cartera", postedAt: row.occurredAt,
        lines: scenario.draft.lines.map((line, index) => ({ ...line.value, lineNumber: index + 1,
          accountCode: line.accountCode, accountName: line.accountName, partyName: line.partyName })) } });
    } else await route.fallback();
  });
  await page.goto("/dashboard/cash-receipts");
  await expect(page.getByText("RC-000001", { exact: true })).toBeVisible();
  await expect(page.getByText("Cliente que pagó", { exact: true })).toBeVisible();
  if (!clone) await expect(page.getByRole("button", { name: "Nuevo recibo de caja", exact: true })).toHaveCount(0);
  await page.getByRole("button", { name: "Ver documento", exact: true }).click();
  const dialog = page.getByRole("dialog");
  await expect(dialog).toBeVisible();
  if (posted) await expect(dialog.getByText("Comprobante balanceado", { exact: true })).toBeVisible();
  else await expect(dialog.getByText(/terminó su procesamiento sin generar un asiento contable/)).toBeVisible();
  await dialog.getByRole("button", { name: "Cerrar", exact: true }).click();
  await page.getByRole("button", { name: "Ver documento", exact: true }).click();
  await expect(page.getByRole("dialog")).toBeVisible();
  if (clone) {
    await page.getByRole("button", { name: "Clonar comprobante", exact: true }).click();
    await expect(page).toHaveURL(/\/dashboard\/financial-traceability\?document=/);
    await expect(page.getByRole("button", { name: "Volver a trazabilidad", exact: true })).toBeVisible();
    await expect(page.getByRole("combobox", { name: "Tipo de comprobante", exact: true })).toHaveCount(0);
  }
  expect(listReads).toBe(1); expect(postingReads).toBe(1); expect(entryReads).toBe(posted ? 1 : 0);
  expect(draftReads).toBe(0); expect(fiscalReads).toBe(0); expect(writes).toBe(clone ? 1 : 0);
  await page.screenshot({ path: testInfo.outputPath("native-cash-receipt.png"), fullPage: true });
});

for (const supplier of [false, true]) for (const pureCross of [false, true]) for (const withAdjustment of [false, true]) {
  test(`${supplier ? "egreso" : "recibo"}: agrega anticipo a partidas con destino explícito ${pureCross ? "sin dinero nuevo" : "y dinero nuevo"}${withAdjustment ? " y ajuste vinculado" : ""}`, async ({ page, baseURL }, testInfo) => {
    const scenario = financialVoucherScenario();
    scenario.user.permissions.push("receivables.payments.create");
    const kind = supplier ? "Payable" : "Receivable", creditKind = supplier ? "SupplierCredit" : "CustomerCredit";
    const creditId = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee", creditAccount = "cccccccc-cccc-cccc-cccc-cccccccccccc";
    scenario.draft.voucherTypeCode = supplier ? "Disbursement" : "CashReceipt";
    scenario.draft.lines[0] = { ...scenario.draft.lines[0], value: { ...scenario.draft.lines[0].value,
      debit: supplier ? (pureCross ? 30 : 80) + (withAdjustment ? 10 : 0) : 0, credit: supplier ? 0 : (pureCross ? 30 : 80) + (withAdjustment ? 10 : 0),
      reference: "F-001", existingPosition: { subledgerKind: kind, subledgerId: scenario.positionId } } };
    scenario.draft.lines[1] = { ...scenario.draft.lines[1], value: { ...scenario.draft.lines[1].value,
      debit: supplier ? 0 : 50, credit: supplier ? 50 : 0, paymentEvidence: null } };
    const cashAccountId = scenario.draft.lines[1].value.accountId;
    if (pureCross) scenario.draft.lines.pop();
    if (withAdjustment) scenario.draft.lines.push({ value: { lineId: "dddddddd-2222-4222-8222-222222222222",
      accountId: "dddddddd-3333-4333-8333-333333333333", partyId: scenario.draft.mainPartyId ?? null, costCenterId: null,
      description: "Reducción explícita", debit: supplier ? 0 : 10, credit: supplier ? 10 : 0,
      financialConceptId: "dddddddd-4444-4444-8444-444444444444", financialConceptTargetLineId: scenario.draft.lines[0].value.lineId,
      financialGroupId: scenario.documentId }, accountCode: "51959501", accountName: "Contrapartida",
      partyName: scenario.draft.mainPartyName ?? null, partyIdentification: null, costCenterName: null,
      financialConceptCode: "AJUSTE", financialConceptName: "Reducción explícita", financialConceptSideCode: supplier ? "Credit" : "Debit" });
    let draft = scenario.draft;
    let draftReads = 0, positionReads = 0;
    const writes: SaveVoucherDraft[] = [];
    const credit: AccountingOpenPosition = { subledgerKind: creditKind, subledgerId: creditId,
      partyId: draft.mainPartyId!, partyName: draft.mainPartyName!, partyIdentification: "900123456",
      sourceDocumentId: scenario.documentId, sourceDocumentType: "ManualAccountingVoucher", documentNumber: "ANT-001",
      occurredAt: "2026-10-01T12:00:00Z", dueDate: null, currencyCode: "COP", creditNature: "Advance",
      originalAmount: 70, outstandingAmount: 50, availableAmount: 50, otherMovementsAmount: 20,
      pendingPaymentAmount: 0, pendingAdjustmentAmount: 0, pendingApplicationAmount: 0,
      openingEntryId: scenario.documentId, openingEntryLineNumber: 1,
      accountId: creditAccount, accountCode: supplier ? "13300501" : "28050501", accountName: "Anticipos",
      accountIsActive: true, openingLinkIssue: null, openingDebit: supplier ? 70 : 0, openingCredit: supplier ? 0 : 70,
      counterpartyRoleId: null, status: "Open" };
    await installFinancialVoucherContext(page, baseURL!, scenario);
    await page.route("**/api/**", async route => {
      const request = route.request(), url = new URL(request.url()), path = url.pathname;
      if (path.endsWith("/accounting/open-positions")) {
        positionReads++;
        expect(url.searchParams.get("voucherTypeCode")).toBe(supplier ? "Disbursement" : "CashReceipt");
        await route.fulfill({ json: { items: [credit], page: 1, pageSize: 10, totalCount: 1, totalOutstandingAmount: 50 } }); return;
      }
      if (path.endsWith(`/accounting/manual/drafts/${draft.documentId}`)) {
        if (request.method() === "GET") draftReads++;
        if (request.method() === "PUT") {
          const value = request.postDataJSON() as SaveVoucherDraft; writes.push(value);
          draft = { ...draft, ...value, rowVersion: "AAAAAAAAAAI=", lines: value.lines.map(line => {
            const previous = scenario.draft.lines.find(item => item.value.accountId === line.accountId);
            return { accountCode: previous?.accountCode ?? credit.accountCode, accountName: previous?.accountName ?? credit.accountName,
              partyName: previous?.partyName ?? credit.partyName, partyIdentification: previous?.partyIdentification ?? credit.partyIdentification,
              costCenterName: null, value: line };
          }) };
        }
        await route.fulfill({ json: draft }); return;
      }
      await route.fallback();
    });
    await page.goto(`/dashboard/financial-traceability?document=${draft.documentId}`);
    await page.getByRole("button", { name: "Ver documentos", exact: true }).click();
    const row = page.getByRole("row", { name: "Documento ANT-001", exact: true });
    await row.getByRole("button", { name: "Cruzar", exact: true }).click();
    await page.getByRole("combobox", { name: "Factura para cruzar ANT-001" }).click();
    await page.getByRole("option", { name: /F-001/ }).click();
    await row.getByLabel("Abono a ANT-001").fill(pureCross && withAdjustment ? "31" : "51");
    await expect(page.getByRole("button", { name: "Agregar cruce", exact: true })).toBeDisabled();
    await row.getByLabel("Abono a ANT-001").fill("30");
    await page.getByRole("button", { name: "Agregar cruce", exact: true }).click();
    await expect(page.getByRole("region", { name: "Cruces de saldos a favor" })).toContainText("ANT-001");
    await expect(page.getByRole("region", { name: "Cruces de saldos a favor" })).toContainText("F-001");
    await expect(row.getByLabel("Abono a ANT-001")).toHaveValue("20");
    await page.getByRole("button", { name: "Guardar borrador", exact: true }).click();
    await expect(page.getByRole("button", { name: "Guardar borrador", exact: true })).toBeDisabled();
    expect(writes).toHaveLength(1);
    const saved = writes[0];
    expect(saved.lines).toHaveLength(pureCross ? withAdjustment ? 4 : 2 : withAdjustment ? 5 : 4);
    if (withAdjustment) {
      const concept = saved.lines.find(line => line.financialConceptTargetLineId)!;
      const adjusted = saved.lines.find(line => line.lineId === concept.financialConceptTargetLineId)!;
      expect(adjusted).toBeTruthy(); expect(adjusted.financialGroupId).toBe(concept.financialGroupId);
      expect(adjusted.debit + adjusted.credit).toBe(pureCross ? 10 : 60);
    }
    const creditLine = saved.lines.find(line => line.existingPosition?.subledgerId === creditId)!;
    expect(creditLine.debit + creditLine.credit).toBe(30);
    expect(creditLine.financialGroupId).toBeTruthy();
    const target = saved.lines.find(line => line.financialGroupId === creditLine.financialGroupId && line !== creditLine)!;
    expect(creditLine.financialGroupId).toBe(target.lineId);
    expect(target.existingPosition).toEqual({ subledgerKind: kind, subledgerId: scenario.positionId });
    expect(target.debit + target.credit).toBe(30);
    expect(saved.lines.reduce((sum, line) => sum + line.debit - line.credit, 0)).toBe(0);
    expect(saved.lines.filter(line => line.accountId === cashAccountId)).toHaveLength(pureCross ? 0 : 1);
    if (!pureCross) {
      const cash = saved.lines.find(line => line.accountId === cashAccountId)!;
      expect(cash.debit + cash.credit).toBe(50);
      expect(cash.financialGroupId).toBe(saved.documentId);
      await page.getByRole("button", { name: "Retirar cruce ANT-001", exact: true }).click();
      await page.getByRole("button", { name: "Guardar borrador", exact: true }).click();
      await expect(page.getByRole("button", { name: "Guardar borrador", exact: true })).toBeDisabled();
      expect(writes).toHaveLength(2);
      expect(writes[1].lines).toHaveLength(withAdjustment ? 3 : 2);
      if (!withAdjustment) expect(writes[1].lines.every(line => line.debit + line.credit === 50)).toBe(true);
      expect(writes[1].lines.reduce((sum,line)=>sum+line.debit-line.credit,0)).toBe(0);
    }
    expect(draftReads).toBe(1); expect(positionReads).toBe(1);
    await page.screenshot({ path: testInfo.outputPath("administrative-advance-crossing.png"), fullPage: true });
  });
}

test("el menú de recibos filtra en servidor y abre la captura compartida con cartera y anticipos del tercero", async ({ page, baseURL }, testInfo) => {
  await page.clock.setFixedTime(new Date("2026-11-09T15:00:00Z"));
  const scenario = financialVoucherScenario();
  scenario.timeZone = "Pacific/Kiritimati";
  const listQueries: URL[] = [], positions: URL[] = [];
  const common = { partyId: scenario.draft.mainPartyId!, partyName: "Tercero que entrega el dinero", partyIdentification: "900123456",
    sourceDocumentId: scenario.documentId, sourceDocumentType: "ManualAccountingVoucher", occurredAt: "2026-10-01T12:00:00Z",
    currencyCode: "COP", pendingPaymentAmount: 0, pendingAdjustmentAmount: 0, pendingApplicationAmount: 0,
    openingEntryId: "dddddddd-dddd-dddd-dddd-dddddddddddd", openingEntryLineNumber: 1,
    accountIsActive: true, openingLinkIssue: null, counterpartyRoleId: null, status: "Open" };
  const pending: AccountingOpenPosition[] = [
    { ...common, subledgerKind: "Receivable", subledgerId: scenario.positionId, documentNumber: "F-001", dueDate: "2026-10-29T12:00:00Z",
      originalAmount: 100, outstandingAmount: 80, availableAmount: 80, paidAmount: 20, otherMovementsAmount: 0, overdueDays: 12,
      accountId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", accountCode: "13050501", accountName: "Cuentas por cobrar", openingDebit: 100, openingCredit: 0 },
    { ...common, subledgerKind: "CustomerCredit", subledgerId: "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee", documentNumber: "RC-ADV-001", dueDate: null,
      originalAmount: 50, outstandingAmount: 30, availableAmount: 30, paidAmount: null, otherMovementsAmount: 20, overdueDays: null,
      creditNature: "Advance", accountId: "cccccccc-cccc-cccc-cccc-cccccccccccc", accountCode: "28050501", accountName: "Anticipos recibidos", openingDebit: 0, openingCredit: 50 },
  ];
  await installFinancialVoucherContext(page, baseURL!, scenario);
  await page.route("**/api/**", async route => {
    const url = new URL(route.request().url()), path = url.pathname;
    if (path.endsWith("/accounting/documents")) {
      listQueries.push(url);
      await route.fulfill({ json: { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 } });
      return;
    }
    if (path.endsWith("/parties/role-options")) {
      expect(url.searchParams.get("role")).toBe("Any");
      await route.fulfill({ json: { items: [{ partyId: scenario.draft.mainPartyId, roleId: "supplier-1", role: "Supplier",
        displayName: "Tercero que entrega el dinero", identification: "900123456" }], page: 1, pageSize: 10, totalCount: 1, totalPages: 1 } });
      return;
    }
    if (path.endsWith("/accounting/open-positions")) {
      positions.push(url);
      await route.fulfill({ json: { items: pending, page: 1, pageSize: 10, totalCount: 2, totalOutstandingAmount: 110, referenceDate: "2026-11-10" } });
      return;
    }
    await route.fallback();
  });
  await page.goto("/dashboard/financial-traceability");
  await page.getByRole("link", { name: "Recibos de caja", exact: true }).click();
  await expect(page).toHaveURL(/\/dashboard\/cash-receipts$/);
  await expect(page.getByRole("heading", { name: "Recibos de caja", level: 1, exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Filtros de documentos", exact: true }).click();
  await expect(page.getByLabel("Desde", { exact: true })).toBeVisible();
  await expect(page.getByLabel("Hasta", { exact: true })).toBeVisible();
  await expect(page.getByRole("combobox", { name: "Tipo de operación" })).toHaveCount(0);
  await page.getByPlaceholder("Número o referencia del recibo").fill("RC-001");
  await expect.poll(() => listQueries.at(-1)?.searchParams.get("search")).toBe("RC-001");
  expect(listQueries.at(-1)?.searchParams.get("voucherTypeCode")).toBe("CashReceipt");
  expect(listQueries.at(-1)?.searchParams.has("documentType")).toBe(false);
  await page.getByRole("button", { name: "Nuevo recibo de caja", exact: true }).click();
  await expect(page.getByLabel("Fecha de pago", { exact: true })).toContainText("10 de noviembre de 2026");
  await expect(page.getByRole("combobox", { name: "Tipo de comprobante", exact: true })).toHaveText("Recibo de caja");
  await expect(page.getByRole("combobox", { name: "Tipo de comprobante", exact: true })).toBeDisabled();
  const listReads = listQueries.length;
  await page.getByRole("combobox", { name: "Seleccionar tercero principal", exact: true }).click();
  await page.getByRole("option", { name: /Tercero que entrega el dinero/ }).click();
  await expect(page.getByRole("region", { name: "Documentos pendientes del tercero" })).toBeVisible();
  await expect.poll(() => positions.length).toBe(1);
  expect(positions[0].searchParams.get("subledgerKind")).toBe("All");
  expect(positions[0].searchParams.get("voucherTypeCode")).toBe("CashReceipt");
  await expect(page.getByRole("row", { name: "Documento F-001", exact: true })).toBeVisible();
  await expect(page.getByRole("row", { name: "Documento RC-ADV-001", exact: true })).toBeVisible();
  await expect(page.getByText("Anticipo", { exact: true })).toBeVisible();
  await expect(page.getByText("12 días", { exact: true })).toBeVisible();
  await expect(page.getByText(/Saldos y edad consultados al 10\/11\/2026/)).toBeVisible();
  await page.getByLabel("Fecha de pago", { exact: true }).click();
  await page.getByRole("button", { name: "9 de noviembre de 2026", exact: true }).click();
  await expect.poll(() => positions.length).toBe(2);
  expect(positions[1].searchParams.get("effectiveOn")).toBe("2026-11-09");
  await expect(page.getByText("12 días", { exact: true })).toBeVisible();
  await expect(page.getByText(/Saldos y edad consultados al 10\/11\/2026/)).toBeVisible();
  expect(listQueries.length).toBe(listReads);
  await page.screenshot({ path: testInfo.outputPath("cash-receipt-party-positions.png"), fullPage: true });
});

test("recibo de caja admite cualquier tercero y conserva el guardado canónico sin volver a cargar el documento", async ({ page, baseURL }, testInfo) => {
  const scenario = financialVoucherScenario();
  scenario.timeZone = "Pacific/Kiritimati";
  scenario.user.permissions.push("receivables.payments.create");
  scenario.draft.voucherTypeCode = "CashReceipt";
  scenario.draft.mainPartyName = "Tercero del recibo";
  scenario.draft.lines = scenario.draft.lines.map((line, index) => ({ ...line,
    accountCode: index === 0 ? "13050501" : line.accountCode,
    accountName: index === 0 ? "Cuentas por cobrar" : line.accountName,
    partyName: index === 0 ? "Tercero del recibo" : null,
    value: { ...line.value, debit: index === 0 ? 0 : 100.1234, credit: index === 0 ? 100.1234 : 0,
      description: index === 0 ? "Cobro de obligación" : "Dinero recibido",
      existingPosition: index === 0 ? { subledgerKind: "Receivable", subledgerId: scenario.positionId } : null },
  }));
  scenario.draft.row = { ...scenario.draft.row, debitTotal: 100.1234, creditTotal: 100.1234 };
  let draft = scenario.draft;
  let reads = 0, writes = 0, listReads = 0;
  const partyRoles: string[] = [], positionKinds: string[] = [];
  await installFinancialVoucherContext(page, baseURL!, scenario);
  await page.route("**/api/**", async route => {
    const request = route.request(), url = new URL(request.url()), path = url.pathname;
    if (path.endsWith("/accounting/documents")) {
      listReads++;
      const items = [{ ...draft.row, sourceDocumentNumber: writes ? "RC-SERVIDOR" : "RC-ANTES" }];
      if (writes) items.push({ ...draft.row, sourceDocumentId: "88888888-8888-8888-8888-888888888888", sourceDocumentNumber: "RC-CONCURRENTE" });
      await route.fulfill({ json: { items, page: 1, pageSize: 25, totalCount: items.length, totalPages: 1 } });
      return;
    }
    if (path.endsWith("/parties/role-options")) {
      partyRoles.push(url.searchParams.get("role") ?? "");
      await route.fulfill({ json: { items: [{ partyId: draft.mainPartyId, roleId: "supplier-1", role: "Supplier",
        displayName: draft.mainPartyName, identification: "900123456" }], page: 1, pageSize: 10, totalCount: 1, totalPages: 1 } });
      return;
    }
    if (path.endsWith("/reference-options/accounting-subledger-kind")) {
      await route.fulfill({ json: [{ id: "payable", code: "Payable", label: "Cuenta por pagar" },
        { id: "receivable", code: "Receivable", label: "Cuenta por cobrar" },
        { id: "supplier-credit", code: "SupplierCredit", label: "Saldo a favor proveedor" },
        { id: "customer-credit", code: "CustomerCredit", label: "Saldo a favor cliente" }] });
      return;
    }
    if (path.endsWith("/accounting/open-positions")) {
      positionKinds.push(url.searchParams.get("subledgerKind") ?? "");
      expect(url.searchParams.get("voucherTypeCode")).toBe("CashReceipt");
      await route.fulfill({ json: { items: [], page: 1, pageSize: 10, totalCount: 0, totalOutstandingAmount: 0 } });
      return;
    }
    if (path.endsWith(`/accounting/manual/drafts/${draft.documentId}`)) {
      if (request.method() === "GET") reads++;
      if (request.method() === "PUT") {
        writes++;
        const value = request.postDataJSON() as SaveVoucherDraft;
        expect(value.voucherTypeCode).toBe("CashReceipt");
        expect(value.occurredAt).toBe("2026-10-22T12:00:00+14:00");
        expect(value.mainPartyId).toBe(draft.mainPartyId);
        expect(value.lines[0].existingPosition?.subledgerKind).toBe("Receivable");
        draft = { ...draft, occurredAt: value.occurredAt, description: value.description, rowVersion: "AAAAAAAAAAI=",
          row: { ...draft.row, occurredAt: value.occurredAt } };
      }
      await route.fulfill({ json: draft });
      return;
    }
    await route.fallback();
  });
  await page.goto("/dashboard/financial-traceability");
  await page.getByRole("button", { name: "Ver documento", exact: true }).click();
  await expect(page.getByRole("combobox", { name: "Tipo de comprobante", exact: true })).toContainText("Recibo de caja");
  await page.getByRole("combobox", { name: "Seleccionar tercero principal", exact: true }).click();
  await expect(page.getByRole("option", { name: /Tercero del recibo/ })).toBeVisible();
  await page.keyboard.press("Escape");
  expect(partyRoles).toEqual(["Any"]);
  await page.getByRole("button", { name: "Ver documentos", exact: true }).click();
  const grid = page.getByRole("region", { name: "Documentos pendientes del tercero" });
  await expect(grid.getByText(/No hay documentos/)).toBeVisible();
  expect(positionKinds).toEqual(["All"]);
  await grid.getByText("Filtros", { exact: true }).click();
  await grid.getByRole("combobox", { name: "Tipo de saldo pendiente" }).click();
  await expect(page.getByRole("option", { name: "Cuenta por cobrar", exact: true })).toBeVisible();
  await expect(page.getByRole("option", { name: "Cuenta por pagar", exact: true })).toHaveCount(0);
  await expect(page.getByRole("option", { name: "Saldo a favor proveedor", exact: true })).toHaveCount(0);
  await page.getByRole("option", { name: "Saldo a favor cliente", exact: true }).click();
  await expect.poll(() => positionKinds).toEqual(["All", "CustomerCredit"]);
  await page.getByLabel("Fecha de pago", { exact: true }).click();
  await page.getByRole("button", { name: "22 de octubre de 2026", exact: true }).click();
  await page.getByLabel("Descripción", { exact: true }).fill("Recibo del tercero revisado");
  await page.getByRole("button", { name: "Guardar borrador", exact: true }).click();
  await expect(page.getByRole("button", { name: "Guardar borrador", exact: true })).toBeDisabled();
  expect(writes).toBe(1);
  expect(reads).toBe(1);
  expect(listReads).toBe(1);
  await expect(page.getByLabel("Fecha de pago", { exact: true })).toContainText("22 de octubre de 2026");
  await page.getByRole("button", { name: "Volver a trazabilidad", exact: true }).click();
  await expect(page.getByText("RC-SERVIDOR", { exact: true })).toBeVisible();
  await expect(page.getByText("RC-CONCURRENTE", { exact: true })).toBeVisible();
  await expect(page.getByRole("row").filter({ hasText: "RC-SERVIDOR" }).getByRole("cell").first()).toHaveText("22/10/2026");
  await expect(page.getByRole("row").filter({ hasText: "RC-SERVIDOR" }).getByRole("cell").nth(6)).toContainText("100,1234");
  await expect(page.getByText("2 documentos", { exact: true })).toBeVisible();
  expect(listReads).toBe(2);
  await page.getByRole("button", { name: "Ver documento", exact: true }).first().click();
  await expect(page.getByLabel("Descripción", { exact: true })).toHaveValue("Recibo del tercero revisado");
  await page.getByRole("button", { name: "Volver a trazabilidad", exact: true }).click();
  await expect(page.getByText("2 documentos", { exact: true })).toBeVisible();
  expect(listReads).toBe(2);
  expect(reads).toBe(1);
  await page.screenshot({ path: testInfo.outputPath("cash-receipt-authoritative-list.png"), fullPage: true });
});
