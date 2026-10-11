import { expect, test } from "@playwright/test";
import { login } from "./support/auth";

const businessId = process.env.AURALY_E2E_RESTAURANT_BUSINESS_ID;
const waiterSecret = process.env.AURALY_E2E_RESTAURANT_WAITER_SECRET;

test("comandas vuelve al preparador compartido antes de abrir mesas", async ({ page }) => {
  await login(page, undefined, 60_000);
  const bootstrapResponse = page.waitForResponse((response) =>
    response.request().method() === "GET" &&
    response.url().includes("/pos/workspace/bootstrap"));
  await page.goto("/pos?workspace=change");
  const bootstrap = await bootstrapResponse;
  expect(bootstrap.ok()).toBe(true);
  const options = (await bootstrap.json() as { options: { businessId: string; pointOfSaleMode: string }[] }).options;
  const restaurant = options.find((option) => option.pointOfSaleMode === "RestaurantBar");
  test.skip(!restaurant, "El tenant no tiene una sede Restaurante y Bar.");
  await page.addInitScript((value) => {
    localStorage.setItem("selected_business_id", value);
  }, restaurant!.businessId.toLowerCase());
  await page.goto("/pos/restaurant");
  await expect(page.getByRole("heading", { name: "¿A qué mesa atendemos?" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Periféricos" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Cerrar sesión de venta" })).toBeVisible();
  await page.getByRole("button", { name: "Periféricos" }).click();
  const peripherals = page.getByRole("dialog", { name: "Periféricos" });
  await expect(peripherals.getByText("Punto de venta web")).toBeVisible();
  await expect(peripherals.getByRole("link", { name: /Instalar Auraly/ })).toHaveCount(0);
  await peripherals.getByRole("button", { name: "Cerrar" }).click();
  await page.getByRole("button", { name: "Cerrar sesión de venta" }).click();
  const closure = page.getByRole("dialog").filter({
    hasText: /Cerrar sesión operativa|Autorización requerida/,
  });
  await expect(closure).toBeVisible();
  await closure.getByRole("button", { name: "Cancelar" }).click();
  await page.getByRole("link", { name: "Comandas" }).click();
  await page.getByRole("link", { name: "Volver" }).click();
  await expect(page).toHaveURL(/\/pos\?workspace=change$/);
  await expect(page.getByRole("heading", { name: "Prepara facturación" })).toBeVisible();
  await expect(page.getByText("Elige tu bodega")).toHaveCount(0);
  await page.getByRole("button", { name: "Comprobante de venta" }).click();
  await page.getByRole("button", { name: "Continuar a ventas" }).click();
  await expect(page.getByRole("heading", { name: "¿A qué mesa atendemos?" })).toBeVisible();
});

