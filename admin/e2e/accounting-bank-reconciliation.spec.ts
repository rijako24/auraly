import {expect,test} from "@playwright/test";

test("liquida tarjetas y cierra la conciliación bancaria en español",async({page,baseURL})=>{
  const tenantId="11111111-1111-1111-1111-111111111111",businessId="22222222-2222-2222-2222-222222222222",reconciliationId="33333333-3333-3333-3333-333333333333";
  const user={userId:"44444444-4444-4444-4444-444444444444",tenantId,tenantKey:"TEST",username:"contador",firstName:"Contador",lastName:"Prueba",roles:["ACCOUNTANT"],permissions:["dashboard.read","accounting.read","accounting.configure","accounting.manual.create","accounting.bank-reconciliation.read","accounting.bank-reconciliation.manage","accounting.bank-reconciliation.close"]};
  const bank={bankAccountId:"55555555-5555-5555-5555-555555555555",accountingAccountId:"66666666-6666-6666-6666-666666666666",accountingAccountCode:"11100501",accountingAccountName:"Bancolombia ahorros",accountTypeOptionId:"77777777-7777-7777-7777-777777777777",accountTypeCode:"Savings",accountTypeName:"Ahorros",bankName:"Bancolombia",accountNumber:"1234",displayName:"Cuenta principal",currencyCode:"COP",isPrimary:true,isActive:true,rowVersion:"AAAAAAAAAAE="};
  const summary={reconciliationId,bankAccountId:bank.bankAccountId,bankDisplayName:bank.displayName,accountingAccountCode:bank.accountingAccountCode,accountingAccountName:bank.accountingAccountName,periodFrom:"2026-09-01",periodTo:"2026-09-30",openingBalance:0,closingBalance:970,fileName:"extracto.csv",fileSha256:"AB",status:"Preparation",statementLineCount:1,matchedLineCount:0,statementMovementTotal:970,bookMovementTotal:970,difference:0,requiresReview:false,rowVersion:"AAAAAAAAAAI=",updatedAt:"2026-09-30T12:00:00-05:00"};
  const statement={statementLineId:"88888888-8888-8888-8888-888888888888",lineNumber:1,transactionDate:"2026-09-05",description:"Abono operador de tarjetas",reference:"OP-1",amount:970,balance:970,allocatedAmount:0,isMatched:false};
  const book={entryId:"99999999-9999-9999-9999-999999999999",entryLineNumber:1,entryNumber:"CC-0001",occurredAt:"2026-09-05T12:00:00-05:00",businessId,businessName:"Sede",sourceDocumentType:"ManualAccountingVoucher",sourceDocumentId:"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",description:"Abono neto al banco",amount:970,allocatedAmount:0,availableAmount:970};
  let detail={summary,statementLines:[statement],bookLines:[book],allocations:[] as Array<Record<string,unknown>>,statusEvents:[] as Array<Record<string,unknown>>},imports=0,allocations=0,closes=0;
  await page.context().addCookies([{name:"auth_token",value:"e2e",url:baseURL!,httpOnly:true,sameSite:"Lax"}]);
  await page.addInitScript(({tenantId,businessId,user})=>{localStorage.setItem("selected_tenant_id",tenantId);localStorage.setItem("selected_business_id",businessId);localStorage.setItem("auth-state",JSON.stringify({state:{isAuthenticated:true,user},version:0}))},{tenantId,businessId,user});
  await page.route("**/api/**",async route=>{
    const path=new URL(route.request().url()).pathname,method=route.request().method();let body:unknown=[];
    if(path==="/api/auth/me")body=user;
    else if(path.endsWith("/execution-context/tenants"))body=[{tenantId,name:"Prueba"}];
    else if(path.endsWith("/execution-context/businesses"))body=[{tenantId,businessId,name:"Sede"}];
    else if(path.endsWith("/execution-context/access"))body={tenantId,businessId,roles:user.roles,permissions:user.permissions};
    else if(path.endsWith("/readiness"))body={status:"Ready",blockingIssues:[],canEditOpeningBalances:false};
    else if(path.endsWith("/bank-accounts"))body=[bank];
    else if(path.endsWith("/bank-reconciliations")&&method==="POST"){imports++;body=detail}
    else if(path.endsWith("/bank-reconciliations")&&method==="GET")body=imports?[detail.summary]:[];
    else if(path.endsWith(reconciliationId))body=detail;
    else if(path.endsWith("/allocations")&&method==="POST"){
      allocations++;detail={...detail,summary:{...detail.summary,matchedLineCount:1,rowVersion:"AAAAAAAAAAM="},statementLines:[{...statement,allocatedAmount:970,isMatched:true}],bookLines:[{...book,allocatedAmount:970,availableAmount:0}],allocations:[{matchId:"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",statementLineId:statement.statementLineId,entryId:book.entryId,entryLineNumber:1,amount:970,createdAt:"2026-09-30T12:05:00-05:00"}]};body=detail;
    } else if(path.endsWith("/close")&&method==="POST"){closes++;detail={...detail,summary:{...detail.summary,status:"Closed",rowVersion:"AAAAAAAAAAQ="},statusEvents:[{statusEventId:"cccccccc-cccc-cccc-cccc-cccccccccccc",status:"Closed",reason:null,actorUserId:user.userId,actorName:"Contador Prueba",occurredAt:"2026-09-30T12:10:00-05:00",statementClosingBalance:970,bookClosingBalance:970,accountingCutoffPostedAt:"2026-09-30T12:09:00-05:00"}]};body=detail}
    await route.fulfill({status:method==="POST"&&path.endsWith("/bank-reconciliations")?201:200,contentType:"application/json",body:JSON.stringify(body)});
  });
  await page.goto("/dashboard/accounting");
  await page.getByRole("button",{name:"Conciliación bancaria",exact:true}).click();
  await page.locator('input[type="file"]').setInputFiles({name:"extracto.csv",mimeType:"text/csv",buffer:Buffer.from("fecha;descripcion;referencia;valor;saldo\n2026-09-05;Abono operador de tarjetas;OP-1;970;970\n")});
  const inputs=page.locator('form input[inputmode="decimal"]');await inputs.nth(0).fill("0");await inputs.nth(1).fill("970");
  await expect(page.getByText("Previsualización · 1 movimientos",{exact:true})).toBeVisible();
  await page.getByRole("button",{name:"Importar extracto revisado",exact:true}).click();
  await expect.poll(()=>imports).toBe(1);
  await expect(page.getByText("Abono operador de tarjetas",{exact:true}).first()).toBeVisible();
  await page.getByRole("button",{name:/Cruzar 1 coincidencias exactas/}).click();
  await expect.poll(()=>allocations).toBe(1);
  await page.getByRole("button",{name:"Cerrar conciliación",exact:true}).click();
  await expect.poll(()=>closes).toBe(1);
  await expect(page.getByText("Cerrada",{exact:true}).first()).toBeVisible();
  await expect(page.getByText(/Cierre.*Contador Prueba/)).toBeVisible();
});

