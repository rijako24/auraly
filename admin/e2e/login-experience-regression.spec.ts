import { expect, test } from "@playwright/test";

test.describe("enterprise login experience", () => {
  test("shows the generic tenant prompt with the Auraly example", async ({ page }) => {
    await page.goto("/login");

    await expect(page.getByRole("heading", { name: "Bienvenido de vuelta" })).toBeVisible();
    await expect(page.getByText("Plataforma empresarial conectada")).toBeVisible();
    await expect(page.locator("#tenantKey")).toHaveAttribute("placeholder", "@auraly");
    await expect(page.locator("#tenantKey")).toBeEnabled();
    await expect(page.getByRole("button", { name: "Iniciar sesión" })).toBeVisible();
  });

  test("locks the tenant when the company link identifies it", async ({ page }) => {
    await page.goto("/login?tenant=%40auraly-demo");

    const tenantInput = page.locator("#tenantKey");
    await expect(tenantInput).toHaveValue("@auraly-demo");
    await expect(tenantInput).toBeDisabled();
    await expect(page.getByText("Empresa identificada")).toBeVisible();
    await expect(page.getByText("Este enlace ya está conectado con tu empresa.")).toBeVisible();
  });

  test("prefills the last company remembered on this device", async ({ page }) => {
    await page.goto("/login");
    await page.evaluate(() => localStorage.setItem("auraly:last-tenant-key", "@empresa-recordada"));
    await page.reload();

    await expect(page.locator("#tenantKey")).toHaveValue("@empresa-recordada");
    await expect(page.locator("#tenantKey")).toBeEnabled();
    await expect(page.getByText("Recordamos esta empresa en este dispositivo. Puedes cambiarla si lo necesitas.")).toBeVisible();
  });

  test("remembers the canonical company only after a successful login", async ({ page }) => {
    await page.route("**/api/auth/login", async (route) => route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        user: {
          userId: "10000000-0000-0000-0000-000000000001",
          tenantId: "a0a10000-0000-0000-0000-000000000000",
          tenantKey: "@auraly",
          username: "admin",
          email: "admin@auraly.ai",
          firstName: "Administrador",
          lastName: "Auraly",
          avatarUrl: null,
          roles: [],
          permissions: [],
        },
      }),
    }));
    await page.goto("/login");
    await page.locator("#tenantKey").fill("  @AURALY  ");
    await page.locator("#username").fill("admin");
    await page.locator("#password").fill("valid-password");
    await page.getByRole("button", { name: "Iniciar sesión" }).click();

    await expect.poll(() => page.evaluate(() => localStorage.getItem("auraly:last-tenant-key"))).toBe("@auraly");
  });
});
