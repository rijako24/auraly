import { expect, test } from "@playwright/test";

test("cierres abre modales distintos para caja y despacho sin cargar detalles ocultos", async ({ page, baseURL }) => {
  test.setTimeout(120_000);
  const tenantId="11111111-1111-1111-1111-111111111111", businessId="22222222-2222-2222-2222-222222222222";
  const user={userId:"33333333-3333-3333-3333-333333333333",tenantId,tenantKey:"AURALY",username:"receiver",firstName:"Receptor",lastName:"Prueba",roles:[],permissions:["work-sessions.differences.read","work-sessions.closures.reconcile","dispatches.settle"]};
  await page.context().addCookies([{name:"auth_token",value:"e2e",url:baseURL!,httpOnly:true}]);
  await page.addInitScript(({tenantId,businessId,user})=>{localStorage.setItem("selected_tenant_id",tenantId);localStorage.setItem("selected_business_id",businessId);localStorage.setItem("auth-state",JSON.stringify({state:{isAuthenticated:true,user},version:0}));},{tenantId,businessId,user});
  const cash={workSessionClosureId:"cash-id",workSessionId:"session-id",businessId,businessName:"Sede",warehouseName:"Bodega",userId:user.userId,userName:"Cajero prueba",openedAt:"2026-11-17T12:00:00Z",closedAt:"2026-11-17T14:00:00Z",salesCount:1,creditSalesCount:0,returnCount:0,totalSales:100000,totalRefunds:0,netAmount:100000,reconciliationStatus:"Pending",accountingStatus:"AccountingDisabled",paymentTotals:[]};
  const dispatch={dispatchId:"dispatch-id",dispatchNumber:"DSP-PRUEBA",businessId,businessName:"Sede",driverName:"Transportador prueba",closedAt:"2026-11-17T14:00:00Z",status:"PendingReview",expectedCash:50000,declaredCash:50000,cashReceived:null,difference:0};
  const execution={...dispatch,scheduledDate:"2026-11-17",status:"PendingSettlement",vehiclePlate:"ABC123",expenses:[{expenseId:"expense-1",category:"Peaje",amount:1000,description:"Paso por peaje",approvalStatus:"Rejected",approvedAmount:0}],settlement:{grossCash:50000,approvedCashExpenses:0,expectedCash:50000,declaredCash:50000,difference:0,depositTotal:0,remainingCreditTotal:0,creditAdvanceTotal:0,returnTotal:0,undeliveredTotal:0,dispatchTotal:50000,balanceDifference:0,status:"PendingReview"},documents:[{dispatchSourceDocumentId:"document-1",documentNumber:"FVL-123",customerName:"Cliente entrega",documentTotal:50000,deliveryStatus:"Delivered",deliveryAddress:"Dirección del cliente",payments:[{paymentId:"payment-1",applicationType:"InvoicePayment",paymentMethod:"Cash",amount:50000}],returns:[],lines:[]}]};
  let executionReads=0,cashDetailReads=0,inboxReads=0;
  await page.route("**/api/**",async route=>{
    const path=new URL(route.request().url()).pathname; let body:unknown=[];
    if(path.endsWith("/auth/me"))body=user;
    else if(path.endsWith("/execution-context/tenants"))body=[{tenantId,name:"Prueba"}];
    else if(path.endsWith("/execution-context/businesses"))body=[{tenantId,businessId,name:"Sede",timeZone:"America/Bogota"}];
    else if(path.endsWith("/execution-context/access"))body={tenantId,businessId,roles:[],permissions:user.permissions};
    else if(path.endsWith("/closures/inbox")){inboxReads++;body={items:[{closureId:"dispatch-close-id",type:"Dispatch",cashClosure:null,dispatchClosure:dispatch},{closureId:"cash-id",type:"Cash",cashClosure:cash,dispatchClosure:null}],page:1,pageSize:50,totalItems:2};}
    else if(path.endsWith("/dispatches/dispatch-id/execution")){executionReads++;body=execution;}
    else if(path.endsWith("/payment-verifications/page")){cashDetailReads++;body={items:[],groups:[],page:1,pageSize:1,totalItems:0};}
    else if(path.endsWith("/invoice-charges/page")){cashDetailReads++;body={items:[],page:1,pageSize:50,totalItems:0,totalAmount:0,closureSummary:{count:0,invoicedAmount:0,expenseAmount:0,paymentMethods:[]}};}
    await route.fulfill({status:200,contentType:"application/json",body:JSON.stringify(body)});
  });
  await page.goto("/dashboard/cash-differences");
  await expect(page.getByRole("cell",{name:"Despacho",exact:true})).toBeVisible();
  expect(executionReads).toBe(0);expect(cashDetailReads).toBe(0);expect(inboxReads).toBe(1);
  await page.getByRole("button",{name:"Ver despacho",exact:true}).click();
  const dispatchDialog=page.getByRole("dialog",{name:"Recepción y liquidación del despacho"});
  await expect(dispatchDialog).toContainText("DSP-PRUEBA");await expect(dispatchDialog).toContainText("Transportador prueba");
  await expect(dispatchDialog).toContainText("Declaración del transportador");await expect(dispatchDialog).toContainText("Peaje");
  await dispatchDialog.getByRole("region",{name:"Entregas del despacho"}).getByText("FVL-123 · Cliente entrega",{exact:true}).click();
  await expect(dispatchDialog).toContainText("Dirección del cliente");
  expect(executionReads).toBe(1);expect(cashDetailReads).toBe(0);expect(inboxReads).toBe(1);
  await page.screenshot({path:"../work/release-preparation-current/cierre-despacho-modal.png",fullPage:true});
  await page.setViewportSize({width:390,height:844});
  expect(await dispatchDialog.evaluate(element=>element.scrollWidth<=element.clientWidth)).toBe(true);
  await page.keyboard.press("Escape");await expect(dispatchDialog).toHaveCount(0);
  await page.getByRole("button",{name:"Conciliar",exact:true}).click();
  const cashDialog=page.getByRole("dialog",{name:"Conciliar cierre",exact:true});
  await expect(cashDialog).toBeVisible();await expect(cashDialog).toContainText("Cajero prueba");
  await expect(cashDialog.getByText("Entregas y recaudos",{exact:false})).toHaveCount(0);
  expect(executionReads).toBe(1);await expect.poll(()=>cashDetailReads).toBe(2);
});
