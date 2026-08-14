import { expect, test, type Locator, type Page } from "@playwright/test";
import { login } from "./support/auth";

function field(scope: Locator, label: RegExp | string) {
  return scope.getByText(label, { exact: typeof label === "string" }).locator("..");
}


async function chooseFirst(page: Page, scope: Locator, label: RegExp | string) {
  const select = field(scope, label).getByRole("combobox");
  await expect(select).toBeEnabled({ timeout: 20_000 });
  await select.click();
  await page.getByRole("option").first().click();
}

async function createParty(page: Page, role: "Cliente" | "Vendedor", suffix: string) {
  const name = `${role} ruta ${suffix}`;
  await page.goto("/dashboard/parties");
  await page.getByRole("button", { name: "Nuevo tercero" }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByRole("button", { name: new RegExp(`^${role}`) }).click();
  await field(dialog, /Identificaci.n/).locator("input").fill(`11${suffix}`);
  await field(dialog, "Nombre visible").locator("input").fill(name);
  await field(dialog, "Nombres").locator("input").fill(role);
  await field(dialog, "Apellidos").locator("input").fill(`Ruta ${suffix}`);
  await field(dialog, /Tel.fono/).locator("input").fill("3001234567");
  await field(dialog, "Correo").locator("input").fill(`${role.toLowerCase()}-${suffix}@routes.e2e.test`);
  await chooseFirst(page, dialog, /Pa.s/);
  await chooseFirst(page, dialog, "Departamento");
  await chooseFirst(page, dialog, "Ciudad");
  await field(dialog, /Nombre de la sede/).locator("input").fill("Establecimiento principal");
  await field(dialog, /Direcci.n/).locator("input").fill("Carrera 7 # 10-20");
  await field(dialog, "Barrio").locator("input").fill("Centro");
  if (role === "Vendedor")
    await field(dialog, /Comisi.n/).locator("input").fill("4");
  await dialog.getByRole("button", { name: "Guardar tercero" }).click();
  await expect(dialog).toBeHidden({ timeout: 20_000 });
  return name;
}

test("crea, programa y edita una ruta con vendedor y establecimiento desde UI", async ({ page }) => {
  test.setTimeout(180_000);
  await login(page);
  const suffix = String(Date.now()).slice(-8);
  const customerName = await createParty(page, "Cliente", `${suffix}1`);
  const sellerName = await createParty(page, "Vendedor", `${suffix}2`);
  const routeCode = `RT-${suffix}`;
  const routeName = `Ruta navegador ${suffix}`;

  await page.goto("/dashboard/routes");
  await page.getByRole("button", { name: "Nueva ruta" }).click();
  let dialog = page.getByRole("dialog");
  await field(dialog, /C.digo/).locator("input").fill(routeCode);
  await field(dialog, "Nombre").locator("input").fill(routeName);
  await field(dialog, "Vendedor").getByRole("combobox").click();
  await page.getByRole("option", { name: new RegExp(sellerName) }).click();
  await dialog.getByRole("button", { name: "Crear zona comercial" }).click();
  const zoneDialog = page.getByRole("dialog").last();
  await field(zoneDialog, /C.digo/).locator("input").fill(`ZN-${suffix}`);
  await field(zoneDialog, "Nombre").locator("input").fill(`Zona ${suffix}`);
  await zoneDialog.getByRole("button", { name: "Crear zona" }).click();
  await expect(page.getByRole("dialog")).toHaveCount(1, { timeout: 20_000 });
  await dialog.getByRole("button", { name: "Crear ruta" }).click();
  await expect(page.getByText(/Ruta creada/)).toBeVisible({ timeout: 20_000 });

  dialog = page.getByRole("dialog");
  await dialog.getByRole("tab", { name: /Recorrido/ }).click();
  await dialog.getByRole("button", { name: "Asignar clientes" }).click();
  const candidateDialog = page.getByRole("dialog").last();
  await candidateDialog.getByPlaceholder(/Cliente, documento, sede/).fill(customerName);
  const candidate = candidateDialog.getByText(new RegExp(customerName)).first().locator("..").locator("..");
  await expect(candidate).toBeVisible({ timeout: 20_000 });
  await candidate.getByRole("checkbox").click();
  await candidateDialog.getByRole("button", { name: "Agregar y ordenar" }).click();
  await expect(page.getByRole("dialog")).toHaveCount(1, { timeout: 20_000 });
  await expect(dialog.getByText(new RegExp(customerName)).first()).toBeVisible({ timeout: 20_000 });
  await dialog.getByRole("button", { name: "Cerrar" }).click();

  await page.getByPlaceholder(/Ruta, c.digo/).fill(routeCode);
  const row = page.getByRole("row").filter({ hasText: routeCode });
  await expect(row).toBeVisible({ timeout: 20_000 });
  await row.getByRole("button", { name: "Abrir" }).click();
  dialog = page.getByRole("dialog");
  await field(dialog, "Nombre").locator("input").fill(`${routeName} editada`);
  await dialog.getByRole("button", { name: "Guardar cambios" }).click();
  await expect(page.getByText("Ruta actualizada", { exact: true })).toBeVisible({ timeout: 20_000 });
  await dialog.getByRole("button", { name: "Desactivar" }).click();
  await expect(page.getByText("Ruta desactivada", { exact: true })).toBeVisible({ timeout: 20_000 });
  await dialog.getByRole("button", { name: "Cerrar" }).click();
});
