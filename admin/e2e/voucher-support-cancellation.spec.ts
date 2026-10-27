import { expect, test } from "@playwright/test";
import type { SaveVoucherDraft, VoucherDraft, VoucherDraftPreparation } from "../src/services/api/accounting";
import { financialVoucherScenario, installFinancialVoucherContext } from "./support/financial-voucher";

for (const width of [1440, 390]) {
  test(`anulación prepara el asiento original, revisa y guarda antes del envío (${width})`, async ({ page, baseURL }) => {
    await page.setViewportSize({ width, height: 1000 });
    const scenario = financialVoucherScenario();
    let draft: VoucherDraft = { ...scenario.draft, voucherTypeCode: "Accounting", fiscalCapture: null };
    const prepared: VoucherDraftPreparation = { description: "Anulación del soporte DS-10", reference: "DS-10",
      mainPartyId: draft.mainPartyId!, mainPartyName: draft.mainPartyName!, mainPartyIdentification: "900123456",
      lines: draft.lines.map((line, index) => ({ ...line, accountCode: index ? "51959501" : "22050501",
        accountName: index ? "Servicio" : "Proveedor", partyName: draft.mainPartyName!, partyIdentification: "900123456",
        value: { ...line.value, partyId: draft.mainPartyId!, partySiteId: index ? null : "11111111-2222-3333-4444-555555555555",
          description: index ? "Anulación: servicio" : "Anulación: obligación", paymentEvidence: null,
          baseAmount: index ? -100 : null, dueDate: null, financialGroupId: null } })) };
    let prepares = 0, reviews = 0, writes = 0, sends = 0, reads = 0, options = 0, taxReads = 0;
    const hash = "B".repeat(64);
    await installFinancialVoucherContext(page, baseURL!, scenario);
    await page.route("**/api/**", async route => {
      const request = route.request(), url = new URL(request.url()), path = url.pathname;
      if (path.endsWith("/tax-profiles")) taxReads++;
      if (path.endsWith("/accounting/open-positions")) {
        if (url.searchParams.get("manualSupportOnly") !== "true") {
          await route.fulfill({ json: { items: [], page: 1, pageSize: 10, totalCount: 0, totalOutstandingAmount: 0 } }); return;
        }
        options++;
        expect(url.searchParams.get("manualSupportOnly")).toBe("true");
        expect(url.searchParams.get("partyId")).toBe(draft.mainPartyId);
        await route.fulfill({ json: { items: [{ subledgerKind: "Payable", subledgerId: scenario.positionId,
          partyId: draft.mainPartyId, partyName: draft.mainPartyName, partyIdentification: "900123456",
          sourceDocumentId: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", sourceDocumentType: "ManualAccountingVoucher",
          documentNumber: "DS-10", accountId: prepared.lines[0].value.accountId, accountCode: "22050501", accountName: "Proveedor",
          originalAmount: 100, outstandingAmount: 100, availableAmount: 100, openingCredit: 100, openingDebit: 0,
          currencyCode: "COP", openingLinkIssue: null, dueDate: "2026-10-20T12:00:00-05:00" }],
          page: 1, pageSize: 25, totalCount: 1, totalOutstandingAmount: 100 } }); return;
      }
      if (path.endsWith("/accounting/support-cancellation-capture")) {
        prepares++; expect(url.searchParams.get("payableId")).toBe(scenario.positionId);
        if (prepares === 1) { await route.fulfill({ status: 409, json: { detail: "El soporte tiene un cargo asociado a compras." } }); return; }
        await route.fulfill({ json: prepared }); return;
      }
      if (path.endsWith("/accounting/manual/fiscal-preview")) {
        reviews++; const capture = request.postDataJSON();
        expect(capture.voucherTypeCode).toBe("SupportDocumentCancellation");
        expect(capture.fiscalCapture.acquisitions).toEqual([]);
        expect(capture.lines[1].baseAmount).toBe(-100);
        await route.fulfill({ json: { acquisitions: [], calculationHash: hash,
          canConfirm: reviews > 1, diagnostics: reviews === 1 ? ["Hay un abono pendiente. No se anuló el soporte."] : [],
          withholding: { grossAmount: 100, withholdingTotal: 0, netAmount: 100, lines: [] } } }); return;
      }
      if (path.endsWith(`/accounting/manual/drafts/${draft.documentId}`)) {
        if (request.method() === "PUT") {
          writes++; const capture = request.postDataJSON() as SaveVoucherDraft;
          expect(capture.fiscalCapture?.acquisitions).toEqual([]);
          draft = { ...draft, ...capture, rowVersion: "AAAAAAAAAAI=",
            lines: prepared.lines.map((line, index) => ({ ...line, value: capture.lines[index] })) };
        } else reads++;
        await route.fulfill({ json: draft }); return;
      }
      if (path.endsWith(`/accounting/manual/drafts/${draft.documentId}/send`)) {
        sends++; expect(draft.fiscalCapture?.calculationHash).toBe(hash);
        draft = { ...draft, sentAt: "2026-10-25T17:00:00Z", canEdit: false, canSend: false,
          row: { ...draft.row, fiscalDocumentType: "SupportDocumentAdjustment", dianNumber: "DS-11", fiscalStatus: "PendingGeneration" } };
        await route.fulfill({ json: draft }); return;
      }
      await route.fallback();
    });
    await page.goto(`/dashboard/financial-traceability?document=${draft.documentId}`);
    await page.getByRole("combobox", { name: "Tipo de comprobante" }).click();
    await page.getByRole("option", { name: "Anulación de documento soporte", exact: true }).click();
    await page.getByRole("button", { name: "Seleccionar soporte original", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: "Seleccionar soporte original", exact: true });
    await dialog.getByRole("combobox", { name: "Soporte original para anular" }).click();
    await page.getByRole("option", { name: /DS-10/ }).click();
    await expect(dialog.getByText("Cuenta: 22050501 · Proveedor", { exact: true })).toBeVisible();
    await dialog.getByRole("button", { name: "Preparar anulación", exact: true }).click();
    await expect(dialog.getByRole("alert")).toContainText("cargo asociado");
    await dialog.getByRole("button", { name: "Preparar anulación", exact: true }).click();
    await expect(dialog).toBeHidden();
    await expect(page.getByRole("button", { name: "Agregar documento pendiente", exact: true })).toHaveCount(0);
    const first = page.getByRole("row", { name: "Partida 1", exact: true });
    await expect(first.getByLabel("Débito de la partida", { exact: true })).toBeDisabled();
    await page.getByRole("button", { name: "Revisar anulación", exact: true }).click();
    await expect(page.getByRole("alert").filter({ hasText: "No se anuló el soporte" })).toBeVisible();
    await page.getByRole("button", { name: "Revisar anulación", exact: true }).click();
    await expect(page.getByText("Anulación revisada. Guarda el comprobante antes de contabilizar.", { exact: true })).toBeVisible();
    await page.screenshot({ path: `../work/anulacion-soporte-manual-${width}.png`, fullPage: true });
    await page.getByRole("button", { name: "Guardar comprobante", exact: true }).click();
    await expect(page.getByRole("button", { name: "Contabilizar", exact: true })).toBeEnabled();
    await page.locator("#voucher-description").fill("Motivo actualizado de la anulación");
    await page.getByRole("button", { name: "Guardar comprobante", exact: true }).click();
    await expect(page.getByRole("button", { name: "Contabilizar", exact: true })).toBeDisabled();
    expect(draft.fiscalCapture?.acquisitions).toEqual([]);
    await page.getByRole("button", { name: "Revisar anulación", exact: true }).click();
    await page.getByRole("button", { name: "Guardar comprobante", exact: true }).click();
    await page.getByRole("button", { name: "Contabilizar", exact: true }).click();
    await expect(page.getByText("Nota de ajuste del documento soporte", { exact: false })).toBeVisible();
    expect(prepares).toBe(2); expect(reviews).toBe(3); expect(writes).toBe(3); expect(sends).toBe(1);
    expect(reads).toBe(1); expect(options).toBe(1); expect(taxReads).toBe(0);
  });
}
