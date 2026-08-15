import { expect, test, type Page } from "@playwright/test";
import { login as sharedLogin } from "./support/auth";

async function login(page: Page) {
  await sharedLogin(page, undefined, 60_000);
}

async function selectDemoTenant(page: Page) {
  const demoTenantId = await page.evaluate(async () => {
    const response = await fetch("/api/execution-context/tenants", {
      credentials: "include",
      cache: "no-store",
    });
    if (!response.ok) throw new Error("No se pudieron consultar los tenants autorizados.");
    const tenants = await response.json() as Array<{ tenantId: string; name: string }>;
    const tenant = tenants.find((item) => item.name === "Mimos Baby Spa 2222");
    if (!tenant) throw new Error("El tenant demo no está autorizado para el usuario E2E.");
    return tenant.tenantId;
  });
  const demoBusinessId = await page.evaluate(async (tenantId) => {
    const response = await fetch("/api/execution-context/businesses", {
      credentials: "include",
      cache: "no-store",
      headers: { "X-Tenant-Id": tenantId },
    });
    if (!response.ok) throw new Error("No se pudieron consultar las sedes autorizadas.");
    const businesses = await response.json() as Array<{ businessId: string; name: string }>;
    const business = businesses.find((item) => item.name === "Mimos Baby Spa Principal");
    if (!business) throw new Error("La sede demo no está autorizada para el usuario E2E.");
    return business.businessId;
  }, demoTenantId);

  // The dashboard URL becomes visible before its execution-context queries finish.
  // Open the selector only after its authorized tenant query populated the menu.
  await page.getByRole("button", { name: "Menú de usuario" }).click();
  const organization = page.getByRole("menuitem", { name: /Organización/ });
  await expect(organization).toBeVisible({ timeout: 20_000 });
  await organization.hover();
  const tenant = page.getByRole("menuitemradio", { name: "Mimos Baby Spa 2222" });
  await expect(tenant).toBeVisible();
  await tenant.click();

  await expect.poll(() => page.evaluate(() => localStorage.getItem("selected_tenant_id")), {
    timeout: 20_000,
  }).toBe(demoTenantId);
  await expect.poll(() => page.evaluate(() => localStorage.getItem("selected_business_id")), {
    timeout: 20_000,
  }).toBe(demoBusinessId);
}

async function openOnlinePos(page: Page) {
  await page.goto("/pos");
  const setup = page.getByRole("heading", { name: "Prepara tu espacio de venta" });
  if (await setup.isVisible({ timeout: 20_000 }).catch(() => false)) {
    const business = page.getByRole("button", { name: /Mimos Baby Spa Principal/ });
    if (await business.count()) await business.click();
    const warehouse = page.getByRole("button", { name: /Bodega principal/ });
    if (await warehouse.count()) await warehouse.click();
    const numberingButton = page.getByRole("button", { name: /Guardar numeraci.n/ });
    if (await numberingButton.count() > 0 && await numberingButton.isEnabled()) {
      const numberingResponse = page.waitForResponse((response) =>
        response.request().method() === "PUT" &&
        response.url().includes("/commerce/v1/fiscal/numbering?"),
      );
      await numberingButton.click();
      expect((await numberingResponse).ok()).toBe(true);
    }

    const fiscalKey = page.getByPlaceholder("Ingresa la clave técnica");
    const saveFiscalButton = page.getByRole("button", { name: "Guardar resolución" });
    if (await saveFiscalButton.isVisible().catch(() => false) ||
        await saveFiscalButton.waitFor({ state: "visible", timeout: 20_000 }).then(() => true).catch(() => false)) {
      await expect(fiscalKey).toBeVisible();
      await fiscalKey.fill("Auraly-E2E-Technical-Key-Only");
      const invalidFields = await page.locator("input:invalid").evaluateAll((inputs) =>
        inputs.map((input) => ({
          type: (input as HTMLInputElement).type,
          value: (input as HTMLInputElement).value,
          placeholder: (input as HTMLInputElement).placeholder,
        })),
      );
      expect(invalidFields, "La configuración fiscal visible debe poder enviarse sin campos inválidos").toEqual([]);
      const saveResponse = page.waitForResponse((response) =>
        response.request().method() === "PUT" &&
        response.url().includes("/commerce/v1/fiscal/configuration?"),
      );
      await page.getByRole("button", { name: "Guardar resolución" }).click();
      const response = await saveResponse;
      expect(response.ok()).toBe(true);
      const saved = await response.json() as { isReadyForOnlineSales: boolean };
      expect(saved.isReadyForOnlineSales).toBe(true);
    }
    await expect(page.getByText(/Resolución .* lista/)).toBeVisible({ timeout: 20_000 });
    const continueButton = page.getByRole("button", { name: "Continuar en línea" });
    await expect(continueButton).toBeEnabled({ timeout: 20_000 });
    await continueButton.click();
  }
  await expect(page.getByText("Lista para vender", { exact: true })).toBeVisible({ timeout: 30_000 });
}

