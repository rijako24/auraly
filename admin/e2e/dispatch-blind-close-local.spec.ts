import { expect, test } from "@playwright/test";
import { readFileSync } from "node:fs";
import { login } from "./support/auth";
import type { DispatchExecution } from "../src/services/api/dispatches";

test("despacho real completa entregas, cierre ciego y consulta administrativa en móvil",async({page,baseURL,browser})=>{
  test.setTimeout(180_000);
  const profilePath=process.env.AURALY_DRIVER_CLOSE_PROFILE_PATH;
  test.skip(!profilePath,"Requiere un despacho local separado del reservado para el usuario.");
  expect(["127.0.0.1","localhost"]).toContain(new URL(baseURL!).hostname);
  const profile=JSON.parse(readFileSync(profilePath!,"utf8").replace(/^\uFEFF/,""));
  const selectContext=({tenant,business}:{tenant:string;business:string})=>{localStorage.setItem("selected_tenant_id",tenant);localStorage.setItem("selected_business_id",business)};
  await page.addInitScript(selectContext,{tenant:profile.TenantId,business:profile.BusinessId});
  await page.context().grantPermissions(["geolocation"]);await page.context().setGeolocation({latitude:4.6,longitude:-74.1});
  await login(page,{tenantKey:profile.TenantKey,username:profile.Username,password:profile.Password},90_000);
  await page.setViewportSize({width:390,height:844});
  await page.getByRole("button",{name:new RegExp(profile.DispatchNumber)}).click();
  const headers={"X-Tenant-Id":profile.TenantId,"X-Business-Id":profile.BusinessId};
  let execution=(await(await page.request.get(`/api/commerce/v1/dispatches/${profile.DispatchId}/execution`,{headers})).json()) as DispatchExecution;
  if(!execution.settlement){
    for(const sale of profile.Sales){
      if(execution.documents.find(document=>document.sourceDocumentId===sale.documentId)!.deliveryStatus!=="Pending")continue;
      await page.getByRole("button").filter({hasText:sale.documentNumber}).click();
      const delivery=page.getByRole("dialog",{name:"Confirmar entrega",exact:true});
      await delivery.getByRole("button",{name:/^Efectivo$/i}).click();
      const form=profile.Forms.find((item:{methodCode:string})=>item.methodCode==="Cash");
      await delivery.getByRole("combobox",{name:"Forma de pago de la entrega"}).click();
      await page.getByPlaceholder("Buscar código o nombre…").fill(form.name);
      await page.getByRole("option").filter({hasText:form.name}).first().click();
      const result=page.waitForResponse(response=>response.url().endsWith("/delivery-results")&&response.request().method()==="PUT");
      await delivery.getByRole("button",{name:"Confirmar entrega",exact:true}).click();
      const saved=await result;expect(saved.ok(),await saved.text()).toBe(true);execution=await saved.json();
    }
    await page.getByRole("button",{name:"Cerrar despacho",exact:true}).click();
    const close=page.getByRole("dialog",{name:"Cerrar despacho",exact:true});
    await expect(close.getByRole("textbox",{name:"Efectivo contado",exact:true})).toHaveValue("");
    await close.getByRole("textbox",{name:"Efectivo contado",exact:true}).fill("19000");
    await close.getByRole("textbox",{name:"Total de tarjetas",exact:true}).fill("0");
    await close.getByRole("textbox",{name:"Total de transferencias",exact:true}).fill("0");
    await expect(close).not.toContainText(/faltante|sobrante|esperado/i);
    const receiptResult=page.waitForResponse(response=>response.url().endsWith("/closure-receipt"));
    const closeResult=page.waitForResponse(response=>response.url().endsWith("/close-route"));
    await close.getByRole("button",{name:"Confirmar cierre",exact:true}).click();
    const closed=await closeResult;expect(closed.ok(),await closed.text()).toBe(true);execution=await closed.json();
    const receipt=await receiptResult;expect(receipt.ok(),await receipt.text()).toBe(true);
    const html=(await receipt.json()).html as string;
    expect(html).toContain("Valores declarados");expect(html).not.toContain("Efectivo esperado");
  }
  expect(execution.settlement!.declaredCash).toBe(19000);expect(execution.settlement!.declaredCard).toBe(0);expect(execution.settlement!.declaredTransfer).toBe(0);
  await expect(page.getByRole("region",{name:"Declaración guardada"})).toContainText("19.000");
  await expect(page.getByRole("button",{name:"Cerrar despacho",exact:true})).toHaveCount(0);
  await page.screenshot({path:"../work/release-preparation-current/transportador-cierre-real.png",fullPage:true});
  const adminPath=process.env.AURALY_DRIVER_ADMIN_PROFILE_PATH;
  expect(adminPath,"El escenario también comprueba la revisión administrativa").toBeTruthy();
  const admin=JSON.parse(readFileSync(adminPath!,"utf8").replace(/^\uFEFF/,""));
  const context=await browser.newContext({baseURL,viewport:{width:1440,height:1000}});
  try{
    const review=await context.newPage();await review.addInitScript(selectContext,{tenant:profile.TenantId,business:profile.BusinessId});
    await login(review,{tenantKey:admin.TenantKey,username:admin.Username,password:admin.Password},90_000);
    await review.goto("/dashboard/cash-differences");
    await review.getByRole("row").filter({hasText:profile.DispatchNumber}).getByRole("button",{name:"Ver despacho",exact:true}).click();
    const dialog=review.getByRole("dialog",{name:"Recepción y liquidación del despacho"});
    await expect(dialog).toContainText("Declaración del transportador");await expect(dialog).toContainText("19.000");
    await expect(dialog.getByRole("region",{name:"Recaudos por forma de pago"})).toContainText("3 facturas");
    await review.screenshot({path:"../work/release-preparation-current/administrativo-cierre-real.png",fullPage:true});
  }finally{await context.close()}
});
