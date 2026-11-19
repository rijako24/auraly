import {expect,test} from "@playwright/test";
import type {DispatchExecution,DispatchExpense,DispatchStatus} from "../src/services/api/dispatches";

test("la revisión aprueba con Gastos, rechaza sin descontar y reutiliza el resultado sin releer",async({page,baseURL})=>{
  test.setTimeout(120_000);
  const tenantId="11111111-1111-1111-1111-111111111111",businessId="22222222-2222-2222-2222-222222222222";
  const dispatchId="44444444-4444-4444-4444-444444444444",conceptId="55555555-5555-5555-5555-555555555555",supplierId="66666666-6666-6666-6666-666666666666";
  const user={userId:"33333333-3333-3333-3333-333333333333",tenantId,tenantKey:"@test",username:"receiver",firstName:"Receptor",lastName:"Prueba",roles:[],permissions:["dispatches.read","dispatches.read-all","dispatches.settle","expenses.read","expenses.create"]};
  await page.context().addCookies([{name:"auth_token",value:"e2e",url:baseURL!,httpOnly:true}]);
  await page.addInitScript(({user,businessId})=>{localStorage.setItem("selected_tenant_id",user.tenantId);localStorage.setItem("selected_business_id",businessId);localStorage.setItem("auth-state",JSON.stringify({state:{isAuthenticated:true,user},version:0}))},{user,businessId});
  const dispatch={dispatchId,businessId,dispatchNumber:"DSP-GASTOS",scheduledDate:"2026-11-17",driverName:"Transportador",vehiclePlate:null,status:"PendingSettlement" as DispatchStatus,rowVersion:"AAAAAAAAAAA=",documentCount:1,lineCount:1,expectedQuantity:1,verifiedQuantity:1,shortageQuantity:0,documents:[],lines:[],shortages:[]};
  const report=(id:string,category:string,amount:number):DispatchExpense=>({expenseId:id,category,amount,description:category,evidenceUrl:null,approvalStatus:"Pending",approvedAmount:null,occurredAt:"2026-11-17T16:00:00Z",conceptId,acceptedExpenseId:null,acceptedExpenseStatus:null});
  let execution:DispatchExecution & {settlement:NonNullable<DispatchExecution["settlement"]>}={...dispatch,expenses:[report("report-approve","Peaje reportado",1000),report("report-reject","Parqueadero rechazado",500)],settlement:{grossCash:50000,approvedCashExpenses:0,expectedCash:50000,declaredCash:49000,declaredCard:0,declaredTransfer:0,difference:-1000,depositTotal:0,cardTotal:0,remainingCreditTotal:0,creditAdvanceTotal:0,returnTotal:0,undeliveredTotal:0,dispatchTotal:50000,balanceDifference:0,status:"PendingReview",receivedBy:null,receivedAt:null}};
  let reads=0,options=0,previews=0,confirmations=0,rejections=0;
  let confirmation:Record<string,unknown>|null=null;
  const cash={paymentMethodId:"77777777-7777-7777-7777-777777777777",methodCode:"Cash",code:"CAJA",name:"Caja del recorrido",version:3,accountCode:"110505",accountName:"Caja",isActive:true};
  await page.route("**/api/**",async route=>{
    const request=route.request(),url=new URL(request.url()),path=url.pathname;let body:unknown=[];
    if(path.endsWith("/auth/me"))body=user;
    else if(path.endsWith("/execution-context/tenants"))body=[{tenantId,name:"Empresa"}];
    else if(path.endsWith("/execution-context/businesses"))body=[{tenantId,businessId,name:"Sede",timeZone:"America/Bogota"}];
    else if(path.endsWith("/execution-context/access"))body={tenantId,businessId,roles:[],permissions:user.permissions};
    else if(path.endsWith("/closures/inbox"))body={items:[{closureId:"closure",type:"Dispatch",cashClosure:null,dispatchClosure:{dispatchId,dispatchNumber:dispatch.dispatchNumber,businessId,businessName:"Sede",driverName:"Transportador",closedAt:"2026-11-17T17:00:00Z",status:"PendingReview",expectedCash:50000,declaredCash:49000,cashReceived:null,difference:-1000}}],page:1,pageSize:50,totalItems:1};
    else if(path.endsWith("/execution")){reads++;body=execution;}
    else if(path.endsWith("/expenses/options")){options++;body={concepts:[{conceptId,businessId,code:"PEAJE",name:"Peaje",expenseAccountId:"account",expenseAccountCode:"519595",expenseAccountName:"Peajes",defaultCostCenterId:null,defaultCostCenterName:null,withholdingConceptCode:null,isActive:true}],suppliers:[],expenseAccounts:[],costCenters:[],taxes:[],taxTreatments:[{code:"CapitalizedCost",label:"Mayor valor del gasto"}],withholdingConceptCodes:[],purchaseEvidenceTypes:[{code:"InternalReceiptVoucher",label:"Comprobante interno"}]};}
    else if(path.replace(/\/$/,"").endsWith("/payment-methods")){expect(url.searchParams.get("usage")).toBe("SupplierPayments");expect(url.searchParams.get("methodCode")).toBe("Cash");body={items:[cash],page:1,pageSize:10,totalCount:1,totalPages:1};}
    else if(path.endsWith("/parties/site-options"))body={items:[{partyId:supplierId,roleId:supplierId,supplierId,partySiteId:"88888888-8888-8888-8888-888888888888",siteName:"Principal",displayName:"Proveedor de peajes",identification:"900123456",isPrimary:true,supplierPurchaseEvidencePolicy:"InternalReceiptVoucher",supplierDefaultPaymentDueDays:0}],page:1,pageSize:20,totalCount:1,totalPages:1};
    else if(path.endsWith("/expenses/preview")){previews++;const value=request.postDataJSON();expect(value.lines[0].conceptId).toBe(conceptId);body={lines:value.lines.map((line:object,index:number)=>({...line,lineNumber:index+1,expenseAccountId:"account",accountCode:"519595",accountName:"Peajes",vatAmount:0,taxRate:0})),calculationHash:"reviewed-expense",canConfirm:true,diagnostics:[],withholding:{grossAmount:1000,withholdingTotal:0,netAmount:1000,lines:[]}};}
    else if(path.endsWith("/expenses/confirm")){confirmations++;confirmation=request.postDataJSON();execution={...execution,expenses:execution.expenses.map(item=>item.expenseId==="report-approve"?{...item,approvalStatus:"Approved",approvedAmount:1000,acceptedExpenseId:confirmation!.expenseId as string,acceptedExpenseStatus:"Accepted"}:item),settlement:{...execution.settlement,approvedCashExpenses:1000,expectedCash:49000,difference:0}};body={expenseId:confirmation!.expenseId,status:"Accepted",documentNumber:"GTO-1",idempotentReplay:false,dispatchApproval:{dispatchId,dispatchExpenseId:"report-approve",approvedCash:1000,expectedCash:49000}};}
    else if(path.endsWith("/report-reject/review")){rejections++;expect(request.postDataJSON().decision).toBe("Rejected");execution={...execution,expenses:execution.expenses.map(item=>item.expenseId==="report-reject"?{...item,approvalStatus:"Rejected",approvedAmount:0}:item)};body=execution;}
    await route.fulfill({status:200,contentType:"application/json",body:JSON.stringify(body)});
  });
  await page.goto("/dashboard/cash-differences");await page.getByRole("row").filter({hasText:"DSP-GASTOS"}).getByRole("button",{name:"Ver despacho",exact:true}).click();
  const review=page.getByRole("dialog",{name:"Recepción y liquidación del despacho",exact:true});
  await review.getByRole("button",{name:"Aprobar",exact:true}).first().click();
  const approval=page.getByRole("dialog",{name:"Aprobar gasto del despacho",exact:true});await expect(approval).toContainText("Peaje reportado");
  await expect(approval.getByRole("combobox",{name:"Efectivo usado en el gasto del despacho"})).toContainText(cash.name);
  await approval.getByRole("combobox",{name:"Seleccionar proveedor y sede"}).click();await page.getByRole("option").filter({hasText:"Proveedor de peajes"}).click();
  await expect(approval.getByRole("button",{name:"Confirmar gasto",exact:true})).toBeEnabled();
  await approval.getByRole("button",{name:"Confirmar gasto",exact:true}).click();await expect(approval).toHaveCount(0);
  expect(confirmation).toMatchObject({dispatchId,dispatchExpenseId:"report-approve",dispatchCashPaymentMethodId:cash.paymentMethodId,dispatchCashPaymentMethodVersion:3});
  expect(confirmations).toBe(1);expect(options).toBe(1);expect(previews).toBe(1);expect(reads).toBe(1);
  await expect(review).toContainText("Gasto aceptado, pendiente de procesamiento");await expect(review.getByRole("button",{name:"Aceptar liquidación",exact:true})).toBeDisabled();
  await review.getByRole("button",{name:"Rechazar",exact:true}).click();const rejection=page.getByRole("dialog",{name:"Rechazar gasto del despacho",exact:true});
  await expect(rejection.getByRole("button",{name:"Confirmar rechazo",exact:true})).toBeDisabled();await rejection.getByLabel("Motivo del rechazo").fill("No existe un soporte válido");await rejection.getByRole("button",{name:"Confirmar rechazo",exact:true}).click();
  await expect(rejection).toHaveCount(0);expect(rejections).toBe(1);expect(reads).toBe(1);expect(execution.settlement.expectedCash).toBe(49000);await expect(review).toContainText("Rechazado");
});
