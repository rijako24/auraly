import { execFileSync } from "node:child_process";
import { expect, test, type Locator, type Page } from "@playwright/test";
import { login as sharedLogin } from "./support/auth";
import { calculateNitVerificationDigit } from "../src/lib/tenant-legal-identity";

function field(scope: Locator, label: string) {
  return scope.getByText(label, { exact: true }).locator("..");
}

async function chooseFirst(page: Page, scope: Locator, label: string) {
  await field(scope, label).getByRole("combobox").click();
  const option = page.getByRole("option").first();
  await expect(option).toBeVisible({ timeout: 20_000 });
  await option.click();
}

async function login(page: Page, tenantKey: string, username: string, password: string) {
  await sharedLogin(page, { tenantKey, username, password });
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
  const adminUsername = `admin-${suffix}`;
  const password = "AuralyE2E-2026!";
  const nit = suffix.slice(-9);

  const platformTenantKey = process.env.AURALY_E2E_PLATFORM_TENANT_KEY ?? queryScalar(
    "SELECT TenantKey FROM dbo.Tenants WHERE TenantKey=N'@auraly';",
  );
  await login(page, platformTenantKey, "admin", "Admin123!");
  await page.goto("/dashboard/tenants/new");
  const main = page.locator("main");
  await field(main, "Razón social").locator("input").fill(`Empresa UI ${suffix} SAS`);
  await field(main, "Nombre comercial").locator("input").fill(tradeName);
  await field(main, "Número de identificación").locator("input").fill(nit);
  await field(main, "Dígito de verificación").locator("input").fill(String(calculateNitVerificationDigit(nit)));
  await chooseFirst(page, main, "País");
  await chooseFirst(page, main, "Departamento");
  await chooseFirst(page, main, "Ciudad");
  await field(main, "Dirección principal").locator("input").fill("Calle 10 # 20-30");
  await field(main, "Teléfono").locator("input").fill("3001234567");
  await field(main, "Correo empresarial").locator("input").fill(`empresa-${suffix}@e2e.auraly.test`);
  await page.getByRole("button", { name: "Continuar" }).click();

  await field(main, "Nombre de la sede").locator("input").fill("Sede principal E2E");
  await field(main, "Enviar enlace a").locator("input").fill(adminEmail);
  await page.getByRole("button", { name: "Continuar" }).click();
  await expect(page.getByRole("heading", { name: "Plan y capacidad" })).toBeVisible({ timeout: 20_000 });
  await page.getByRole("button", { name: "Continuar" }).click();
  await expect(page.getByText("Bodega de venta (VEN) y Bodega de pedidos (PED)")).toBeVisible();
  await page.getByRole("button", { name: "Aprovisionar empresa" }).click();
  await expect(page).toHaveURL(/\/dashboard\/tenants\/[0-9a-f-]+\?provisioned=1$/i, { timeout: 30_000 });
  await expect(page.getByRole("heading", { name: tradeName })).toBeVisible();

  const tenantId = new URL(page.url()).pathname.split("/").at(-1)!;
  const tenantKey = queryScalar(
    `SELECT TenantKey FROM dbo.Tenants WHERE TenantId='${tenantId}';`,
  );
  expect(tenantKey).toMatch(/^@/);
  const token = queryScalar(`SELECT TOP(1) JSON_VALUE(Payload,'$.activationToken') FROM dbo.TenantProvisioningOutboxMessages WHERE TenantId='${tenantId}' AND Type=N'TenantAdministratorInvitation' ORDER BY OccurredAt DESC;`);
  expect(token).toHaveLength(64);
  expect(queryScalar(`SELECT CONCAT((SELECT COUNT(*) FROM dbo.Businesses WHERE TenantId='${tenantId}'),'|',(SELECT COUNT(*) FROM dbo.Warehouses w JOIN dbo.Businesses b ON b.BusinessId=w.BusinessId WHERE b.TenantId='${tenantId}' AND w.Code IN(N'VEN',N'PED')),'|',(SELECT COUNT(*) FROM dbo.AppRoles WHERE TenantId='${tenantId}' AND Name IN(N'Cajero',N'Supervisor',N'Administrativo',N'Administrador')),'|',(SELECT COUNT(*) FROM dbo.Customers c JOIN dbo.Parties p ON p.PartyId=c.PartyId WHERE p.TenantId='${tenantId}' AND p.DisplayName=N'Consumidor final'));`)).toBe("1|2|4|1");

  await page.context().clearCookies();
  await page.goto(`/activate?token=${token}`);
  await field(page.locator("form"), "Número de identificación").locator("input").fill(`10${suffix.slice(-8)}`);
  await field(page.locator("form"), "Nombres").locator("input").fill("Administrador");
  await field(page.locator("form"), "Apellidos").locator("input").fill("E2E");
  await field(page.locator("form"), "Usuario para entrar").locator("input").fill(adminUsername);
  await field(page.locator("form"), "Teléfono").locator("input").fill("3011234567");
  await field(page.locator("form"), "Dirección").locator("input").fill("Calle 10 # 20-30");
  await field(page.locator("form"), "Contraseña").locator("input").fill(password);
  await field(page.locator("form"), "Confirma la contraseña").locator("input").fill(password);
  await page.getByRole("button", { name: "Crear mi acceso de administrador" }).click();
  await expect(page.getByRole("heading", { name: "Tu empresa está lista" })).toBeVisible({ timeout: 20_000 });
  await expect(page.getByText(tenantKey, { exact: true })).toBeVisible();
  await expect(page.getByText(adminUsername, { exact: true })).toBeVisible();
  await expect(page.getByText(adminEmail, { exact: true })).toHaveCount(0);
  await page.getByRole("link", { name: "Ir a iniciar sesión" }).click();
  await login(page, tenantKey, adminUsername, password);
  await expect(page.getByRole("link", { name: "Tenants", exact: true })).toHaveCount(0);
  await page.getByRole("link", { name: "Sedes", exact: true }).click();
  await expect(page.getByRole("row", { name: /Sede principal E2E/ })).toBeVisible({ timeout: 20_000 });
  expect(queryScalar(`SELECT Status FROM dbo.TenantUserInvitations WHERE TenantId='${tenantId}';`)).toBe("Accepted");
});
