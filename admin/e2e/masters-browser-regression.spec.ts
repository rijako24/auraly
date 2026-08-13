import { expect, test, type Page } from "@playwright/test";

const user = process.env.AURALY_E2E_USERNAME ?? "admin2222";
const password = process.env.AURALY_E2E_PASSWORD ?? "Admin123!";

async function login(page: Page) {
  await page.goto("/login");
  await page.locator("#username").fill(user);
  await page.locator("#password").fill(password);
  await page.getByRole("button", { name: /Iniciar sesi.n/ }).click();
  await expect(page).toHaveURL(/\/dashboard(?:\/|$)/, { timeout: 20_000 });
}

test.describe.serial("maestros administrables desde la interfaz", () => {
  test.setTimeout(120_000);
  test.beforeEach(async ({ page }) => login(page));

  test("ubicación y clasificación comparten explorador, búsqueda, edición y estado", async ({ page }) => {
    const suffix = String(Date.now()).slice(-7);
    const countryCode = String.fromCharCode(65 + (Number(suffix) % 26), 65 + (Math.floor(Number(suffix) / 26) % 26));
    const countryName = `País UI ${suffix}`;
    const areaName = `Área UI ${suffix}`;
    const lineName = `Línea UI ${suffix}`;

    await page.goto("/dashboard/settings/masters");
    const hierarchySearch = page.getByPlaceholder(/Buscar en toda la jerarqu.a/);
    await expect(hierarchySearch).toBeVisible({ timeout: 20_000 });
    await page.getByRole("button", { name: /Nuevo pa.s/ }).click();
    let dialog = page.getByRole("dialog");
    await dialog.getByLabel(/C.digo/).fill(countryCode);
    await dialog.getByLabel("Nombre").fill(countryName);
    await dialog.getByRole("button", { name: "Guardar", exact: true }).click();
    await expect(dialog).toBeHidden({ timeout: 20_000 });

    await hierarchySearch.fill(countryName);
    await expect(page.getByText(countryName, { exact: true })).toBeVisible();
    await page.getByRole("button", { name: `Editar ${countryName}` }).click();
    dialog = page.getByRole("dialog");
    await dialog.getByRole("switch").click();
    await dialog.getByRole("button", { name: "Guardar", exact: true }).click();
    await expect(page.getByText(countryName, { exact: true })).toBeHidden();
    await page.getByText("Mostrar inactivos", { exact: true }).locator("..").getByRole("switch").click();
    await expect(page.getByText(countryName, { exact: true })).toBeVisible();
    await expect(page.getByText("Inactivo", { exact: true })).toBeVisible();

    await page.getByRole("button", { name: /^Productos/ }).click();
    const productHierarchySearch = page.getByPlaceholder(/Buscar en toda la jerarqu.a/);
    await page.getByRole("button", { name: /Nueva .rea/ }).click();
    dialog = page.getByRole("dialog");
    await dialog.getByLabel("Nombre").fill(areaName);
    await dialog.getByRole("button", { name: "Guardar", exact: true }).click();
    await productHierarchySearch.fill(areaName);
    await page.getByRole("button", { name: `Crear línea en ${areaName}` }).click();
    dialog = page.getByRole("dialog");
    await dialog.getByLabel("Nombre").fill(lineName);
    await dialog.getByRole("button", { name: "Guardar", exact: true }).click();
    await productHierarchySearch.fill(lineName);
    await expect(page.getByText(areaName, { exact: true })).toBeVisible();
    await expect(page.getByText(lineName, { exact: true })).toBeVisible();
  });

  test("marca, unidad e IVA permiten buscar, editar e inactivar", async ({ page }) => {
    const suffix = String(Date.now()).slice(-7);
    await page.goto("/dashboard/settings/masters");
    await page.getByRole("button", { name: /^Productos/ }).click();

    const brandName = `Marca UI ${suffix}`;
    await page.getByRole("button", { name: "Nueva marca" }).click();
    let dialog = page.getByRole("dialog");
    await dialog.getByLabel("Nombre").fill(brandName);
    await dialog.getByRole("button", { name: "Guardar", exact: true }).click();
    const brandSearch = page.getByPlaceholder("Buscar marcas");
    await brandSearch.fill(brandName);
    await page.getByText(brandName, { exact: true }).click();
    dialog = page.getByRole("dialog");
    await dialog.getByLabel("Nombre").fill(`${brandName} editada`);
    await dialog.getByRole("switch").click();
    await dialog.getByRole("button", { name: "Guardar", exact: true }).click();
    await expect(page.getByText(`${brandName} editada`, { exact: true })).toBeHidden();

    const unitName = `Unidad UI ${suffix}`;
    await page.getByRole("button", { name: "Nueva unidad" }).click();
    dialog = page.getByRole("dialog");
    await dialog.getByLabel("Nombre").fill(unitName);
    await dialog.getByLabel(/C.digo/).fill(`U${suffix}`);
    await dialog.getByLabel(/S.mbolo/).fill(`u${suffix}`);
    await dialog.getByRole("button", { name: "Guardar", exact: true }).click();
    await page.getByPlaceholder(/Buscar unidades/).fill(unitName);
    await expect(page.getByText(unitName, { exact: true })).toBeVisible();

    await page.getByRole("button", { name: /^IVA/ }).click();
    const vatName = `IVA UI ${suffix}`;
    await page.getByRole("button", { name: "Nuevo IVA" }).click();
    dialog = page.getByRole("dialog");
    await dialog.getByLabel(/C.digo interno/).fill(`IVA-${suffix}`);
    await dialog.getByLabel(/Tarifa/).fill("7");
    await dialog.getByLabel("Nombre").fill(vatName);
    await dialog.getByRole("button", { name: "Guardar", exact: true }).click();
    await page.getByPlaceholder(/Buscar tarifas de IVA/i).fill(vatName);
    await expect(page.getByText(vatName, { exact: true })).toBeVisible();
  });
});
