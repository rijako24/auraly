import { expectNoHorizontalOverflow } from "./support/layout-bounds";
import { expect, test } from "@playwright/test";
import type { DeliveryResultInput } from "../src/services/api/dispatches";

for (const scenario of ["Cash", "DebitCard", "CreditCard", "Transfer", "TransferOffline", "TransferOfflineRetry", "PriorReturn"] as const) {
  const offline=scenario==="TransferOffline"||scenario==="TransferOfflineRetry";
  const retry=scenario==="TransferOfflineRetry";
  const methodCode=offline?"Transfer":scenario==="PriorReturn"?"Cash":scenario;
  test(`entrega captura ${scenario} con el modal compartido sin confirmar ni consultar dos veces`, async ({ page, baseURL, context }) => {
    test.setTimeout(120_000);
    const tenantId = "11111111-1111-1111-1111-111111111111", businessId = "22222222-2222-2222-2222-222222222222";
    const user = { userId: "33333333-3333-3333-3333-333333333333", tenantId, tenantKey: "AURALY", username: "driver",
      firstName: "Conductor", lastName: "Prueba", roles: [], permissions: ["dispatches.read", "dispatches.delivery.execute"] };
    await page.context().addCookies([{ name: "auth_token", value: "e2e", url: baseURL!, httpOnly: true }]);
    await page.addInitScript(({ tenantId, businessId, user }) => {
      localStorage.setItem("selected_tenant_id", tenantId); localStorage.setItem("selected_business_id", businessId);
      localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
    }, { tenantId, businessId, user });
    const dispatchNumber = "DSP-20261118-01A1EDC180005E08A6D06B970A039C77";
    const dispatch = { dispatchId: "dispatch-id", dispatchNumber, scheduledDate: "2026-11-17", driverName: "Conductor",
      status: "InDelivery", documentCount: 1, lineCount: 1, expectedQuantity: 1, verifiedQuantity: 1, shortageQuantity: 0,
      rowVersion: "AAAAAAAAAAA=", updatedAt: "2026-11-17T12:00:00Z" };
    const document = { dispatchSourceDocumentId: "document-id", sourceDocumentId: "sale-id", documentType: "SalesInvoice",
      documentNumber: "FV-100", customerName: "Cliente de entrega", documentTotal: scenario==="PriorReturn"?37500:50000, creditAmount: 0, sequence: 1,
      deliveryStatus: "Pending", payments: [], returns: [], lines: scenario==="PriorReturn"?[{originalLineNumber:1,productId:"product",productCode:"P-1",description:"Producto con devolución previa",quantity:2,unitPrice:25000,lineTotal:50000,returnedQuantity:.5,returnedLineTotal:12500}]:[], notes: null };
    const execution = { ...dispatch, expenses: [], settlement: null, documents: [document] };
    const labels = { Cash: "Efectivo", DebitCard: "Tarjeta débito", CreditCard: "Tarjeta crédito", Transfer: "Transferencia" };
    // Debit and transfer have two choices; credit and cash have one.
    const forms = (code: string) => Array.from({ length: code === "DebitCard" || code === "Transfer" ? 2 : 1 }, (_, index) => ({
      paymentMethodId: `${code}-${index}`, code: `${code}-${index}`, name: `${labels[code as keyof typeof labels]} ${index + 1}`,
      methodCode: code, accountCode: "111005", accountName: `Cuenta ${code} ${index + 1}`, bankAccountId: code === "Transfer" ? "bank-id" : null,
      version: 7, isActive: true, useForReceipts: true,
    }));
    const formReads: string[] = [];
    let executionReads = 0, franchiseReads = 0, evidenceUploads = 0;
    const submitted: DeliveryResultInput[] = [];
    await page.route("**/api/**", async route => {
      const url = new URL(route.request().url()), path = url.pathname;
      let body: unknown = [];
      if (path.endsWith("/auth/me")) body = user;
      else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Prueba" }];
      else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Sede", timeZone: "America/Bogota" }];
      else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, roles: [], permissions: user.permissions };
      else if (path.endsWith("/reference-options/payment-method")) body = Object.entries(labels).map(([code, label]) => ({ code, label }));
      else if (path.endsWith("/reference-options/card-franchise")) { franchiseReads++; body = [{ code: "Visa", label: "Visa" }]; }
      else if(path.endsWith("/delivery-reasons"))body=[{dispatchReasonId:"reason",reasonType:url.searchParams.get("type"),code:"CUSTOMER_RETURN",name:"Devolución solicitada por el cliente"}];
      else if (path.replace(/\/$/, "").endsWith("/payment-methods")) {
        const code = url.searchParams.get("methodCode")!; formReads.push(code);
        expect(url.searchParams.get("usage")).toBe("Receipts");
        expect(Number(url.searchParams.get("pageSize"))).toBeLessThanOrEqual(10);
        body = { items: forms(code), page: 1, pageSize: 10, totalCount: forms(code).length };
      } else if (path.endsWith("/dispatches/dispatch-id/execution")) { executionReads++; body = execution; }
      else if (path.endsWith("/dispatches/dispatch-id/evidence")) { evidenceUploads++; body={url:"https://qa.invalid/transfer.png"}; }
      else if (path.endsWith("/dispatches/dispatch-id/delivery-results")) {
        submitted.push(route.request().postDataJSON());
        if(retry&&submitted.length===1){await route.fulfill({status:503,contentType:"application/json",body:JSON.stringify({message:"Confirmación no recibida. Reintenta."})});return;}
        body = { ...execution, documents: [{ ...document, deliveryStatus: "Delivered", payments: submitted[0].payments }] };
      } else if (path.endsWith("/dispatches")) body = { items: [dispatch], page: 1, pageSize: 50, totalCount: 1, totalPages: 1 };
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
    });
    await page.goto("/dashboard/deliveries");
    await page.setViewportSize({ width: 390, height: 844 });
    const dispatchCard = page.getByRole("button", { name: new RegExp(dispatchNumber) });
    await expect(dispatchCard).toBeVisible();
    expect(await page.evaluate(() => globalThis.document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await dispatchCard.click();
    await page.getByRole("button", { name: "Entregar", exact: true }).click();
    const delivery = page.getByRole("dialog", { name: "Confirmar entrega", exact: true });
    await expect(delivery.getByRole("combobox", { name: "Forma de pago de la entrega" })).toContainText("Efectivo 1");
    const confirm = delivery.getByRole("button", { name: "Confirmar entrega", exact: true });
    if(scenario==="PriorReturn"){
      await delivery.getByRole("button",{name:/Agregar devolución|Agregar productos/}).click();
      const returned=page.getByRole("dialog",{name:"Agregar productos devueltos",exact:true});
      await expect(returned).toContainText("Disponible: 1.5");
      await expect(returned.getByRole("combobox",{name:"Motivo de devolución",exact:true})).toContainText("Devolución solicitada por el cliente");
      const quantity=returned.getByRole("spinbutton",{name:"Cantidad devuelta de Producto con devolución previa",exact:true});
      await expect(quantity).toHaveAttribute("max","1.5");
      await quantity.fill("0.25");
      await returned.getByRole("button",{name:"Aplicar devolución",exact:true}).click();
    }
    if (methodCode === "Cash") await page.screenshot({ path: test.info().outputPath("entrega-efectivo.png") });
    if (methodCode !== "Cash") {
      await delivery.getByRole("button", { name: labels[methodCode], exact: true }).click();
      const card = methodCode !== "Transfer";
      const details = page.getByRole("dialog", { name: card ? "Datos de la tarjeta" : "Datos de la transferencia", exact: true });
      await expect(details).toBeVisible();
      if (methodCode === "DebitCard") {
        await page.setViewportSize({ width: 360, height: 740 });
        await expect.poll(() => formReads.filter(code => code === methodCode).length).toBe(1);
        await expect(details.getByRole("button", { name: "Guardar datos · Enter", exact: true })).toBeDisabled();
        await details.getByRole("combobox", { name: "Forma de pago de la entrega" }).click();
        await expect(page.getByRole("option", { name: /Tarjeta débito 2/ })).toBeVisible();
        await page.keyboard.press("Escape");
        await expect(details).toBeVisible();
        await details.getByRole("combobox", { name: "Forma de pago de la entrega" }).click();
        await page.screenshot({ path: test.info().outputPath("entrega-selector-tarjetas.png") });
        await page.getByRole("option", { name: /Tarjeta débito 2/ }).click();
      }
      if (methodCode === "Transfer") {
        await page.setViewportSize({ width: 360, height: 740 });
        await expect(details.getByRole("button", { name: "Guardar transferencia", exact: true })).toBeDisabled();
        await details.getByRole("combobox", { name: "Forma de pago de la entrega" }).click();
        await expect(page.getByRole("option", { name: /Transferencia 1/ })).toBeVisible();
        await page.keyboard.press("Escape");
        await expect(details).toBeVisible();
        await details.getByRole("combobox", { name: "Forma de pago de la entrega" }).click();
        await page.getByRole("option", { name: /Transferencia 1/ }).click();
      }
      await expect(details).toBeVisible();
      expect(submitted).toHaveLength(0);
      if (card) {
        await details.getByRole("combobox", { name: "Franquicia" }).click();
        await page.getByRole("option", { name: "Visa", exact: true }).click();
        await details.getByRole("textbox", { name: "Número de aprobación" }).fill("APROB-100");
        if (methodCode === "DebitCard") await page.screenshot({ path: test.info().outputPath("entrega-datos-tarjeta.png") });
        await details.getByRole("button", { name: "Guardar datos · Enter", exact: true }).click();
      } else {
        await expect(details).toContainText("Transferencia 1 · 111005 · Cuenta Transfer 1");
        await details.getByRole("textbox", { name: "Referencia", exact: true }).fill("TRANS-100");
        await page.screenshot({ path: test.info().outputPath("entrega-datos-transferencia.png") });
        await details.getByRole("button", { name: "Guardar transferencia", exact: true }).click();
      }
      await expect(delivery).toBeVisible(); await expect(details).toHaveCount(0);
      await expect(confirm).toBeEnabled(); expect(submitted).toHaveLength(0);
      // Re-selecting the saved choice must not ask for the same details again.
      await delivery.getByRole("button", { name: "Datos del pago", exact: true }).click();
      await details.getByRole("combobox", { name: "Forma de pago de la entrega" }).click();
      await page.getByRole("option", { name: new RegExp(`${labels[methodCode]} ${methodCode === "DebitCard" ? 2 : 1}`) }).click();
      await expect(details).toHaveCount(1);
      await details.getByRole("button", { name: card ? "Cerrar datos de tarjeta" : "Cerrar datos de transferencia", exact: true }).click();
      await expect(details).toHaveCount(0); await expect(confirm).toBeEnabled();
      // Cancel an edit without discarding the previously saved evidence.
      await delivery.getByRole("button", { name: "Datos del pago", exact: true }).click();
      await expect(details).toBeVisible();
      await page.setViewportSize({ width: 390, height: 844 });
      expect(await details.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
      await details.getByRole("button", { name: card ? "Cerrar datos de tarjeta" : "Cerrar datos de transferencia", exact: true }).click();
      await expect(confirm).toBeEnabled();
      await expectNoHorizontalOverflow(delivery);
      if (methodCode === "DebitCard") await delivery.screenshot({ path: test.info().outputPath("delivery-390.png") });
      if (methodCode === "DebitCard") {
        await delivery.getByRole("button", { name: "Datos del pago", exact: true }).click();
        await details.getByRole("combobox", { name: "Forma de pago de la entrega" }).click();
        await page.getByRole("option", { name: /Tarjeta débito 1/ }).click();
        await expect(details.getByRole("textbox", { name: "Número de aprobación" })).toHaveValue("");
        await details.getByRole("button", { name: "Cerrar datos de tarjeta", exact: true }).click();
        await expect(confirm).toBeEnabled();
        await expect(delivery).toContainText("Tarjeta débito 2");
        await delivery.getByRole("button", { name: "Datos del pago", exact: true }).click();
        await details.getByRole("combobox", { name: "Forma de pago de la entrega" }).click();
        await page.getByRole("option", { name: /Tarjeta débito 1/ }).click();
        await details.getByRole("combobox", { name: "Franquicia" }).click();
        await page.getByRole("option", { name: "Visa", exact: true }).click();
        await details.getByRole("textbox", { name: "Número de aprobación" }).fill("APROB-100");
        await details.getByRole("button", { name: "Guardar datos · Enter", exact: true }).click();
      }
      if (methodCode === "Transfer") {
        await delivery.getByRole("button", { name: "Datos del pago", exact: true }).click();
        await details.getByRole("combobox", { name: "Forma de pago de la entrega" }).click();
        await page.getByRole("option", { name: /Transferencia 2/ }).click();
        await expect(details.getByRole("textbox", { name: "Referencia", exact: true })).toHaveValue("");
        await details.getByRole("button", { name: "Cerrar datos de transferencia", exact: true }).click();
        await expect(confirm).toBeEnabled();
        await expect(delivery).toContainText("Transferencia 1");
        await expect(delivery).toContainText("TRANS-100");
      }
    } else {
      await expect(page.getByRole("dialog")).toHaveCount(1);
      expect(franchiseReads).toBe(0);
    }
    if(offline) {
      await context.setOffline(true);
      await delivery.locator('input[type="file"]').setInputFiles({name:"transfer.png",mimeType:"image/png",
        buffer:Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aVx8AAAAASUVORK5CYII=","base64")});
    }
    await confirm.click();
    if(offline) {
      await expect(delivery).toHaveCount(0);
      expect(submitted).toHaveLength(0);
      await context.setOffline(false);
    }
    await expect.poll(() => submitted.length).toBe(1);
    if(retry) {
      await expect(page.getByText("Confirmación no recibida. Reintenta.",{exact:true})).toBeVisible();
      await page.getByRole("button",{name:"Continuar",exact:true}).click();
      await expect.poll(()=>submitted.length).toBe(2);
      expect(submitted[1]).toEqual(submitted[0]);
    }
    if(offline) {
      expect(evidenceUploads).toBe(1);
      expect(submitted[0].payments[0].evidenceUrl).toBe("https://qa.invalid/transfer.png");
      await expect(page.getByRole("button",{name:/Cliente de entrega FV-100/})).toContainText("Entregado");
    }
    expect(submitted[0].payments).toEqual([expect.objectContaining({ amount: scenario==="PriorReturn"?31250:50000, paymentMethod: methodCode,
      paymentMethodId: `${methodCode}-0`, paymentMethodVersion: 7,
      reference: methodCode === "Transfer" ? "TRANS-100" : null,
      cardFranchiseCode: methodCode === "DebitCard" || methodCode === "CreditCard" ? "Visa" : null,
      approvalNumber: methodCode === "DebitCard" || methodCode === "CreditCard" ? "APROB-100" : null })]);
    if(scenario==="PriorReturn")expect(submitted[0].returns).toEqual([expect.objectContaining({originalLineNumber:1,quantity:.25,reasonCode:"CUSTOMER_RETURN",reasonDescription:"Devolución solicitada por el cliente"})]);
    expect(executionReads).toBe(1);
    expect(formReads).toEqual(methodCode === "Cash" ? ["Cash"] : ["Cash", methodCode]);
    expect(franchiseReads).toBe(methodCode === "DebitCard" || methodCode === "CreditCard" ? 1 : 0);
    await expect(delivery).toHaveCount(0);
    await expect.poll(() => page.locator("body").evaluate(element => getComputedStyle(element).pointerEvents)).not.toBe("none");
  });
}
