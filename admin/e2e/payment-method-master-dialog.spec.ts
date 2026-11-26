import { expect, test } from "@playwright/test";
import type { PaymentMethod, SavePaymentMethod } from "../src/services/api/payment-methods";

for (const viewport of [{ width: 1366, height: 768 }, { width: 1440, height: 1000 }, { width: 390, height: 844 }]) {
  test(`formas de pago crea y edita en modal con usos ordenados ${viewport.width}`, async ({ page, baseURL }, info) => {
    await page.setViewportSize(viewport);
    const tenantId = "11111111-1111-1111-1111-111111111111", businessId = "22222222-2222-2222-2222-222222222222";
    const user = { userId: "33333333-3333-3333-3333-333333333333", tenantId, tenantKey: "AURALY", username: "e2e", firstName: "Prueba", lastName: "E2E", roles: ["Administrator"], permissions: ["catalog.read", "catalog.update", "dashboard.read"] };
    await page.context().addCookies([{ name: "auth_token", value: "e2e", url: baseURL!, httpOnly: true }]);
    await page.addInitScript(({ tenantId, businessId, user }) => {
      localStorage.setItem("selected_tenant_id", tenantId); localStorage.setItem("selected_business_id", businessId);
      localStorage.setItem("auraly:pwa-install-dismissed", "1");
      localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
    }, { tenantId, businessId, user });
    const types = ["Efectivo", "Tarjeta débito", "Tarjeta crédito", "Transferencia", "Crédito cliente"].map((label, index) => ({ id: `10000000-0000-0000-0000-${String(index + 1).padStart(12, "0")}`, code: ["Cash", "DebitCard", "CreditCard", "Transfer", "Credit"][index], label }));
    const account = { accountId: "44444444-4444-4444-4444-444444444444", code: "130505", name: "Clientes nacionales", allowsPosting: true, isActive: true };
    let items: PaymentMethod[] = [], listReads = 0, typeReads = 0, writes = 0,bankReads=0;
    const bank={bankAccountId:"55555555-5555-5555-5555-555555555555",displayName:"Cuenta de recaudos",bankName:"Banco de prueba",accountNumber:"123456789",isActive:true};
    await page.route("**/api/**", async route => {
      const url = new URL(route.request().url()), path = url.pathname;
      let body: unknown = [];
      if (path.endsWith("/auth/me")) body = user;
      else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Auraly" }];
      else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Sede", timeZone: "America/Bogota" }];
      else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, roles: user.roles, permissions: user.permissions };
      else if (path.endsWith("/reference-options/payment-method")) { typeReads++; body = types; }
      else if (path.endsWith("/account-options")) body = { items: [account], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 };
      else if(path.endsWith("/bank-account-options")){bankReads++;body={items:[bank],page:1,pageSize:10,totalCount:1,totalPages:1};}
      else if (path.includes("/payment-methods")) {
        if (route.request().method() === "PUT") {
          writes++;
          const value = route.request().postDataJSON() as SavePaymentMethod;
          const type=types.find(type=>type.id===value.paymentTypeId)!;
          const method: PaymentMethod = { ...value, methodCode: type.code, typeLabel: type.label, accountCode: account.code, accountName: account.name, bankAccountId: value.bankAccountId??null,bankAccountName:value.bankAccountId?bank.displayName:null, version: writes, rowVersion: `VERSION-${writes}` };
          items = [method]; body = { method, page: { items, page: 1, pageSize: 50, totalCount: 1 } };
        } else { listReads++; body = { items, page: 1, pageSize: 50, totalCount: items.length }; }
      }
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
    });
    await page.goto("/dashboard/settings/masters");
    const navigation=page.getByRole("navigation").filter({has:page.getByRole("button",{name:/^Ubicación/})});
    await expect(navigation.getByRole("button").first()).toContainText("Ubicación");
    await expect(navigation.getByRole("button").last()).toContainText("Formas de pago");
    await expect(navigation.getByRole("button",{name:/Conceptos/})).toHaveCount(0);
    expect(listReads).toBe(0);
    await page.getByRole("button", { name: /^Formas de pago/ }).click();
    await expect(page.getByText("No hay formas de pago para este filtro.")).toBeVisible();
    expect(typeReads).toBe(0);
    await page.getByRole("button", { name: "Nueva forma", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: /^(Nueva|Editar) forma de pago$/ });
    await expect(dialog.getByRole("heading", { name: "Nueva forma de pago" })).toBeVisible();
    await dialog.getByLabel("Código", { exact: true }).fill("CREDITO-DOMICILIO");
    await dialog.getByLabel("Nombre", { exact: true }).fill("Cartera a domicilio de clientes con entrega en ruta");
    await dialog.getByRole("combobox", { name: "Tipo", exact: true }).click();
    await expect(page.getByRole("option", { name: "Por recaudar", exact: true })).toHaveCount(0);
    await page.getByRole("option", { name: "Crédito cliente", exact: true }).click();
    await expect(dialog.getByRole("checkbox", { name: "Ventas y pedidos" })).toBeChecked();
    await expect(dialog.getByRole("checkbox", { name: "Recibos de caja" })).toBeDisabled();
    await expect(dialog.getByRole("checkbox", { name: "Pagos a proveedores" })).toBeDisabled();
    await dialog.getByRole("combobox", { name: "Seleccionar cuenta contable" }).click();
    await page.getByRole("option", { name: "130505 · Clientes nacionales", exact: true }).click();
    const save = dialog.getByRole("button", { name: "Guardar forma", exact: true });
    await info.attach("modal-geometry", { body: JSON.stringify(await dialog.evaluate(element => [element, ...element.querySelectorAll("form, h2, form > div")].map(node => {
      const box = node.getBoundingClientRect(), css = getComputedStyle(node);
      return { tag: node.tagName, className: node.className, y: box.y, height: box.height, scrollTop: node.scrollTop, scrollHeight: node.scrollHeight, display: css.display, flex: css.flex, minHeight: css.minHeight, overflowY: css.overflowY };
    }))), contentType: "application/json" });
    await expect(save).toBeInViewport();
    await expect(dialog.getByRole("heading", { name: "Nueva forma de pago" })).toBeInViewport();
    const bounds = await dialog.boundingBox();
    expect(bounds!.y).toBeGreaterThanOrEqual(0);
    expect(bounds!.y + bounds!.height).toBeLessThanOrEqual(viewport.height);
    expect(await dialog.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
    await dialog.getByLabel("Orden en la lista", { exact: true }).scrollIntoViewIfNeeded();
    await expect(save).toBeInViewport();
    await expect(dialog.getByRole("heading", { name: "Nueva forma de pago" })).toBeInViewport();
    await page.screenshot({ path: info.outputPath(`payment-form-modal-${viewport.width}.png`) });
    await save.click();
    await expect(dialog).toBeHidden();
    await expect(page.getByRole("cell", { name: "Cartera a domicilio de clientes con entrega en ruta", exact: true })).toBeVisible();
    expect(listReads).toBe(1); expect(writes).toBe(1);
    await page.getByRole("button", { name: "Editar", exact: true }).click();
    await expect(dialog.getByRole("heading", { name: "Editar forma de pago" })).toBeVisible();
    await expect(dialog.getByLabel("Código", { exact: true })).toBeDisabled();
    await expect(dialog.getByLabel("Nombre", { exact: true })).toHaveValue(items[0].name);
    await dialog.getByRole("button", { name: "Cancelar", exact: true }).click();
    await expect(dialog).toBeHidden(); expect(writes).toBe(1); expect(listReads).toBe(1);
    expect(bankReads).toBe(0);
    await page.getByRole("button",{name:"Nueva forma",exact:true}).click();
    await dialog.getByLabel("Código",{exact:true}).fill("TRANSFERENCIA");
    await dialog.getByLabel("Nombre",{exact:true}).fill("Recaudos por transferencia");
    await dialog.getByRole("combobox",{name:"Tipo",exact:true}).click();
    await page.getByRole("option",{name:"Transferencia",exact:true}).click();
    expect(bankReads).toBe(0);
    const bankCombo=dialog.getByRole("combobox",{name:"Cuenta bancaria de transferencia",exact:true});
    await bankCombo.click();
    await page.getByRole("option",{name:/Cuenta de recaudos/}).click();
    await expect(bankCombo).toContainText("Cuenta de recaudos");expect(bankReads).toBe(1);
    await dialog.getByRole("combobox",{name:"Seleccionar cuenta contable"}).click();
    await page.getByRole("option",{name:"130505 · Clientes nacionales",exact:true}).click();
    const typeBounds=await dialog.getByRole("combobox",{name:"Tipo",exact:true}).boundingBox();
    const accountBounds=await dialog.getByRole("combobox",{name:"Seleccionar cuenta contable"}).boundingBox();
    const bankBounds=await bankCombo.boundingBox();
    expect(bankBounds!.y).toBeGreaterThanOrEqual(accountBounds!.y+accountBounds!.height);
    if(viewport.width>=640){
      expect(Math.abs(typeBounds!.y-accountBounds!.y)).toBeLessThanOrEqual(2);
      expect(Math.abs(typeBounds!.x-bankBounds!.x)).toBeLessThanOrEqual(2);
      expect(bankBounds!.width).toBeGreaterThan(accountBounds!.width);
    }
    await expect(save).toBeInViewport();
    await page.screenshot({path:info.outputPath(`transfer-form-${viewport.width}.png`)});
    await save.click();await expect(dialog).toBeHidden();
    expect(items[0].bankAccountId).toBe(bank.bankAccountId);expect(writes).toBe(2);expect(listReads).toBe(1);
    await page.getByRole("button",{name:"Editar",exact:true}).click();
    await expect(bankCombo).toContainText("Cuenta de recaudos");expect(bankReads).toBe(1);
  });
}
