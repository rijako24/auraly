import { expect, test, type Page } from "@playwright/test";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { realAccountingContext } from "./support/real-accounting-workspace";

type Product = { productId: string; productCode: string; name: string; manageInventory: boolean };
type Fixture = { tenantId: string; businesses: Record<string, {businessId:string; warehouseId:string}>; products: Record<string, Product> };
type Balance = {productId:string; quantityOnHand:number};
test.use({serviceWorkers:"block",actionTimeout:60_000});

async function enter(page:Page,mode:string) {
  const active = mode === "RestaurantBar" ? null : page.waitForResponse(r=>new URL(r.url()).pathname.endsWith("/pos/drafts/active")&&r.ok());
  await page.goto("/pos?workspace=change");
  await page.getByRole("button",{name:"Comprobante de venta",exact:true}).click();
  await page.getByRole("button",{name:"Continuar a ventas",exact:true}).click();
  if(mode === "RestaurantBar") {
    await expect(page.getByRole("heading",{name:"¿A qué mesa atendemos?"})).toBeVisible({timeout:60_000});
    return;
  }
  const draft=await (await active!).json() as {lines:unknown[]};
  expect(draft.lines,"La prueba empieza sin descartar ventas anteriores.").toHaveLength(0);
  await expect(page.locator("#pos-scanner")).toBeEnabled();
}

for(const mode of ["DirectSale","Store","RestaurantBar"]) {
  test(`${mode}: vende recetas y combos desde la pantalla y concilia el inventario`,async({page})=>{
    test.skip(!process.env.AURALY_E2E_COMPOSITION_FIXTURE,"Requiere fixture local explícita.");
    test.setTimeout(600_000);
    const fixture=JSON.parse(readFileSync(resolve(process.env.AURALY_E2E_COMPOSITION_FIXTURE!),"utf8")) as Fixture;
    const business=fixture.businesses[mode];
    expect(process.env.AURALY_E2E_TENANT_ID).toBe(fixture.tenantId);
    const {scopedJson}=await realAccountingContext(page,business.businessId);
    const balances=()=>scopedJson<{items:Balance[];totalCount:number}>("GET",`/api/commerce/v1/inventory/balances?warehouseId=${business.warehouseId}&page=1&pageSize=100`);
    await enter(page,mode);
    for(const key of ["recipeNo","recipeYes","comboNo","comboYes"]) {
      const product=fixture.products[key];
      const before=await balances();
      expect(before.totalCount).toBeLessThanOrEqual(100);
      if(mode === "RestaurantBar") {
        await page.getByRole("button",{name:/UI-INV/}).click();
        await expect(page.getByRole("heading",{name:"Elige productos"})).toBeVisible();
        await page.getByRole("textbox",{name:"Buscar producto",exact:true}).fill(product.productCode);
        await page.getByRole("button",{name:`Agregar ${product.name}`,exact:true}).click();
        const plus=page.getByRole("button",{name:`Agregar uno de ${product.name}`,exact:true});
        for(let i=0;i<2;i++){await expect(plus).toBeEnabled();await plus.click();}
        await expect(page.getByText("3 unidades",{exact:true})).toBeVisible();
        await page.getByRole("button",{name:"Facturar mesa",exact:true}).click();
        const payment=page.getByRole("dialog",{name:"Cobrar mesa",exact:true});
        if(await payment.getByText("Factura electrónica",{exact:true}).count()) {
          await payment.getByRole("button",{name:"Cambiar documento",exact:true}).click();
          await page.getByRole("dialog",{name:"Tipo de documento",exact:true}).getByRole("button",{name:/Comprobante de venta/}).click();
        }
        await payment.getByLabel("Efectivo entregado",{exact:true}).fill("30000");
        await payment.getByRole("button",{name:"Sin imprimir",exact:true}).click();
        const completed=page.waitForResponse(r=>r.request().method()==="POST"&&/\/restaurant\/accounts\/[^/]+\/complete$/.test(new URL(r.url()).pathname),{timeout:90_000});
        await payment.getByRole("button",{name:"Cobrar y emitir documento",exact:true}).click();
        const response=await completed;expect(response.ok(),await response.text()).toBe(true);
        const result=await response.json() as {receipt:{documentId:string}};
        expect(result.receipt.documentId).toBeTruthy();
        await expect(page.getByRole("heading",{name:"¿A qué mesa atendemos?"})).toBeVisible();
      } else {
        if(mode==="Store") {
          await page.getByRole("textbox",{name:"Buscar producto en tiendas",exact:true}).fill(product.productCode);
          await page.getByRole("button",{name:`Agregar ${product.name}`,exact:true}).click();
        } else {
          const scanner=page.locator("#pos-scanner");await scanner.fill(product.productCode);await scanner.press("Enter");
        }
        const quantity=page.getByRole("spinbutton",{name:`Cantidad de ${product.name}`,exact:true});
        await expect(quantity).toHaveValue("1");
        await expect(page.locator("#pos-scanner")).toBeEnabled();
        await quantity.fill("3");await quantity.press("Enter");
        await expect(quantity).toHaveValue("3");await expect(page.locator("#pos-scanner")).toBeEnabled();
        await page.getByRole("button",{name:/^Cobrar/}).click();
        const payment=page.getByRole("dialog",{name:"Finalizar venta",exact:true});
        await payment.getByRole("textbox",{name:"Valor recibido en Efectivo",exact:true}).fill("30000");
        const completed=page.waitForResponse(r=>r.request().method()==="POST"&&/\/pos\/drafts\/[^/]+\/complete$/.test(new URL(r.url()).pathname),{timeout:90_000});
        await payment.getByRole("button",{name:"Sin imprimir",exact:true}).click();
        const response=await completed;expect(response.ok(),await response.text()).toBe(true);
        const result=await response.json() as {receipt:{documentId:string}};expect(result.receipt.documentId).toBeTruthy();
        await expect(payment).toBeHidden();
        await expect(page.getByRole("spinbutton",{name:/Cantidad de/})).toHaveCount(0);
        if(mode==="Store")await expect(page.getByRole("textbox",{name:"Buscar producto en tiendas",exact:true})).toHaveValue("");
      }
      const expected:Record<string,number>=key==="recipeNo"?{a:-6,b:-.75}:key==="recipeYes"?{recipeYes:-3}:key==="comboNo"?{recipeYes:-3,side:-3}:{comboYes:-3};
      // Inventory is asynchronous; read the authoritative bounded page until the
      // expected transition is visible. This is test observation, never app polling.
      await expect.poll(async()=>{
        const after=await balances();
        return Object.fromEntries(Object.entries(fixture.products).map(([id,p])=>{
          const old=before.items.find(b=>b.productId===p.productId);const current=after.items.find(b=>b.productId===p.productId);
          expect(old,`Balance aprovisionado antes: ${id}`).toBeTruthy();expect(current,`Balance aprovisionado después: ${id}`).toBeTruthy();
          return [id,Math.round((current!.quantityOnHand-old!.quantityOnHand)*10000)/10000];
        }));
      },{timeout:60_000,intervals:[250,500,1000,2000]}).toEqual(Object.fromEntries(Object.keys(fixture.products).map(id=>[id,expected[id]??0])));
      await page.screenshot({path:`test-results/composition-${mode}-${key}.png`,fullPage:true});
    }
  });
}
