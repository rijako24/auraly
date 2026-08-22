import { expect, test, type Locator, type Page } from "@playwright/test";
import { login } from "./support/auth";

function field(scope: Locator, label: string) {
  return scope.getByText(label, { exact: true }).locator("..");
}

async function selectFirst(page: Page, scope: Locator, label: string) {
  await field(scope, label).getByRole("combobox").click();
  await page.getByRole("option").first().click();
}

test("crea, consulta y edita nombre, referencia y descripción en el mismo orden", async ({ page }) => {
  test.setTimeout(150_000);
  await login(page);
  await page.goto("/dashboard/products");

  const suffix = String(Date.now()).slice(-9);
  const name = `Producto identidad ${suffix}`;
  const reference = `REF-${suffix}`;
  const description = `Descripción inicial ${suffix}`;

  await page.getByRole("button", { name: "Nuevo producto" }).click();
  const create = page.getByRole("dialog", { name: "Crear producto" });
  const baseSections = ["identity", "classification", "sale", "family", "supplier", "taxes", "images"];
  await expect.poll(() => create.locator("section[id^='new-']").evaluateAll((items) => items.map((item) => item.id)))
    .toEqual(baseSections.map((id) => `new-${id}`));
  const identity = create.locator("#new-identity");
  const labels = await identity.locator("label").allTextContents();
  expect(labels.slice(0, 3).map((value) => value.replace("*", "").trim())).toEqual([
    "Nombre", "Referencia", "Descripción",
  ]);
  await identity.getByPlaceholder("Nombre claro para venta y búsqueda").fill(name);
  await identity.getByPlaceholder("Referencia del fabricante").fill(reference);
  await field(identity, "Descripción").getByRole("textbox").fill(description);
  const barcode = create.getByPlaceholder(/Escanea o escribe un c.digo/);
  await barcode.fill(`779${suffix}`);
  await barcode.press("Enter");
  await selectFirst(page, create, "IVA de venta *");
  await selectFirst(page, create, "IVA de compra *");
  await field(create, "Costo base").locator("input").fill("10000");
  await field(create, "Margen sobre el precio antes de IVA").locator("input").fill("20");
  await create.getByRole("button", { name: "Crear producto" }).click();
  await expect(create).toBeHidden({ timeout: 20_000 });

  const search = page.getByPlaceholder("Buscar por nombre o SKU");
  await search.fill(name);
  const row = page.getByRole("row").filter({ hasText: name });
  await expect(row).toContainText(reference, { timeout: 20_000 });
  await expect(row).toContainText(description);
  await row.click();

  const detail = page.getByRole("dialog", { name });
  await expect.poll(() => detail.locator("section[id^='product-']").evaluateAll((items) => items.map((item) => item.id)))
    .toEqual(baseSections.map((id) => `product-${id}`));
  const overview = detail.locator("#product-identity");
  await expect(overview).toContainText(reference, { timeout: 20_000 });
  const overviewText = await overview.innerText();
  expect(overviewText.indexOf("Nombre")).toBeLessThan(overviewText.indexOf("Referencia"));
  expect(overviewText.indexOf("Referencia")).toBeLessThan(overviewText.indexOf("Descripción"));

  await detail.getByRole("button", { name: "Editar información" }).click();
  await expect.poll(() => detail.locator("section[id^='product-']").evaluateAll((items) => items.map((item) => item.id)))
    .toEqual(baseSections.map((id) => `product-${id}`));
  const editIdentity = detail.locator("#product-identity");
  const updatedReference = `${reference}-EDIT`;
  const updatedDescription = `Descripción actualizada ${suffix}`;
  await editIdentity.locator("#product-reference").fill(updatedReference);
  await editIdentity.locator("#product-description").fill(updatedDescription);
  await detail.getByRole("button", { name: "Guardar producto" }).click();
  await expect(page.getByText("Producto guardado completamente", { exact: true })).toBeVisible({ timeout: 20_000 });
  await expect(detail).toBeHidden();

  await row.click();
  const reopened = page.getByRole("dialog", { name });
  await expect(reopened.locator("#product-identity")).toContainText(updatedReference, { timeout: 20_000 });
  await expect(reopened.locator("#product-identity")).toContainText(updatedDescription);
});
