import { expect, test } from "@playwright/test";

test("transportador declara sin valores esperados, imprime y conserva un único cierre", async ({page,baseURL})=>{
  const tenantId="11111111-1111-1111-1111-111111111111",businessId="22222222-2222-2222-2222-222222222222";
  const user={userId:"33333333-3333-3333-3333-333333333333",tenantId,tenantKey:"AURALY",username:"driver",firstName:"Conductor",lastName:"Prueba",roles:[],permissions:["dispatches.read","dispatches.delivery.execute"]};
  await page.context().addCookies([{name:"auth_token",value:"e2e",url:baseURL!,httpOnly:true}]);
  await page.addInitScript(({tenantId,businessId,user})=>{localStorage.setItem("selected_tenant_id",tenantId);localStorage.setItem("selected_business_id",businessId);localStorage.setItem("auth-state",JSON.stringify({state:{isAuthenticated:true,user},version:0}));},{tenantId,businessId,user});
  const dispatch={dispatchId:"dispatch-id",dispatchNumber:"DSP-CIERRE",scheduledDate:"2026-11-17",driverName:"Conductor",status:"PendingSettlement",documentCount:1,rowVersion:"AAAAAAAAAAA="};
  const document={dispatchSourceDocumentId:"source-id",sourceDocumentId:"sale-id",documentType:"SalesReceipt",documentNumber:"CV-100",customerName:"Cliente",documentTotal:50000,creditAmount:0,deliveryStatus:"Delivered",payments:[],returns:[],lines:[]};
  let execution:Record<string,unknown>={...dispatch,documents:[document],expenses:[],settlement:null};
  let reads=0,closeWrites=0,prints=0;
  await page.route("**/api/**",async route=>{
    const path=new URL(route.request().url()).pathname;let body:unknown=[];
    if(path.endsWith("/auth/me"))body=user;
    else if(path.endsWith("/execution-context/tenants"))body=[{tenantId,name:"Prueba"}];
    else if(path.endsWith("/execution-context/businesses"))body=[{tenantId,businessId,name:"Sede local",timeZone:"America/Bogota"}];
    else if(path.endsWith("/execution-context/access"))body={tenantId,businessId,roles:[],permissions:user.permissions};
    else if(path.endsWith("/dispatches/dispatch-id/execution")){reads++;body=execution;}
    else if(path.endsWith("/close-route")){
      closeWrites++;const input=route.request().postDataJSON();
      expect(input).toEqual({declaredCash:19000,declaredCard:3000,declaredTransfer:4000,idempotencyKey:expect.any(String)});
      execution={...execution,settlement:{declaredCash:19000,declaredCard:3000,declaredTransfer:4000,transporterClosedBy:user.userId,transporterClosedAt:"2026-11-17T15:00:00Z",status:"PendingReview"}};body=execution;
    }else if(path.endsWith("/closure-receipt")){prints++;body={html:"<!doctype html><html><body>Cierre de despacho</body></html>"};}
    else if(path.endsWith("/dispatches"))body={items:[dispatch],page:1,pageSize:50,totalCount:1,totalPages:1};
    await route.fulfill({status:200,contentType:"application/json",body:JSON.stringify(body)});
  });
  await page.goto("/dashboard/deliveries");await page.getByRole("button",{name:/DSP-CIERRE/}).click();
  await page.setViewportSize({width:360,height:740});
  await page.getByRole("button",{name:"Gastos",exact:true}).click();
  await expect(page.getByRole("button",{name:"Registrar gasto",exact:true})).toBeVisible();
  await page.getByRole("button",{name:"Lista",exact:true}).click();
  await page.getByRole("button",{name:"Cerrar despacho",exact:true}).click();
  const close=page.getByRole("dialog",{name:"Cerrar despacho",exact:true});
  for(const label of ["Efectivo contado","Total de tarjetas","Total de transferencias"])await expect(close.getByRole("textbox",{name:label,exact:true})).toHaveValue("");
  await expect(close.getByRole("button",{name:"Confirmar cierre",exact:true})).toBeDisabled();
  await expect(close).not.toContainText("50.000");await expect(close).not.toContainText(/faltante|sobrante|esperado/i);
  await close.getByRole("textbox",{name:"Efectivo contado",exact:true}).fill("19000");
  await expect(close.getByRole("textbox",{name:"Efectivo contado",exact:true})).toHaveValue("19.000");
  await close.getByRole("textbox",{name:"Total de tarjetas",exact:true}).fill("3000");
  await close.getByRole("textbox",{name:"Total de transferencias",exact:true}).fill("4000");
  expect(await close.evaluate(element=>element.scrollWidth<=element.clientWidth)).toBe(true);
  await page.screenshot({path:"../work/release-preparation-current/transportador-cierre-ciego.png"});
  await close.getByRole("button",{name:"Confirmar cierre",exact:true}).click();
  await expect(close).toHaveCount(0);
  await expect(page.getByRole("region",{name:"Declaración guardada"})).toContainText("19.000");
  await expect(page.getByRole("button",{name:"Cerrar despacho",exact:true})).toHaveCount(0);
  await page.getByRole("button",{name:"Gastos",exact:true}).click();
  await expect(page.getByRole("button",{name:"Registrar gasto",exact:true})).toHaveCount(0);
  await expect.poll(()=>prints).toBe(1);expect(closeWrites).toBe(1);expect(reads).toBe(1);
  await page.getByRole("button",{name:"Imprimir cierre",exact:true}).click();
  await expect.poll(()=>prints).toBe(2);expect(closeWrites).toBe(1);expect(reads).toBe(1);
});
