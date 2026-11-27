import { expect, test } from "@playwright/test";

test("vincula un reembolso real sin cargar devoluciones por cada venta ni volver a pagarlo",async({page,baseURL})=>{
  test.setTimeout(120_000);
  const tenant="11111111-1111-1111-1111-111111111111",business="22222222-2222-2222-2222-222222222222";
  const dispatchId="44444444-4444-4444-4444-444444444444",sale="55555555-5555-5555-5555-555555555555",refund="66666666-6666-6666-6666-666666666666";
  const user={userId:"33333333-3333-3333-3333-333333333333",tenantId:tenant,tenantKey:"AURALY",username:"receiver",firstName:"Prueba",lastName:"Receptor",roles:[],
    permissions:["dispatches.read","dispatches.read-all","dispatches.settle","sales.returns.read","sales.returns.create"]};
  await page.context().addCookies([{name:"auth_token",value:"e2e",url:baseURL!,httpOnly:true}]);
  await page.addInitScript(({tenant,business,user})=>{
    localStorage.setItem("selected_tenant_id",tenant);localStorage.setItem("selected_business_id",business);
    localStorage.setItem("auth-state",JSON.stringify({state:{isAuthenticated:true,user},version:0}));
  },{tenant,business,user});
  const document={dispatchSourceDocumentId:"source",sourceDocumentId:sale,documentNumber:"CVI-101",customerName:"Cliente",
    documentTotal:10000,creditAmount:0,collectionMode:"Session",deliveryStatus:"PartiallyDelivered",returnTotal:5000,lines:[],returns:[],payments:[]};
  const execution={dispatchId,dispatchNumber:"DSP-101",scheduledDate:"2026-11-18",driverName:"Transportador",vehiclePlate:null,status:"PendingSettlement",
    documents:[document],expenses:[],settlement:{grossCash:0,approvedCashExpenses:0,expectedCash:0,declaredCash:0,difference:0,depositTotal:0,
      remainingCreditTotal:0,creditAdvanceTotal:0,returnTotal:5000,undeliveredTotal:0,dispatchTotal:10000,balanceDifference:0,previouslyCollectedTotal:5000,status:"PendingReview"}};
  let returnReads=0,executionReads=0,otherWrites=0;
  let command:{returnIds?:string[];cashReceived?:number}|null=null;
  const linkedExecution={...execution,status:"SettlementProcessing",settlement:{...execution.settlement,status:"Processing"},linkedReturns:[{returnId:refund,originalDocumentId:sale,documentNumber:"DEV-101",totalAmount:5000,economicResolution:"Refund"}]};
  await page.route("**/api/**",async route=>{
    const request=route.request(),url=new URL(request.url()),path=url.pathname;
    let body:unknown=[];
    if(request.method()!=="GET"&&!path.endsWith("/settle"))otherWrites++;
    if(path.endsWith("/auth/me"))body=user;
    else if(path.endsWith("/execution-context/tenants"))body=[{tenantId:tenant,name:"Prueba"}];
    else if(path.endsWith("/execution-context/businesses"))body=[{tenantId:tenant,businessId:business,name:"Sede",timeZone:"America/Bogota"}];
    else if(path.endsWith("/execution-context/access"))body={tenantId:tenant,businessId:business,roles:[],permissions:user.permissions};
    else if(path.endsWith("/work-sessions/current"))body={workSessionId:"receiver-session"};
    else if(path.endsWith("/closures/inbox"))body={items:[{closureId:"close-id",type:"Dispatch",cashClosure:null,dispatchClosure:{dispatchId,dispatchNumber:"DSP-101",businessId:business,businessName:"Sede",driverName:"Transportador",closedAt:"2026-11-18T14:00:00Z",openedAt:"2026-11-18T12:00:00Z",status:command?"Processing":"PendingReview",expectedCash:0,declaredCash:0,cashReceived:null,difference:0}}],page:1,pageSize:50,totalItems:1};
    else if(path.endsWith("/sales-returns")){
      returnReads++;expect(url.searchParams.get("originalDocumentId")).toBe(sale);expect(url.searchParams.get("status")).toBe("Processed");
      body={items:[{returnId:refund,documentNumber:"DEV-101",originalDocumentId:sale,originalDocumentNumber:"CVI-101",economicResolution:"Refund",totalAmount:5000,status:"Processed"}],page:1,pageSize:10,totalCount:1,totalPages:1};
    }else if(path.endsWith("/settle")){
      command=request.postDataJSON();
      body=linkedExecution;
    }else if(path.endsWith("/execution")){executionReads++;body=command?linkedExecution:execution;}
    await route.fulfill({status:200,contentType:"application/json",body:JSON.stringify(body)});
  });
  await page.goto("/dashboard/cash-differences");
  await page.getByRole("button",{name:"Conciliar",exact:true}).click();
  const dialog=page.getByRole("dialog",{name:"Recepción y liquidación del despacho"});
  const section=dialog.getByRole("region",{name:"Devoluciones administrativas vinculadas"});
  await expect(section).toBeVisible();expect(returnReads).toBe(0);
  await expect(section.getByRole("link",{name:"Registrar devolución de CVI-101"})).toHaveAttribute("href","/dashboard/sales-returns?document=CVI-101");
  await section.getByRole("combobox",{name:"Devoluciones registradas"}).click();
  await page.getByRole("option",{name:/DEV-101/}).click();
  await page.keyboard.press("Escape");
  await expect(section).toContainText("DEV-101");expect(returnReads).toBe(1);
  await expect(section.getByRole("combobox",{name:"Devoluciones registradas"})).toContainText("1 seleccionado");
  await page.setViewportSize({width:390,height:844});
  expect(await dialog.evaluate(element=>element.scrollWidth<=element.clientWidth)).toBe(true);
  await section.scrollIntoViewIfNeeded();
  await page.screenshot({path:"../work/release-preparation-current/dispatch-administrative-return-mobile.png",fullPage:true});
  await dialog.getByRole("button",{name:"Aceptar liquidación",exact:true}).click();
  await expect.poll(()=>command).not.toBeNull();
  expect(command).toEqual(expect.objectContaining({cashReceived:0,returnIds:[refund]}));
  expect(executionReads).toBe(1);expect(otherWrites).toBe(0);
  await dialog.getByRole("button",{name:"Cerrar",exact:true}).click();
  await page.getByRole("button",{name:"Ver cierre",exact:true}).click();
  await expect(section).toContainText("DEV-101");
  await expect(section.getByRole("combobox",{name:"Devoluciones registradas"})).toBeDisabled();
  await expect(section).toContainText("Reembolso");
  expect(returnReads).toBe(1);
});