test("rechaza valores ambiguos antes de importar el extracto",async({page,baseURL})=>{
  const tenantId="11111111-1111-1111-1111-111111111111",businessId="22222222-2222-2222-2222-222222222222";
  const user={userId:"44444444-4444-4444-4444-444444444444",tenantId,tenantKey:"TEST",username:"contador",firstName:"Contador",lastName:"Prueba",roles:["ACCOUNTANT"],permissions:["dashboard.read","accounting.read","accounting.bank-reconciliation.read","accounting.bank-reconciliation.manage"]};
  await page.context().addCookies([{name:"auth_token",value:"e2e",url:baseURL!,httpOnly:true,sameSite:"Lax"}]);
  await page.addInitScript(({tenantId,businessId,user})=>{localStorage.setItem("selected_tenant_id",tenantId);localStorage.setItem("selected_business_id",businessId);localStorage.setItem("auth-state",JSON.stringify({state:{isAuthenticated:true,user},version:0}))},{tenantId,businessId,user});
  await page.route("**/api/**",async route=>{const path=new URL(route.request().url()).pathname;let body:unknown=[];if(path==="/api/auth/me")body=user;else if(path.endsWith("/execution-context/tenants"))body=[{tenantId,name:"Prueba"}];else if(path.endsWith("/execution-context/businesses"))body=[{tenantId,businessId,name:"Sede"}];else if(path.endsWith("/execution-context/access"))body={tenantId,businessId,roles:user.roles,permissions:user.permissions};else if(path.endsWith("/readiness"))body={status:"Ready",blockingIssues:[],canEditOpeningBalances:false};else if(path.endsWith("/bank-accounts"))body=[{bankAccountId:"55555555-5555-5555-5555-555555555555",accountingAccountId:"66666666-6666-6666-6666-666666666666",accountingAccountCode:"11100501",accountingAccountName:"Banco",accountTypeOptionId:"77777777-7777-7777-7777-777777777777",accountTypeCode:"Savings",accountTypeName:"Ahorros",bankName:"Banco",accountNumber:"1234",displayName:"Cuenta",currencyCode:"COP",isPrimary:true,isActive:true,rowVersion:"AAAAAAAAAAE="}];await route.fulfill({status:200,contentType:"application/json",body:JSON.stringify(body)});});
  await page.goto("/dashboard/accounting");
  await page.getByRole("button",{name:"Conciliación bancaria",exact:true}).click();
  await page.locator('input[type="file"]').setInputFiles({name:"ambiguo.csv",mimeType:"text/csv",buffer:Buffer.from("fecha;descripcion;valor;saldo\n2026-09-05;Abono;1.234,56;1.234,56\n")});
  await expect(page.getByText(/valor ambiguo/)).toBeVisible();
  await expect(page.getByRole("button",{name:"Importar extracto revisado",exact:true})).toBeDisabled();
});
