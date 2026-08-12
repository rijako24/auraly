import { execFileSync } from "node:child_process";
import { expect, test, type Locator, type Page } from "@playwright/test";

function field(scope: Locator, label: string) {
  return scope.getByText(label, { exact: true }).locator("..");
}

async function chooseFirst(page: Page, scope: Locator, label: string) {
  await field(scope, label).getByRole("combobox").click();
  const option = page.getByRole("option").first();
  await expect(option).toBeVisible({ timeout: 20_000 });
  await option.click();
}

async function login(page: Page, username: string, password: string) {
  await page.goto("/login");
  await page.locator("#username").fill(username);
  await page.locator("#password").fill(password);
  await page.getByRole("button", { name: "Iniciar sesión" }).click();
  await expect(page).toHaveURL(/\/dashboard(?:\/|$)/, { timeout: 20_000 });
}

function queryScalar(query: string) {
  const database = process.env.AURALY_E2E_DATABASE ?? "AuralyLocal";
  return execFileSync("sqlcmd", ["-S", ".\\LOCAL", "-d", database, "-E", "-C", "-h", "-1", "-Q", `SET NOCOUNT ON; ${query}`], { encoding: "utf8" }).trim();
}

test("aprovisiona una empresa desde UI, activa la invitación e ingresa como administrador", async ({ page }) => {
  test.setTimeout(180_000);
  const suffix = String(Date.now());
  const tradeName = `Empresa UI ${suffix}`;
  const adminEmail = `admin-${suffix}@e2e.auraly.test`;
  const password = "AuralyE2E-2026!";

  await login(page, "admin", "Admin123!");
  await page.goto("/dashboard/tenants/new");
  const main = page.locator("main");
  await field(main, "Razón social").locator("input").fill(`Empresa UI ${suffix} SAS`);
  await field(main, "Nombre comercial").locator("input").fill(tradeName);
  await field(main, "NIT").locator("input").fill(suffix.slice(-9));
  await field(main, "Dígito de verificación").locator("input").fill("1");
  await chooseFirst(page, main, "País");
  await chooseFirst(page, main, "Departamento");
  await chooseFirst(page, main, "Ciudad");
  await field(main, "Dirección principal").locator("input").fill("Calle 10 # 20-30");
  await field(main, "Teléfono").locator("input").fill("3001234567");
  await field(main, "Correo empresarial").locator("input").fill(`empresa-${suffix}@e2e.auraly.test`);
  await page.getByRole("button", { name: "Continuar" }).click();

  await field(main, "Nombre de la sede").locator("input").fill("Sede principal E2E");
  await field(main, "Teléfono").locator("input").fill("3007654321");
  await field(main, "Correo").locator("input").fill(`sede-${suffix}@e2e.auraly.test`);
  await page.getByRole("button", { name: "Continuar" }).click();

  await field(main, "Número de identificación").locator("input").fill(`10${suffix.slice(-8)}`);
  await field(main, "Nombres").locator("input").fill("Administrador");
  await field(main, "Apellidos").locator("input").fill("E2E");
  await field(main, "Correo de acceso").locator("input").fill(adminEmail);
  await field(main, "Teléfono").locator("input").fill("3011234567");
  await page.getByRole("button", { name: "Continuar" }).click();
  await expect(page.getByText("Bodega de venta (VEN) y Bodega de pedidos (PED)")).toBeVisible();
  await page.getByRole("button", { name: "Crear empresa y recursos" }).click();
  await expect(page).toHaveURL(/\/dashboard\/tenants\/[0-9a-f-]+$/i, { timeout: 30_000 });
  await expect(page.getByRole("heading", { name: tradeName })).toBeVisible();

  const tenantId = page.url().split("/").at(-1)!;
  const token = queryScalar(`SELECT TOP(1) JSON_VALUE(Payload,'$.activationToken') FROM dbo.TenantProvisioningOutboxMessages WHERE TenantId='${tenantId}' AND Type=N'TenantAdministratorInvitation' ORDER BY OccurredAt DESC;`);
  expect(token).toHaveLength(64);
  expect(queryScalar(`SELECT CONCAT((SELECT COUNT(*) FROM dbo.Businesses WHERE TenantId='${tenantId}'),'|',(SELECT COUNT(*) FROM dbo.Warehouses w JOIN dbo.Businesses b ON b.BusinessId=w.BusinessId WHERE b.TenantId='${tenantId}' AND w.Code IN(N'VEN',N'PED')),'|',(SELECT COUNT(*) FROM dbo.AppRoles WHERE TenantId='${tenantId}' AND Name IN(N'Cajero',N'Supervisor',N'Administrativo',N'Administrador')),'|',(SELECT COUNT(*) FROM dbo.Customers c JOIN dbo.Parties p ON p.PartyId=c.PartyId WHERE p.TenantId='${tenantId}' AND p.DisplayName=N'Consumidor final'));`)).toBe("1|2|4|1");

  await page.context().clearCookies();
  await page.goto(`/activate?token=${token}`);
  await page.getByLabel("Contraseña", { exact: true }).fill(password);
  await page.getByLabel("Confirma la contraseña").fill(password);
  await page.getByRole("button", { name: "Activar cuenta" }).click();
  await expect(page.getByRole("heading", { name: "Tu empresa está lista" })).toBeVisible({ timeout: 20_000 });
  await page.getByRole("link", { name: "Ir a iniciar sesión" }).click();
  await login(page, adminEmail, password);
  await expect(page.getByRole("link", { name: "Tenants", exact: true })).toHaveCount(0);
  await page.getByRole("link", { name: "Negocios", exact: true }).click();
  await expect(page.getByText("Sede principal E2E", { exact: true }).first()).toBeVisible({ timeout: 20_000 });
  expect(queryScalar(`SELECT Status FROM dbo.TenantUserInvitations WHERE TenantId='${tenantId}';`)).toBe("Accepted");
});
