import { expect, test } from "@playwright/test";
import type { SaveVoucherDraft, VoucherDraft } from "../src/services/api/accounting";
import type { PaymentMethod } from "../src/services/api/payment-methods";
import { financialVoucherScenario, installFinancialVoucherContext } from "./support/financial-voucher";
import { paymentCatalogFixture } from "./support/payment-catalog";

for (const supplier of [false, true]) for (const posted of [false, true]) test(`formas largas en móvil: ${supplier ? "egreso" : "recibo"} ${posted ? "contabilizado" : "borrador"} conserva el ancho de la página`, async ({page,baseURL},info)=>{
  await page.setViewportSize({width:390,height:844});
  const scenario=financialVoucherScenario();
  scenario.draft.voucherTypeCode=supplier?"Disbursement":"CashReceipt";
  scenario.draft.lines[0]={...scenario.draft.lines[0],accountCode:supplier?"22050501":"13050501",accountName:supplier?"Proveedor":"Cliente",
    value:{...scenario.draft.lines[0].value,debit:supplier?100:0,credit:supplier?0:100,existingPosition:{subledgerKind:supplier?"Payable":"Receivable",subledgerId:scenario.positionId}}};
  const forms=paymentCatalogFixture({Cash:"Caja operativa adicional para verificar nombres completos",DebitCard:"Tarjeta débito adicional para verificar nombres completos",CreditCard:"Tarjeta crédito adicional para verificar nombres completos",Transfer:"Transferencia bancaria adicional para verificar nombres completos"},supplier).forms;
  const prototype=scenario.draft.lines[1];
  scenario.draft.lines=[scenario.draft.lines[0],...forms.map((form,index)=>({...prototype,accountCode:"110505000000000000000001",accountName:"Cuenta utilizada por el medio de pago",
    value:{...prototype.value,lineId:`bbbbbbbb-bbbb-bbbb-bbbb-${String(index+1).padStart(12,"0")}`,debit:supplier?0:25,credit:supplier?25:0,
      paymentEvidence:{tenderedAmount:25,paymentMethodId:form.paymentMethodId,paymentMethodTypeCode:form.methodCode,paymentMethodName:form.name,paymentMethodCode:form.code,paymentMethodVersion:form.version,cardFranchiseCode:"Visa",approvalNumber:"APROBACION-MOVIL",notes:null}}}))];
  if(posted){scenario.draft.sentAt="2026-11-21T06:00:00Z";scenario.draft.status="Posted";scenario.draft.canEdit=false;scenario.draft.row.status="Posted";}
  await installFinancialVoucherContext(page,baseURL!,scenario);
  await page.route(`**/manual/drafts/${scenario.documentId}`,route=>route.fulfill({json:scenario.draft}));
  await page.goto(`/dashboard/${supplier?"financial-traceability":"cash-receipts"}?document=${scenario.documentId}`);
  const panel=page.getByRole("region",{name:"Formas de pago del comprobante",exact:true});await expect(panel).toBeVisible();
  await expect(page.getByText("Iniciando Auraly", { exact: true })).toBeHidden();
  await expect(panel.getByRole("group",{name:"Forma de pago 1",exact:true})).toBeVisible();
  await expect.poll(()=>page.getByRole("main").evaluate(main=>main.scrollWidth-main.clientWidth)).toBeLessThanOrEqual(1);
  for(const fieldset of await panel.locator("fieldset").all()) expect(await fieldset.evaluate(e=>e.getBoundingClientRect().width<=e.parentElement!.clientWidth)).toBe(true);
  await panel.getByRole("group",{name:"Forma de pago 2",exact:true}).scrollIntoViewIfNeeded();
  await page.screenshot({path:info.outputPath("voucher-long-payments-mobile.png"),fullPage:true});
});

