import { randomUUID } from "node:crypto";
import { expect, test } from "@playwright/test";
import type { AccountingAccount, AccountingEntry, AccountingOpenPositionPage, SaveVoucherDraft, VoucherDraft } from "../src/services/api/accounting";
import { writeFile } from "node:fs/promises";
import { closeOpeningNotificationHint, openingPortfolioContext } from "./support/opening-portfolio-fixture";
import type { PartySiteRoleOptionPage } from "../src/services/api/parties";
import { proposedAccountingDate } from "../src/lib/accounting-document-date";
import { realAccountingAuxiliary, realAccountingContext, realPartyRequest, waitForRealPosting } from "./support/real-accounting-workspace";

for (const supplier of [false, true]) {
  test(`menú real ${supplier ? "proveedor" : "cliente"}: conserva dos abonos y contabiliza una sola vez`, async ({ page }, testInfo) => {
    test.setTimeout(240_000);
    page.setDefaultTimeout(20_000);
    const initial = process.env.AURALY_E2E_DEV_OPENING_FIXTURE ? await openingPortfolioContext(page) : null;
    const context = initial?.context ?? await realAccountingContext(page);
    const { scopedJson } = context, api = "/api/commerce/v1", kind = supplier ? "Payable" : "Receivable";
    let name: string, party: { partyId: string; supplierId?: string; customerId?: string }, roleId: string, cash: AccountingAccount | null;
    if (initial) {
      expect(initial.fixture.Opening).toBeTruthy();
      const prepared = initial.fixture.Opening![kind];
      name = prepared.name; roleId = prepared.roleId;
      party = { partyId: prepared.partyId, ...(supplier ? { supplierId: roleId } : { customerId: roleId }) };
      const accounts = await scopedJson<AccountingAccount[]>("GET", `${api}/accounting/accounts`);
      cash = supplier ? accounts.find(account => account.accountId === initial.fixture.Opening!.cashAccountId)! : null;
    } else {
    name = `QA menú ${supplier ? "proveedor" : "cliente"} ${randomUUID().slice(0, 8)}`;
    party = await scopedJson<{ partyId: string; supplierId?: string; customerId?: string }>("POST",
      `${api}/${supplier ? "suppliers" : "customers"}`, {
        ...await realPartyRequest(context, name),
        ...(supplier ? { purchaseEvidencePolicy: "InternalReceiptVoucher", defaultPaymentDueDays: 0 } : {}),
      });
    roleId = supplier ? party.supplierId! : party.customerId!;
    cash = supplier ? await realAccountingAuxiliary(context, "1109", "Asset", "Cash", "Debit") : null;
    }
    const sites = await scopedJson<PartySiteRoleOptionPage>("GET",
      `${api}/portfolio/parties/site-options?role=${supplier ? "Supplier" : "Customer"}&roleId=${roleId}&page=1&pageSize=1`);
    expect(sites.items).toHaveLength(1);
    if (!initial) {
    const debt = await realAccountingAuxiliary(context, supplier ? "2296" : "1395", supplier ? "Liability" : "Asset",
      kind, supplier ? "Credit" : "Debit", null, true);
    const contra = await realAccountingAuxiliary(context, supplier ? "5196" : "4196", supplier ? "Expense" : "Revenue",
      "General", supplier ? "Debit" : "Credit");
    const openingId = randomUUID(), at = proposedAccountingDate(context.timeZone);
    const opening: SaveVoucherDraft = {
      documentId: openingId, documentType: "ManualAccountingVoucher", payloadVersion: 4, voucherTypeCode: "Accounting",
      occurredAt: at, conceptCode: "MANUAL_VOUCHER", description: "Dos obligaciones QA para el menú de pago",
      reference: null, rowVersion: null, adjustment: null, mainPartyId: party.partyId,
      lines: [1, 2].map(index => ({ accountId: debt.accountId, partyId: party.partyId, costCenterId: null,
        partySiteId: sites.items[0].partySiteId, description: `Factura QA ${index}`, reference: `MENU-${index}`,
        dueDate: at, lineId: randomUUID(), debit: supplier ? 0 : 1000, credit: supplier ? 1000 : 0 })),
    };
    opening.lines.push({ accountId: contra.accountId, partyId: party.partyId, costCenterId: null,
      description: "Contrapartida de prueba", lineId: randomUUID(), debit: supplier ? 2000 : 0, credit: supplier ? 0 : 2000 });
    const savedOpening = await scopedJson<VoucherDraft>("PUT", `${api}/accounting/manual/drafts/${openingId}`, opening);
    await scopedJson("POST", `${api}/accounting/manual/drafts/${openingId}/send`, { rowVersion: savedOpening.rowVersion });
    await waitForRealPosting(context, openingId);
    }
    await scopedJson("POST", `${api}/work-sessions/current`, { businessId: context.businessId, warehouseId: null, deviceId: null });
    const positions = await scopedJson<AccountingOpenPositionPage>("GET",
      `${api}/accounting/open-positions?subledgerKind=${kind}&counterpartyRoleId=${roleId}&page=1&pageSize=10`);
    expect(positions.items).toHaveLength(2);
    await page.goto(`/dashboard/${supplier ? "payables" : "receivables"}`);
    await page.getByRole("button", { name: supplier ? "Pagar proveedores" : "Abono a cartera", exact: true }).click();
    const selection = page.getByRole("dialog", { name: supplier ? "Pago a proveedores" : "Abono a cartera", exact: true });
    await selection.getByRole("combobox").first().click();
    await page.getByPlaceholder("Buscar por nombre o identificación…").fill(name);
    await page.getByRole("option").filter({ hasText: name }).click();
    for (const [index, position] of positions.items.entries())
      await selection.getByLabel(`Abono para ${position.documentNumber}`, { exact: true }).fill(index === 0 ? "125" : "75");
    await selection.getByRole("button", { name: "Ir a pagar", exact: true }).click();
    const loading = page.waitForResponse(response => response.url().endsWith("/accounting/open-positions/selection"));
    await page.getByRole("menuitem", { name: supplier ? "Comprobante de egreso" : "Recibo de caja", exact: true }).click();
    expect((await loading).ok()).toBe(true);
    const capture = page.getByRole("dialog", { name: supplier ? "Comprobante de egreso" : "Recibo de caja", exact: true });
    await expect(capture.getByRole("combobox", { name: "Seleccionar tercero principal", exact: true })).toContainText(name);
    if (supplier) {
      await capture.getByRole("button", { name: "Agregar partida", exact: true }).click();
      const row = capture.getByRole("row", { name: "Partida 3", exact: true });
      await row.getByRole("combobox", { name: "Seleccionar cuenta contable", exact: true }).click();
      await page.getByPlaceholder("Buscar por código o nombre…").fill(cash!.code);
      await page.getByRole("option", { name: new RegExp(`^${cash!.code} · `) }).click();
      await row.getByLabel("Descripción de la partida", { exact: true }).fill("Salida de efectivo para pagar las facturas seleccionadas");
      await row.getByLabel("Crédito de la partida", { exact: true }).fill("200");
    } else {
      await capture.getByRole("combobox", { name: "Seleccionar forma de pago", exact: true }).click();
      await page.getByPlaceholder("Buscar código o nombre…").fill(initial?.fixture.Opening?.cashFormName ?? process.env.AURALY_E2E_CASH_FORM_NAME ?? "Efectivo");
      await page.getByRole("option").first().click();
      await capture.getByLabel("Valor de la nueva forma de pago", { exact: true }).fill("200");
      await capture.getByRole("button", { name: "Agregar forma de pago", exact: true }).click();
    }
    const saving = page.waitForResponse(response => response.request().method() === "PUT" && response.url().includes("/manual/drafts/"));
    await capture.getByRole("button", { name: "Guardar borrador", exact: true }).click();
    const saveResponse = await saving;
    expect(saveResponse.ok(), await saveResponse.text()).toBe(true);
    const draft = await saveResponse.json() as VoucherDraft;
    const selected = draft.lines.filter(line => line.value.existingPosition);
    expect(selected.map(line => line.value.existingPosition!.subledgerId)).toEqual(positions.items.map(item => item.subledgerId));
    expect(selected.map(line => supplier ? line.value.debit : line.value.credit)).toEqual([125, 75]);
    expect(selected.every(line => line.value.partyId === party.partyId && line.value.partySiteId === sites.items[0].partySiteId)).toBe(true);
    await capture.screenshot({ path: testInfo.outputPath("selected-payment-desktop.png") });
    await page.setViewportSize({ width: 390, height: 844 });
    await closeOpeningNotificationHint(page);
    expect(await capture.evaluate(node => node.scrollWidth <= node.clientWidth)).toBe(true);
    await capture.screenshot({ path: testInfo.outputPath("selected-payment-mobile.png") });
    const sending = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith(`/manual/drafts/${draft.documentId}/send`));
    await capture.getByRole("button", { name: "Guardar comprobante", exact: true }).click();
    const sentResponse = await sending;
    expect(sentResponse.ok(), await sentResponse.text()).toBe(true);
    await waitForRealPosting(context, draft.documentId);
    const sent = await scopedJson<VoucherDraft>("GET", `${api}/accounting/manual/drafts/${draft.documentId}`);
    expect(sent.status, sent.row.errorMessage ?? undefined).toBe("Posted");
    const entry = await scopedJson<AccountingEntry>("GET", `${api}/accounting/entries/by-document/${draft.documentId}`);
    expect(entry.debitTotal).toBe(200); expect(entry.creditTotal).toBe(200);
    await scopedJson("POST", `${api}/accounting/manual/drafts/${draft.documentId}/send`, { rowVersion: sent.rowVersion });
    const final = await scopedJson<AccountingOpenPositionPage>("GET",
      `${api}/accounting/open-positions?subledgerKind=${kind}&counterpartyRoleId=${roleId}&page=1&pageSize=10`);
    const expected = positions.items.map((item, index) => Number((item.outstandingAmount - (index === 0 ? 125 : 75)).toFixed(4)));
    expect(positions.items.map(item => final.items.find(value => value.subledgerId === item.subledgerId)?.outstandingAmount)).toEqual(expected);
    const replayEntry = await scopedJson<AccountingEntry>("GET", `${api}/accounting/entries/by-document/${draft.documentId}`);
    expect(replayEntry.entryId).toBe(entry.entryId);
    if (initial) {
      initial.fixture.Payments ??= {};
      initial.fixture.Payments[kind] = { documentId: draft.documentId, outstanding: expected };
      await writeFile(initial.path, JSON.stringify(initial.fixture, null, 2), "utf8");
    }
  });
}
