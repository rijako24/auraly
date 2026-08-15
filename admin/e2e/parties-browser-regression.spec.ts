import { expect, test, type Locator, type Page } from "@playwright/test";
import { login } from "./support/auth";

type Role = "Cliente" | "Proveedor" | "Vendedor" | "Transportador";
function field(scope: Locator, label: string) {
  return scope.getByText(label, { exact: true }).locator("..");
}


async function selectFirst(page: Page, dialog: Locator, label: string) {
  const combo = field(dialog, label).getByRole("combobox");
  await expect(combo).toBeEnabled({ timeout: 20_000 });
  await combo.click();
  await page.getByRole("option").first().click();
}

async function createAndEdit(page: Page, role: Role) {
  const suffix = `${Date.now()}`.slice(-9);
  const name = `${role} navegador ${suffix}`;
  await page.goto("/dashboard/parties");
  await page.getByRole("button", { name: "Nuevo tercero" }).click();
  let dialog = page.getByRole("dialog");
  await dialog.getByRole("button", { name: new RegExp(`^${role}`) }).click();
  await field(dialog, "Identificación").locator("input").fill(`10${suffix}`);
  await field(dialog, "Nombre visible").locator("input").fill(name);
  await field(dialog, "Nombres").locator("input").fill(role);
  await field(dialog, "Apellidos").locator("input").fill(`Navegador ${suffix}`);
  await field(dialog, "Teléfono").locator("input").fill("3001234567");
  await field(dialog, "Correo").locator("input").fill(`${role.toLowerCase()}-${suffix}@example.test`);
  await selectFirst(page, dialog, "País");
  await selectFirst(page, dialog, "Departamento");
  await selectFirst(page, dialog, "Ciudad");
  await field(dialog, "Nombre de la sede").locator("input").fill("Principal");
  await field(dialog, "Dirección").locator("input").fill("Calle 1 # 2-3");
  await field(dialog, "Barrio").locator("input").fill("Centro");
  if (role === "Vendedor") await field(dialog, "Comisión %").locator("input").fill("5");
  if (role === "Transportador") {
    await field(dialog, "Modalidad de transporte").getByRole("combobox").click();
    await page.getByRole("option", { name: "Terrestre" }).click();
  }
  await dialog.getByRole("button", { name: "Guardar tercero" }).click();
  await expect(dialog).toBeHidden({ timeout: 20_000 });

  const search = page.getByPlaceholder(/Nombre, documento, correo o/);
  await search.fill(name);
  const row = page.getByRole("row").filter({ hasText: name });
  await expect(row).toBeVisible({ timeout: 20_000 });
  await row.getByRole("button", { name: "Editar" }).click();
  dialog = page.getByRole("dialog");
  await expect(dialog.getByRole("tab", { name: role })).toBeVisible();
  await field(dialog, "Teléfono").locator("input").fill("3017654321");
  await dialog.getByRole("button", { name: "Guardar tercero" }).click();
  await expect(page.getByText("Tercero actualizado", { exact: true })).toBeVisible({ timeout: 20_000 });
  await expect(dialog.getByText("3017654321", { exact: true })).toBeVisible();
  await dialog.getByRole("button", { name: "Cerrar" }).click();
}

test.describe.serial("terceros desde navegador", () => {
  test.setTimeout(90_000);
  test.beforeEach(async ({ page }) => login(page));
  for (const role of ["Cliente", "Proveedor", "Vendedor", "Transportador"] as const) {
    test(`crea y edita ${role.toLowerCase()}`, async ({ page }) => createAndEdit(page, role));
  }
});
