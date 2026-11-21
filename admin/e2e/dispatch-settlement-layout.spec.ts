import { expect, test } from "@playwright/test";

for (const cashForms of [1,2,0]) test(`liquidación administrativa atribuye la diferencia con ${cashForms} formas de efectivo`, async ({ page, baseURL }) => {
  test.setTimeout(120_000);
  const tenantId = "11111111-1111-1111-1111-111111111111", businessId = "22222222-2222-2222-2222-222222222222";
  const dispatchId = "44444444-4444-4444-4444-444444444444";
  const user = { userId: "33333333-3333-3333-3333-333333333333", tenantId, tenantKey: "AURALY", username: "receiver",
    firstName: "Prueba", lastName: "Receptor", roles: [], permissions: ["dispatches.read", "dispatches.read-all", "dispatches.settle"] };
  await page.context().addCookies([{ name: "auth_token", value: "e2e", url: baseURL!, httpOnly: true }]);
  await page.addInitScript(({ tenantId, businessId, user }) => {
    localStorage.setItem("selected_tenant_id", tenantId); localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
  }, { tenantId, businessId, user });
  const dispatch = { dispatchId, businessId, dispatchNumber: "DSP-20261118-01A1EDC180005E08A6D06B970A039C77", scheduledDate: "2026-11-16",
    driverName: "Transportador", status: "PendingSettlement", rowVersion: "AAAAAAAAAAA=", documentCount: 1, lineCount: 1,
    expectedQuantity: 1, verifiedQuantity: 1, shortageQuantity: 0, documents: [], lines: [], shortages: [] };
  const formId="55555555-5555-5555-5555-555555555555";
  const nativeCash={paymentMethodId:formId,code:"CASH-CUSTOM",name:"Caja principal de reparto",methodCode:"Cash",accountCode:"110506",accountName:"Caja reparto",version:1,isActive:true};
  const documents=Array.from({length:cashForms},(_,index)=>({dispatchSourceDocumentId:`source-${index}`,sourceDocumentId:`sale-${index}`,documentNumber:`CVI-${index}`,customerName:"Cliente",lines:[],returns:[],payments:[{paymentId:`payment-${index}`,applicationType:"InvoicePayment",paymentMethod:"Cash",paymentMethodId:index===0?formId:"66666666-6666-6666-6666-666666666666",formName:index===0?nativeCash.name:"Otra caja",amount:100000/cashForms,evidenceUrl:"https://media.auraly.test/private/proof.png",evidenceReadUrl:"https://media.auraly.test/private/proof.png?read=temporary"}]}));
  const execution = { ...dispatch, documents, expenses: [], settlement: { grossCash: 100000, approvedCashExpenses: 0,
    expectedCash: 100000, declaredCash: 100000, difference: 0, depositTotal: 0, remainingCreditTotal: 0,
    creditAdvanceTotal: 0, returnTotal: 0, undeliveredTotal: 0, dispatchTotal: 100000, balanceDifference: 0, status: "PendingReview" } };
  let submitted: { cashReceived: number } | null = null;
  await page.route("**/api/**", async route => {
    const path = new URL(route.request().url()).pathname;
    let body: unknown = [];
    if (path.endsWith("/auth/me")) body = user;
    else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Prueba" }];
    else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Sede", timeZone: "America/Bogota" }];
    else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, roles: [], permissions: user.permissions };
    else if (path.replace(/\/$/,"").endsWith("/payment-methods")) body={items:[nativeCash],page:1,pageSize:50,totalCount:1};
    else if (path.endsWith("/work-sessions/current")) body = { workSessionId: "receiver-session" };
    else if (path.endsWith("/closures/inbox")) body = { items: [{ closureId: "close-id", type: "Dispatch", cashClosure: null,
      dispatchClosure: { dispatchId, dispatchNumber: dispatch.dispatchNumber, businessId, businessName: "Sede", driverName: "Transportador",
        closedAt: "2026-11-17T14:00:00Z", openedAt: "2026-11-17T12:00:00Z", status: "PendingReview", expectedCash: 100000, declaredCash: 100000, cashReceived: null, difference: 0 } }], page: 1, pageSize: 50, totalItems: 1 };
    else if (path.endsWith("/settle")) {
      submitted = route.request().postDataJSON();
      body = { ...execution, status: "SettlementProcessing", settlement: { ...execution.settlement, status: "Processing" } };
    } else if (path.endsWith("/execution")) body = execution;
    else if (path.endsWith(`/dispatches/${dispatchId}`)) body = dispatch;
    else if (path.endsWith("/dispatches")) body = { items: [dispatch], page: 1, pageSize: 25, totalCount: 1, totalPages: 1 };
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
  });
  await page.route("https://media.auraly.test/**", route => route.fulfill({status:200,contentType:"image/png",body:Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aWQAAAABJRU5ErkJggg==","base64")}));
  await page.goto("/dashboard/cash-differences");
  await page.getByRole("button", { name: "Ver despacho", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Recepción y liquidación del despacho" });
  if(cashForms>0){
    await expect(dialog.getByRole("img",{name:"Soporte de CVI-0",exact:true})).toHaveAttribute("src","https://media.auraly.test/private/proof.png?read=temporary");
    await dialog.getByRole("button",{name:/Ampliar soporte/}).first().click();
    await expect(page.getByRole("img",{name:"Soporte ampliado del pago",exact:true})).toHaveAttribute("src","https://media.auraly.test/private/proof.png?read=temporary");
    await page.getByRole("dialog",{name:"Soporte del pago",exact:true}).getByRole("button",{name:"Close",exact:true}).click();
  }
  const input = dialog.getByRole("textbox", { name: "Efectivo físico recibido", exact: true });
  await expect(input).toHaveValue("100.000");
  await input.fill("90000"); await expect(input).toHaveValue("90.000");
  const result = dialog.getByRole("status", { name: "Resultado del efectivo" });
  await expect(result).toContainText("Faltante"); await expect(result).toContainText("10.000");
  for (const width of [1440, 1366, 820]) {
    await page.setViewportSize({ width, height: width===1366 ? 768 : 1000 });
    const a = await input.boundingBox(), b = await result.boundingBox();
    expect(a).not.toBeNull(); expect(b).not.toBeNull();
    expect(Math.abs(a!.y - b!.y)).toBeLessThanOrEqual(1); expect(a!.height).toBe(b!.height);
    expect(await input.evaluate(element=>getComputedStyle(element).fontSize)).toBe(await result.locator("strong").evaluate(element=>getComputedStyle(element).fontSize));
  }
  await page.screenshot({ path: "../work/release-preparation-current/liquidacion-alineada.png", fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await dialog.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
  await dialog.getByPlaceholder("¿Por qué faltó efectivo?").fill("Validación del efectivo entregado");
  if(cashForms!==1) await expect(dialog.getByRole("combobox",{name:"Forma de efectivo para la diferencia"})).toContainText(nativeCash.name);
  else await expect(dialog.getByRole("combobox",{name:"Forma de efectivo para la diferencia"})).toHaveCount(0);
  await dialog.getByRole("button", { name: "Aceptar liquidación", exact: true }).click();
  await expect.poll(() => submitted).not.toBeNull();
  expect(submitted).toEqual(expect.objectContaining({ cashReceived: 90000 }));
  if(cashForms!==1) expect(submitted).toEqual(expect.objectContaining({cashDifferencePaymentMethodId:formId}));
  else expect(submitted).not.toHaveProperty("cashDifferencePaymentMethodId");
});
