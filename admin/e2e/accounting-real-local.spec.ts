import { randomUUID } from "node:crypto";
import { readFile } from "node:fs/promises";
import { expect, test, type Page } from "@playwright/test";
import readExcelFile from "read-excel-file/node";
import type { AccountingAccount, AccountingEntry, AccountingOpenPositionPage, SaveVoucherDraft, VoucherDraft } from "../src/services/api/accounting";
import type { CustomerAcceptance, PartyRoleOptionPage, SupplierAcceptance } from "../src/services/api/parties";
import type { CustomerPaymentAcceptance, CustomerPaymentHistoryPage, ReceivableDetail } from "../src/services/api/receivables";
import { proposedAccountingDate } from "../src/lib/accounting-document-date";
import type { SupplierPaymentHistoryPage } from "../src/services/api/payables";
import type { ExpenseDetail, ExpenseOptions } from "../src/services/api/expenses";
import { realAccountingAuxiliary, realAccountingWorkspace, realPartyRequest } from "./support/real-accounting-workspace";
import type { PosWorkSessionClosurePreview as WorkSessionClosurePreview } from "../src/services/pos/pos-edge-client";

async function unapplyRealPayment(page: Page, context: Awaited<ReturnType<typeof realAccountingWorkspace>>,
  kind: "Payable" | "Receivable", documentNumber: string, positionId: string, partyId: string, previousOutstanding: number,
  restoreEntireCross = false) {
  const payable = kind === "Payable";
  const { scopedJson } = context;
  const api = "/api/commerce/v1";
  const advance = await realAccountingAuxiliary(context, payable ? "1394" : "2809",
    payable ? "Asset" : "Liability", payable ? "SupplierAdvance" : "CustomerAdvance", payable ? "Debit" : "Credit", null, true);
  const detail = page.getByRole("dialog", { name: documentNumber, exact: true });
  await detail.getByRole("button", { name: "Desaplicar", exact: true }).click();
  const editor = page.getByRole("dialog", { name: "Desaplicar abono", exact: true });
  const advanceRow = editor.getByRole("row", { name: "Partida 2", exact: true });
  await advanceRow.getByRole("combobox", { name: "Seleccionar cuenta contable", exact: true }).click();
  await page.getByPlaceholder("Buscar por código o nombre…").fill(advance.code);
  await page.getByRole("option", { name: new RegExp(`^${advance.code} · `) }).click();
  await editor.getByRole("row", { name: "Partida 1", exact: true })
    .getByLabel(payable ? "Crédito de la partida" : "Débito de la partida", { exact: true }).fill("50.0001");
  await advanceRow.getByLabel(payable ? "Débito de la partida" : "Crédito de la partida", { exact: true }).fill("50.0001");
  const saving = page.waitForResponse(response => response.request().method() === "PUT" && response.url().includes("/accounting/manual/drafts/"));
  await editor.getByRole("button", { name: "Guardar comprobante", exact: true }).click();
  const saveResponse = await saving;
  expect(saveResponse.ok(), await saveResponse.text()).toBe(true);
  const draft = await saveResponse.json() as VoucherDraft;
  const balanceBeforeSending = await scopedJson<{ outstandingAmount: number }>("GET", `${api}/${payable ? "payables" : "receivables"}/${positionId}`);
  expect(balanceBeforeSending.outstandingAmount).toBe(previousOutstanding);
  const sending = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith(`/manual/drafts/${draft.documentId}/send`));
  await editor.getByRole("button", { name: "Contabilizar", exact: true }).click();
  const sendResponse = await sending;
  expect(sendResponse.ok(), await sendResponse.text()).toBe(true);
  const sent = await sendResponse.json() as VoucherDraft;
  expect(sent.status, sent.row.errorMessage ?? undefined).toBe("Posted");
  const balanceAfterSending = await scopedJson<{ outstandingAmount: number }>("GET", `${api}/${payable ? "payables" : "receivables"}/${positionId}`);
  expect(balanceAfterSending.outstandingAmount).toBe(Number((previousOutstanding + 50.0001).toFixed(4)));
  const credits = await scopedJson<AccountingOpenPositionPage>("GET",
    `${api}/accounting/open-positions?subledgerKind=${payable ? "SupplierCredit" : "CustomerCredit"}&partyId=${partyId}&page=1&pageSize=10`);
  expect(credits.items.filter(item => item.sourceDocumentId === draft.documentId)).toEqual([
    expect.objectContaining({ accountId: advance.accountId, originalAmount: 50.0001, outstandingAmount: 50.0001 }),
  ]);
  const entry = await scopedJson<AccountingEntry>("GET", `${api}/accounting/entries/by-document/${draft.documentId}`);
  expect(entry.debitTotal).toBe(50.0001);
  expect(entry.creditTotal).toBe(50.0001);
  const bounds = await editor.evaluate(node => ({ width: node.clientWidth, content: node.scrollWidth, left: node.scrollLeft }));
  expect(bounds.content).toBeLessThanOrEqual(bounds.width);
  expect(bounds.left).toBe(0);
  await editor.screenshot({ path: `../work/desaplicacion-${kind}-real.png` });
  await editor.getByRole("button", { name: "Cerrar", exact: true }).click();
  // Explicit refresh uses the existing visible invoice-page owner.
  await page.getByRole("button", { name: "Actualizar", exact: true }).click();
  const invoice = page.getByRole("row").filter({ hasText: documentNumber });
  await expect(invoice).toContainText(new Intl.NumberFormat("es-CO", { maximumFractionDigits: 4 })
    .format(balanceAfterSending.outstandingAmount));
  await invoice.click();
  await expect(detail.getByText("Abono desaplicado", { exact: true })).toBeVisible();
  await detail.getByRole("button", { name: "Cerrar", exact: true }).click();

  // Apply that existing advance through the same manual-voucher engine, then
  // restore part of the crossing from the real portfolio view.
  const originalCredit = credits.items.find(item => item.sourceDocumentId === draft.documentId)!;
  const debtAccount = draft.lines.find(line => line.value.existingPosition?.subledgerId === positionId)?.value.accountId;
  if (!debtAccount) throw new Error("La desaplicación guardada debe conservar la cuenta original de la obligación.");
  const groupId = randomUUID();
  const crossId = randomUUID();
  const crossCapture: SaveVoucherDraft = {
    documentId: crossId, documentType: "ManualAccountingVoucher", payloadVersion: 4,
    occurredAt: proposedAccountingDate(), conceptCode: "MANUAL_VOUCHER", voucherTypeCode: "Accounting",
    mainPartyId: partyId, description: "Aplicar el anticipo existente", reference: null, adjustment: null, rowVersion: null,
    lines: [
      { accountId: debtAccount, partyId, costCenterId: null, description: "Cruzar obligación", debit: payable ? 25 : 0,
        credit: payable ? 0 : 25, lineId: randomUUID(), financialGroupId: groupId,
        existingPosition: { subledgerKind: kind, subledgerId: positionId } },
      { accountId: advance.accountId, partyId, costCenterId: null, description: "Usar anticipo", debit: payable ? 0 : 25,
        credit: payable ? 25 : 0, lineId: randomUUID(), financialGroupId: groupId,
        existingPosition: { subledgerKind: payable ? "SupplierCredit" : "CustomerCredit", subledgerId: originalCredit.subledgerId } },
    ],
  };
  const cross = await scopedJson<VoucherDraft>("PUT", `${api}/accounting/manual/drafts/${crossId}`, crossCapture);
  const crossed = await scopedJson<VoucherDraft>("POST", `${api}/accounting/manual/drafts/${crossId}/send`, { rowVersion: cross.rowVersion });
  expect(crossed.status, crossed.row.errorMessage ?? undefined).toBe("Posted");
  await page.getByRole("button", { name: "Actualizar", exact: true }).click();
  await invoice.click();
  await detail.getByRole("button", { name: "Desaplicar cruce", exact: true }).click();
  const restore = page.getByRole("dialog", { name: "Desaplicar cruce", exact: true });
  await expect(restore).toContainText("Restituye el saldo a favor original");
  await expect(restore.getByRole("row", { name: "Partida 2", exact: true })
    .getByRole("combobox", { name: "Seleccionar cuenta contable", exact: true })).toContainText(advance.code);
  const restoreAmount = restoreEntireCross ? 25 : 10.0001;
  await restore.getByRole("row", { name: "Partida 1", exact: true })
    .getByLabel(payable ? "Crédito de la partida" : "Débito de la partida", { exact: true }).fill(String(restoreAmount));
  await restore.getByRole("row", { name: "Partida 2", exact: true })
    .getByLabel(payable ? "Débito de la partida" : "Crédito de la partida", { exact: true }).fill(String(restoreAmount));
  const savingRestore = page.waitForResponse(response => response.request().method() === "PUT" && response.url().includes("/accounting/manual/drafts/"));
  await restore.getByRole("button", { name: "Guardar comprobante", exact: true }).click();
  const restoredDraftResponse = await savingRestore;
  expect(restoredDraftResponse.ok(), await restoredDraftResponse.text()).toBe(true);
  const restoredDraft = await restoredDraftResponse.json() as VoucherDraft;
  const sendingRestore = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith(`/manual/drafts/${restoredDraft.documentId}/send`));
  await restore.getByRole("button", { name: "Contabilizar", exact: true }).click();
  const restoredResponse = await sendingRestore;
  expect(restoredResponse.ok(), await restoredResponse.text()).toBe(true);
  const restored = await restoredResponse.json() as VoucherDraft;
  expect(restored.status, restored.row.errorMessage ?? undefined).toBe("Posted");
  const finalCredits = await scopedJson<AccountingOpenPositionPage>("GET",
    `${api}/accounting/open-positions?subledgerKind=${payable ? "SupplierCredit" : "CustomerCredit"}&partyId=${partyId}&page=1&pageSize=10`);
  expect(finalCredits.items).toEqual([expect.objectContaining({ subledgerId: originalCredit.subledgerId,
    originalAmount: 50.0001, outstandingAmount: restoreEntireCross ? 50.0001 : 35.0002 })]);
  const finalDebt = await scopedJson<{ outstandingAmount: number }>("GET", `${api}/${payable ? "payables" : "receivables"}/${positionId}`);
  expect(finalDebt.outstandingAmount).toBe(Number((previousOutstanding + 25.0001 + restoreAmount).toFixed(4)));
  const restoreEntry = await scopedJson<AccountingEntry>("GET", `${api}/accounting/entries/by-document/${restored.documentId}`);
  expect(restoreEntry.debitTotal).toBe(restoreAmount);
  expect(restoreEntry.creditTotal).toBe(restoreAmount);
  await expect(restore.getByRole("status", { exact: true })).toHaveText("Contabilizado");
  await restore.screenshot({ path: `../work/restitucion-credito-${kind}-real.png` });
  await restore.getByRole("button", { name: "Ver comprobante contabilizado", exact: true }).click();
  const postedRestore = page.getByRole("dialog", { name: `Comprobante contable ${restoreEntry.entryNumber}`, exact: true });
  await expect(postedRestore.getByText("Restituir saldo a favor original", { exact: true })).toBeVisible();
  await postedRestore.getByRole("button", { name: "Cerrar", exact: true }).click();
  await restore.getByRole("button", { name: "Cerrar", exact: true }).click();
  await page.getByRole("button", { name: "Actualizar", exact: true }).click();
  await invoice.click();
  await expect(detail.getByText("Cruce desaplicado · saldo a favor restituido", { exact: true })).toBeVisible();
  const cash = await realAccountingAuxiliary(context, "1109", "Asset", "Cash", "Debit", null, true);
  await detail.getByRole("button", { name: "Desaplicar", exact: true }).click();
  await expect(editor).toContainText("si el dinero fue devuelto");
  await editor.getByRole("row", { name: "Partida 2", exact: true }).getByRole("combobox", { name: "Seleccionar cuenta contable", exact: true }).click();
  await page.getByPlaceholder("Buscar por código o nombre…").fill(cash.code);
  await page.getByRole("option", { name: new RegExp(`^${cash.code} · `) }).click();
  await editor.getByRole("row", { name: "Partida 1", exact: true })
    .getByLabel(payable ? "Crédito de la partida" : "Débito de la partida", { exact: true }).fill("20.0001");
  await editor.getByRole("row", { name: "Partida 2", exact: true })
    .getByLabel(payable ? "Débito de la partida" : "Crédito de la partida", { exact: true }).fill("20.0001");
  const savingReturn = page.waitForResponse(response => response.request().method() === "PUT" && response.url().includes("/accounting/manual/drafts/"));
  await editor.getByRole("button", { name: "Guardar comprobante", exact: true }).click();
  const returnSavedResponse = await savingReturn;
  expect(returnSavedResponse.ok(), await returnSavedResponse.text()).toBe(true);
  const returnDraft = await returnSavedResponse.json() as VoucherDraft;
  const sendingReturn = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith(`/manual/drafts/${returnDraft.documentId}/send`));
  await editor.getByRole("button", { name: "Contabilizar", exact: true }).click();
  const returnSentResponse = await sendingReturn;
  expect(returnSentResponse.ok(), await returnSentResponse.text()).toBe(true);
  const returned = await returnSentResponse.json() as VoucherDraft;
  expect(returned.status, returned.row.errorMessage ?? undefined).toBe("Posted");
  expect(returned).toMatchObject({ financialEffects: { positions: [], payments: [], unapplications: [
    expect.objectContaining({ reversedTransactionType: "PaymentReturn", amount: 20.0001 })] } });
  const returnedEntry = await scopedJson<AccountingEntry>("GET", `${api}/accounting/entries/by-document/${returned.documentId}`);
  expect(returnedEntry.debitTotal).toBe(20.0001);
  expect(returnedEntry.lines.find(line => line.accountId === cash.accountId)).toMatchObject({
    debit: payable ? 20.0001 : 0, credit: payable ? 0 : 20.0001 });
  const debtAfterReturn = await scopedJson<{ outstandingAmount: number }>("GET", `${api}/${payable ? "payables" : "receivables"}/${positionId}`);
  expect(debtAfterReturn.outstandingAmount).toBe(Number((finalDebt.outstandingAmount + 20.0001).toFixed(4)));
  const creditsAfterReturn = await scopedJson<AccountingOpenPositionPage>("GET",
    `${api}/accounting/open-positions?subledgerKind=${payable ? "SupplierCredit" : "CustomerCredit"}&partyId=${partyId}&page=1&pageSize=10`);
  expect(creditsAfterReturn.items).toEqual(finalCredits.items);
  await editor.screenshot({ path: `../work/devolucion-dinero-${kind}-real.png` });
  await editor.getByRole("button", { name: "Cerrar", exact: true }).click();
  await page.getByRole("button", { name: "Actualizar", exact: true }).click();
  await invoice.click();
}

