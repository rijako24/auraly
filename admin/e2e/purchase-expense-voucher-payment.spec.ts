import { expect, test } from "@playwright/test";
import type { AccountingOpenPosition, SaveVoucherDraft } from "../src/services/api/accounting";
import { financialVoucherScenario, installFinancialVoucherContext } from "./support/financial-voucher";

for (const expense of [false, true]) for (const direct of [false, true]) for (const blocked of direct ? [false] : [false, true]) {
  test(`${expense ? "gasto" : "compra"}: Pagar ${direct ? "abre pago directo sin permisos contables" : blocked ? "expone la apertura sin conciliar" : "prepara su CxP exacta en el egreso existente"}`, async ({ page, baseURL }, testInfo) => {
    const scenario = financialVoucherScenario();
    scenario.user.permissions.push("expenses.read", "purchasing.goods-receipts.read", "payables.read");
    scenario.user.permissions = scenario.user.permissions.filter(permission => direct
      ? !permission.startsWith("accounting.") : permission !== "payables.payments.create");
    const number = expense ? "GTO-001" : "EM-001", sourceType = expense ? "Expense" : "GoodsReceipt";
    const position: AccountingOpenPosition = { subledgerKind: "Payable", subledgerId: scenario.positionId,
      partyId: scenario.draft.mainPartyId!, partyName: "Proveedor de prueba", partyIdentification: "900123456",
      sourceDocumentId: scenario.documentId, sourceDocumentType: sourceType, documentNumber: number,
      occurredAt: "2026-11-01T17:00:00Z", dueDate: "2026-11-30T17:00:00Z", currencyCode: "COP",
      originalAmount: 100, outstandingAmount: 80, pendingPaymentAmount: 20, pendingAdjustmentAmount: 0, availableAmount: 60,
      openingEntryId: blocked ? null : "aaaaaaaa-1111-1111-1111-111111111111", openingEntryLineNumber: blocked ? null : 1,
      accountId: blocked ? null : scenario.draft.lines[0].value.accountId,
      accountCode: "22050501", accountName: "Proveedor", accountIsActive: true, openingDebit: 0, openingCredit: 100,
      openingLinkIssue: blocked ? "Falta conciliar la apertura original" : null, counterpartyRoleId: scenario.draft.mainPartyId!, status: "Partial" };
    let lists = 0, positions = 0, detailReads = 0, financialLists = 0, draftReads = 0;
    const writes: SaveVoucherDraft[] = [];
    await installFinancialVoucherContext(page, baseURL!, scenario);
    await page.route("**/api/**", async route => {
      const request = route.request(), url = new URL(request.url()), path = url.pathname;
      if (path.endsWith(expense ? "/expenses" : "/goods-receipts")) {
        lists++;
        const entry = { documentId: scenario.documentId, expenseId: scenario.documentId, documentNumber: number,
          supplierId: scenario.draft.mainPartyId, supplierName: "Proveedor de prueba", supplierInvoiceNumber: "FV-001",
          supplierDocumentNumber: "FV-001", warehouseName: "Principal", receivedAt: position.occurredAt,
          updatedAt: position.occurredAt, issuedAt: position.occurredAt, dueDate: position.dueDate, status: "Processed",
          grandTotal: 100, grossAmount: 100, withholdingAmount: 0, netPayable: 100, conceptName: "Servicio",
          currencyCode: "COP", payableId: position.subledgerId, outstandingAmount: 80, payableStatus: "Partial", chargeReturned: false };
        await route.fulfill({ json: { items: [entry, { ...entry, documentId: "aaaaaaaa-2222-1111-1111-111111111111",
          expenseId: "aaaaaaaa-2222-1111-1111-111111111111", documentNumber: "SALDADO", outstandingAmount: 0, payableStatus: "Paid" }],
          page: 1, pageSize: 25, totalPages: 1, totalCount: 2, grossTotal: 200, withholdingTotal: 0, netPayableTotal: 200 } }); return;
      }
      if (path.endsWith(`/expenses/${scenario.documentId}`) || path.endsWith(`/goods-receipts/${scenario.documentId}`)) {
        detailReads++;
        await route.fulfill({json:{documentId:scenario.documentId,expenseId:scenario.documentId,documentNumber:number,status:"Processed",
          supplierId:scenario.draft.mainPartyId,supplierName:"Proveedor de prueba",currencyCode:"COP",dueDate:position.dueDate,chargeReturned:false,
          payable:{payableId:position.subledgerId,currencyCode:"COP",status:"PartiallyPaid",outstandingAmount:80,dueDate:position.dueDate,
            partySiteId:"aaaaaaaa-3333-1111-1111-111111111111",partySiteName:"Principal"}}});return;
      }
      if(path.endsWith("/payables")){
        expect(url.searchParams.get("payableId")).toBe(position.subledgerId);
        await route.fulfill({json:{items:[{payableId:position.subledgerId,documentNumber:number,currencyCode:"COP",outstandingAmount:80,
          dueDate:position.dueDate,isOverdue:false,status:"PartiallyPaid"}],page:1,pageSize:20,totalCount:1,totalPages:1,currencyTotals:[]}});return;
      }
      if(path.endsWith("/accounting/settlement-configuration")){
        await route.fulfill({json:{isAccountingEnabled:false,bankAccounts:[],paymentMethods:{contractVersion:2,forms:[]}}});return;
      }
      if(path.endsWith("/parties/site-options")){
        await route.fulfill({json:{items:[],page:1,pageSize:20,totalCount:0,totalPages:0}});return;
      }
      if (path.endsWith("/accounting/documents")) financialLists++;
      if (path.endsWith("/accounting/open-positions")) {
        positions++;
        expect(url.searchParams.get("subledgerId")).toBe(position.subledgerId);
        expect(url.searchParams.get("subledgerKind")).toBe("Payable");
        // Voucher-scoped searches require a party. This exact-ID lookup uses
        // the canonical Payable query; the editor selects Disbursement below.
        expect(url.searchParams.has("voucherTypeCode")).toBe(false);
        expect(url.searchParams.get("page")).toBe("1");
        expect(url.searchParams.get("effectiveOn")).toMatch(/^\d{4}-\d{2}-\d{2}$/);
        expect(url.searchParams.get("pageSize")).toBe("1");
        await route.fulfill({ json: { items: [position], page: 1, pageSize: 1, totalCount: 1, totalOutstandingAmount: 80 } }); return;
      }
      if (path.includes("/accounting/manual/drafts/")) {
        if (request.method() === "GET") draftReads++;
        else if (request.method() === "PUT") {
          const value = request.postDataJSON() as SaveVoucherDraft; writes.push(value);
          await route.fulfill({ json: { ...scenario.draft, ...value, rowVersion: "AAAAAAAAAAI=",
            lines: value.lines.map(line => ({ value: line, accountCode: "22050501", accountName: "Proveedor",
              partyName: "Proveedor de prueba", partyIdentification: "900123456", costCenterName: null })) } }); return;
        }
      }
      await route.fallback();
    });
    await page.goto(expense ? "/dashboard/expenses" : "/dashboard/purchasing/goods-receipts");
    await expect(page.getByRole("button", { name: /Pagar .*SALDADO/ })).toHaveCount(0);
    const payButton=page.getByRole("button", { name: `Pagar ${expense ? "gasto" : "compra"} ${number}`, exact: true });
    if(direct){await payButton.focus();await page.keyboard.press("Enter");}else await payButton.click();
    expect(positions).toBe(0); expect(detailReads).toBe(0);
    await expect(page.getByRole("menuitem",{name:direct?"Comprobante de egreso":"Pago directo",exact:true})).toBeDisabled();
    if(direct){
      await page.getByRole("menuitem",{name:"Pago directo",exact:true}).press("Enter");
      const wizard=page.getByRole("dialog",{name:"Pago a proveedores",exact:true});
      await expect(wizard.getByLabel(`Abono para ${number}`,{exact:true})).toHaveValue("80");
      await expect(wizard.getByRole("combobox",{name:"Seleccionar proveedor y sede",exact:true})).toContainText("Proveedor de prueba · Principal");
      await wizard.getByRole("button",{name:"Ir a pagar",exact:true}).click();
      await page.getByRole("menuitem",{name:"Pago directo",exact:true}).click();
      await expect(wizard.getByRole("button",{name:"Fecha del pago",exact:true})).toBeVisible();
      await page.setViewportSize({width:390,height:844});
      await page.screenshot({path:testInfo.outputPath("direct-payment-mobile.png"),fullPage:true});
      expect(lists).toBe(1);expect(detailReads).toBe(1);expect(positions).toBe(0);expect(financialLists).toBe(0);expect(writes).toHaveLength(0);
      return;
    }
    await page.getByRole("menuitem", { name: "Comprobante de egreso", exact: true }).click();
    await expect(page).toHaveURL(new RegExp(`financial-traceability\\?new=1&payable=${position.subledgerId}`));
    if (blocked) {
      await expect(page.getByRole("alert").filter({ hasText: "Falta conciliar la apertura original" })).toBeVisible();
      await expect(page.getByRole("button", { name: "Guardar borrador", exact: true })).toHaveCount(0);
    } else {
      await expect(page.getByRole("region", { name: "Formas de pago del comprobante", exact: true })).toBeVisible();
      const line = page.getByRole("row", { name: "Partida 1", exact: true });
      await expect(line.getByLabel("Débito de la partida", { exact: true })).toHaveValue("60");
      await expect(line).toContainText("22050501");
      await page.getByRole("button", { name: "Guardar borrador", exact: true }).click();
      await expect(page.getByRole("button", { name: "Guardar borrador", exact: true })).toBeDisabled();
      expect(writes).toHaveLength(1);
      expect(writes[0].voucherTypeCode).toBe("Disbursement");
      expect(writes[0].mainPartyId).toBe(position.partyId);
      expect(writes[0].lines).toHaveLength(1);
      expect(writes[0].lines[0].existingPosition?.subledgerId).toBe(position.subledgerId);
      expect(writes[0].lines[0].debit).toBe(60);
    }
    expect(lists).toBe(1); expect(positions).toBe(1); expect(detailReads).toBe(0);
    expect(financialLists).toBe(0); expect(draftReads).toBe(0);
  });
}
