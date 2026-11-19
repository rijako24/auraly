import { expect, test } from "@playwright/test";
import { readFileSync, writeFileSync } from "node:fs";
import { login } from "./support/auth";
import type { DispatchExecution } from "../src/services/api/dispatches";

test.use({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true });

test("transportador real en móvil guarda, reabre y conserva pagos y soportes en la base local", async ({ page, baseURL }) => {
  test.setTimeout(240_000);
  const profilePath = process.env.AURALY_DRIVER_PROFILE_PATH;
  test.skip(!profilePath, "Requiere el perfil privado de pruebas locales del transportador.");
  expect(["127.0.0.1", "localhost"]).toContain(new URL(baseURL!).hostname);
  const profile = JSON.parse(readFileSync(profilePath!, "utf8").replace(/^\uFEFF/, "")) as {
    TenantKey: string; TenantId: string; BusinessId: string; Username: string; Password: string; DispatchId: string; DispatchNumber: string;
    Sales: Array<{ documentId: string; documentNumber: string; total: number }>;
    Forms: Array<{ paymentMethodId: string; methodCode: string; name: string }>;
  };
  const headers = { "X-Tenant-Id": profile.TenantId, "X-Business-Id": profile.BusinessId };
  const executionUrl = `/api/commerce/v1/dispatches/${profile.DispatchId}/execution`;
  const failures: string[] = [];
  page.on("response", response => {
    if (response.url().includes("/api/") && response.status() >= 500) failures.push(`${response.status()} ${new URL(response.url()).pathname}`);
  });
  await page.context().grantPermissions(["geolocation"]);
  await page.context().setGeolocation({ latitude: 4.6, longitude: -74.1 });
  await page.addInitScript(({ tenant, business }) => {
    localStorage.setItem("selected_tenant_id", tenant); localStorage.setItem("selected_business_id", business);
  }, { tenant: profile.TenantId, business: profile.BusinessId });
  await login(page, { tenantKey: profile.TenantKey, username: profile.Username, password: profile.Password }, 90_000);
  await expect(page).toHaveURL(/\/dashboard\/deliveries/);
  await page.getByRole("button", { name: new RegExp(profile.DispatchNumber) }).click();
  const evidence: Array<Record<string, unknown>> = [];
  const screenshotRoot = "../work/release-preparation-current";
  const png = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aVAAAAABJRU5ErkJggg==", "base64");
  for (const [index, method] of ["Cash", "DebitCard", "CreditCard", "Transfer", "Cash", "NotDelivered"].entries()) {
    const sale = profile.Sales[index];
    const detailsName = method === "Transfer" ? "Datos de la transferencia" : "Datos de la tarjeta";
    await page.getByRole("button").filter({ hasText: sale.documentNumber }).click();
    const delivery = page.getByRole("dialog", { name: "Confirmar entrega", exact: true });
    await expect(delivery).toBeVisible();
    expect(await delivery.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
    if (method === "NotDelivered") await delivery.getByRole("button", { name: "No se entregó", exact: true }).click();
    else {
      if (index === 4) {
        const addReturn = delivery.getByRole("button", { name: "Agregar devolución", exact: true });
        if (await addReturn.isVisible()) await addReturn.click();
        else await delivery.getByRole("button", { name: "Editar", exact: true }).click();
        const returns = page.getByRole("dialog", { name: "Agregar productos devueltos", exact: true });
        await returns.getByRole("spinbutton").first().fill("1");
        await returns.getByRole("button", { name: "Aplicar devolución", exact: true }).click();
      }
      const label = method === "Cash" ? /^Efectivo$/i : method === "DebitCard" ? /tarjeta.*d[eé]bito/i
        : method === "CreditCard" ? /tarjeta.*cr[eé]dito/i : /^Transferencia$/i;
      await delivery.getByRole("button", { name: label }).click();
      const details = page.getByRole("dialog", { name: detailsName, exact: true });
      const form = profile.Forms.find(item => item.methodCode === method)!;
      expect(form).toBeTruthy();
      {
        await (method === "Cash" ? delivery : details).getByRole("combobox", { name: "Forma de pago de la entrega" }).click();
        await page.getByPlaceholder("Buscar código o nombre…").fill(form.name);
        await page.getByRole("option").filter({ hasText: form.name }).first().click();
        if (method !== "Cash" && !await details.isVisible()) await delivery.getByRole("button", { name: "Datos del pago", exact: true }).click();
      }
      if (method !== "Cash") {
        await expect(details).toBeVisible();
        expect(await details.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
        if (method === "Transfer") await details.getByRole("textbox", { name: "Referencia", exact: true }).fill("TRANSFER-LOCAL-17");
        else {
          await details.getByRole("combobox", { name: "Franquicia" }).click();
          await page.getByRole("option").first().click();
          await details.getByRole("textbox", { name: "Número de aprobación" }).fill(`APROB-LOCAL-${index}`);
        }
        await page.screenshot({ path: `${screenshotRoot}/transportador-movil-${method}.png` });
        // A reduced viewport checks reachability with less space for the keyboard.
        await page.setViewportSize({ width: 390, height: 430 });
        await details.getByRole("button", { name: method === "Transfer" ? "Guardar transferencia" : "Guardar datos · Enter", exact: true }).click();
        await page.setViewportSize({ width: 390, height: 844 });
        if (method === "Transfer") await delivery.locator('input[type="file"]').first().setInputFiles({ name: "transferencia-local.png", mimeType: "image/png", buffer: png });
      }
    }
    const confirmation = delivery.getByRole("button", { name: "Confirmar entrega", exact: true });
    await expect(confirmation).toBeEnabled();
    expect((await confirmation.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    const responsePromise = page.waitForResponse(response => response.url().endsWith("/delivery-results") && response.request().method() === "PUT");
    await confirmation.click();
    const response = await responsePromise;
    expect(response.ok(), await response.text()).toBe(true);
    const saved = await response.json() as DispatchExecution;
    const document = saved.documents.find(item => item.sourceDocumentId === sale.documentId)!;
    expect(document.deliveryStatus).toBe(method === "NotDelivered" ? "NotDelivered" : index === 4 ? "PartiallyDelivered" : "Delivered");
    expect(document.payments).toHaveLength(method === "NotDelivered" ? 0 : 1);
    if (method !== "NotDelivered") {
      expect(document.payments[0].paymentMethod).toBe(method);
      expect(document.payments[0].amount).toBeCloseTo(index === 4 ? sale.total / 2 : sale.total, 2);
    }
    if (method === "Transfer") {
      const payment = document.payments[0];
      expect(payment.reference).toBe("TRANSFER-LOCAL-17");
      expect(new URL(payment.evidenceUrl!).hostname).toBe("127.0.0.1");
      expect((await page.request.get(payment.evidenceUrl!)).ok()).toBe(true);
    }
    await expect(delivery).toHaveCount(0);
    if (method === "DebitCard" || method === "Transfer") {
      // Reopen the authoritative saved result; no second upload or implicit modal.
      await page.getByRole("button").filter({ hasText: sale.documentNumber }).click();
      await expect(delivery).toContainText(document.payments[0].formName!);
      await expect(page.getByRole("dialog", { name: detailsName, exact: true })).toHaveCount(0);
      await expect(delivery).toContainText(method === "Transfer" ? "TRANSFER-LOCAL-17" : `APROB-LOCAL-${index}`);
      if (method === "Transfer") await expect(delivery.getByRole("link", { name: "Ver soporte guardado", exact: true })).toBeVisible();
      await delivery.getByRole("button", { name: "Cancelar", exact: true }).click();
    }
    evidence.push({ documentId: sale.documentId, status: document.deliveryStatus, payment: document.payments[0]?.paymentMethod ?? null });
  }
  const read = await page.request.get(executionUrl, { headers }); expect(read.ok()).toBe(true);
  const persisted = await read.json() as DispatchExecution;
  expect(persisted.documents.filter(item => item.deliveryStatus === "Pending")).toHaveLength(4);
  expect((await page.request.put(`/api/commerce/v1/dispatches/${profile.DispatchId}/payments/${persisted.documents.flatMap(item => item.payments)[0].paymentId}/verification`,
    { headers, data: { isVerified: true } })).status()).toBe(403);
  expect(failures).toEqual([]);
  await page.screenshot({ path: `${screenshotRoot}/transportador-movil-despacho.png`, fullPage: true });
  writeFileSync(`${screenshotRoot}/transportador-local-real-browser.json`, JSON.stringify({ mocked: false, viewport: "390x844 / reduced 390x430", evidence, pendingForUser: 4, serverErrors: failures }, null, 2));
});

test("soportes reales rechazan formato, tamaño y despacho no autorizado sin guardar una entrega", async ({ page, baseURL }) => {
  const profilePath = process.env.AURALY_DRIVER_PROFILE_PATH;
  test.skip(!profilePath, "Requiere perfil local privado.");
  expect(["127.0.0.1", "localhost"]).toContain(new URL(baseURL!).hostname);
  const profile = JSON.parse(readFileSync(profilePath!, "utf8").replace(/^\uFEFF/, ""));
  await page.addInitScript(({ tenant, business }) => {
    localStorage.setItem("selected_tenant_id", tenant); localStorage.setItem("selected_business_id", business);
  }, { tenant: profile.TenantId, business: profile.BusinessId });
  await login(page, { tenantKey: profile.TenantKey, username: profile.Username, password: profile.Password }, 90_000);
  const headers = { "X-Tenant-Id": profile.TenantId, "X-Business-Id": profile.BusinessId };
  const evidenceUrl = `/api/commerce/v1/dispatches/${profile.DispatchId}/evidence`;
  for (const file of [
    { name: "no-es-imagen.txt", mimeType: "text/plain", buffer: Buffer.from("soporte de prueba") },
    { name: "imagen-demasiado-grande.png", mimeType: "image/png", buffer: Buffer.alloc(8_388_609) },
  ]) {
    const rejected = await page.request.post(evidenceUrl, { headers, multipart: { file } });
    expect(rejected.status()).toBe(400);
    expect((await rejected.json()).detail).toContain("imagen");
  }
  const foreignUpload = await page.request.post("/api/commerce/v1/dispatches/00000000-0000-0000-0000-000000000001/evidence",
    { headers, multipart: { file: { name: "soporte.png", mimeType: "image/png", buffer: Buffer.from([1, 2, 3]) } } });
  expect(foreignUpload.status()).toBe(404);
});

test("tarjetas y transferencia reales seleccionan la forma dentro del mismo modal móvil y guardan el resultado", async ({ page, baseURL }) => {
  test.setTimeout(120_000);
  const path = process.env.AURALY_DRIVER_PROFILE_PATH;
  test.skip(!path, "Requiere perfil local privado.");
  expect(["127.0.0.1", "localhost"]).toContain(new URL(baseURL!).hostname);
  const profile = JSON.parse(readFileSync(path!, "utf8").replace(/^\uFEFF/, ""));
  await page.context().grantPermissions(["geolocation"]);
  await page.context().setGeolocation({ latitude: 4.6, longitude: -74.1 });
  await page.addInitScript(({ tenant, business }) => {
    localStorage.setItem("selected_tenant_id", tenant); localStorage.setItem("selected_business_id", business);
  }, { tenant: profile.TenantId, business: profile.BusinessId });
  await login(page, { tenantKey: profile.TenantKey, username: profile.Username, password: profile.Password }, 90_000);
  await page.getByRole("button", { name: new RegExp(profile.DispatchNumber) }).click();
  await page.setViewportSize({ width: 360, height: 740 });
  for (const [index, method] of [[1, "DebitCard"], [2, "CreditCard"], [3, "Transfer"]] as const) {
    const sale = profile.Sales[index];
    const form = profile.Forms.find((item: { methodCode: string }) => item.methodCode === method);
    await page.getByRole("button").filter({ hasText: sale.documentNumber }).click();
    const delivery = page.getByRole("dialog", { name: "Confirmar entrega", exact: true });
    await delivery.getByRole("button", { name: "Datos del pago", exact: true }).click();
    const details = page.getByRole("dialog", { name: method === "Transfer" ? "Datos de la transferencia" : "Datos de la tarjeta", exact: true });
    await details.getByRole("combobox", { name: "Forma de pago de la entrega" }).click();
    await page.getByPlaceholder("Buscar código o nombre…").fill(form.name);
    await page.getByRole("option").filter({ hasText: form.name }).first().click();
    await expect(details).toHaveCount(1);
    if (method === "Transfer") await details.getByRole("textbox", { name: "Referencia", exact: true }).fill("TRANSFER-LOCAL-17");
    else {
      await details.getByRole("combobox", { name: "Franquicia" }).click();
      await page.getByRole("option").first().click();
      await details.getByRole("textbox", { name: "Número de aprobación" }).fill(`APROB-MODAL-LOCAL-${index}`);
    }
    expect(await details.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
    if (method === "DebitCard") await page.screenshot({ path: "../work/release-preparation-current/transportador-movil-card-form-dentro.png" });
    if (method === "Transfer") await page.screenshot({ path: "../work/release-preparation-current/transportador-movil-transferencia-dentro.png" });
    await page.setViewportSize({ width: 360, height: 430 });
    await details.getByRole("button", { name: method === "Transfer" ? "Guardar transferencia" : "Guardar datos · Enter", exact: true }).click();
    await page.setViewportSize({ width: 360, height: 740 });
    await expect(delivery.getByRole("combobox", { name: "Forma de pago de la entrega" })).toHaveCount(0);
    const savedPromise = page.waitForResponse(response => response.url().endsWith("/delivery-results") && response.request().method() === "PUT");
    await delivery.getByRole("button", { name: "Confirmar entrega", exact: true }).click();
    const saved = await savedPromise; expect(saved.ok(), await saved.text()).toBe(true);
    const execution = await saved.json() as DispatchExecution;
    const payment = execution.documents.find(document => document.sourceDocumentId === sale.documentId)!.payments[0];
    expect(payment.paymentMethodId).toBe(form.paymentMethodId);
    if (method === "Transfer") {
      expect(payment.reference).toBe("TRANSFER-LOCAL-17");
      expect(payment.evidenceUrl).toBeTruthy();
    } else expect(payment.approvalNumber).toBe(`APROB-MODAL-LOCAL-${index}`);
    expect(payment.amount).toBe(sale.total);
    expect(execution.documents.filter(document => document.deliveryStatus === "Pending")).toHaveLength(4);
  }
});
