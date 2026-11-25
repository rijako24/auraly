import { expect, test } from "@playwright/test";
import type { ProductMerchandising } from "../src/services/api/product-merchandising";
import type { ProductPricingContext } from "../src/services/api/pricing";
import { financialVoucherScenario, installFinancialVoucherContext } from "./support/financial-voucher";

for (const [storedLink, currentLink, sharedInventory, unavailable] of [
  [false, true, true, false], [true, false, true, false], [true, true, true, false],
  [true, true, false, false], [true, false, true, true],
]) {
  test(`recuperación de familia: ${storedLink} → ${currentLink}, inventario compartido ${sharedInventory}, error ${unavailable}`, async ({ page, baseURL }, testInfo) => {
    const scenario = financialVoucherScenario(), id = scenario.documentId;
    scenario.user.permissions.push("catalog.read", "catalog.update", "pricing.read", "pricing.cost-basis.read");
    await installFinancialVoucherContext(page, baseURL!, scenario);
    const product = { productId: id, businessId: scenario.businessId, productCode: "HIJO", name: "Presentación hija",
      reference: "REF-HIJO", description: "Presentación de prueba", categoryName: "", unitPrice: 10000, currency: "COP",
      manageStock: !currentLink || !sharedInventory, isIngredient: false, hasIngredients: false, isCombo: false, isActive: true };
    const link = { parentProductId: "99999999-1111-1111-1111-111111111111", parentProductCode: "PADRE",
      parentProductName: "Producto principal", sharesInventory: sharedInventory, inventoryFactor: sharedInventory ? 2 : null,
      sharesPrice: true, priceFactor: 2, allowsConversion: false, conversionFactor: null };
    const config: ProductMerchandising = { productId: id, productCategoryId: null, productBrandId: null, baseUnitCode: "EA",
      isGenericProduct: false, manageInventory: !currentLink || !sharedInventory, allowsFractionalSale: false, isWeighable: false,
      unitGrossWeightKg: null, scale: null, barcodes: [], link: currentLink ? link : null, linkedProducts: [], conversionMaximumLossPercent: null };
    const pricing: ProductPricingContext = { productId: id, productName: product.name, preparedSalePrice: 10000,
      publicSalePrice: 10000, costBasisAmount: 8000, costBasisOrigin: currentLink || storedLink ? "LinkedProduct" : "Manual",
      currentMarginPercent: 20, inputMode: "Margin", salesTaxRate: 0, roundingIncrement: 1, roundingMode: "Nearest", isCostLinked: currentLink,
      costSourceProductId: currentLink ? link.parentProductId : null, costSourceProductName: currentLink ? link.parentProductName : null,
      costFactor: currentLink ? 2 : null };
    let merchandisingReads = 0, pricingReads = 0, writes = 0;
    const errors: string[] = []; page.on("pageerror", error => errors.push(error.message));
    await page.route("**/api/**", async route => {
      const path = new URL(route.request().url()).pathname;
      if (route.request().method() === "PUT") {
        writes++; await route.fulfill({ status: 409, json: { title: "No se esperaba guardar en este escenario" } }); return;
      }
      let json: unknown;
      if (path.endsWith("/products")) json = { items: [product], page: 1, pageSize: 25, totalCount: 1, totalPages: 1 };
      else if (path.endsWith(`/products/${id}/merchandising`)) {
        merchandisingReads++;
        if (unavailable) { await route.fulfill({ status: 503, json: { title: "Configuración no disponible" } }); return; }
        json = config;
      }
      else if (path.endsWith(`/products/${id}/context`)) { pricingReads++; json = pricing; }
      else if (path.endsWith(`/products/${id}/recipe`)) json = null;
      else if (path.endsWith(`/products/${id}`)) json = { ...product, baseUnitCode: "EA", manageInventory: !currentLink || !sharedInventory,
        suppliers: [], prices: [{ amount: 10000, currencyCode: "COP", costBasisAmount: 8000, targetMarginPercent: 20 }], barcodes: [] };
      else if (path.endsWith(`/products/${id}/configuration`)) json = { aliases: [], searchTerms: [] };
      else if (path.endsWith("/product-units")) json = [{ productUnitId: "unit", code: "EA", name: "Unidad", symbol: "un", allowsFractionalQuantity: false, decimalPlaces: 0, isActive: true }];
      else { await route.fallback(); return; }
      await route.fulfill({ json });
    });
    await page.goto("/dashboard/products");
    await expect(page.getByRole("row").filter({ hasText: product.name })).toBeVisible();
    await page.evaluate(async ({ key, value }) => {
      await new Promise<void>((resolve, reject) => {
        const open = indexedDB.open("auraly-catalog-work", 1);
        open.onupgradeneeded = () => { open.result.createObjectStore("catalog-drafts", { keyPath: "key" }); };
        open.onerror = () => reject(open.error);
        open.onsuccess = () => {
          const db = open.result, transaction = db.transaction("catalog-drafts", "readwrite");
          transaction.objectStore("catalog-drafts").put({ key, value, updatedAt: "2026-11-10T12:00:00Z" });
          transaction.oncomplete = () => { db.close(); resolve(); };
          transaction.onerror = () => { db.close(); reject(transaction.error); };
        };
      });
    }, { key: `product-edit:${scenario.user.userId}:${scenario.businessId}:${id}`, value: {
      form: { name: "Captura sin guardar", reference: product.reference, description: "Descripción recuperada", categoryName: "", isIngredient: false },
      merchandising: { ...config, manageInventory: !storedLink || !sharedInventory, link: storedLink ? link : null },
    } });
    await page.getByRole("row").filter({ hasText: product.name }).getByRole("button", { name: "Editar", exact: true }).click();
    const dialog = page.getByRole("dialog");
    await expect(dialog.locator("#product-name")).toHaveValue("Captura sin guardar");
    await expect(dialog.getByLabel("Descripción", { exact: true })).toHaveValue("Descripción recuperada");
    if (unavailable) {
      await expect(dialog.getByRole("alert").filter({ hasText: "No fue posible cargar la configuración comercial." })).toBeVisible();
      await expect(dialog.getByText("Cargando identidad comercial del producto…", { exact: true })).toHaveCount(0);
      await expect(dialog.getByText("Control de inventario", { exact: true })).toHaveCount(0);
      await dialog.getByRole("button", { name: "Guardar producto", exact: true }).click();
      await expect(dialog.getByRole("alert").filter({ hasText: "Espera a que termine de cargar la configuración comercial." })).toBeVisible();
      expect(writes).toBe(0); expect(merchandisingReads).toBe(2); expect(errors).toEqual([]); return;
    }
    await expect(dialog.getByText("Todavía no hay componentes configurados.", { exact: true })).toBeVisible();
    const inventory = dialog.getByText("Control de inventario", { exact: true }).locator("..").locator("..").getByRole("switch");
    const cost = dialog.getByText("Costo base *", { exact: true }).locator("../..").getByRole("textbox");
    if (currentLink && sharedInventory) { await expect(inventory).toBeDisabled(); await expect(inventory).not.toBeChecked(); }
    else await expect(inventory).toBeEnabled();
    if (currentLink) {
      await expect(dialog.getByText("Este producto está vinculado a Producto principal", { exact: true })).toBeVisible();
      await expect(dialog.getByText("Costo vinculado y bloqueado.", { exact: true })).toBeVisible();
      await expect(cost).toBeDisabled();
    } else {
      await expect(inventory).toBeEnabled();
      await expect(dialog.getByText("Este producto está vinculado a Producto principal", { exact: true })).toHaveCount(0);
      await expect(dialog.getByText("Costo vinculado y bloqueado.", { exact: true })).toHaveCount(0);
      await expect(cost).toBeEnabled();
      await inventory.click(); await expect(inventory).toBeChecked();
      await inventory.click(); await expect(inventory).not.toBeChecked();
    }
    expect(merchandisingReads).toBe(1); expect(pricingReads).toBe(1); expect(errors).toEqual([]);
    // Wait for the existing local draft owner to finish, then reproduce F5.
    // No extra HTTP request is used to recover or validate family membership.
    await expect.poll(() => page.evaluate(async ({ key, expected }) => new Promise<boolean>((resolve, reject) => {
      const open = indexedDB.open("auraly-catalog-work", 1);
      open.onerror = () => reject(open.error);
      open.onsuccess = () => {
        const db = open.result, request = db.transaction("catalog-drafts").objectStore("catalog-drafts").get(key);
        request.onsuccess = () => { db.close(); resolve((request.result?.value?.merchandising?.link !== null) === expected); };
        request.onerror = () => { db.close(); reject(request.error); };
      };
    }), { key: `product-edit:${scenario.user.userId}:${scenario.businessId}:${id}`, expected: currentLink })).toBe(true);
    await page.reload();
    await page.getByRole("row").filter({ hasText: product.name }).getByRole("button", { name: "Editar", exact: true }).click();
    await expect(dialog.locator("#product-name")).toHaveValue("Captura sin guardar");
    await expect(dialog.getByLabel("Descripción", { exact: true })).toHaveValue("Descripción recuperada");
    if (currentLink && sharedInventory) await expect(inventory).toBeDisabled();
    else await expect(inventory).toBeEnabled();
    if (currentLink) { await expect(dialog.getByText("Costo vinculado y bloqueado.", { exact: true })).toBeVisible(); await expect(cost).toBeDisabled(); }
    else { await expect(dialog.getByText("Costo vinculado y bloqueado.", { exact: true })).toHaveCount(0); await expect(cost).toBeEnabled(); }
    expect(merchandisingReads).toBe(2); expect(pricingReads).toBe(2); expect(writes).toBe(0); expect(errors).toEqual([]);
    await dialog.locator("#product-sale").screenshot({ path: testInfo.outputPath("product-inventory-recovery.png") });
    await dialog.locator("#product-family").screenshot({ path: testInfo.outputPath("product-family-recovery.png") });
  });
}
