import { expect, test } from "@playwright/test";
import { financialVoucherScenario, installFinancialVoucherContext } from "./support/financial-voucher";

test.use({ serviceWorkers: "block" });
for (const dispatch of [false, true]) for (const method of ["Cash", "DebitCard", "CreditCard", "Transfer"]) {
  test(`Facturas corrige ${method} en ${dispatch ? "despacho" : "caja"} sin navegar ni conciliar otros documentos`, async ({ page, baseURL }) => {
    const scenario = financialVoucherScenario();
    scenario.user.permissions.push("sales.reprint", "sales.payments.correct", "dispatches.settle");
    await installFinancialVoucherContext(page, baseURL!, scenario);
    await page.setViewportSize({ width: 390, height: 844 });
    const dispatchId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", paymentId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    const amount = method === "Transfer" && !dispatch ? 11900.47 : 11900;
    const multiple = !dispatch && method === "Cash";
    const names: Record<string, string> = { Cash: "Efectivo", DebitCard: "Tarjeta débito", CreditCard: "Tarjeta crédito", Transfer: "Transferencia" };
    let reads = 0, saves = 0, searches = 0;
    let posted = false;
    const commands: Array<Record<string, unknown>> = [];
    const form = { paymentMethodId: "cccccccc-cccc-cccc-cccc-cccccccccccc", version: 2, code: "PAY-1", name: "Forma de recaudo verificada",
      methodCode: method, accountId: "dddddddd-dddd-dddd-dddd-dddddddddddd", accountCode: "111005", accountName: "Auxiliar configurado", isActive: true, isReady: true };
    await page.route("**/api/**", async route => {
      const url = new URL(route.request().url()), path = url.pathname;
      let body: unknown;
      if (path.endsWith("/sales/search")) {
        searches++; body = { items: [{ documentId: scenario.documentId, documentType: "SalesReceipt", documentNumber: "CVI-1", issuedAt: "2026-11-24T12:00:00Z",
          customerName: "Cliente de prueba", customerIdentification: "123", total: multiple ? amount * 2 : amount, closureId: dispatch ? null : "closure-1", dispatchId: dispatch ? dispatchId : null,
          payments: [{ paymentNumber: 1, methodCode: "Cash", amount, formName: "Caja principal" }, ...(multiple ? [{ paymentNumber: 2, methodCode: "Cash", amount, formName: "Caja secundaria" }] : [])] }], hasMore: false, nextOffset: null };
      } else if (path.endsWith("/payment-correction") || path.endsWith("/payment-corrections")) {
        if (route.request().method() === "GET") {
          reads++;
          body = dispatch ? [{ paymentId, applicationType: "InvoicePayment", paymentMethod: "Cash", amount, reference: null, evidenceUrl: null, correctedAt: null }]
            : { documentId: scenario.documentId, documentNumber: "CVI-1", workSessionClosureId: "closure-1", countableMethods: ["Cash", "Card", "Transfer"],
              items: [{ verificationKey: "original-payment", paymentMethodCode: "Cash", movementType: "Sale", sourceId: scenario.documentId, documentNumber: "CVI-1", sourceNumber: 1,
                amount, reference: null, cardFranchiseCode: null, approvalNumber: null, occurredAt: "2026-11-24T12:00:00Z", sourceDocumentType: "SalesReceipt", customerName: "Cliente", status: null,
                ...(posted ? { appliedCorrectionId: "accepted", correctionAccountingStatus: "Posted", correctedTenderMethodCode: method,
                  correctedAmount: amount, correctedPaymentMethodName: form.name } : {}) }] };
        } else { saves++; commands.push(route.request().postDataJSON()); body = { correctionId: "accepted", documentId: scenario.documentId, businessId: scenario.businessId, accountingStatus: "Pending", idempotentReplay: false }; }
      } else if (path.endsWith("/correction")) {
        saves++; commands.push(route.request().postDataJSON()); body = { dispatchId, dispatchSourceDocumentId: "source-1", payment: { paymentId, amount: 11900, paymentMethod: method, correctedAt: "2026-11-24T12:00:00Z" } };
      } else if (path.endsWith("/reference-options/payment-method")) body = Object.entries(names).map(([code, label]) => ({ code, label, isActive: true }));
      else if (path.endsWith("/reference-options/cash-closure-method")) body = ["Cash", "Card", "Transfer"].map(code => ({ code, label: code, isActive: true }));
      else if (path.endsWith("/reference-options/card-franchise")) body = [{ code: "Visa", label: "Visa", isActive: true }];
      else if (path.endsWith("/payment-methods/")) body = { items: url.searchParams.get("methodCode") === method ? [form] : [], page: 1, pageSize: 10, totalCount: url.searchParams.get("methodCode") === method ? 1 : 0 };
      else { await route.fallback(); return; }
      if (multiple && path.endsWith("/payment-correction") && route.request().method() === "GET") {
        const data = body as { items: Array<Record<string, unknown>> };
        data.items = [{ ...data.items[0], appliedCorrectionId: undefined, correctionAccountingStatus: undefined, correctedTenderMethodCode: undefined, correctedAmount: undefined, correctedPaymentMethodName: undefined },
          { ...data.items[0], verificationKey: "original-payment-2", sourceNumber: 2 }];
      }
      await route.fulfill({ json: body });
    });
    await page.goto("/dashboard/invoices");
    await page.getByRole("button", { name: "Corregir pago de CVI-1", exact: true }).click();
    const editor = page.getByRole("dialog", { name: "Corregir medio de recaudo", exact: true });
    if (multiple) await editor.locator("div.rounded-xl.border.p-3").filter({ hasText: "Caja secundaria" }).getByRole("button", { name: "Corregir", exact: true }).click();
    await expect(editor.getByLabel("Valor del recaudo")).toBeDisabled();
    if (amount % 1 !== 0) await expect(editor.getByLabel("Valor del recaudo")).toHaveValue(/11\.900,47/);
    if (method !== "Cash") {
      await editor.getByRole("combobox", { name: "Tipo de pago real", exact: true }).click();
      await page.getByRole("option", { name: names[method], exact: true }).click();
    }
    await expect(editor.getByRole("combobox", { name: "Forma de pago del recaudo" })).toContainText(form.name);
    const save = editor.getByRole("button", { name: "Guardar corrección", exact: true });
    if (method === "Transfer") {
      await expect(editor.getByLabel("Referencia de transferencia")).toHaveAttribute("maxlength", dispatch ? "120" : "160");
      await expect(editor.getByLabel("Referencia de transferencia")).toBeFocused();
      await expect(save).toBeDisabled(); await editor.getByLabel("Referencia de transferencia").fill("TRF-1");
    } else if (method.endsWith("Card")) {
      await expect(editor.getByLabel("Número de aprobación", { exact: true })).toHaveAttribute("maxlength", dispatch ? "80" : "100");
      await expect(editor.getByLabel("Número de aprobación", { exact: true })).toBeFocused();
      await expect(save).toBeDisabled();
      await editor.getByRole("combobox", { name: "Franquicia", exact: true }).click();
      await page.getByRole("option", { name: "Visa", exact: true }).click();
      await editor.getByLabel("Número de aprobación", { exact: true }).fill("AP-1");
    } else await expect(editor.getByLabel("Referencia de transferencia")).toHaveCount(0);
    await editor.getByLabel("Motivo de la corrección").fill("Recaudo verificado por administrativo");
    expect(await editor.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
    await page.screenshot({ path: test.info().outputPath("correccion-movil.png") });
    await save.click();
    await expect(editor.getByText(dispatch ? "Guardada en la revisión del despacho" : "Corrección pendiente de contabilizar", { exact: true })).toBeVisible();
    await expect(editor.getByText(form.name, { exact: true })).toBeVisible();
    expect(saves).toBe(1); expect(reads).toBe(1); expect(searches).toBe(1);
    expect(commands[0]).toMatchObject(dispatch ? { methodCode: method, paymentMethodId: form.paymentMethodId, paymentMethodVersion: 2, expectedCorrectedAt: null }
      : { correction: { tenderMethodCode: method, paymentMethodId: form.paymentMethodId, paymentMethodVersion: 2, amount } });
    if (multiple) expect(commands[0]).toMatchObject({ correction: { verificationKey: "original-payment-2" } });
    expect(page.url()).toContain("/dashboard/invoices");
    if (!dispatch) {
      posted = true; await editor.getByRole("button", { name: "Actualizar estado" }).click();
      await expect(editor.getByText("Corrección aplicada", { exact: true })).toBeVisible();
      expect(reads).toBe(2); expect(saves).toBe(1);
    }
  });
}