for (const method of ["Cash", "Transfer"] as const) {
test(`tesorería directa ${method} se contabiliza desde la vista sin factura ficticia`, async ({ page }) => {
  test.setTimeout(120_000);
  const context = await realAccountingWorkspace(page);
  const { scopedJson, businessId } = context;
  const path = "/api/commerce/v1/accounting";
  const session = await scopedJson<{ workSessionId: string }>("POST", "/api/commerce/v1/work-sessions/current", { businessId });
  const before = await scopedJson<WorkSessionClosurePreview>("GET", `/api/commerce/v1/work-sessions/${session.workSessionId}/closure-preview`);
  const expense = await realAccountingAuxiliary(context, "5194", "Expense", "General", "Debit");
  let bankId: string | null = null;
  if (method === "Transfer") {
    const types = await scopedJson<{ id: string; code: string }[]>("GET", "/api/commerce/v1/reference-options/bank-account-type");
    const checking = types.find(item => item.code === "Checking");
    expect(checking).toBeTruthy();
    bankId = randomUUID();
    await scopedJson("PUT", `${path}/bank-accounts/${bankId}`, { bankAccountId: bankId,
      accountingAccountId: null, accountTypeOptionId: checking!.id, bankName: "Banco E2E cierre",
      accountNumber: bankId, displayName: "Banco del auxiliar E2E", isPrimary: false, isActive: true, rowVersion: null });
  }
  const cash = await realAccountingAuxiliary(context, "1109", "Asset", method === "Cash" ? "Cash" : "Bank", "Debit", bankId);
  await page.goto("/dashboard/financial-traceability");
  await page.getByRole("button", { name: "Nuevo comprobante", exact: true }).click();
  const supplier = (await scopedJson<PartyRoleOptionPage>("GET",
    "/api/commerce/v1/parties/role-options?role=Supplier&page=1&pageSize=1")).items[0];
  expect(supplier).toBeTruthy();
  await page.getByRole("combobox", { name: "Seleccionar tercero principal", exact: true }).click();
  await page.getByPlaceholder("Buscar por nombre o identificación…").fill(supplier.displayName);
  await page.getByRole("option").filter({ hasText: supplier.displayName }).click();
  await page.getByRole("combobox", { name: "Tipo de comprobante", exact: true }).click();
  await page.getByRole("option", { name: "Egreso", exact: true }).click();
  await page.getByLabel("Descripción", { exact: true }).fill("Gasto directo sin obligación pendiente");
  for (const [index, account] of [expense, cash].entries()) {
    const row = page.getByRole("row", { name: `Partida ${index + 1}`, exact: true });
    await row.getByRole("combobox", { name: "Seleccionar cuenta contable", exact: true }).click();
    await page.getByPlaceholder("Buscar por código o nombre…").fill(account.code);
    await page.getByRole("option", { name: new RegExp(`^${account.code} · `) }).click();
    await row.getByLabel("Descripción de la partida", { exact: true }).fill(index === 0 ? "Gasto directo" : "Efectivo entregado");
    await row.getByLabel(index === 0 ? "Débito de la partida" : "Crédito de la partida", { exact: true }).fill("500");
  }
  const transferReference = `BANCO-${randomUUID()}`;
  if (method === "Transfer") {
    await page.getByRole("button", { name: "Detalles de la partida 2", exact: true }).click();
    const details = page.getByRole("dialog", { name: "Detalles de la partida 2", exact: true });
    await details.getByLabel("Referencia del documento", { exact: true }).fill(transferReference);
    await details.getByRole("button", { name: "Cerrar detalles", exact: true }).click();
  }
  await expect(page.getByRole("button", { name: /Vincular sesión|Retomar sesión|Quitar sesión/ })).toHaveCount(0);
  const saving = page.waitForResponse(response => response.request().method() === "PUT" && response.url().includes("/accounting/manual/drafts/"));
  await page.getByRole("button", { name: "Guardar comprobante", exact: true }).click();
  const saveResponse = await saving;
  expect(saveResponse.ok(), await saveResponse.text()).toBe(true);
  const saved = await saveResponse.json() as VoucherDraft;
  expect(saved.workSessionId).toBeNull();
  const sending = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith(`/manual/drafts/${saved.documentId}/send`));
  await page.getByRole("button", { name: "Contabilizar", exact: true }).click();
  const sendResponse = await sending;
  expect(sendResponse.ok(), await sendResponse.text()).toBe(true);
  const sent = await sendResponse.json();
  expect(sent.status, sent.row.errorMessage ?? undefined).toBe("Posted");
  expect(sent.financialEffects.payments).toEqual([]);
  expect(sent.financialEffects.positions).toEqual([]);
  expect(sent.financialEffects.directMovements).toEqual([expect.objectContaining({ accountId: cash.accountId, amount: -500 })]);
  const after = await scopedJson<WorkSessionClosurePreview>("GET", `/api/commerce/v1/work-sessions/${session.workSessionId}/closure-preview`);
  expect(after.expectedCash).toBe(before.expectedCash);
  expect(after.cashMovements?.filter(item => item.documentId === saved.documentId)).toHaveLength(0);
  expect(after.paymentTotals.find(item => item.paymentMethodCode === method)!.netAmount)
    .toBe(before.paymentTotals.find(item => item.paymentMethodCode === method)!.netAmount);
  const entry = await scopedJson<AccountingEntry>("GET", `${path}/entries/by-document/${saved.documentId}`);
  expect(entry.debitTotal).toBe(500);
  expect(entry.creditTotal).toBe(500);
  const bounds = await page.evaluate(() => ({ viewport: document.documentElement.clientWidth, content: document.documentElement.scrollWidth }));
  expect(bounds.content, "El auxiliar largo debe desplazarse dentro de su tabla, sin ensanchar toda la página.").toBeLessThanOrEqual(bounds.viewport);
  await page.screenshot({ path: `../work/tesoreria-directa-${method}-real.png`, fullPage: true });
});
}


