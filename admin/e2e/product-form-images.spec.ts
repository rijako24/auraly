import { expect, test, type Page } from "@playwright/test";

const user = process.env.AURALY_E2E_USERNAME ?? "admin";
const password = process.env.AURALY_E2E_PASSWORD ?? "Admin123!";

async function login(page: Page) {
  await page.goto("/login");
  await page.locator("#username").fill(user);
  await page.locator("#password").fill(password);
  await page.getByRole("button", { name: /Iniciar sesi.n/ }).click();
  await expect(page).toHaveURL(/\/dashboard(?:\/|$)/, { timeout: 60_000 });
}

test("producto usa un solo control de inventario y prepara imágenes localmente", async ({ page }) => {
  test.setTimeout(120_000);
  let uploads = 0;
  page.on("request", (request) => {
    if (request.method() === "POST" && /\/commerce\/v1\/products\/[^/]+\/images/.test(request.url())) uploads += 1;
  });

  await login(page);
  await page.goto("/dashboard/products");
  await page.getByRole("button", { name: "Nuevo producto" }).click();

  const createDialog = page.getByRole("dialog", { name: "Crear producto" });
  await expect(createDialog).toBeVisible();
  await expect(createDialog.getByText("Controla inventario", { exact: true })).toHaveCount(1);
  await expect(createDialog.getByText("Maneja inventario", { exact: true })).toHaveCount(0);
  const createBox = await createDialog.boundingBox();

  await createDialog.locator('input[type="file"]').setInputFiles({
    name: "aceite-principal.png",
    mimeType: "image/png",
    buffer: Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=", "base64"),
  });
  await expect(createDialog.locator('img[alt="aceite-principal.png"]').first()).toBeVisible();
  await expect(createDialog.getByText("1 imagen", { exact: true })).toBeVisible();
  await page.waitForTimeout(500);
  expect(uploads).toBe(0);

  await createDialog.getByRole("button", { name: "Cancelar" }).click();
  await expect(createDialog).toBeHidden();

  const editButton = page.getByRole("button", { name: "Editar" }).first();
  await expect(editButton).toBeVisible({ timeout: 20_000 });
  await editButton.click();
  const editDialog = page.getByRole("dialog");
  await expect(editDialog.getByText("Editar producto", { exact: true })).toBeVisible();
  await expect(editDialog.getByText("Controla inventario", { exact: true })).toHaveCount(1);
  await expect(editDialog.getByText("Maneja inventario", { exact: true })).toHaveCount(0);
  const editBox = await editDialog.boundingBox();

  expect(createBox).not.toBeNull();
  expect(editBox).not.toBeNull();
  expect(Math.abs(createBox!.width - editBox!.width)).toBeLessThanOrEqual(2);
  await editDialog.getByRole("button", { name: "Cancelar" }).click();
  await expect(editDialog).toBeHidden();
});
