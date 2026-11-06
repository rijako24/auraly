import { expect, test } from "@playwright/test";
import { login } from "./support/auth";

test("Editar sede opens its existing editable configuration", async ({ page }) => {
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
  await expect(page).toHaveURL(new RegExp(`/dashboard/businesses/${business.businessId}$`));
  await expect(page.getByRole("heading", { name: business.name, exact: true })).toBeVisible();
  await expect(page.getByText("Tipo de operación", { exact: true })).toBeVisible();
  await expect(page.getByText("This page could not be found.", { exact: true })).toHaveCount(0);
});