test("asiento libre real conserva terceros y bases al guardar, contabilizar y clonar", async ({ page }) => {
  test.setTimeout(120_000);
  const context = await realAccountingWorkspace(page);
  const { scopedJson } = context;
  const debitAccount = await realAccountingAuxiliary(context, "5195", "Expense", "General", "Debit");
  const creditAccount = await realAccountingAuxiliary(context, "3115", "Equity", "General", "Credit");
  const base = "/api/commerce/v1/accounting";
  const parties = (await scopedJson<PartyRoleOptionPage>("GET", "/api/commerce/v1/parties/role-options?role=Any&page=1&pageSize=2")).items;
  expect(parties.length, "La base de prueba necesita dos terceros activos.").toBe(2);

  await page.goto("/dashboard/financial-traceability");
  await page.getByRole("button", { name: "Nuevo comprobante", exact: true }).click();
  await page.getByRole("combobox", { name: "Seleccionar tercero principal", exact: true }).click();
  await page.getByRole("option").filter({ hasText: parties[0].displayName }).click();
  const reference = `REAL-${randomUUID()}`;
  await page.getByLabel("Descripción", { exact: true }).fill("Reclasificación verificada desde la vista");
  const first = page.getByRole("row", { name: "Partida 1", exact: true });
  await page.getByRole("button", { name: "Detalles de la partida 1", exact: true }).click();
  const firstDetails = page.getByRole("dialog", { name: "Detalles de la partida 1", exact: true });
  await firstDetails.getByLabel("Referencia del documento", { exact: true }).fill(reference);
  await firstDetails.getByRole("button", { name: "Cerrar detalles", exact: true }).click();
  await first.getByRole("combobox", { name: "Seleccionar cuenta contable" }).click();
  await page.getByPlaceholder("Buscar por código o nombre…").fill(debitAccount.code);
  await page.getByRole("option", { name: new RegExp(`^${debitAccount.code} · `) }).click();
  await first.getByLabel("Descripción de la partida", { exact: true }).fill("Partida deudora real");
  await first.getByLabel("Base de la partida", { exact: true }).fill("-12.3456");
  await first.getByLabel("Débito de la partida", { exact: true }).fill("1200.1234");
  const second = page.getByRole("row", { name: "Partida 2", exact: true });
  await second.getByRole("combobox", { name: "Seleccionar tercero", exact: true }).click();
  await page.getByRole("option").filter({ hasText: parties[1].displayName }).click();
  await second.getByRole("combobox", { name: "Seleccionar cuenta contable" }).click();
  await page.getByPlaceholder("Buscar por código o nombre…").fill(creditAccount.code);
  await page.getByRole("option", { name: new RegExp(`^${creditAccount.code} · `) }).click();
  await second.getByLabel("Descripción de la partida", { exact: true }).fill("Contrapartida real");
  await second.getByLabel("Base de la partida", { exact: true }).fill("0");
  await second.getByLabel("Crédito de la partida", { exact: true }).fill("1200.1234");
  const saving = page.waitForResponse(response => response.request().method() === "PUT" && response.url().includes("/accounting/manual/drafts/"));
  await page.getByRole("button", { name: "Guardar comprobante", exact: true }).click();
  const saveResponse = await saving;
  expect(saveResponse.ok(), await saveResponse.text()).toBe(true);
  const saved = await saveResponse.json() as VoucherDraft;
  expect(saved.status).toBe("Created");
  expect(saved.sentAt).toBeNull();
  expect(saved.row.entryId).toBeNull();
  expect(saved.mainPartyId).toBe(parties[0].partyId);
  expect(saved.lines[0].value.reference).toBe(reference);
  expect(saved.lines.map(line => line.value.partyId)).toEqual(parties.map(party => party.partyId));
  expect(saved.lines.map(line => line.value.baseAmount)).toEqual([-12.3456, 0]);
  await expect(page).toHaveURL(new RegExp(`document=${saved.documentId}`));
  await page.reload();
  await expect(first.getByLabel("Base de la partida", { exact: true })).toHaveValue("-12.3456");
  await expect(second.getByLabel("Base de la partida", { exact: true })).toHaveValue("0");
  const sending = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith(`/manual/drafts/${saved.documentId}/send`));
  await page.getByRole("button", { name: "Contabilizar", exact: true }).click();
  const sendResponse = await sending;
  expect(sendResponse.ok(), await sendResponse.text()).toBe(true);
  const sent = await sendResponse.json() as VoucherDraft;
  expect(sent.status, sent.row.errorMessage ?? undefined).toBe("Posted");
  expect(sent.row.entryId).toBeTruthy();
  await expect(first.getByLabel("Base de la partida", { exact: true })).toBeDisabled();
  const entry = await scopedJson<AccountingEntry>("GET", `${base}/entries/by-document/${saved.documentId}`);
  expect(entry.mainPartyId).toBe(saved.mainPartyId);
  expect(entry.debitTotal).toBe(1200.1234);
  expect(entry.creditTotal).toBe(1200.1234);
  expect(entry.lines.map(line => line.partyId)).toEqual(parties.map(party => party.partyId));
  expect(entry.lines.map(line => line.baseAmount)).toEqual([-12.3456, 0]);
  const replay = await scopedJson<VoucherDraft>("POST", `${base}/manual/drafts/${saved.documentId}/send`, { rowVersion: saved.rowVersion });
  expect(replay.row.entryId).toBe(entry.entryId);
  const fiscalReads: string[] = [];
  page.on("request", request => {
    if (request.method() === "GET" && request.url().includes(`/fiscal/documents/${saved.documentId}`)) fiscalReads.push(request.url());
  });
  await page.getByRole("button", { name: "Ver comprobante contabilizado", exact: true }).click();
  const posted = page.getByRole("dialog").filter({ hasText: `Comprobante contable ${entry.entryNumber}` });
  await expect(posted).toBeVisible();
  await expect(posted.getByRole("row").filter({ hasText: "Partida deudora real" })).toContainText(parties[0].displayName);
  await expect(posted.getByRole("row").filter({ hasText: "Partida deudora real" }).getByRole("cell").nth(4)).toContainText("1.200,1234");
  await expect(posted.getByRole("row").filter({ hasText: "Contrapartida real" })).toContainText(parties[1].displayName);
  await posted.screenshot({ path: "../work/comprobante-real-contabilizado.png" });
  await posted.getByRole("button", { name: "Abrir reporte", exact: true }).click();
  const pdfDownload = page.waitForEvent("download");
  await page.getByRole("button", { name: "Descargar PDF", exact: true }).click();
  const pdf = await pdfDownload;
  expect((await readFile((await pdf.path())!)).subarray(0, 5).toString()).toBe("%PDF-");
  const excelDownload = page.waitForEvent("download");
  await page.getByRole("button", { name: "Exportar Excel", exact: true }).click();
  const excel = await excelDownload;
  expect((await readFile((await excel.path())!)).subarray(0, 4)).toEqual(Buffer.from([0x50, 0x4b, 0x03, 0x04]));
  const workbook = await readExcelFile((await excel.path())!);
  const cells = workbook.flatMap(sheet => sheet.data.flat());
  expect(cells).toContain(debitAccount.code);
  expect(cells).toContain(parties[0].displayName);
  expect(cells).toContain("Partida deudora real");
  expect(cells).toContain(1200.1234);
  await page.getByRole("button", { name: "Cerrar", exact: true }).click();
  await posted.getByRole("button", { name: "Cerrar", exact: true }).click();
  expect(fiscalReads).toHaveLength(0);
  const cloning = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith(`/accounting/documents/${saved.documentId}/clone`));
  await page.getByRole("button", { name: "Clonar comprobante", exact: true }).click();
  const cloneResponse = await cloning;
  expect(cloneResponse.ok(), await cloneResponse.text()).toBe(true);
  const clone = await cloneResponse.json() as VoucherDraft;
  expect(clone.documentId).not.toBe(saved.documentId);
  expect(clone.clonedFromDocumentId).toBe(saved.documentId);
  expect(clone.status).toBe("Created");
  expect(clone.sentAt).toBeNull();
  expect(clone.row.entryId).toBeNull();
  expect(clone.lines.map(line => line.value.baseAmount)).toEqual([-12.3456, 0]);
  const originalLines = new Set(saved.lines.map(line => line.value.lineId));
  expect(clone.lines.every(line => !originalLines.has(line.value.lineId))).toBe(true);
  await expect(page).toHaveURL(new RegExp(`document=${clone.documentId}`));
  await expect(page.getByLabel("Referencia", { exact: true })).toHaveCount(0);
  await expect(first.getByLabel("Base de la partida", { exact: true })).toHaveValue("-12.3456");
  await page.goto("/dashboard/accounting");
  await page.getByRole("button", { name: "Balance de prueba", exact: true }).click();
  await page.getByRole("button", { name: new RegExp(`^${debitAccount.code} · `) }).click();
  const auxiliary = page.getByRole("dialog", { name: new RegExp(`^${debitAccount.code} · `) });
  const movement = auxiliary.getByRole("row").filter({ hasText: entry.entryNumber });
  await expect(movement).toContainText("Partida deudora real");
  await expect(movement).toContainText(parties[0].displayName);
  await expect(movement.getByRole("cell").nth(7)).toContainText("12,3456");
  await expect(movement.getByRole("cell").nth(7)).toContainText("-");
  await expect(movement.getByRole("cell").nth(8)).toContainText("1.200,1234");
  const dialogBounds = (await auxiliary.boundingBox())!;
  const amountBounds = (await movement.getByRole("cell").nth(10).boundingBox())!;
  expect(amountBounds.x + amountBounds.width).toBeLessThanOrEqual(dialogBounds.x + dialogBounds.width);
  await auxiliary.getByRole("combobox", { name: "Filtrar auxiliar por tercero", exact: true }).click();
  await page.getByRole("option").filter({ hasText: parties[0].displayName }).click();
  await expect(movement).toBeVisible();
  await auxiliary.screenshot({ path: "../work/libro-auxiliar-real.png" });
  const reportReads: string[] = [];
  page.on("request", request => {
    if (request.method() === "GET" && request.url().includes("/accounting/reports/account-movements")) reportReads.push(request.url());
  });
  const fullResponse = page.waitForResponse(response => response.request().method() === "GET"
    && new URL(response.url()).pathname.endsWith("/accounting/reports/account-movements/print"));
  await auxiliary.getByRole("button", { name: "Imprimir / exportar libro completo", exact: true }).click();
  const full = await fullResponse;
  expect(full.ok(), await full.text()).toBe(true);
  const fullBook = await full.json() as import("../src/services/api/accounting").AccountMovementPage;
  expect(fullBook.totalCount).toBe(fullBook.items.length);
  expect(fullBook.items).toEqual(expect.arrayContaining([expect.objectContaining({
    sourceDocumentId: saved.documentId, partyId: parties[0].partyId, baseAmount: -12.3456, debit: 1200.1234,
  })]));
  const book = page.getByRole("dialog", { name: "Libro auxiliar para imprimir y exportar", exact: true });
  const reportedMovement = book.getByRole("row").filter({
    has: page.getByRole("cell", { name: entry.entryNumber, exact: true }),
  });
  await expect(reportedMovement.getByRole("cell", { name: "Partida deudora real", exact: true })).toBeVisible();
  await expect(reportedMovement).toContainText(parties[0].displayName);
  await expect(book.getByPlaceholder("Buscar dentro del reporte")).toHaveCount(0);
  for (const [button, extension] of [["Descargar PDF", "pdf"], ["Exportar Excel", "xlsx"]] as const) {
    const event = page.waitForEvent("download");
    await book.getByRole("button", { name: button, exact: true }).click();
    const download = await event;
    await download.saveAs(`../work/libro-auxiliar-real.${extension}`);
    if (extension === "xlsx") {
      const sheets = await readExcelFile((await download.path())!);
      const values = sheets.flatMap(sheet => sheet.data.flat());
      expect(values).toContain(-12.3456);
      expect(values).toContain(1200.1234);
      expect(values).toContain(parties[0].displayName);
    }
  }
  expect(reportReads).toHaveLength(1);
});