test("vende un comprobante online con teclado, imprime tirilla y deja una venta nueva", async ({ page }) => {
  test.setTimeout(150_000);
  await login(page);
  await selectDemoTenant(page);
  await openOnlinePos(page);

  // A failed previous browser run may have left the recoverable active draft intact.
  // Reset it through the UI so this regression also proves the confirmation flow.
  const existingDemoLine = page.getByRole("row").filter({ hasText: "DEMO-001" });
  if (await existingDemoLine.count()) {
    await page.getByRole("button", { name: /Reiniciar/ }).click();
    const confirmation = page.getByRole("alertdialog");
    await expect(confirmation).toBeVisible();
    await confirmation.getByRole("button", { name: /reiniciar/i }).click();
    await expect(existingDemoLine).toHaveCount(0);
  }

  await page.getByRole("button", { name: /Tipo de documento:/ }).click();
  const typeDialog = page.getByRole("dialog");
  await typeDialog.getByRole("button", { name: /Comprobante de venta/ }).click();
  await expect(page.getByText("Comprobante de venta seleccionado", { exact: true })).toBeVisible();

  const scanner = page.locator("#pos-scanner");
  await expect(scanner).toBeFocused();
  await scanner.fill("7700000000001");
  await scanner.press("Enter");
  const line = page.getByRole("row").filter({ hasText: "DEMO-001" });
  await expect(line).toBeVisible({ timeout: 20_000 });
  await expect(scanner).toBeFocused();

  await scanner.press("ArrowDown");
  const quantity = line.getByRole("spinbutton", { name: /Cantidad de/ });
  await expect(quantity).toBeFocused();
  await quantity.fill("2");
  await quantity.press("Enter");
  await expect(scanner).toBeFocused();

  await page.getByRole("button", { name: /^Cobrar/ }).click();
  const payment = page.getByRole("form", { name: "Finalizar venta" });
  await expect(payment).toBeVisible();
  const cash = payment.getByRole("textbox", { name: "Valor recibido en Efectivo" });
  await cash.fill("30000");
  await expect(payment.getByText(/Cambio listo:/)).toContainText("$ 5.000");

  const previewPromise = page.waitForEvent("popup");
  await payment.getByRole("button", { name: /Emitir e imprimir/ }).click();
  const preview = await previewPromise;
  await preview.waitForLoadState("domcontentloaded");
  await expect(preview.locator("body")).toContainText("Comprobante", { timeout: 20_000 });
  await preview.close();

  await expect(page.getByText(/emitida e impresa/)).toBeVisible({ timeout: 30_000 });
  await expect(page.getByText("Entregar al cliente", { exact: true })).toBeVisible();
  await expect(page.getByText("Cambio", { exact: true })).toBeVisible();
  await expect(scanner).toBeFocused();
  await expect(page.getByRole("row").filter({ hasText: "DEMO-001" })).toHaveCount(0);
});