test("un excedente requiere clasificación explícita y puede conservarse como borrador", async ({page,baseURL})=>{
  const scenario=financialVoucherScenario();
  scenario.draft.voucherTypeCode="CashReceipt";
  scenario.draft.lines[0].value={...scenario.draft.lines[0].value,debit:0,credit:50,
    existingPosition:{subledgerKind:"Receivable",subledgerId:scenario.positionId}};
  scenario.draft.lines[1].value={...scenario.draft.lines[1].value,debit:100,credit:0};
  let draft=scenario.draft,sends=0;
  const writes:SaveVoucherDraft[]=[];
  await installFinancialVoucherContext(page,baseURL!,scenario);
  await page.route("**/api/**",async route=>{
    const request=route.request(),path=new URL(request.url()).pathname;
    if(path.endsWith(`/manual/drafts/${draft.documentId}/send`)) {
      sends++;await route.fulfill({status:500,json:{}});return;
    }
    if(path.endsWith(`/manual/drafts/${draft.documentId}`)) {
      if(request.method()==="PUT") {
        const value=request.postDataJSON() as SaveVoucherDraft;writes.push(value);
        draft={...draft,...value,rowVersion:"AAAAAAAAAAI=",canSend:false,
          lines:value.lines.map((value,index)=>({...draft.lines[index],value}))};
      }
      await route.fulfill({json:draft});return;
    }
    await route.fallback();
  });
  await page.goto(`/dashboard/cash-receipts?document=${draft.documentId}`);
  await page.getByLabel("Descripción",{exact:true}).fill("Recepción mayor al abono aplicado");
  await page.getByRole("button",{name:"Guardar comprobante",exact:true}).click();
  await expect(page.getByRole("alert").filter({hasText:"por clasificar"})).toContainText("cuenta de anticipo");
  await expect(page.getByRole("alert").filter({hasText:"por clasificar"})).not.toContainText("concepto");
  expect(writes).toHaveLength(0);expect(sends).toBe(0);
  await expect(page.getByRole("row",{name:/^Partida \d+$/})).toHaveCount(2);
  await page.getByRole("button",{name:"Guardar borrador",exact:true}).click();
  await expect(page.getByRole("button",{name:"Guardar borrador",exact:true})).toBeDisabled();
  expect(writes).toHaveLength(1);expect(sends).toBe(0);
  expect(writes[0].lines).toHaveLength(2);
  expect(writes[0].lines.some(line=>line.financialConceptId)).toBe(false);
});

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
    const panel = page.getByRole("region", { name: "Formas de pago del comprobante", exact: true });
    await expect(panel).toBeVisible();
    await expect(page.getByText("Grupo de partidas vinculadas",{exact:true})).toHaveCount(0);
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
  await expect(page.getByRole("main")).toContainText("Contabilizado");
  await expect(page.getByRole("main")).not.toContainText("Posted");
  expect(positionReads).toBe(1);
  expect(reads).toBe(1); expect(formReads).toBe(1); expect(sends).toBe(1); expect(writes).toHaveLength(1);
  await page.screenshot({ path: testInfo.outputPath("voucher-configured-forms.png"), fullPage: true });
});