test("abono real de cartera conserva la obligación, el recaudo y un solo asiento al reintentar", async ({ page }) => {
  test.setTimeout(120_000);
  const context = await realAccountingWorkspace(page);
  const { scopedJson, businessId } = context;
  const api = "/api/commerce/v1";
  const name = `Cliente cartera E2E ${randomUUID().slice(0, 8)}`;
  const customer = await scopedJson<CustomerAcceptance>("POST", `${api}/customers`, await realPartyRequest(context, name));
  const counterpart = (await scopedJson<AccountingAccount[]>("GET", `${api}/accounting/accounts`))
    .find(item => item.isActive && item.allowsPosting && !item.requiresParty && item.accountType === "Equity");
  expect(counterpart).toBeTruthy();
  const receivableId = randomUUID();
  const documentNumber = `CXC-E2E-${randomUUID()}`;
  const date = proposedAccountingDate();
  await scopedJson("POST", `${api}/receivables/preexisting/import`, { businessId, items: [{
    receivableId, customerId: customer.customerId, customerIdentification: null, partySiteId: null,
    documentNumber, issuedAt: date, dueDate: date, amount: 1000.5001,
    counterpartAccountId: counterpart!.accountId, notes: "Apertura aislada para regresión de recaudo",
  }] });
  const before = await scopedJson<ReceivableDetail>("GET", `${api}/receivables/${receivableId}`);
  expect(before).toMatchObject({ receivableId, originalAmount: 1000.5001, outstandingAmount: 1000.5001, status: "Open" });
  await page.goto("/dashboard/receivables");
  await page.locator("summary").filter({ hasText: "Filtros" }).click();
  await page.getByRole("combobox", { name: "Filtrar cliente y sede", exact: true }).click();
  await page.getByPlaceholder("Buscar por nombre o identificación…").fill(name);
  await page.getByRole("option").filter({ hasText: name }).click();
  await page.getByRole("button", { name: "Abono a cartera", exact: true }).click();
  const wizard = page.getByRole("dialog", { name: "Abono a cartera", exact: true });
  await wizard.getByRole("combobox", { name: "Seleccionar customer", exact: true }).click();
  await page.getByPlaceholder("Buscar por nombre o identificación…").fill(name);
  await page.getByRole("option").filter({ hasText: name }).click();
  await expect(wizard.getByText(/^Saldo /)).toContainText("1.000,5001");
  await wizard.getByLabel(`Abono para ${documentNumber}`, { exact: true }).fill("100.25");
  await expect(wizard.getByText("Total seleccionado", { exact: true }).locator("..")).toContainText("100,25");
  await wizard.getByRole("button", { name: "Ir a pagar", exact: true }).click();
  const checkout = page.getByRole("dialog", { name: "Abono a cartera", exact: true });
  await expect(checkout).toContainText("100,25");
  const paying = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith("/receivable-payments/confirm"));
  await checkout.getByRole("button", { name: "Sin imprimir", exact: true }).click();
  const confirmed = await paying;
  expect(confirmed.ok(), await confirmed.text()).toBe(true);
  const payment = await confirmed.json() as CustomerPaymentAcceptance;
  expect(payment.status).toBe("Accepted");
  await expect(checkout).toBeHidden();
  const after = await scopedJson<ReceivableDetail>("GET", `${api}/receivables/${receivableId}`);
  expect(after).toMatchObject({ receivableId, sourceDocumentId: before.sourceDocumentId, originalAmount: 1000.5001,
    outstandingAmount: 900.2501, status: "PartiallyPaid" });
  const entry = await scopedJson<AccountingEntry>("GET", `${api}/accounting/entries/by-document/${payment.paymentId}`);
  expect(entry.debitTotal).toBe(100.25);
  expect(entry.creditTotal).toBe(100.25);
  expect(entry.lines.some(item => item.partyId === customer.partyId)).toBe(true);
  const replay = await scopedJson<CustomerPaymentAcceptance>("POST", `${api}/receivable-payments/confirm`,
    confirmed.request().postDataJSON(), { "Idempotency-Key": confirmed.request().headers()["idempotency-key"] });
  expect(replay).toMatchObject({ paymentId: payment.paymentId, idempotentReplay: true });
  const history = await scopedJson<CustomerPaymentHistoryPage>("GET", `${api}/customers/${customer.customerId}/payments?page=1&pageSize=25`);
  expect(history.items).toHaveLength(1);
  expect(history.items[0]).toMatchObject({ paymentId: payment.paymentId, status: "Processed", totalAmount: 100.25,
    applications: [{ receivableId, amount: 100.25 }], payments: [{ methodCode: "Cash", amount: 100.25 }] });
  const final = await scopedJson<ReceivableDetail>("GET", `${api}/receivables/${receivableId}`);
  expect(final.outstandingAmount).toBe(after.outstandingAmount);
  expect(final.transactions).toHaveLength(before.transactions.length + 1);
  await expect(page.getByRole("row").filter({ hasText: name })).toContainText("900,2501");
  await page.getByRole("tab", { name: "Facturas", exact: true }).click();
  const invoiceRow = page.getByRole("row").filter({ hasText: documentNumber });
  await expect(invoiceRow).toContainText("900,2501");
  await invoiceRow.click();
  const detail = page.getByRole("dialog", { name: documentNumber, exact: true });
  await expect(detail).toContainText("900,2501");
  await expect(detail).toContainText("100,25");
  await detail.getByRole("button", { name: "Cerrar", exact: true }).click();
  await page.getByRole("button", { name: "Abono a cartera", exact: true }).click();
  await wizard.getByRole("combobox", { name: "Seleccionar customer", exact: true }).click();
  await page.getByPlaceholder("Buscar por nombre o identificación…").fill(name);
  await page.getByRole("option").filter({ hasText: name }).click();
  await expect(wizard.getByText(documentNumber, { exact: true })).toBeVisible();
  await expect(wizard.getByText(/^Saldo /)).toContainText("900,2501");
  await wizard.screenshot({ path: "../work/cartera-real-abono.png" });
  await wizard.getByRole("button", { name: "Cancelar", exact: true }).click();
  await invoiceRow.click();
  await unapplyRealPayment(page, context, "Receivable", documentNumber, receivableId, customer.partyId, 900.2501);
  expect((await scopedJson<CustomerPaymentHistoryPage>("GET", `${api}/customers/${customer.customerId}/payments?page=1&pageSize=25`)).items).toEqual(history.items);
});


