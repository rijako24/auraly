import { expect, test } from "@playwright/test";

for (const bankCount of [0,1,8]) {
for (const methodCode of ["Transfer", "DebitCard", "CreditCard"] as const) {
test(`cartera conserva tipos y permite seleccionar ${bankCount} formas ${methodCode}`, async ({page,baseURL}) => {
  const id=(value:number)=>`00000000-0000-0000-0000-${String(value).padStart(12,"0")}`;
  const tenantId=id(1),businessId=id(2),customerId=id(3);
  const user={userId:id(4),tenantId,tenantKey:"@forms",username:"forms",firstName:"Prueba",lastName:"Formas",
    roles:[],permissions:["receivables.read","receivables.payments.create","accounting.read"]};
  await page.context().addCookies([{name:"auth_token",value:"forms-test",url:baseURL!,httpOnly:true,sameSite:"Lax"}]);
  await page.addInitScript(({user,businessId})=>{
    localStorage.setItem("selected_tenant_id",user.tenantId);localStorage.setItem("selected_business_id",businessId);
    localStorage.setItem("auth-state",JSON.stringify({state:{isAuthenticated:true,user},version:0}));
  },{user,businessId});
  const forms=[{paymentMethodId:id(10),methodCode:"Cash",name:"Caja principal",bankAccountId:null},
    ...Array.from({length:bankCount},(_,index)=>({paymentMethodId:id(20+index),methodCode,name:`Banco ${index+1}`,bankAccountId:methodCode==="Transfer"?id(40+index):null}))]
    .map((form,index)=>({...form,code:`FORM-${index}`,paymentTypeId:id(60),typeLabel:form.methodCode,
      accountId:id(70+index),accountCode:`1110${index}`,accountName:form.name,useForSales:true,useForReceipts:true,
      useForSupplierPayments:false,useForPayroll:false,isActive:true,sortOrder:index,version:1,rowVersion:"AAAAAAAAAAE="}));
  const singleInvoice = bankCount === 8 && methodCode === "Transfer";
  const invoices=(singleInvoice ? [1] : [1,2]).map(index=>({receivableId:id(100+index),customerId,customerName:"Cliente configurado",
    documentNumber:`FV-${index}`,currencyCode:"COP",originalAmount:800,paidAmount:100,outstandingAmount:700,
    adjustmentAmount:0,...(singleInvoice ? {originalAmount:1500,outstandingAmount:1400} : {}),dueDate:"2026-10-05T12:00:00-05:00",issuedAt:"2026-09-01T12:00:00-05:00",
    createdAt:"2026-09-01T12:00:00-05:00",status:"PartiallyPaid",isOverdue:true,overdueDays:35,agingBandCode:"Overdue31To60"}));
  let confirmation:Record<string,unknown>|null=null;
  let configurationReads=0;
  let receiptListReads=0;
  await page.route("**/api/**",async route=>{
    const path=new URL(route.request().url()).pathname;
    let body:unknown=[];
    if(path==="/api/auth/me")body=user;
    else if(path.endsWith("/execution-context/tenants"))body=[{tenantId,name:"Pruebas"}];
    else if(path.endsWith("/execution-context/businesses"))body=[{tenantId,businessId,name:"Sede de prueba",timeZone:"America/Bogota"}];
    else if(path.endsWith("/execution-context/access"))body={tenantId,businessId,roles:[],permissions:user.permissions};
    else if(path.endsWith("/receivables/customers"))body={items:[{customerId,customerName:"Cliente configurado",identification:"1001",
      invoiceCount:invoices.length,originalAmount:singleInvoice?1500:1600,paidAmount:singleInvoice?100:200,outstandingAmount:1400,overdueAmount:1400}],page:1,pageSize:20,totalCount:1,totalPages:1,
      totalOutstanding:1400,totalOverdue:1400,totalInvoiceCount:invoices.length};
    else if(path.endsWith("/receivables"))body={items:invoices,page:1,pageSize:20,totalCount:invoices.length,totalPages:1,
      totalOriginal:singleInvoice?1500:1600,totalPaid:singleInvoice?100:200,totalAdjustments:0,totalOutstanding:1400,totalOverdue:1400,referenceDate:"2026-11-09"};
    else if(path.endsWith("/receivable-payments"))body={items:[],page:1,pageSize:20,totalCount:0,totalPages:0};
    else if(path.endsWith("/accounting/documents")){
      receiptListReads++;
      const items=confirmation?[{sourceDocumentId:confirmation.paymentId,sourceDocumentType:"ReceivablePayment",
        sourceDocumentNumber:"RCC-2",occurredAt:"2026-11-09T12:00:00-05:00",status:"CommercialEffectsApplied",attemptCount:1,
        errorCode:null,errorMessage:null,entryId:null,entryNumber:null,debitTotal:null,creditTotal:null,
        postedAt:null,fiscalDocumentType:null,dianNumber:null,uniqueCodeType:null,uniqueCode:null,fiscalStatus:null,
        hasManualDraft:false,voucherTypeCode:"CashReceipt",mainPartyName:"Cliente configurado"}]:[];
      body={items,page:1,pageSize:25,totalCount:items.length,totalPages:items.length};
    }
    else if(path.endsWith("/reference-options/payment-method"))body=["Cash","DebitCard","CreditCard","Transfer","Credit"].map((code,index)=>({id:id(200+index),code,label:{Cash:"Efectivo",DebitCard:"Tarjeta débito",CreditCard:"Tarjeta crédito",Transfer:"Transferencia",Credit:"Crédito cliente"}[code],sortOrder:index}));
    else if(path.endsWith("/reference-options/card-franchise"))body=[{id:id(250),code:"Visa",label:"Visa",sortOrder:1}];
    else if(path.endsWith("/accounting/settlement-configuration")){configurationReads++;body={isAccountingEnabled:false,bankAccounts:[],paymentMethods:{contractVersion:2,forms}};}
    else if(path.endsWith("/work-sessions/current"))body={workSessionId:id(5)};
    else if(path.endsWith("/receivable-payments/confirm")){
      confirmation=route.request().postDataJSON();body={paymentId:confirmation!.paymentId,accountingJobId:id(6),documentNumber:"RCC-2",
        status:"Processed",idempotentReplay:false,paidAt:"2026-11-09T12:00:00-05:00"};
    }
    await route.fulfill({status:200,contentType:"application/json",body:JSON.stringify(body)});
  });
  if(bankCount===1){
    await page.goto("/dashboard/cash-receipts");
    await expect(page.getByText("No hay documentos para estos filtros.", {exact:true})).toBeVisible();
    expect(receiptListReads).toBe(1);
    await page.getByRole("complementary").getByRole("link",{name:"Cuentas por cobrar",exact:true}).click();
    await expect(page.getByRole("heading",{name:"Cuentas por cobrar",exact:true})).toBeVisible();
  }else await page.goto("/dashboard/receivables");
  await page.getByRole("row",{name:/Cliente configurado/}).click();
  const selection=page.getByRole("dialog",{name:"Abono a cartera"});
  await expect(selection.getByText("Original · COP")).toBeVisible();
  await expect(selection.getByText("35 d")).toHaveCount(invoices.length);
  await expect(selection.getByRole("button", {name:"Fecha del pago",exact:true})).toHaveCount(0);
  await selection.getByLabel("Abono para FV-1").fill(singleInvoice?"1400":"700");
  if(!singleInvoice)await selection.getByLabel("Abono para FV-2").fill("700");
  await selection.getByRole("button",{name:"Ir a pagar"}).click();
  await page.getByRole("menuitem", { name: "Pago directo", exact: true }).click();
  const checkout=page.getByRole("dialog",{name:"Abono a cartera"});
  await expect(checkout.getByText("Cambio a entregar",{exact:true})).toHaveCount(0);
  const paymentDate = checkout.getByRole("button", {name:"Fecha del pago",exact:true});
  await expect(paymentDate).toContainText("9 de noviembre de 2026");
  if (bankCount === 1) {
    await paymentDate.click();
    await checkout.getByRole("button", {name:"8 de noviembre de 2026",exact:true}).click();
    await expect(paymentDate).toContainText("8 de noviembre de 2026");
  }
  const shortcut = methodCode === "Transfer" ? "F4" : methodCode === "DebitCard" ? "F2" : "F3";
  await page.keyboard.press(shortcut);
  const cardDialog = page.getByRole("dialog", {name:"Datos de la tarjeta",exact:true});
  if (bankCount !== 1) await expect(cardDialog).toHaveCount(0);
  if(bankCount===0) {
    await expect(checkout.getByText("No hay formas activas para este tipo de pago y operación. Revisa Formas de pago.")).toBeVisible();
    await expect(checkout.getByRole("button",{name:"Sin imprimir",exact:true})).toBeDisabled();
    expect(confirmation).toBeNull();
    expect(configurationReads).toBe(1);
    return;
  }
  if (methodCode !== "Transfer") {
    const picker = checkout.getByRole("combobox", {name:"Medio de pago"}).first();
    if (bankCount === 1) await expect(picker).toContainText("Banco 1");
    else {
      await picker.click();
      await page.getByRole("option", {name:"Banco 7",exact:true}).click();
    }
    await expect(cardDialog).toBeVisible();
    await cardDialog.getByPlaceholder("Aprobación del datáfono").fill("APROBACION-1");
    await cardDialog.getByRole("button", {name:"Guardar datos · Enter",exact:true}).click();
    await expect(cardDialog).toHaveCount(0);
    await picker.click();
    await page.getByRole("option", {name:bankCount === 1 ? "Banco 1" : "Banco 7",exact:true}).click();
    await expect(cardDialog).toHaveCount(0);
    await checkout.getByRole("button", {name:/Visa · APROBACION-1/}).click();
    await expect(cardDialog.getByPlaceholder("Aprobación del datáfono")).toHaveValue("APROBACION-1");
    await cardDialog.getByRole("button", {name:"Cerrar datos de tarjeta",exact:true}).click();
    const amount = checkout.getByRole("textbox", {name:methodCode === "DebitCard" ? "Valor recibido en Tarjeta débito" : "Valor recibido en Tarjeta crédito"}).first();
    await amount.fill("1600");
    await expect(checkout.getByText("La diferencia requiere un concepto o corregir los importes.", {exact:true})).toBeVisible();
    await expect(checkout.getByRole("button", {name:"Sin imprimir",exact:true})).toBeDisabled();
    await amount.fill("1400");
    // A covered amount still replaces the active type; cash has no data modal.
    await page.keyboard.press("F1");
    await expect(picker).toContainText("Caja principal");
    await expect(cardDialog).toHaveCount(0);
    await expect(checkout.getByRole("textbox", {name:"Valor recibido en Efectivo"})).toHaveValue("1.400");
    await page.keyboard.press(shortcut);
    if (bankCount !== 1) {
      await expect(cardDialog).toHaveCount(0);
      await picker.click(); await page.getByRole("option", {name:"Banco 8",exact:true}).click();
    }
    await expect(cardDialog).toBeVisible();
    await cardDialog.getByPlaceholder("Aprobación del datáfono").fill("APROBACION-2");
    await cardDialog.getByRole("button", {name:"Guardar datos · Enter",exact:true}).click();
    await checkout.getByRole("button", {name:"Sin imprimir",exact:true}).click();
    await expect.poll(()=>confirmation).not.toBeNull();
    expect(confirmation).toMatchObject({paidOn:bankCount===1?"2026-11-08":"2026-11-09",payments:[{methodCode,paymentMethodId:id(bankCount===1?20:27),
      amount:1400,cardFranchiseCode:"Visa",approvalNumber:"APROBACION-2"}]});
    expect(configurationReads).toBe(1);
    return;
  }
  if(bankCount===1) {
    await expect(checkout.getByRole("combobox",{name:"Medio de pago"})).toContainText("Banco 1");
    await page.getByPlaceholder("Número o referencia del comprobante").fill("REF-1");
    await page.getByRole("button",{name:"Guardar transferencia",exact:true}).click();
    await checkout.getByRole("button",{name:"Sin imprimir",exact:true}).click();
    await expect.poll(()=>confirmation).not.toBeNull();
    expect(confirmation).toMatchObject({paidOn:"2026-11-08",payments:[{methodCode:"Transfer",paymentMethodId:id(20),bankAccountId:id(40),amount:1400,reference:"REF-1"}]});
    expect(configurationReads).toBe(1);
    await expect(checkout).not.toBeVisible();
    expect(receiptListReads).toBe(1);
    await page.getByRole("complementary").getByRole("link",{name:"Recibos de caja",exact:true}).click();
    await expect(page.getByText("RCC-2",{exact:true})).toBeVisible();
    expect(receiptListReads).toBe(2);
    await page.getByRole("complementary").getByRole("link",{name:"Cuentas por cobrar",exact:true}).click();
    await expect(page.getByRole("heading",{name:"Cuentas por cobrar",exact:true})).toBeVisible();
    await page.getByRole("complementary").getByRole("link",{name:"Recibos de caja",exact:true}).click();
    await expect(page.getByText("RCC-2",{exact:true})).toBeVisible();
    expect(receiptListReads).toBe(2);
    return;
  }
  await checkout.getByRole("combobox",{name:"Medio de pago"}).first().click();
  await page.getByRole("option",{name:"Banco 7",exact:true}).click();
  await page.getByPlaceholder("Número o referencia del comprobante").fill("REF-7");
  await page.getByRole("button",{name:"Guardar transferencia",exact:true}).click();
  await checkout.getByRole("textbox",{name:"Valor recibido en Transferencia"}).first().fill("700");
  await page.keyboard.press("F4");
  await checkout.getByRole("combobox",{name:"Medio de pago"}).nth(1).click();
  await page.getByRole("option",{name:"Banco 8",exact:true}).click();
  await page.getByPlaceholder("Número o referencia del comprobante").fill("REF-8");
  await page.getByRole("button",{name:"Guardar transferencia",exact:true}).click();
  await checkout.getByRole("button",{name:"Sin imprimir",exact:true}).click();
  await expect.poll(()=>confirmation).not.toBeNull();
  expect(confirmation).toMatchObject({paidOn:"2026-11-09",allocations:[{amount:1400}],payments:[
    {methodCode:"Transfer",paymentMethodId:id(26),paymentMethodVersion:1,bankAccountId:id(46),amount:700,reference:"REF-7"},
    {methodCode:"Transfer",paymentMethodId:id(27),paymentMethodVersion:1,bankAccountId:id(47),amount:700,reference:"REF-8"},
  ]});
  expect(configurationReads).toBe(1);
});

}
}
