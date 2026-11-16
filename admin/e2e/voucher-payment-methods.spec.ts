import { expect, test } from "@playwright/test";
import type { SaveVoucherDraft, VoucherDraft } from "../src/services/api/accounting";
import type { PaymentMethod } from "../src/services/api/payment-methods";
import { financialVoucherScenario, installFinancialVoucherContext } from "./support/financial-voucher";

for (const supplier of [false, true]) test(`${supplier ? "egreso" : "recibo"} conserva dos formas del catálogo y muestra la cuenta utilizada sin recargas`, async ({ page, baseURL }, testInfo) => {
  const scenario = financialVoucherScenario();
  scenario.user.permissions.push("receivables.payments.create");
  scenario.draft.voucherTypeCode = supplier ? "Disbursement" : "CashReceipt";
  scenario.draft.lines[0].value = { ...scenario.draft.lines[0].value, debit: supplier ? 100 : 0, credit: supplier ? 0 : 100,
    existingPosition: { subledgerKind: supplier ? "Payable" : "Receivable", subledgerId: scenario.positionId } };
  scenario.draft.lines[1].value = { ...scenario.draft.lines[1].value, debit: supplier ? 0 : 100, credit: supplier ? 100 : 0 };
  const id = (number: number) => `00000000-0000-0000-0000-${String(number).padStart(12, "0")}`;
  const forms: PaymentMethod[] = Array.from({ length: 8 }, (_, index) => ({ paymentMethodId: id(index + 1), code: `CAJA-${index + 1}`,
    name: `Caja configurada ${index + 1}`, paymentTypeId: id(30), methodCode: "Cash", typeLabel: "Efectivo",
    accountId: id(index + 10), accountCode: `110505${index + 1}`, accountName: `Auxiliar de caja ${index + 1}`, bankAccountId: null,
    useForSales: false, useForReceipts: true, useForSupplierPayments: true, useForPayroll: false, useForVouchers: false,
    isActive: true, sortOrder: index, version: 1, rowVersion: "AAAAAAAAAAE=" }));
  let draft = scenario.draft, reads = 0, formReads = 0, sends = 0, positionReads = 0;
  const writes: SaveVoucherDraft[] = [];
  await installFinancialVoucherContext(page, baseURL!, scenario);
  await page.route("**/api/**", async route => {
    const request = route.request(), url = new URL(request.url());
    if (url.pathname.endsWith("/accounting/open-positions")) {
      positionReads++;
      await route.fulfill({ json: { items: [], page: 1, pageSize: 10, totalCount: 0, totalOutstandingAmount: 0 } }); return;
    }
    if (url.pathname.endsWith("/payment-methods/")) {
      formReads++;
      expect(url.searchParams.get("usage")).toBe(supplier ? "SupplierPayments" : "Receipts");
      await route.fulfill({ json: { items: forms, page: 1, pageSize: 10, totalCount: 8 } }); return;
    }
    if (url.pathname.endsWith(`/manual/drafts/${draft.documentId}`)) {
      if (request.method() === "GET") reads++;
      else if (request.method() === "PUT") {
        const value = request.postDataJSON() as SaveVoucherDraft; writes.push(value);
        draft = { ...draft, ...value, rowVersion: "AAAAAAAAAAI=", lines: value.lines.map(line => {
          const previous = draft.lines.find(item => item.value.lineId === line.lineId);
          const method = forms.find(item => item.paymentMethodId === line.paymentEvidence?.paymentMethodId);
          return { ...previous, value: line, accountCode: method?.accountCode ?? previous?.accountCode ?? null,
            accountName: method?.accountName ?? previous?.accountName ?? null, partyName: previous?.partyName ?? draft.mainPartyName ?? null,
            partyIdentification: previous?.partyIdentification ?? null, costCenterName: previous?.costCenterName ?? null };
        }) };
      }
      await route.fulfill({ json: draft }); return;
    }
    if (url.pathname.endsWith(`/manual/drafts/${draft.documentId}/send`)) {
      sends++;
      const monetary = draft.lines.filter(line => line.value.paymentEvidence?.paymentMethodId);
      draft = { ...draft, sentAt: "2026-11-10T15:00:00Z", status: "Posted", canEdit: false, canSend: false,
        row: { ...draft.row, status: "Posted" }, appliedPaymentMethods: monetary.map((line, index) => ({
          sourceLineId: line.value.lineId!, paymentMethodId: line.value.paymentEvidence!.paymentMethodId!, methodCode: "Cash",
          version: index === 0 ? 2 : 1, code: line.value.paymentEvidence!.paymentMethodCode!,
          name: index === 0 ? "Caja utilizada" : line.value.paymentEvidence!.paymentMethodName!,
          accountId: index === 0 ? id(100) : line.value.accountId!, accountCode: index === 0 ? "11059901" : line.accountCode!,
          accountName: index === 0 ? "Auxiliar vigente al contabilizar" : line.accountName!, bankAccountId: null })) } satisfies VoucherDraft;
      await route.fulfill({ json: draft }); return;
    }
    await route.fallback();
  });
  await page.goto(`/dashboard/financial-traceability?document=${draft.documentId}`);
  if (supplier) {
    await page.getByRole("button", { name: "Detalles de la partida 2", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: /Detalles de la partida/ });
    await dialog.getByRole("combobox", { name: "Forma de pago de la partida", exact: true }).click();
    await page.getByRole("option", { name: /Caja configurada 1/ }).click();
    await dialog.getByLabel(supplier ? "Valor pagado por la forma" : "Valor recibido por la forma").fill("60");
    await expect(dialog.getByText(/Cuenta: 1105051/)).toBeVisible();
    await expect(dialog.getByLabel("Efectivo entregado en la partida")).toHaveCount(0);
    await dialog.getByRole("button", { name: "Cerrar detalles", exact: true }).click();
    await expect(page.getByRole("row", { name: "Partida 2", exact: true }).getByRole("combobox", { name: "Seleccionar cuenta contable", exact: true })).toBeDisabled();
    await page.getByRole("button", { name: "Agregar forma de pago", exact: true }).click();
    await dialog.getByRole("combobox", { name: "Forma de pago de la partida", exact: true }).click();
    await page.getByRole("option", { name: /Caja configurada 1/ }).click();
    await expect(dialog.getByRole("alert")).toContainText("La forma ya está en las partidas");
    await dialog.getByRole("combobox", { name: "Forma de pago de la partida", exact: true }).click();
    await page.getByRole("option", { name: /Caja configurada 8/ }).click();
    await dialog.getByLabel(supplier ? "Valor pagado por la forma" : "Valor recibido por la forma").fill("40");
    await dialog.getByRole("button", { name: "Cerrar detalles", exact: true }).click();
  } else {
    const panel = page.getByRole("region", { name: "Formas de pago del comprobante", exact: true });
    await expect(panel).toBeVisible();
    const rowCount = await page.getByRole("row", { name: /^Partida \d+$/ }).count();
    await panel.getByRole("combobox", { name: "Seleccionar forma de pago", exact: true }).click();
    await page.keyboard.press("Escape");
    await expect(page.getByRole("row", { name: /^Partida \d+$/ })).toHaveCount(rowCount);
    await expect(page.getByRole("button", { name: "Guardar borrador", exact: true })).toBeDisabled();
    await panel.getByRole("combobox", { name: "Forma de pago 1", exact: true }).click();
    await page.getByRole("option", { name: /Caja configurada 1/ }).click();
    await panel.getByLabel(`${supplier ? "Valor pagado" : "Valor recibido"} por la forma 1`, { exact: true }).fill("60");
    await expect(panel.getByText(/Cuenta: 1105051/)).toBeVisible();
    await expect(panel.getByLabel("Número de aprobación")).toHaveCount(0);
    await expect(panel.getByLabel("Referencia de transferencia")).toHaveCount(0);
    await expect(page.getByRole("dialog", { name: /Detalles de la partida/ })).toHaveCount(0);
    await expect(page.getByRole("row", { name: "Partida 2", exact: true }).getByRole("combobox", { name: "Seleccionar cuenta contable", exact: true })).toBeDisabled();
    const add = panel.getByRole("group", { name: "Agregar forma de pago", exact: true });
    await add.getByRole("combobox", { name: "Seleccionar forma de pago", exact: true }).click();
    await page.getByRole("option", { name: /Caja configurada 1/ }).click();
    await expect(add.getByRole("alert")).toContainText("La forma ya está en las partidas");
    await expect(add.getByRole("button", { name: "Agregar forma de pago", exact: true })).toBeDisabled();
    await expect(page.getByRole("row", { name: /^Partida \d+$/ })).toHaveCount(rowCount);
    await add.getByRole("combobox", { name: "Seleccionar forma de pago", exact: true }).click();
    await page.getByRole("option", { name: /Caja configurada 8/ }).click();
    await add.getByLabel("Valor de la nueva forma de pago", { exact: true }).fill("40");
    await expect(page.getByRole("row", { name: /^Partida \d+$/ })).toHaveCount(rowCount);
    await add.getByRole("button", { name: "Agregar forma de pago", exact: true }).click();
    await expect(page.getByRole("row", { name: /^Partida \d+$/ })).toHaveCount(rowCount + 1);
    await expect(add.getByRole("button", { name: "Agregar forma de pago", exact: true })).toBeDisabled();
  }
  await page.getByRole("button", { name: "Guardar borrador", exact: true }).click();
  await expect(page.getByRole("button", { name: "Guardar borrador", exact: true })).toBeDisabled();
  expect(writes).toHaveLength(1);
  expect(writes[0].lines.filter(line => line.paymentEvidence?.paymentMethodId).map(line => line.paymentEvidence!.paymentMethodId)).toEqual([forms[0].paymentMethodId, forms[7].paymentMethodId]);
  expect(writes[0].lines.every(line => line.financialGroupId === draft.documentId)).toBe(true);
  expect(writes[0].lines.reduce((sum, line) => sum + line.debit - line.credit, 0)).toBe(0);
  await page.getByRole("button", { name: "Ver documentos", exact: true }).click();
  await expect(page.getByRole("region", { name: "Documentos pendientes del tercero", exact: true })).toContainText("No hay documentos pendientes");
  await page.getByRole("button", { name: "Guardar comprobante", exact: true }).click();
  await expect(page.getByRole("region", { name: "Formas de pago utilizadas", exact: true })).toContainText("Caja utilizada · 11059901 · Auxiliar vigente al contabilizar");
  await expect(page.getByRole("row", { name: "Partida 2", exact: true })).toContainText("11059901");
  await expect(page.getByRole("region", { name: "Documentos pendientes del tercero", exact: true })).toHaveCount(0);
  expect(positionReads).toBe(1);
  expect(reads).toBe(1); expect(formReads).toBe(1); expect(sends).toBe(1); expect(writes).toHaveLength(1);
  await page.screenshot({ path: testInfo.outputPath("voucher-configured-forms.png"), fullPage: true });
});

