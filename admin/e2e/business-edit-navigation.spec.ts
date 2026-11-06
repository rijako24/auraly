import { expect, test } from "@playwright/test";
import { login } from "./support/auth";

test("Editar sede abre el modal completo sin cambiar de página", async ({ page }) => {
  await login(page, undefined, 60_000);
  const listing = page.waitForResponse(response => response.request().method() === "GET"
    && /\/api\/businesses(?:\?|$)/.test(response.url()));
  await page.goto("/dashboard/businesses");
  const response = await listing;
  expect(response.ok()).toBe(true);
  const business = (await response.json() as { items: { businessId: string; name: string }[] }).items[0];
  expect(business).toBeTruthy();
  await page.getByRole("button", { name: `Acciones de ${business.name}`, exact: true }).click();
  await page.getByRole("menuitem", { name: "Editar", exact: true }).click();
  await expect(page).toHaveURL(/\/dashboard\/businesses$/);
  const dialog = page.getByRole("dialog", { name: "Editar sede", exact: true });
  await expect(dialog.getByRole("textbox", { name: "Nombre", exact: true })).toHaveValue(business.name);
  await expect(dialog.getByRole("button", { name: "Guardar", exact: true })).toBeVisible();
  await expect(dialog.getByRole("button", { name: "Cancelar", exact: true })).toBeVisible();
  await expect(page.getByText("This page could not be found.", { exact: true })).toHaveCount(0);
});
