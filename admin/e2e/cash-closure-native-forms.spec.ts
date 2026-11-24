import { expect, test } from "@playwright/test";

for (const documentType of ["SalesInvoice", "SalesReceipt"]) for (const type of ["Cash", "DebitCard", "CreditCard", "Transfer"]) for(const creditSale of documentType==="SalesReceipt"?[false,true]:[false]) for(const canCorrect of type==="Cash"&&documentType==="SalesReceipt"?[true,false]:[true]) {
  test(`corregir recaudo ${type} de ${documentType} ${creditSale?"a crédito":"de contado"} exige forma y soporte sin cambiar importe ${canCorrect?"con permiso":"sin permiso"}`, async ({ page, baseURL }) => {
    const tenantId = "11111111-1111-1111-1111-111111111111", businessId = "22222222-2222-2222-2222-222222222222";
    const user = { userId: "33333333-3333-3333-3333-333333333333", tenantId, tenantKey: "AURALY", username: "reviewer", firstName: "Revisor", lastName: "Prueba", roles: [], permissions: ["work-sessions.differences.read", "work-sessions.closures.reconcile",...(canCorrect?["sales.payments.correct"]:[])] };
    await page.context().addCookies([{ name: "auth_token", value: "e2e", url: baseURL!, httpOnly: true }]);
    await page.addInitScript(({ tenantId, businessId, user }) => {
      localStorage.setItem("selected_tenant_id", tenantId); localStorage.setItem("selected_business_id", businessId);
      localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
    }, { tenantId, businessId, user });
    const totals = ["Cash", "Card", "Transfer"].map(paymentMethodCode => ({ paymentMethodCode, salesAmount: paymentMethodCode === "Cash" ? 11900 : 0, refundAmount: 0, otherAmount: 0, netAmount: paymentMethodCode === "Cash" ? 11900 : 0, countedAmount: paymentMethodCode === "Cash" ? 11900 : 0, difference: 0, requiresCount: true }));
    const closure = { workSessionClosureId: "cash-id", workSessionId: "session-id", businessId, businessName: "Sede", warehouseName: "Bodega", userId: user.userId, userName: "Cajero prueba", openedAt: "2026-11-17T12:00:00Z", closedAt: "2026-11-17T14:00:00Z", salesCount: 1, creditSalesCount: 0, returnCount: 0, totalSales: 11900, totalRefunds: 0, netAmount: 11900, reconciliationStatus: "Pending", accountingStatus: "Posted", paymentTotals: totals };
    const sale = { verificationKey: creditSale?"CreditSale:original:0":"Sale:original:1", paymentMethodCode: creditSale?"Credit":"Cash", movementType: creditSale?"CreditSale":"Sale", sourceDocumentType: documentType, sourceId: "original", documentNumber: "FVL-123", sourceNumber: creditSale?0:1, amount: creditSale?30000:11900, collectibleAmount:creditSale?11900:null, occurredAt: "2026-11-17T13:00:00Z", tenderMethodCode: creditSale?"Credit":"Cash", status: null };
    const names: Record<string, string> = { Cash: "Efectivo", DebitCard: "Tarjeta débito", CreditCard: "Tarjeta crédito", Transfer: "Transferencia" };
    const forms = [1, 2].map(index => ({ paymentMethodId: `${type}-${index}`, code: `QA-${index}`, name: `${names[type]} entidad ${index} con nombre completo`, methodCode: type, version: index, accountCode: `11100${index}`, accountName: `Cuenta ${index}`, isActive: true, useForSales: true }));
    let validated: Array<Record<string, unknown>> = [], details = 0, formReads = 0;
    await page.route("**/api/**", async route => {
      const url = new URL(route.request().url()), path = url.pathname; let body: unknown = [];
      if (path.endsWith("/auth/me")) body = user;
      else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Prueba" }];
      else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Sede", timeZone: "America/Bogota" }];
      else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, roles: [], permissions: user.permissions };
      else if (path.endsWith("/closures/inbox")) body = { items: [{ closureId: "cash-id", type: "Cash", cashClosure: closure, dispatchClosure: null }], page: 1, pageSize: 50, totalItems: 1 };
      else if (path.endsWith("/payment-verifications/page")) { details++; body = { items: [sale], groups: [{ paymentMethodCode: sale.paymentMethodCode, movementType: sale.movementType, count: 1, totalAmount: sale.amount }], page: 1, pageSize: 100, totalItems: 1 }; }
      else if (path.endsWith("/invoice-charges/page")) body = { items: [], page: 1, pageSize: 50, totalItems: 0, totalAmount: 0, closureSummary: { count: 0, invoicedAmount: 0, expenseAmount: 0, paymentMethods: [] } };
      else if (path.endsWith("/reference-options/payment-method")) body = Object.entries(names).map(([code, label]) => ({ code, label, isActive: true }));
      else if (path.endsWith("/reference-options/card-franchise")) body = [{ code: "Visa", label: "Visa", isActive: true }];
      else if (path.endsWith("/payment-methods/")) {
        formReads++;
        const method = url.searchParams.get("methodCode"), items = method === type ? forms : [];
        body = { items, totalCount: items.length, page: 1, pageSize: 10 };
      }
      else if (path.endsWith("/payment-corrections/validate")) { validated = route.request().postDataJSON(); body = {}; }
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
    });
    await page.goto("/dashboard/cash-differences");
    await page.getByRole("button", { name: "Conciliar", exact: true }).click();
    const parent = page.getByRole("dialog", { name: "Conciliar cierre", exact: true });
    await parent.getByRole("button", { name: creditSale?/Facturas a crédito/:/Facturas y comprobantes/ }).first().click();
    const action=creditSale?"Registrar recaudo":"Corregir";
    if(!canCorrect){await expect(parent.getByRole("button",{name:action,exact:true})).toHaveCount(0);return;}
    await parent.getByRole("button", { name: action, exact: true }).first().click();
    const editor = page.getByRole("dialog", { name: "Corregir medio de recaudo", exact: true });
    if (documentType === "SalesInvoice") await expect(editor).toContainText("La factura electrónica emitida se conserva");
    await expect(editor.getByLabel("Valor del recaudo")).toBeDisabled();
    if (type !== "Cash" || creditSale) {
      await editor.getByRole("combobox", { name: "Tipo de pago real", exact: true }).click();
      await page.getByRole("option", { name: names[type], exact: true }).click();
    }
    const apply = editor.getByRole("button", { name: "Aplicar", exact: true });
    await editor.getByLabel("Motivo de la corrección").fill("Comprobante verificado");
    await expect(apply).toBeDisabled();
    await editor.getByRole("combobox", { name: "Forma de pago del recaudo" }).click();
    await page.getByRole("option", { name: new RegExp(forms[1].name) }).click();
    if (type === "Transfer") {
      await expect(apply).toBeDisabled(); await editor.getByLabel("Referencia de transferencia").fill("TR-123");
    } else if (type === "DebitCard" || type === "CreditCard") {
      await expect(apply).toBeDisabled();
      await editor.getByRole("combobox", { name: "Franquicia", exact: true }).click();
      await page.getByRole("option", { name: "Visa", exact: true }).click();
      await expect(apply).toBeDisabled(); await editor.getByLabel("Número de aprobación", { exact: true }).fill("AP-123");
    } else {
      await expect(editor.getByLabel("Referencia de transferencia")).toHaveCount(0);
      await expect(editor.getByLabel("Número de aprobación", { exact: true })).toHaveCount(0);
    }
    const before = details;
    await expect(apply).toBeEnabled();
    await page.setViewportSize({ width: 390, height: 844 });
    expect(await editor.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
    await apply.click(); await expect(editor).toHaveCount(0);
    expect(validated).toHaveLength(1); expect(validated[0]).toMatchObject({ paymentMethodId: forms[1].paymentMethodId, paymentMethodVersion: 2, tenderMethodCode: type, amount: 11900 });
    if (type === "Transfer") expect(validated[0].reference).toBe("TR-123");
    if (type.endsWith("Card")) expect(validated[0]).toMatchObject({ cardFranchiseCode: "Visa", approvalNumber: "AP-123" });
    expect(details).toBe(before); expect(formReads).toBe(type === "Cash" || creditSale ? 1 : 2);
  });
}
