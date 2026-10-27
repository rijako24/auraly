import { expect, test } from "@playwright/test";
import type { SaveVoucherDraft, VoucherDraft, VoucherFiscalPreview } from "../src/services/api/accounting";
import { financialVoucherScenario, installFinancialVoucherContext } from "./support/financial-voucher";

for (const width of [1440, 390]) {
  test(`documento soporte conserva la revisión y exige revisarla después de editar partidas (${width})`, async ({ page, baseURL }) => {
    await page.setViewportSize({ width, height: 1000 });
    const scenario = financialVoucherScenario();
    let draft: VoucherDraft = { ...scenario.draft, voucherTypeCode: "SupportDocument", reference: null, description: "Servicio con soporte",
      fiscalCapture: { acquisitions: [{ sourceLineId: scenario.draft.lines[0].value.lineId!, taxExclusiveAmount: 100, taxProfileId: null, taxTreatment: "CapitalizedCost" as const }] },
      lines: scenario.draft.lines.map((line, index) => ({ ...line, accountCode: index ? "22050501" : "51959501", accountName: index ? "Proveedor" : "Servicio",
        value: { ...line.value, existingPosition: null, paymentEvidence: null, description: index ? "Obligación" : "Servicio", dueDate: index ? "2026-11-23T12:00:00+00:00" : null } })) };
    let reviews = 0, taxReads = 0, writes = 0, sends = 0, draftReads = 0, accountReads = 0;
    let fail = true;
    const hash = "A".repeat(64);
    await installFinancialVoucherContext(page, baseURL!, scenario);
    await page.route("**/api/**", async route => {
      const request = route.request(), url = new URL(request.url()), path = url.pathname;
      if (path.endsWith("/accounting/account-options")) accountReads++;
      if (path.endsWith("/tax-profiles")) {
        taxReads++; expect(url.searchParams.get("includeInactive")).toBe("false");
        await route.fulfill({ json: [] }); return;
      }
      if (path.endsWith("/reference-options/purchase-tax-treatment")) {
        await route.fulfill({ json: [{ id: "capitalized", code: "CapitalizedCost", label: "Mayor valor del costo" },
          { id: "deductible", code: "DeductibleInputVat", label: "IVA descontable" }] }); return;
      }
      if (path.endsWith("/accounting/manual/fiscal-preview")) {
        reviews++;
        expect(request.postDataJSON().voucherId).toBe(draft.documentId);
        expect(request.postDataJSON().businessId).toBe(scenario.businessId);
        if (fail) { fail = false; await route.fulfill({ status: 409, json: { detail: "Revisa las partidas del soporte" } }); return; }
        const result: VoucherFiscalPreview = { acquisitions: [], calculationHash: hash, canConfirm: true, diagnostics: [],
          withholding: { grossAmount: 100, withholdingTotal: 0, netAmount: 100, lines: [] } };
        await route.fulfill({ json: result }); return;
      }
      if (path.endsWith(`/accounting/manual/drafts/${draft.documentId}`)) {
        if (request.method() === "PUT") {
          writes++; const value = request.postDataJSON() as SaveVoucherDraft;
          draft = { ...draft, ...value, fiscalCapture: value.fiscalCapture as typeof draft.fiscalCapture,
            rowVersion: `AAAAAAAAAA${writes}=`, lines: draft.lines.map((line, index) => ({ ...line, value: { ...line.value, ...value.lines[index] } })) };
        } else draftReads++;
        await route.fulfill({ json: draft }); return;
      }
      if (path.endsWith(`/accounting/manual/drafts/${draft.documentId}/send`)) {
        sends++; draft = { ...draft, sentAt: new Date().toISOString(), canEdit: false, canSend: false,
          row: { ...draft.row, fiscalDocumentType: "SupportDocument", dianNumber: "DS100", fiscalStatus: "PendingGeneration" } };
        await route.fulfill({ json: draft }); return;
      }
      await route.fallback();
    });
    await page.goto(`/dashboard/financial-traceability?document=${draft.documentId}`);
    await expect(page.getByText("Documento soporte electrónico", { exact: false })).toBeVisible();
    await expect(page.getByRole("button", { name: "Contabilizar", exact: true })).toBeDisabled();
    expect(taxReads).toBe(0);
    await page.getByRole("button", { name: "Datos fiscales del soporte", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: "Datos fiscales del documento soporte" });
    await expect(dialog.getByLabel("Base sin IVA 1", { exact: true })).toHaveValue("100");
    await dialog.getByRole("button", { name: "Revisar cálculo fiscal", exact: true }).click();
    await expect(dialog.getByRole("alert")).toContainText("Revisa las partidas del soporte");
    await dialog.getByRole("button", { name: "Revisar cálculo fiscal", exact: true }).click();
    await expect(dialog.getByText("Revisión completa", { exact: true })).toBeVisible();
    const bounds = await dialog.boundingBox();
    expect(bounds!.x).toBeGreaterThanOrEqual(0);
    expect(bounds!.width).toBeLessThanOrEqual(width);
    await page.screenshot({ path: `../work/documento-soporte-manual-revision-${width}.png`, fullPage: true });
    await dialog.getByRole("button", { name: "Conservar datos fiscales", exact: true }).click();
    await page.getByRole("button", { name: "Guardar comprobante", exact: true }).click();
    await expect(page.getByRole("button", { name: "Contabilizar", exact: true })).toBeEnabled();
    expect(draft.fiscalCapture?.calculationHash).toBe(hash);
    const first = page.getByRole("row", { name: "Partida 1", exact: true });
    await first.getByLabel("Descripción de la partida", { exact: true }).fill("Servicio revisado");
    await expect(page.getByRole("button", { name: "Contabilizar", exact: true })).toBeDisabled();
    await page.getByRole("button", { name: "Guardar comprobante", exact: true }).click();
    await expect(page.getByRole("button", { name: "Guardar comprobante", exact: true })).toBeDisabled();
    expect(draft.fiscalCapture?.calculationHash).toBeNull();
    await page.getByRole("button", { name: "Datos fiscales del soporte", exact: true }).click();
    await dialog.getByRole("button", { name: "Quitar base", exact: true }).click();
    await dialog.getByRole("button", { name: "Conservar datos fiscales", exact: true }).click();
    await page.getByRole("button", { name: "Guardar comprobante", exact: true }).click();
    await expect(page.getByRole("button", { name: "Guardar comprobante", exact: true })).toBeDisabled();
    expect(draft.fiscalCapture).toBeNull();
    await page.getByRole("button", { name: "Datos fiscales del soporte", exact: true }).click();
    await dialog.getByRole("combobox", { name: "Partida para base fiscal" }).click();
    await page.getByRole("option", { name: /1\. 51959501/ }).click();
    await dialog.getByRole("button", { name: "Agregar base fiscal", exact: true }).click();
    await dialog.getByLabel("Base sin IVA 1", { exact: true }).fill("100");
    await dialog.getByRole("button", { name: "Revisar cálculo fiscal", exact: true }).click();
    await expect(dialog.getByText("Revisión completa", { exact: true })).toBeVisible();
    await dialog.getByRole("button", { name: "Conservar datos fiscales", exact: true }).click();
    await page.getByRole("button", { name: "Guardar comprobante", exact: true }).click();
    await page.getByRole("button", { name: "Contabilizar", exact: true }).click();
    await expect(first.getByLabel("Descripción de la partida", { exact: true })).toBeDisabled();
    expect(reviews).toBe(3); expect(writes).toBe(4); expect(sends).toBe(1);
    expect(draftReads).toBe(1); expect(taxReads).toBe(1); expect(accountReads).toBe(0);
  });
}
