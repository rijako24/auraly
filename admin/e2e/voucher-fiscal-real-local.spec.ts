import { randomUUID } from "node:crypto";
import { expect, test } from "@playwright/test";
import type { SaveVoucherDraft, VoucherDraft, VoucherFiscalPreview } from "../src/services/api/accounting";
import type { SupplierAcceptance } from "../src/services/api/parties";
import { proposedAccountingDate } from "../src/lib/accounting-document-date";
import { realAccountingAuxiliary, realAccountingWorkspace, realPartyRequest } from "./support/real-accounting-workspace";

test("captura fiscal manual real revisa, guarda y reabre sin crear una deuda ni emitir", async ({ page }) => {
  test.setTimeout(120_000);
  const context = await realAccountingWorkspace(page);
  const { scopedJson } = context;
  const api = "/api/commerce/v1";
  const supplier = await scopedJson<SupplierAcceptance>("POST", `${api}/suppliers`, {
    ...await realPartyRequest(context, `Proveedor soporte ${randomUUID().slice(0, 8)}`),
    purchaseEvidencePolicy: "BuyerElectronicSupportDocument",
  });
  await scopedJson("PUT", `${api}/taxation/counterparty-profiles/${supplier.supplierId}`, {
    businessId: context.businessId, counterpartyId: supplier.supplierId, appliesWithholding: false,
    responsibilities: [], jurisdictionCode: "11001",
  });
  const expense = await realAccountingAuxiliary(context, "5195", "Expense", "General", "Debit", null, true);
  const payable = await realAccountingAuxiliary(context, "2295", "Liability", "Payable", "Credit", null, true);
  const at = proposedAccountingDate();
  const description = `Adquisición E2E ${randomUUID().slice(0, 8)}`;
  const request: SaveVoucherDraft = {
    documentId: randomUUID(), documentType: "ManualAccountingVoucher", occurredAt: at,
    conceptCode: "MANUAL_VOUCHER", description, reference: null, rowVersion: null, adjustment: null,
    mainPartyId: supplier.partyId, payloadVersion: 4, voucherTypeCode: "SupportDocument",
    lines: [{ lineId: randomUUID(), accountId: expense.accountId, partyId: supplier.partyId, costCenterId: null,
      description: "Servicio con soporte", debit: 5000, credit: 0 },
    { lineId: randomUUID(), accountId: payable.accountId, partyId: supplier.partyId, costCenterId: null,
      description: "Obligación del soporte", debit: 0, credit: 5000, dueDate: at }],
  };
  const path = `${api}/accounting/manual/drafts/${request.documentId}`;
  const initial = await scopedJson<VoucherDraft>("PUT", path, request);
  expect(initial.sentAt).toBeNull();
  let profileReads = 0, previews = 0, writes = 0, sends = 0;
  page.on("request", request => {
    const url = new URL(request.url());
    if (url.pathname.endsWith("/tax-profiles")) profileReads++;
    if (url.pathname.endsWith("/accounting/manual/fiscal-preview")) previews++;
    if (url.pathname === path && request.method() === "PUT") writes++;
    if (url.pathname === path + "/send") sends++;
  });
  await page.goto(`/dashboard/financial-traceability?document=${initial.documentId}`);
  await expect(page.getByLabel("Descripción", { exact: true })).toHaveValue(description);
  expect(profileReads).toBe(0);
  await page.getByRole("button", { name: "Datos fiscales del soporte", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Datos fiscales del documento soporte" });
  await dialog.getByRole("combobox", { name: "Partida para base fiscal" }).click();
  await page.getByRole("option", { name: new RegExp(`1\\. ${expense.code}`) }).click();
  await dialog.getByRole("button", { name: "Agregar base fiscal", exact: true }).click();
  await dialog.getByLabel("Base sin IVA 1", { exact: true }).fill("5000");
  const reviewing = page.waitForResponse(response => response.url().includes("/accounting/manual/fiscal-preview"));
  await dialog.getByRole("button", { name: "Revisar cálculo fiscal", exact: true }).click();
  const reviewedResponse = await reviewing;
  expect(reviewedResponse.ok(), await reviewedResponse.text()).toBe(true);
  const reviewed = await reviewedResponse.json() as VoucherFiscalPreview;
  expect(reviewed.canConfirm, reviewed.diagnostics.join(" ")).toBe(true);
  expect(reviewed.withholding).toMatchObject({ grossAmount: 5000, netAmount: 5000, withholdingTotal: 0 });
  await expect(dialog.getByText("Revisión completa", { exact: true })).toBeVisible();
  await dialog.screenshot({ path: "../work/documento-soporte-manual-revision-real.png" });
  await dialog.getByRole("button", { name: "Conservar datos fiscales", exact: true }).click();
  const saving = page.waitForResponse(response => new URL(response.url()).pathname === path && response.request().method() === "PUT");
  await page.getByRole("button", { name: "Guardar borrador", exact: true }).click();
  const savedResponse = await saving;
  expect(savedResponse.ok(), await savedResponse.text()).toBe(true);
  const saved = await savedResponse.json() as VoucherDraft;
  expect(saved.fiscalCapture?.calculationHash).toBe(reviewed.calculationHash);
  expect(saved.sentAt).toBeNull();
  await page.reload();
  await expect(page.getByText("Cálculo fiscal revisado", { exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Guardar comprobante", exact: true })).toBeEnabled();
  expect(previews).toBe(1); expect(writes).toBe(1); expect(sends).toBe(0); expect(profileReads).toBe(1);
  const status = await page.evaluate(async ({ path, tenantId, businessId }) => {
    const response = await fetch(path, { headers: { "X-Tenant-Id": tenantId!, "X-Business-Id": businessId }, credentials: "include" });
    return response.status;
  }, { path: `${api}/accounting/postings/by-document/${initial.documentId}`, tenantId: process.env.AURALY_E2E_TENANT_ID, businessId: context.businessId });
  expect(status).toBe(404);
});