for (const methodCode of ["Cash", "DebitCard", "CreditCard", "Transfer"] as const) {
  test(`recibo captura ${methodCode} en el panel y agrega una sola partida con su evidencia`, async ({ page, baseURL }) => {
    const scenario = financialVoucherScenario();
    scenario.draft.voucherTypeCode = "CashReceipt";
    scenario.draft.lines = [{ ...scenario.draft.lines[0], value: { ...scenario.draft.lines[0].value,
      debit: 0, credit: 100, existingPosition: { subledgerKind: "Receivable", subledgerId: scenario.positionId } } }];
    const forms: PaymentMethod[] = ["Cash", "DebitCard", "CreditCard", "Transfer"].map((code, index) => ({
      paymentMethodId: `00000000-0000-0000-0000-00000000000${index + 1}`, code: `FORMA-${index}`, name: `Medio ${code}`,
      paymentTypeId: `00000000-0000-0000-0000-00000000001${index}`, methodCode: code, typeLabel: code,
      accountId: `00000000-0000-0000-0000-00000000002${index}`, accountCode: `11050${index}`, accountName: `Cuenta ${code}`,
      bankAccountId: null, useForSales: false, useForReceipts: true, useForSupplierPayments: false,
      useForPayroll: false, useForVouchers: false, isActive: true, sortOrder: index, version: 1, rowVersion: "AAAAAAAAAAE=",
    }));
    let draft = scenario.draft, reads = 0, formReads = 0, franchiseReads = 0;
    const writes: SaveVoucherDraft[] = [];
    await installFinancialVoucherContext(page, baseURL!, scenario);
    await page.route("**/api/**", async route => {
      const request = route.request(), path = new URL(request.url()).pathname;
      if (path.endsWith("/payment-methods/")) {
        formReads++; await route.fulfill({ json: { items: forms, page: 1, pageSize: 10, totalCount: 4 } }); return;
      }
      if (path.endsWith("/reference-options/card-franchise")) {
        franchiseReads++; await route.fulfill({ json: [{ id: "visa", code: "Visa", label: "Visa" }] }); return;
      }
      if (path.endsWith(`/manual/drafts/${draft.documentId}`)) {
        if (request.method() === "GET") reads++;
        else {
          const value = request.postDataJSON() as SaveVoucherDraft; writes.push(value);
          draft = { ...draft, ...value, rowVersion: "AAAAAAAAAAI=", lines: value.lines.map(line => ({ value: line,
            accountCode: "110505", accountName: "Cuenta", partyName: draft.mainPartyName ?? null, partyIdentification: null, costCenterName: null })) };
        }
        await route.fulfill({ json: draft }); return;
      }
      await route.fallback();
    });
    await page.goto(`/dashboard/cash-receipts?document=${draft.documentId}`);
    const panel = page.getByRole("region", { name: "Formas de pago del comprobante", exact: true });
    const add = panel.getByRole("group", { name: "Agregar forma de pago", exact: true });
    await add.getByRole("combobox", { name: "Seleccionar forma de pago", exact: true }).click();
    await page.getByRole("option", { name: `Medio ${methodCode}`, exact: false }).click();
    await expect(page.getByRole("dialog", { name: /Detalles/ })).toHaveCount(0);
    await expect(page.getByRole("row", { name: /^Partida \d+$/ })).toHaveCount(1);
    await expect(page.getByRole("button", { name: "Guardar borrador", exact: true })).toBeDisabled();
    await add.getByLabel("Valor de la nueva forma de pago", { exact: true }).fill("100");
    const card = methodCode === "DebitCard" || methodCode === "CreditCard";
    await expect(add.getByLabel("Número de aprobación", { exact: true })).toHaveCount(card ? 1 : 0);
    await expect(add.getByLabel("Referencia de transferencia", { exact: true })).toHaveCount(methodCode === "Transfer" ? 1 : 0);
    if (card) {
      await expect(add.getByRole("button", { name: "Agregar forma de pago", exact: true })).toBeDisabled();
      await add.getByLabel("Número de aprobación", { exact: true }).fill("AP-123");
      await add.getByRole("combobox", { name: "Franquicia de la nueva forma", exact: true }).click();
      await page.getByRole("option", { name: "Visa", exact: true }).click();
    }
    if (methodCode === "Transfer") {
      await expect(add.getByRole("button", { name: "Agregar forma de pago", exact: true })).toBeDisabled();
      await add.getByLabel("Referencia de transferencia", { exact: true }).fill("TR-123");
    }
    await add.getByRole("button", { name: "Agregar forma de pago", exact: true }).click();
    await expect(page.getByRole("row", { name: /^Partida \d+$/ })).toHaveCount(2);
    await expect(add.getByRole("button", { name: "Agregar forma de pago", exact: true })).toBeDisabled();
    await page.getByRole("button", { name: "Guardar borrador", exact: true }).click();
    await expect(page.getByRole("button", { name: "Guardar borrador", exact: true })).toBeDisabled();
    expect(writes).toHaveLength(1);
    const payment = writes[0].lines.find(line => line.paymentEvidence?.paymentMethodId)!;
    expect(payment.debit).toBe(100); expect(payment.credit).toBe(0);
    expect(payment.financialGroupId).toBe(draft.documentId);
    expect(payment.reference ?? null).toBe(methodCode === "Transfer" ? "TR-123" : null);
    expect(payment.paymentEvidence?.approvalNumber ?? null).toBe(card ? "AP-123" : null);
    expect(payment.paymentEvidence?.cardFranchiseCode ?? null).toBe(card ? "Visa" : null);
    if (card) {
      await panel.getByRole("combobox", { name: "Forma de pago 1", exact: true }).click();
      await page.getByRole("option", { name: /Medio Cash/ }).click();
      await expect(panel.getByLabel("Número de aprobación", { exact: true })).toHaveCount(0);
      await page.getByRole("button", { name: "Guardar borrador", exact: true }).click();
      await expect(page.getByRole("button", { name: "Guardar borrador", exact: true })).toBeDisabled();
      const changed = writes[1].lines.find(line => line.lineId === payment.lineId)!;
      expect(changed.debit).toBe(100); expect(changed.paymentEvidence?.approvalNumber).toBeNull();
      expect(changed.paymentEvidence?.cardFranchiseCode).toBeNull();
    }
    expect(reads).toBe(1); expect(formReads).toBe(1); expect(franchiseReads).toBe(card ? 1 : 0);
  });
}