test("un gasto pendiente aparece debajo del tercero en el comprobante manual", async ({ page }) => {
  test.setTimeout(120_000);
  const context = await realAccountingWorkspace(page);
  const { scopedJson, businessId } = context;
  const api = "/api/commerce/v1";
  const name = `Proveedor prueba comprobante ${randomUUID().slice(0, 8)}`;
  const supplier = await scopedJson<SupplierAcceptance>("POST", `${api}/suppliers`, {
    ...await realPartyRequest(context, name),
    purchaseEvidencePolicy: "InternalReceiptVoucher", defaultPaymentDueDays: 0,
  });
  await scopedJson("PUT", `${api}/taxation/counterparty-profiles/${supplier.supplierId}`, {
    businessId, counterpartyId: supplier.supplierId, appliesWithholding: false, responsibilities: [], jurisdictionCode: "11001",
  });
  const options = await scopedJson<ExpenseOptions>("GET", `${api}/expenses/options?includeDirectories=false`);
  const concept = options.concepts.find(item => item.isActive)!;
  expect(concept).toBeTruthy();
  await page.goto("/dashboard/expenses");
  await page.getByRole("button", { name: "Nuevo gasto", exact: true }).click();
  const form = page.getByRole("dialog", { name: "Registrar gasto", exact: true });
  await form.getByRole("combobox", { name: "Seleccionar proveedor y sede" }).click();
  await page.getByPlaceholder("Buscar por nombre o identificación…").fill(name);
  await page.getByRole("option").filter({ hasText: name }).click();
  await form.getByLabel("Referencia (opcional)").fill(`PRUEBA-COMP-${randomUUID().slice(0, 8)}`);
  await form.getByRole("button", { name: "Agregar gasto", exact: true }).click();
  const line = page.getByRole("dialog", { name: "Agregar gasto", exact: true });
  await line.getByLabel("Concepto de gasto").click();
  await page.getByRole("option", { name: concept.name, exact: true }).click();
  await line.getByLabel("Base antes de IVA").fill("1000.50");
  await line.getByLabel("Base antes de IVA").blur();
  await line.getByRole("button", { name: "Agregar a la grilla", exact: true }).click();
  const confirming = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith("/expenses/confirm"));
  await form.getByRole("button", { name: "Confirmar gasto", exact: true }).click();
  const confirmed = await confirming;
  expect(confirmed.ok(), await confirmed.text()).toBe(true);
  const { expenseId } = await confirmed.json() as { expenseId: string };
  const expense = await scopedJson<ExpenseDetail>("GET", `${api}/expenses/${expenseId}`);
  expect(expense.payable?.outstandingAmount).toBe(1000.5);
  await page.goto("/dashboard/financial-traceability");
  await page.getByRole("button", { name: "Nuevo comprobante", exact: true }).click();
  await page.getByRole("combobox", { name: "Seleccionar tercero principal" }).click();
  await page.getByPlaceholder("Buscar por nombre o identificación…").fill(name);
  await page.getByRole("option").filter({ hasText: name }).click();
  await page.getByRole("button", { name: "Ver documentos", exact: true }).click();
  const grid = page.getByRole("region", { name: "Documentos pendientes del tercero" });
  const row = grid.getByRole("row", { name: `Documento ${expense.documentNumber}` });
  await expect(row).toContainText(expense.documentNumber);
  await expect(row).toContainText(name);
  await expect(row).toContainText("1.000,5");
  const accountCode = (await row.locator("td").nth(1).innerText()).split(" · ")[0];
  await row.getByLabel(`Abono a ${expense.documentNumber}`).fill("300");
  await row.getByRole("button", { name: "Agregar" }).click();
  await expect(page.getByRole("row", { name: "Partida 1", exact: true })).toContainText(accountCode);
  console.log(`PROVEEDOR_DEMO=${name} GASTO_DEMO=${expense.documentNumber}`);
});

