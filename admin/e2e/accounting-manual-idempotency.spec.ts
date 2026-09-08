import { expect, test } from "@playwright/test";

test("reintenta el mismo comprobante sin duplicarlo cuando se pierde la primera respuesta", async ({ page, baseURL }) => {
  const tenantId="11111111-1111-1111-1111-111111111111",businessId="22222222-2222-2222-2222-222222222222";
  const user={userId:"44444444-4444-4444-4444-444444444444",tenantId,tenantKey:"TEST",username:"contador",firstName:"Contador",lastName:"Prueba",roles:["ACCOUNTANT"],permissions:["dashboard.read","accounting.read","accounting.manual.create"]};
  const bank={bankAccountId:"55555555-5555-5555-5555-555555555555",accountingAccountId:"66666666-6666-6666-6666-666666666666",accountingAccountCode:"11100501",accountingAccountName:"Banco",accountTypeOptionId:"77777777-7777-7777-7777-777777777777",accountTypeCode:"Savings",accountTypeName:"Ahorros",bankName:"Banco",accountNumber:"1234",displayName:"Cuenta",currencyCode:"COP",isPrimary:true,isActive:true,rowVersion:"AAAAAAAAAAE="};
  const cardAccount={accountId:"88888888-8888-8888-8888-888888888888",code:"130515",name:"Tarjetas crédito por cobrar",accountType:"Asset",allowsPosting:true,requiresParty:false,isActive:true};
  const bankAccount={accountId:bank.accountingAccountId,code:bank.accountingAccountCode,name:bank.accountingAccountName,accountType:"Asset",allowsPosting:true,requiresParty:false,isActive:true};
  const voucherIds:string[]=[];
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
    else if(path.endsWith("/accounts"))body=[bankAccount,cardAccount];
    else if(path.endsWith("/account-mappings"))body=[{mappingId:"99999999-9999-9999-9999-999999999999",tenantId,businessId:null,category:"CreditCardClearing",accountId:cardAccount.accountId,effectiveFrom:"2000-01-01",effectiveTo:null}];
    else if(path.includes("/reference-options/accounting-manual-concept"))body=[{id:"concept",code:"MANUAL_VOUCHER",label:"Comprobante manual"}];
    else if(path.includes("/reference-options/accounting-subledger-kind"))body=[{id:"payable",code:"Payable",label:"Por pagar"},{id:"receivable",code:"Receivable",label:"Por cobrar"}];
    else if(path.includes("/reference-options/accounting-adjustment-direction"))body=[{id:"increase",code:"Increase",label:"Aumentar"},{id:"decrease",code:"Decrease",label:"Disminuir"}];
    else if(path.endsWith("/manual/vouchers")&&method==="POST"){
      const request=route.request().postDataJSON();voucherIds.push(request.voucherId);
      if(voucherIds.length===1){await route.fulfill({status:500,contentType:"application/problem+json",body:JSON.stringify({title:"La respuesta se perdió"})});return;}
      body={documentId:request.voucherId,documentType:"ManualAccountingVoucher",status:"Posted",isDuplicate:true};
    } else if(path.includes("/payables" )||path.includes("/receivables"))body={items:[],page:1,pageSize:100,totalCount:0,totalPages:0};
    await route.fulfill({status:200,contentType:"application/json",body:JSON.stringify(body)});
  });
  await page.goto("/dashboard/accounting");
  await page.getByRole("button",{name:"Documentos manuales",exact:true}).click();
  await page.locator('input[maxlength="500"]').nth(1).fill("Liquidación de tarjeta");
  await page.getByRole("button",{name:"Agregar banco",exact:true}).click();
  await page.getByRole("button",{name:"Agregar tarjeta crédito",exact:true}).click();
  const rows=page.locator("tbody tr");
  await rows.nth(0).locator('input[type="number"]').nth(0).fill("970");
  await rows.nth(1).locator('input[type="number"]').nth(1).fill("970");
  const confirm=page.getByRole("button",{name:"Confirmar comprobante cuadrado",exact:true});
  await confirm.click();
  await expect.poll(()=>voucherIds.length).toBe(1);
  await confirm.click();
  await expect.poll(()=>voucherIds.length).toBe(2);
  expect(voucherIds[1]).toBe(voucherIds[0]);
});
