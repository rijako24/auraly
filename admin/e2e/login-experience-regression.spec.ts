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
});