test("gasto real usa cuenta automática y su abono actualiza la misma cuenta por pagar", async ({ page }) => {
  test.setTimeout(120_000);
  const context = await realAccountingWorkspace(page);
  const { scopedJson, businessId } = context;
  const api = "/api/commerce/v1";
  const name = `Proveedor E2E ${randomUUID().slice(0, 8)}`;
  const supplier = await scopedJson<SupplierAcceptance>("POST", `${api}/suppliers`, {
    ...await realPartyRequest(context, name),
    purchaseEvidencePolicy: "InternalReceiptVoucher", defaultPaymentDueDays: 0,
  });
  await scopedJson("PUT", `${api}/taxation/counterparty-profiles/${supplier.supplierId}`, {
    businessId, counterpartyId: supplier.supplierId, appliesWithholding: false, responsibilities: [], jurisdictionCode: "11001",
  });
  const options = await scopedJson<ExpenseOptions>("GET", `${api}/expenses/options?includeDirectories=false`);
  const concept = options.concepts.find(item => item.isActive)!;
  expect(concept, "La configuración oficial debe ofrecer un concepto de gasto activo.").toBeTruthy();
  await page.goto("/dashboard/expenses");
  await page.getByRole("button", { name: "Nuevo gasto", exact: true }).click();
  const form = page.getByRole("dialog", { name: "Registrar gasto", exact: true });
  await form.getByRole("combobox", { name: "Seleccionar proveedor y sede" }).click();
  await page.getByPlaceholder("Buscar por nombre o identificación…").fill(name);
  await page.getByRole("option").filter({ hasText: name }).click();
  await form.getByLabel("Referencia (opcional)").fill(`E2E-${randomUUID()}`);
  await form.getByRole("button", { name: "Agregar gasto", exact: true }).click();
  const line = page.getByRole("dialog", { name: "Agregar gasto", exact: true });
  await expect(line.getByRole("combobox", { name: "Seleccionar cuenta contable" })).toHaveCount(0);
  await line.getByLabel("Concepto de gasto").click();
  await page.getByRole("option", { name: concept.name, exact: true }).click();
  await line.getByLabel("Base antes de IVA").fill("1000.50");
  await line.getByLabel("Base antes de IVA").blur();
  await line.getByRole("button", { name: "Agregar a la grilla", exact: true }).click();
  const confirming = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith("/expenses/confirm"));
  await form.getByRole("button", { name: "Confirmar gasto", exact: true }).click();
  const confirmed = await confirming;
  expect(confirmed.ok(), await confirmed.text()).toBe(true);
  const { expenseId } = await confirmed.json() as { expenseId: string };
  const expense = await scopedJson<ExpenseDetail>("GET", `${api}/expenses/${expenseId}`);
  expect(expense.status).toBe("Processed");
  expect(expense.lines?.[0].expenseAccountId).toBe(concept.expenseAccountId);
  expect(expense.netPayable).toBe(1000.5);
  expect(expense.payable?.outstandingAmount).toBe(1000.5);
  await page.getByRole("button", { name: `Ver gasto ${expense.documentNumber}`, exact: true }).click();
  await page.getByRole("button", { name: "Pagar", exact: true }).click();
  await page.getByLabel(`Abono para ${expense.documentNumber}`, { exact: true }).fill("100.25");
  await page.getByRole("button", { name: "Ir a pagar", exact: true }).click();
  const checkout = page.getByRole("dialog", { name: "Pago a proveedores", exact: true });
  await expect(checkout.getByRole("combobox", { name: "Medio de pago" })).toContainText("Efectivo");
  const paying = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith("/payable-payments/confirm"));
  await checkout.getByRole("button", { name: "Sin imprimir", exact: true }).click();
  const paymentResponse = await paying;
  expect(paymentResponse.ok(), await paymentResponse.text()).toBe(true);
  const payment = await paymentResponse.json() as { status: string; paymentId: string; documentNumber: string };
  expect(payment.status).toBe("Accepted");
  await expect(page.getByText(`${payment.documentNumber} recibido y pendiente de aplicar. El abono aún no está confirmado.`, { exact: true })).toBeVisible();
  const after = await scopedJson<ExpenseDetail>("GET", `${api}/expenses/${expenseId}`);
  expect(after.payable?.payableId).toBe(expense.payable?.payableId);
  expect(after.payable?.outstandingAmount).toBe(900.25);
  const paymentEntry = await scopedJson<AccountingEntry>("GET", `${api}/accounting/entries/by-document/${payment.paymentId}`);
  expect(paymentEntry.debitTotal).toBe(100.25);
  expect(paymentEntry.creditTotal).toBe(100.25);
  expect(paymentEntry.lines.some(item => item.partyId === supplier.partyId)).toBe(true);
  const paymentKey = paymentResponse.request().headers()["idempotency-key"];
  expect(paymentKey).toBeTruthy();
  const replay = await scopedJson<{ paymentId: string; idempotentReplay: boolean }>("POST", `${api}/payable-payments/confirm`,
    paymentResponse.request().postDataJSON(), { "Idempotency-Key": paymentKey });
  expect(replay).toMatchObject({ paymentId: payment.paymentId, idempotentReplay: true });
  const history = await scopedJson<SupplierPaymentHistoryPage>("GET", `${api}/suppliers/${supplier.supplierId}/payments?page=1&pageSize=25`);
  expect(history.items).toHaveLength(1);
  expect(history.items[0]).toMatchObject({ paymentId: payment.paymentId, status: "Processed", totalAmount: 100.25,
    applications: [{ payableId: expense.payable!.payableId, amount: 100.25 }],
    payments: [{ methodCode: "Cash", amount: 100.25 }] });
  let detailReads = 0;
  const detailPath = `${api}/expenses/${expenseId}`;
  page.on("request", request => {
    if (request.method() === "GET" && new URL(request.url()).pathname === detailPath) detailReads++;
  });
  const reopening = page.waitForResponse(response => response.request().method() === "GET" && response.url().endsWith(`/expenses/${expenseId}`));
  await page.getByRole("button", { name: `Ver gasto ${expense.documentNumber}`, exact: true }).click();
  expect((await reopening).ok()).toBe(true);
  const balance = page.getByRole("heading", { name: "Cuenta por pagar", exact: true }).locator("..");
  await expect(balance).toContainText("900,25");
  await expect(balance).toContainText("Pago parcial");
  expect(detailReads).toBe(1);
  await page.screenshot({ path: "../work/gasto-real-abono.png", fullPage: true });
  const detailDialog = page.getByRole("dialog", { name: expense.documentNumber, exact: true });
  await detailDialog.getByRole("button", { name: "Cerrar", exact: true }).click();
  // Only the read-failure stage is simulated. Creation, payment, replay, ledger
  // and history above use the real API and SQL financial engine.
  await page.route(`**${detailPath}`, route => route.fulfill({ status: 503, contentType: "application/json", body: JSON.stringify({ detail: "Fallo de lectura de prueba" }) }));
  await page.getByRole("button", { name: `Ver gasto ${expense.documentNumber}`, exact: true }).click();
  await expect(detailDialog.getByRole("alert")).toContainText("No fue posible consultar el gasto");
  await expect(detailDialog.getByRole("button", { name: "Pagar", exact: true })).toHaveCount(0);
  expect(detailReads).toBe(3); // The existing query policy has one bounded retry.
  await page.unroute(`**${detailPath}`);
  await detailDialog.getByRole("button", { name: "Reintentar", exact: true }).click();
  await expect(balance).toContainText("900,25");
  expect(detailReads).toBe(4);
  await detailDialog.getByRole("button", { name: "Cerrar", exact: true }).click();
  await page.goto("/dashboard/payables");
  await page.locator("summary").filter({ hasText: "Filtros" }).click();
  await page.getByRole("combobox", { name: "Filtrar proveedor y sede", exact: true }).click();
  await page.getByPlaceholder("Buscar por nombre o identificación…", { exact: true }).fill(name);
  await page.getByRole("option").filter({ hasText: name }).click();
  await page.getByRole("tab", { name: "Facturas", exact: true }).click();
  await page.getByRole("row").filter({ hasText: expense.documentNumber }).click();
  await unapplyRealPayment(page, context, "Payable", expense.documentNumber, expense.payable!.payableId, supplier.partyId, 900.25, true);
  expect((await scopedJson<SupplierPaymentHistoryPage>("GET", `${api}/suppliers/${supplier.supplierId}/payments?page=1&pageSize=25`)).items).toEqual(history.items);
  const beforeCancellation = await scopedJson<AccountingOpenPositionPage>("GET",
    `${api}/accounting/open-positions?subledgerKind=SupplierCredit&partyId=${supplier.partyId}&page=1&pageSize=10`);
  const originalAdvance = expect.objectContaining({ originalAmount: 50.0001, outstandingAmount: 50.0001 });
  expect(beforeCancellation.items).toEqual([originalAdvance]);
  await page.getByRole("dialog", { name: expense.documentNumber, exact: true }).getByRole("button", { name: "Cerrar", exact: true }).click();
  await page.goto("/dashboard/expenses");
  await page.getByRole("button", { name: `Ver gasto ${expense.documentNumber}`, exact: true }).click();
  await page.getByRole("button", { name: "Anular gasto", exact: true }).click();
  const cancel = page.getByRole("dialog", { name: "Anular gasto", exact: true });
  await cancel.getByRole("combobox").click();
  await page.getByRole("option").first().click();
  const cancelling = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith(`/expenses/${expenseId}/cancel`));
  await cancel.getByRole("button", { name: "Confirmar anulación", exact: true }).click();
  const cancelledResponse = await cancelling;
  expect(cancelledResponse.ok(), await cancelledResponse.text()).toBe(true);
  const cancelled = await scopedJson<ExpenseDetail>("GET", `${api}/expenses/${expenseId}`);
  expect(cancelled.status).toBe("Cancelled");
  expect(cancelled.payable?.outstandingAmount).toBe(0);
  await expect(detailDialog.getByRole("button", { name: "Anular gasto", exact: true })).toHaveCount(0);
  await expect(detailDialog.getByRole("button", { name: "Ver reversión", exact: true })).toBeVisible();
  const afterCancellation = await scopedJson<AccountingOpenPositionPage>("GET",
    `${api}/accounting/open-positions?subledgerKind=SupplierCredit&partyId=${supplier.partyId}&page=1&pageSize=10`);
  expect(afterCancellation.items).toHaveLength(2);
  expect(afterCancellation.items.find(item => item.subledgerId === beforeCancellation.items[0].subledgerId)).toEqual(beforeCancellation.items[0]);
  expect(afterCancellation.items.find(item => item.sourceDocumentId === cancelled.cancellationId)).toMatchObject({
    creditNature: "Credit", originalAmount: 30.2498, outstandingAmount: 30.2498,
  });
  expect((await scopedJson<SupplierPaymentHistoryPage>("GET", `${api}/suppliers/${supplier.supplierId}/payments?page=1&pageSize=25`)).items).toEqual(history.items);
  await detailDialog.screenshot({ path: "../work/gasto-anulado-tras-desaplicacion-real.png" });
});