for (const supplier of [false,true]) for (const methodCode of ["Cash", "DebitCard", "CreditCard", "Transfer"] as const) {
  test(`${supplier ? "egreso" : "recibo"} captura ${methodCode} en el panel y agrega una sola partida con su evidencia`, async ({ page, baseURL }) => {
    const scenario = financialVoucherScenario();
    scenario.draft.voucherTypeCode = supplier ? "Disbursement" : "CashReceipt";
    scenario.draft.lines = [{ ...scenario.draft.lines[0], value: { ...scenario.draft.lines[0].value,
      debit: supplier ? 100 : 0, credit: supplier ? 0 : 100, existingPosition: { subledgerKind: supplier ? "Payable" : "Receivable", subledgerId: scenario.positionId } } }];
    const forms: PaymentMethod[] = ["Cash", "DebitCard", "CreditCard", "Transfer"].map((code, index) => ({
      paymentMethodId: `00000000-0000-0000-0000-00000000000${index + 1}`, code: `FORMA-${index}`, name: `Medio ${code}`,
      paymentTypeId: `00000000-0000-0000-0000-00000000001${index}`, methodCode: code, typeLabel: code,
      accountId: `00000000-0000-0000-0000-00000000002${index}`, accountCode: `11050${index}`, accountName: `Cuenta ${code}`,
      bankAccountId: null, useForSales: false, useForReceipts: true, useForSupplierPayments: true,
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
    expect(payment.debit).toBe(supplier ? 0 : 100); expect(payment.credit).toBe(supplier ? 100 : 0);
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
      expect(changed.debit).toBe(supplier ? 0 : 100); expect(changed.credit).toBe(supplier ? 100 : 0); expect(changed.paymentEvidence?.approvalNumber).toBeNull();
      expect(changed.paymentEvidence?.cardFranchiseCode).toBeNull();
    }
    expect(reads).toBe(1); expect(formReads).toBe(1); expect(franchiseReads).toBe(card ? 1 : 0);
  });
}

for (const multipleForms of [false, true]) test(`recibo evita la combinación ambigua empezando por ${multipleForms ? "varias formas" : "varias facturas"} sin borrar valores`, async ({ page, baseURL }) => {
  const scenario = financialVoucherScenario();
  const first = paymentCatalogFixture().forms[0];
  const second = { ...first, paymentMethodId: "10000000-0000-0000-0000-000000000099", name: "Segunda caja", code: "CAJA-2" };
  const otherInvoice = "99999999-9999-9999-9999-999999999998";
  const original = scenario.draft.lines[0];
  const invoiceLine = { ...original, value: { ...original.value, debit: 0, credit: multipleForms ? 100 : 50,
    financialGroupId: scenario.documentId, existingPosition: { subledgerKind: "Receivable" as const, subledgerId: scenario.positionId } } };
  const moneyLine = (form: typeof first, amount: number) => ({ ...scenario.draft.lines[1], value: {
    ...scenario.draft.lines[1].value, lineId: form.paymentMethodId, accountId: form.accountId,
    financialGroupId: scenario.documentId, debit: amount, credit: 0, paymentEvidence: {
      paymentMethodId: form.paymentMethodId, paymentMethodVersion: 1, paymentMethodTypeCode: "Cash",
      paymentMethodCode: form.code, paymentMethodName: form.name } } });
  scenario.draft.voucherTypeCode = "CashReceipt";
  scenario.draft.lines = [invoiceLine, ...(multipleForms ? [] : [{ ...invoiceLine, value: { ...invoiceLine.value,
    lineId: otherInvoice, reference: "FACT-2", existingPosition: { subledgerKind: "Receivable" as const, subledgerId: otherInvoice } } }]),
    moneyLine(first, multipleForms ? 60 : 100), ...(multipleForms ? [moneyLine(second, 40)] : [])];
  let reads = 0, positions = 0, catalogs = 0;
  await installFinancialVoucherContext(page, baseURL!, scenario);
  await page.route("**/api/**", async route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith(`/manual/drafts/${scenario.documentId}`)) { reads++; await route.fulfill({ json: scenario.draft }); return; }
    if (path.endsWith("/payment-methods/")) { catalogs++; await route.fulfill({ json: { items: [first, second], page: 1, pageSize: 10, totalCount: 2 } }); return; }
    if (path.endsWith("/accounting/open-positions")) {
      positions++;
      await route.fulfill({ json: { items: [scenario.positionId, otherInvoice].map((id, index) => ({
        subledgerKind: "Receivable", subledgerId: id, partyId: scenario.draft.mainPartyId,
        partyName: "Proveedor de prueba", sourceDocumentId: id, sourceDocumentType: "SalesInvoice",
        documentNumber: index ? "FACT-2" : "FACT-1", occurredAt: "2026-10-01T12:00:00Z", dueDate: "2026-10-30T12:00:00Z",
        originalAmount: 100, outstandingAmount: 100, availableAmount: 100, currencyCode: "COP",
        accountId: original.value.accountId, accountCode: "130505", accountName: "Clientes", openingDebit: 100, openingCredit: 0,
        openingLinkIssue: null, accountIsActive: true, pendingPaymentAmount: 0, pendingAdjustmentAmount: 0,
      })), page: 1, pageSize: 10, totalCount: 2, totalOutstandingAmount: 200 } }); return;
    }
    await route.fallback();
  });
  await page.goto(`/dashboard/cash-receipts?document=${scenario.documentId}`);
  const panel = page.getByRole("region", { name: "Formas de pago del comprobante", exact: true });
  const add = panel.getByRole("group", { name: "Agregar forma de pago", exact: true });
  await expect(page.getByRole("row", { name: /^Partida \d+$/ })).toHaveCount(3);
  if (!multipleForms) {
    await expect(add.getByRole("combobox", { name: "Seleccionar forma de pago", exact: true })).toBeDisabled();
    await expect(panel.getByText("Este recibo aplica a varias facturas y admite una sola forma de pago. Puedes cambiar o quitar la existente.")).toBeVisible();
    await expect(panel.getByLabel("Valor recibido por la forma 1", { exact: true })).toHaveValue("100");
    await expect(page.getByRole("button", { name: "Guardar borrador", exact: true })).toBeDisabled();
    await page.getByRole("button", { name: "Eliminar partida 2", exact: true }).click();
    await expect(add.getByRole("combobox", { name: "Seleccionar forma de pago", exact: true })).toBeEnabled();
  }
  await page.getByRole("button", { name: "Ver documentos", exact: true }).click();
  const nextInvoice = page.getByRole("row", { name: "Documento FACT-2", exact: true });
  if (multipleForms) {
    await expect(nextInvoice.getByRole("button", { name: "Agregar", exact: true })).toBeDisabled();
    await expect(nextInvoice).toContainText("Retira las formas adicionales");
    await expect(panel.getByLabel("Valor recibido por la forma 1", { exact: true })).toHaveValue("60");
    await expect(panel.getByLabel("Valor recibido por la forma 2", { exact: true })).toHaveValue("40");
    await panel.getByRole("button", { name: "Quitar forma de pago 2", exact: true }).click();
    await expect(nextInvoice.getByRole("button", { name: "Agregar", exact: true })).toBeEnabled();
    await nextInvoice.getByRole("button", { name: "Agregar", exact: true }).click();
    await expect(add.getByRole("combobox", { name: "Seleccionar forma de pago", exact: true })).toBeDisabled();
    await expect(panel.getByLabel("Valor recibido por la forma 1", { exact: true })).toHaveValue("60");
  } else {
    await add.getByRole("combobox", { name: "Seleccionar forma de pago", exact: true }).click();
    await page.getByRole("option", { name: /Segunda caja/ }).click();
    await add.getByLabel("Valor de la nueva forma de pago").fill("25");
    await add.getByRole("button", { name: "Agregar forma de pago", exact: true }).click();
    await expect(nextInvoice.getByRole("button", { name: "Agregar", exact: true })).toBeDisabled();
    await expect(panel.getByLabel("Valor recibido por la forma 1", { exact: true })).toHaveValue("100");
  }
  expect(reads).toBe(1); expect(positions).toBe(1); expect(catalogs).toBe(multipleForms ? 0 : 1);
});
