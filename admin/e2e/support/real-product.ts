import { randomUUID } from "node:crypto";
import { expect, type Page } from "@playwright/test";

export async function createRealProduct(page: Page, manageInventory = true) {
  const suffix = randomUUID().slice(0, 8);
  const name = `Producto E2E ${suffix}`;
  await page.goto("/dashboard/products");
  await page.getByRole("button", { name: "Nuevo producto", exact: true }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByPlaceholder("Nombre claro para venta y búsqueda").fill(name);
  await dialog.getByPlaceholder("Referencia del fabricante").fill(`E2E-${suffix}`);
  const barcode = dialog.getByPlaceholder("Escanea o escribe un código");
  await barcode.fill(`E2E${suffix}`);
  await barcode.press("Enter");
  for (const label of ["Proveedor principal *", "IVA de venta *", "IVA de compra *"]) {
    const combo = dialog.getByText(label, { exact: true }).locator("..").getByRole("combobox");
    await expect(combo).toBeEnabled();
    await combo.click();
    await page.getByRole("option").first().click();
  }
  await dialog.getByText(/^Costo base \*/).locator("..").locator("input").fill("10000");
  await dialog.getByText(/^Margen sobre el precio antes de IVA \*/).locator("..").locator("input").fill("20");
  const stockControl = dialog.getByText("Control de inventario", { exact: true }).locator("../..").getByRole("switch");
  await stockControl.setChecked(manageInventory);
  await dialog.getByRole("button", { name: "Crear producto", exact: true }).click();
  await expect(dialog).toBeHidden({ timeout: 20_000 });
  return name;
}
