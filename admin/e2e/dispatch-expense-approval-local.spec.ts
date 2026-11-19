import {expect,test} from "@playwright/test";
import {readFileSync,writeFileSync} from "node:fs";
import {login} from "./support/auth";
import type {DispatchExecution} from "../src/services/api/dispatches";
test.use({trace:"off",actionTimeout:20_000});

test("despacho real: reportar, cerrar a ciegas, aprobar, rechazar y recibir el efectivo una vez",async({browser,baseURL})=>{
  test.setTimeout(240_000);
  const driverFile=process.env.AURALY_DRIVER_EXPENSE_PROFILE_PATH,adminFile=process.env.AURALY_DRIVER_ADMIN_PROFILE_PATH;
  test.skip(!driverFile||!adminFile,"Requiere la preparación local aislada de gastos del despacho.");
  expect(["127.0.0.1","localhost"]).toContain(new URL(baseURL!).hostname);
  const profile=JSON.parse(readFileSync(driverFile!,"utf8").replace(/^\uFEFF/,""));
  const admin=JSON.parse(readFileSync(adminFile!,"utf8").replace(/^\uFEFF/,""));
  const contexts=await Promise.all([browser.newContext({baseURL,viewport:{width:390,height:844}}),browser.newContext({baseURL,viewport:{width:1440,height:1000}})]);
  const [driverContext,adminContext]=contexts;
  const select=({tenant,business}:{tenant:string;business:string})=>{localStorage.setItem("selected_tenant_id",tenant);localStorage.setItem("selected_business_id",business)};
  const headers={"X-Tenant-Id":profile.TenantId,"X-Business-Id":profile.BusinessId};
  const path=`/api/commerce/v1/dispatches/${profile.DispatchId}`;
  try{
    const receiver=await adminContext.newPage();await receiver.addInitScript(select,{tenant:profile.TenantId,business:profile.BusinessId});
    await login(receiver,{tenantKey:admin.TenantKey,username:admin.Username,password:admin.Password},90_000);
    const optionsResponse=await receiver.request.get("/api/commerce/v1/expenses/options",{headers});expect(optionsResponse.ok(),await optionsResponse.text()).toBe(true);
    const options=await optionsResponse.json();
    const conceptName=`QA gasto despacho ${profile.DispatchId.slice(0,8)}`;
    let concept=options.concepts.find((item:{name:string})=>item.name===conceptName);
    if(!concept){const id=crypto.randomUUID();expect(options.expenseAccounts.length).toBeGreaterThan(0);const saved=await receiver.request.put(`/api/commerce/v1/expenses/concepts/${id}`,{headers,data:{conceptId:id,businessId:profile.BusinessId,name:conceptName,expenseAccountId:options.expenseAccounts[0].accountId,defaultCostCenterId:null,withholdingConceptCode:null,isActive:true}});expect(saved.ok(),await saved.text()).toBe(true);concept=await saved.json();}
    const sitesResponse=await receiver.request.get("/api/commerce/v1/portfolio/parties/site-options?role=Supplier&page=1&pageSize=20",{headers});expect(sitesResponse.ok(),await sitesResponse.text()).toBe(true);
    const sites=await sitesResponse.json();const supplier=sites.items.find((site:{supplierPurchaseEvidencePolicy:string|null})=>site.supplierPurchaseEvidencePolicy!=="BuyerElectronicSupportDocument");expect(supplier).toBeTruthy();
    const tax=await receiver.request.put(`/api/commerce/v1/taxation/counterparty-profiles/${supplier.roleId}`,{headers,data:{businessId:profile.BusinessId,counterpartyId:supplier.roleId,appliesWithholding:false,responsibilities:[],jurisdictionCode:"11001"}});expect(tax.ok(),await tax.text()).toBe(true);
    const formsResponse=await receiver.request.get("/api/commerce/v1/payment-methods/?usage=SupplierPayments&methodCode=Cash&page=1&pageSize=10",{headers});expect(formsResponse.ok(),await formsResponse.text()).toBe(true);const cash=(await formsResponse.json()).items[0];expect(cash).toBeTruthy();
    const driver=await driverContext.newPage();await driver.addInitScript(select,{tenant:profile.TenantId,business:profile.BusinessId});
    await login(driver,{tenantKey:profile.TenantKey,username:profile.Username,password:profile.Password},90_000);
    await driver.getByRole("button",{name:new RegExp(profile.DispatchNumber)}).click();
    let execution=(await(await driver.request.get(`${path}/execution`,{headers})).json()) as DispatchExecution;
    if(!execution.settlement){
      for(const sale of profile.Sales){
        if(execution.documents.find(document=>document.sourceDocumentId===sale.documentId)!.deliveryStatus!=="Pending")continue;
        await driver.getByRole("button").filter({hasText:sale.documentNumber}).click();const delivery=driver.getByRole("dialog",{name:"Confirmar entrega",exact:true});
        const form=profile.Forms.find((item:{methodCode:string})=>item.methodCode==="Cash");
        // This fixture has one cash form, which the shared selector chooses automatically.
        await expect(delivery.getByRole("combobox",{name:"Forma de pago de la entrega"})).toContainText(form.name);
        const result=driver.waitForResponse(response=>response.url().endsWith("/delivery-results")&&response.request().method()==="PUT");await delivery.getByRole("button",{name:"Confirmar entrega",exact:true}).click();const saved=await result;expect(saved.ok(),await saved.text()).toBe(true);execution=await saved.json();
      }
      await driver.getByRole("button",{name:/^Gastos/}).click();
      for(const amount of [1000,500]){
        if(execution.expenses.some(item=>item.amount===amount))continue;
        await driver.getByRole("button",{name:"Registrar gasto",exact:true}).click();const report=driver.getByRole("dialog",{name:"Otro gasto del despacho",exact:true});
        await report.getByRole("combobox",{name:"Concepto de gasto del despacho"}).click();await driver.getByPlaceholder("Buscar concepto…").fill(conceptName);await driver.getByRole("option").filter({hasText:conceptName}).click();
        await report.getByLabel("Valor",{exact:true}).fill(String(amount));
        const result=driver.waitForResponse(response=>response.url().endsWith("/expenses")&&response.request().method()==="POST");await report.getByRole("button",{name:"Guardar gasto",exact:true}).click();const saved=await result;expect(saved.ok(),await saved.text()).toBe(true);execution=await saved.json();
      }
      await driver.getByRole("button",{name:"Cerrar despacho",exact:true}).click();const close=driver.getByRole("dialog",{name:"Cerrar despacho",exact:true});
      await close.getByRole("textbox",{name:"Efectivo contado",exact:true}).fill(String(execution.documents.reduce((sum,item)=>sum+item.documentTotal,0)-1000));
      await close.getByRole("textbox",{name:"Total de tarjetas",exact:true}).fill("0");await close.getByRole("textbox",{name:"Total de transferencias",exact:true}).fill("0");
      const saved=driver.waitForResponse(response=>response.url().endsWith("/close-route"));await close.getByRole("button",{name:"Confirmar cierre",exact:true}).click();expect((await saved).ok()).toBe(true);
    }
    await receiver.goto("/dashboard/cash-differences");await receiver.getByRole("row").filter({hasText:profile.DispatchNumber}).getByRole("button",{name:"Ver despacho",exact:true}).click();
    const review=receiver.getByRole("dialog",{name:"Recepción y liquidación del despacho",exact:true});
    execution=(await(await receiver.request.get(`${path}/execution`,{headers})).json()) as DispatchExecution;
    const approvedReport=execution.expenses.find(item=>item.amount===1000)!;
    if(approvedReport.approvalStatus==="Pending"){
      const row=review.getByText(`${conceptName} · $ 1.000`,{exact:true}).locator("..").locator("..");
      await row.getByRole("button",{name:"Aprobar",exact:true}).click();const approval=receiver.getByRole("dialog",{name:"Aprobar gasto del despacho",exact:true});
      await approval.getByRole("combobox",{name:"Efectivo usado en el gasto del despacho"}).click();await receiver.getByPlaceholder("Buscar código o nombre…").fill(cash.name);await receiver.getByRole("option").filter({hasText:cash.name}).first().click();
      await approval.getByRole("combobox",{name:"Seleccionar proveedor y sede"}).click();await receiver.getByPlaceholder("Buscar por nombre o identificación…").fill(supplier.identification);await receiver.getByRole("option").filter({hasText:supplier.siteName}).first().click();
      const number=approval.getByLabel("Número de factura del proveedor");if(await number.count())await number.fill(`QA-DSP-${profile.DispatchId.slice(0,8)}`);
      const result=receiver.waitForResponse(response=>response.url().endsWith("/expenses/confirm"));await approval.getByRole("button",{name:"Confirmar gasto",exact:true}).click();const saved=await result;expect(saved.ok(),await saved.text()).toBe(true);await expect(approval).toHaveCount(0);
    }
    const rejection=execution.expenses.find(item=>item.amount===500)!;
    if(rejection.approvalStatus==="Pending"){
      await review.getByRole("button",{name:"Rechazar",exact:true}).click();const reject=receiver.getByRole("dialog",{name:"Rechazar gasto del despacho",exact:true});await reject.getByLabel("Motivo del rechazo").fill("Soporte no aceptado en la prueba local");await reject.getByRole("button",{name:"Confirmar rechazo",exact:true}).click();await expect(reject).toHaveCount(0);
    }
    await expect.poll(async()=>{execution=(await(await receiver.request.get(`${path}/execution`,{headers})).json()) as DispatchExecution;return execution.expenses.find(item=>item.amount===1000)?.acceptedExpenseStatus},{timeout:30_000,intervals:[500,1000,2000]}).toBe("Processed");
    expect(execution.expenses.find(item=>item.amount===500)!.approvalStatus).toBe("Rejected");expect(execution.settlement!.expectedCash).toBe(execution.settlement!.grossCash-1000);
    await review.getByRole("button",{name:"Actualizar",exact:true}).click();await expect(review).toContainText("Gasto registrado");
    await receiver.screenshot({path:"../work/release-preparation-current/despacho-gastos-aprobado-rechazado-real.png",fullPage:true});
    const currentResponse=await receiver.request.get("/api/commerce/v1/work-sessions/current",{headers});expect(currentResponse.status(),await currentResponse.text()).toBe(200);
    const session=await currentResponse.json();expect(session.workSessionId).toBe(profile.SourceWorkSessionId);
    const previewPath=`/api/commerce/v1/work-sessions/${session.workSessionId}/closure-preview`;
    const beforeResponse=await receiver.request.get(previewPath,{headers});expect(beforeResponse.ok(),await beforeResponse.text()).toBe(true);const before=await beforeResponse.json();
    // A resumed, already accepted fixture would include the extra movement in both
    // snapshots and could falsely pass this regression. Use a fresh receipt instead.
    expect(execution.settlement!.status,"La comparación de caja requiere un despacho aún sin recepción administrativa").toBe("PendingReview");
    await review.getByRole("textbox",{name:"Efectivo físico recibido",exact:true}).fill(String(execution.settlement!.expectedCash));const result=receiver.waitForResponse(response=>response.url().endsWith("/settle"));await review.getByRole("button",{name:"Aceptar liquidación",exact:true}).click();const saved=await result;expect(saved.ok(),await saved.text()).toBe(true);
    const afterResponse=await receiver.request.get(previewPath,{headers});expect(afterResponse.ok(),await afterResponse.text()).toBe(true);const after=await afterResponse.json();
    writeFileSync("../work/release-preparation-current/dispatch-paid-cash-session-check.json",JSON.stringify({dispatchId:profile.DispatchId,sourceWorkSessionId:profile.SourceWorkSessionId,before:before.expectedCash,after:after.expectedCash,received:execution.settlement!.expectedCash},null,2));
    expect(after.expectedCash,"Las ventas de contado ya registradas en esta misma caja no pueden sumarse otra vez al recibir el despacho").toBeLessThanOrEqual(before.expectedCash);
    await expect.poll(async()=>{const result=await receiver.request.get(`${path}/execution`,{headers});expect(result.ok(),await result.text()).toBe(true);return ((await result.json()) as DispatchExecution).status},{timeout:30_000,intervals:[500,1000,2000]}).toBe("Closed");
  }finally{await Promise.all(contexts.map(context=>context.close()))}
});