test("muestra el Salón principal con siete mesas y dos vistas", async ({ page }) => {
  test.skip(!businessId, "Requiere la sede de prueba local.");
  await login(page, undefined, 60_000);
  await page.addInitScript((value) => {
    localStorage.setItem("selected_business_id", value);
  }, businessId!.toLowerCase());
  await page.goto("/pos/restaurant");
  await expect(page.getByRole("heading", { name: "¿A qué mesa atendemos?" })).toBeVisible();
  await expect(page.getByRole("navigation", { name: "Salones" })
    .getByRole("button", { name: "Salón principal" })).toHaveAttribute("aria-pressed", "true");
  await expect(page.getByRole("button", { name: "Plano", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Lista de mesas" })).toBeVisible();
  await expect(page.locator('[data-floor-kind="Kitchen"]')).toBeVisible();
  await expect(page.locator('[data-floor-kind="Bathroom"]')).toBeVisible();
  await expect(page.locator('[data-floor-kind="Window"]').first()).toBeVisible();
  await expect(page.getByText("7 mesas en esta vista")).toBeVisible();
  await expect(page.getByRole("button", { name: /Mesa P-01.*sillas/ })).toBeVisible();
  await page.screenshot({ path: "../tmp/restaurant-salon-principal.png", fullPage: true });
  await page.getByRole("button", { name: "Lista de mesas" }).click();
  await expect(page.locator('[data-floor-kind="Kitchen"]')).toHaveCount(0);
  await expect(page.getByRole("heading", { name: /P-0[1-7]/ })).toHaveCount(7);
});

test("el menú digital local abre su configuración sin menú previo", async ({ page }) => {
  test.skip(!businessId, "Requiere la sede de prueba local.");
  await login(page, undefined, 60_000);
  await page.addInitScript((value) => {
    localStorage.setItem("selected_business_id", value);
  }, businessId!.toLowerCase());
  await page.goto("/dashboard/restaurant/menu");
  await expect(page.getByRole("heading", { name: "Presentación" })).toBeVisible();
  await expect(page.getByText("No pudimos cargar el menú.")).toHaveCount(0);
  await page.getByRole("button", { name: /Vista previa/ }).click();
  const preview = page.getByRole("dialog", { name: "Vista previa del menú" });
  await expect(preview).toBeVisible();
  await expect(preview.getByText("Hamburguesas", { exact: true })).toBeVisible();
  await preview.screenshot({ path: "../tmp/restaurant-menu-preview.png" });
});

test("el menú digital explica cuando la sede es de venta directa", async ({ page }) => {
  test.skip(!businessId, "Requiere la sede de prueba local.");
  await login(page, undefined, 60_000);
  await page.addInitScript(() => {
    localStorage.setItem("selected_business_id", "a0a10000-0000-0000-0000-000000000001");
  });
  await page.goto("/dashboard/restaurant/menu");
  await expect(page.getByRole("alert").getByText("Selecciona una sede Restaurante y Bar"))
    .toBeVisible();
  await expect(page.getByRole("button", { name: "Vista previa" })).toHaveCount(0);
});

test("la edición de producto permite armar la receta con cantidad por ingrediente", async ({ page }) => {
  test.skip(!businessId, "Requiere la sede de prueba local.");
  await login(page, undefined, 60_000);
  await page.addInitScript((value) => {
    localStorage.setItem("selected_business_id", value);
  }, businessId!.toLowerCase());
  await page.goto("/dashboard/products");
  await page.getByPlaceholder("Buscar por nombre o SKU").fill("Hamburguesa de prueba");
  const row = page.getByRole("row").filter({ hasText: "Hamburguesa de prueba" });
  await expect(row).toBeVisible();
  await row.getByRole("button", { name: "Editar" }).click();
  const editor = page.getByRole("dialog");
  const recipe = editor.locator("#product-recipe");
  await expect(recipe.getByRole("heading", { name: "Ingredientes de la receta" })).toBeVisible();
  await page.route("**/recipe-ingredients?**", (route) => route.fulfill({
    status: 200, contentType: "application/json",
    body: JSON.stringify({ items: [{ productId: "66666666-6666-6666-6666-666666666666",
      productCode: "PAN-TEST", name: "Pan de prueba", baseUnitCode: "EA" }],
    page: 1, pageSize: 10, totalCount: 1, totalPages: 1 }),
  }));
  await expect(recipe.getByRole("textbox", { name: "Agregar ingrediente" })).toBeVisible();
  await recipe.getByRole("textbox", { name: "Agregar ingrediente" }).fill("Pan");
  await recipe.getByRole("option", { name: /Pan de prueba/ }).click();
  await recipe.getByRole("spinbutton", { name: "Cantidad de Pan de prueba" }).fill("2");
  await expect(editor.getByRole("button", { name: "Guardar producto" })).toBeEnabled();
  await expect(recipe.getByRole("button", { name: "Publicar receta" })).toHaveCount(0);
  await recipe.screenshot({ path: "../tmp/restaurant-product-recipe.png" });
});

test("cobra una mesa en dos partes y emite una sola factura", async ({ page }) => {
  test.skip(!businessId || !waiterSecret, "Requiere la sede y clave de prueba local.");
  test.setTimeout(120_000);
  await login(page, undefined, 60_000);
  await page.addInitScript((value) => {
    localStorage.setItem("selected_business_id", value);
  }, businessId!.toLowerCase());
  await page.goto("/pos/restaurant");
  await page.getByRole("button", { name: /Mesa P-04/ }).click();
  const waiter = page.getByRole("dialog", { name: "Identificar mesero" });
  if (await waiter.isVisible()) {
    await waiter.getByRole("button", { name: /Mesero 1/i }).click();
    await waiter.getByPlaceholder("Principal o secundaria").fill(waiterSecret!);
    await waiter.getByRole("button", { name: "Abrir mesa" }).click();
  }
  await page.getByRole("button", { name: /Entradas/ }).click();
  await page.getByRole("button", { name: "Agregar Papas bravas" }).click();
  await page.getByRole("button", { name: "Agregar Papas bravas" }).click();
  const accountLayout = await page.locator("aside:has-text('Cuenta abierta')").evaluate((aside) => {
    const [header, items, footer] = Array.from(aside.children) as HTMLElement[];
    return {
      headerTop: header.getBoundingClientRect().top,
      footerBottom: footer.getBoundingClientRect().bottom,
      asideTop: aside.getBoundingClientRect().top,
      asideBottom: aside.getBoundingClientRect().bottom,
      itemsOverflow: getComputedStyle(items).overflowY,
      headerOverflow: getComputedStyle(header).overflowY,
      footerOverflow: getComputedStyle(footer).overflowY,
    };
  });
  expect(accountLayout.itemsOverflow).toBe("auto");
  expect(accountLayout.headerOverflow).not.toBe("auto");
  expect(accountLayout.footerOverflow).not.toBe("auto");
  expect(Math.abs(accountLayout.headerTop - accountLayout.asideTop)).toBeLessThan(2);
  expect(Math.abs(accountLayout.footerBottom - accountLayout.asideBottom)).toBeLessThan(2);
  await page.locator("aside:has-text('Cuenta abierta')").screenshot({ path: "../tmp/restaurant-account-fixed.png" });
  await page.getByRole("button", { name: /Enviar pendientes/ }).click();
  await expect(page.getByRole("button", { name: "Facturar mesa" })).toBeEnabled();
  await page.getByRole("button", { name: "Facturar mesa" }).click();
  const payment = page.getByRole("dialog", { name: "Cobrar mesa" });
  await expect(payment).toBeVisible();
  await expect(payment.getByText("Comprobante de venta", { exact: true })).toBeVisible();
  await expect(payment.getByRole("button", { name: "Cobrar y emitir documento" })).toBeEnabled();
  await payment.getByRole("button", { name: "Cambiar documento" }).click();
  let documentSelector = page.getByRole("dialog", { name: "Tipo de documento" });
  await documentSelector.getByRole("button", { name: /Factura electrónica/ }).click();
  await expect(payment.getByText(/La factura electrónica no está habilitada en esta sede/))
    .toBeVisible();
  await expect(payment.getByRole("button", { name: "Cobrar y emitir factura" })).toBeDisabled();
  await payment.getByRole("button", { name: "Cambiar documento" }).click();
  documentSelector = page.getByRole("dialog", { name: "Tipo de documento" });
  await expect(documentSelector).toBeVisible();
  await documentSelector.getByRole("button", { name: /Comprobante de venta/ }).click();
  await expect(payment.getByText("Comprobante de venta", { exact: true })).toBeVisible();
  await expect(payment.getByText(/La factura electrónica no está habilitada en esta sede/))
    .toHaveCount(0);
  await payment.getByRole("button", { name: "Elegir productos" }).click();
  await payment.getByRole("button", { name: "Marcar Papas bravas" }).first().click();
  const partialPayment = Number(await payment.getByRole("textbox", { name: "Valor recibido" }).inputValue());
  expect(partialPayment).toBeGreaterThan(0);
  await payment.getByRole("button", { name: "Guardar este pago" }).click();
  await expect(payment.getByText("Cobros recibidos")).toBeVisible();
  await payment.getByRole("button", { name: "Todo lo pendiente" }).click();
  await expect(payment.getByRole("button", { name: "Cobrar y emitir documento" })).toBeEnabled();
  await payment.getByRole("button", { name: "Cobrar y emitir documento" }).click();
  await expect(page.getByRole("heading", { name: "¿A qué mesa atendemos?" }))
    .toBeVisible({ timeout: 30_000 });
});

test("abre una mesa real, envía a cocina y factura en la base local", async ({ page }) => {
  test.skip(!businessId || !waiterSecret, "Requiere la sede y clave de prueba local.");
  test.setTimeout(120_000);
  await login(page, undefined, 60_000);
  await page.addInitScript((value) => {
    localStorage.setItem("selected_business_id", value);
  }, businessId!.toLowerCase());
  await page.goto("/pos/restaurant");

  await expect(page.getByRole("heading", { name: "¿A qué mesa atendemos?" }))
    .toBeVisible({ timeout: 30_000 });
  await page.getByRole("button", { name: "Todas", exact: true }).click();
  await page.getByRole("button", { name: /M-04.*(?:Abrir mesa|Ver cuenta)/ }).click({ timeout: 15_000 });
  const waiter = page.getByRole("dialog", { name: "Identificar mesero" });
  if (await waiter.isVisible()) {
    await waiter.getByRole("button", { name: /Mesero 1/i }).click();
    await waiter.getByPlaceholder("Principal o secundaria").fill(waiterSecret!);
    await waiter.getByRole("button", { name: "Abrir mesa" }).click();
  }

  await expect(page.getByRole("heading", { name: "Elige productos" }))
    .toBeVisible({ timeout: 20_000 });
  await page.getByRole("button", { name: /Entradas/ }).click();
  await page.getByRole("button", { name: "Agregar Papas bravas" }).click({ timeout: 15_000 });
  await expect(page.getByRole("button", { name: /Enviar pendientes/ }))
    .toBeEnabled();
  const sent = page.waitForResponse((response) =>
    response.request().method() === "POST" &&
    /\/restaurant\/accounts\/[^/]+\/commands$/.test(new URL(response.url()).pathname),
    { timeout: 20_000 });
  await page.getByRole("button", { name: /Enviar pendientes/ }).click();
  expect((await sent).ok()).toBe(true);

  await expect(page.getByRole("button", { name: "Facturar mesa" })).toBeEnabled();
  await page.getByRole("button", { name: "Facturar mesa" }).click();
  const payment = page.getByRole("dialog", { name: "Cobrar mesa" });
  await expect(payment).toBeVisible();
  await payment.getByRole("textbox", { name: "Valor recibido" }).fill("999999");
  const completed = page.waitForResponse((response) =>
    response.request().method() === "POST" &&
    /\/restaurant\/accounts\/[^/]+\/complete$/.test(new URL(response.url()).pathname),
    { timeout: 20_000 });
  await payment.getByRole("button", { name: "Cobrar y emitir documento" }).click();
  expect((await completed).ok()).toBe(true);
  await expect(page.getByRole("heading", { name: "¿A qué mesa atendemos?" }))
    .toBeVisible({ timeout: 20_000 });
  await page.getByRole("button", { name: "Todas", exact: true }).click();
  await expect(page.getByRole("button", { name: /M-04.*Abrir mesa/ }))
    .toBeVisible({ timeout: 20_000 });
});

test("cobra un producto sin estación sin crear comanda", async ({ page }) => {
  test.skip(!businessId, "Requiere la sede de prueba local.");
  test.setTimeout(120_000);
  await login(page, undefined, 60_000);
  await page.addInitScript((value) => {
    localStorage.setItem("selected_business_id", value);
  }, businessId!.toLowerCase());
  await page.goto("/pos/restaurant");
  await expect(page.getByRole("heading", { name: "¿A qué mesa atendemos?" }))
    .toBeVisible({ timeout: 30_000 });
  await page.getByRole("button", { name: "Todas", exact: true }).click();
  await page.getByRole("button", { name: /P-06.*(?:Abrir mesa|Ver cuenta)/ }).click();
  const waiter = page.getByRole("dialog", { name: "Identificar mesero" });
  if (await waiter.isVisible()) {
    await waiter.getByRole("button", { name: /Mesero 1/i }).click();
    await waiter.getByPlaceholder("Principal o secundaria").fill(waiterSecret!);
    await waiter.getByRole("button", { name: "Abrir mesa" }).click();
  }
  await expect(page.getByRole("heading", { name: "Elige productos" }))
    .toBeVisible({ timeout: 20_000 });
  const priorGaseosa = page.getByRole("button", { name: /Eliminar producto pendiente Gaseosa de prueba/ });
  if (await priorGaseosa.count()) {
    await priorGaseosa.first().click();
    await expect(priorGaseosa).toHaveCount(0);
  }
  await page.getByRole("textbox", { name: "Buscar producto" }).fill("Gaseosa de prueba");
  await page.getByRole("button", { name: "Agregar Gaseosa de prueba" }).click();
  await expect(page.getByText("Sin comanda · se cobra directamente")).toBeVisible();
  await expect(page.getByRole("button", { name: /Enviar pendientes/ })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Facturar mesa" })).toBeEnabled();
  await page.getByRole("button", { name: "Facturar mesa" }).click();
  const payment = page.getByRole("dialog", { name: "Cobrar mesa" });
  await expect(payment).toBeVisible();
  await payment.getByRole("textbox", { name: "Valor recibido" }).fill("6500");
  const completed = page.waitForResponse((response) =>
    response.request().method() === "POST" &&
    /\/restaurant\/accounts\/[^/]+\/complete$/.test(new URL(response.url()).pathname),
    { timeout: 20_000 });
  await payment.getByRole("button", { name: "Cobrar y emitir documento" }).click();
  expect((await completed).ok()).toBe(true);
});
