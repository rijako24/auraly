import {expect,test} from "@playwright/test";
import type {DeliveryResultInput} from "../src/services/api/dispatches";

const creditScenarios: { name: string; credit: number; notDelivered: boolean; collect?: string; width?: number; keep?:boolean; assigned?:boolean }[] = [
  {name:"crédito total",credit:50000,notDelivered:false},{name:"crédito con pago previo",credit:25000,notDelivered:false},
  {name:"crédito no entregado",credit:50000,notDelivered:true},{name:"comprobante pagado",credit:0,notDelivered:false},
  {name:"factura electrónica pagada",credit:0,notDelivered:false},
  {name:"creador sin asignación",credit:0,notDelivered:false,assigned:false},
  ...["Cash","DebitCard","CreditCard","Transfer"].flatMap(collect=>[390,1366].map(width=>({name:`crédito recaudado ${collect} ${width}`,credit:50000,notDelivered:false,collect,width}))),
  ...[0,50000].flatMap(credit=>[390,1366].map(width=>({name:`pago conservado ${credit} ${width}`,credit,notDelivered:false,keep:true,width}))),
];
for(const scenario of creditScenarios){
  test(`despacho confirma ${scenario.name} sin duplicar el recaudo`,async({page,baseURL})=>{
    test.setTimeout(120_000);
    const tenantId="11111111-1111-1111-1111-111111111111",businessId="22222222-2222-2222-2222-222222222222";
    const user={userId:"33333333-3333-3333-3333-333333333333",tenantId,tenantKey:"@test",username:"driver",firstName:"Transportador",lastName:"Prueba",roles:[],permissions:["dispatches.read","dispatches.delivery.execute"]};
    await page.context().addCookies([{name:"auth_token",value:"e2e",url:baseURL!,httpOnly:true}]);
    await page.addInitScript(({user,businessId})=>{localStorage.setItem("selected_tenant_id",user.tenantId);localStorage.setItem("selected_business_id",businessId);localStorage.setItem("auth-state",JSON.stringify({state:{isAuthenticated:true,user},version:0}))},{user,businessId});
    const dispatch={dispatchId:"dispatch-credit",dispatchNumber:"DSP-CREDITO",scheduledDate:"2026-11-17",driverName:"Transportador",status:"InDelivery",documentCount:1,lineCount:1,expectedQuantity:1,verifiedQuantity:1,shortageQuantity:0,rowVersion:"AAAAAAAAAAA="};
    const document={dispatchSourceDocumentId:"source",sourceDocumentId:"sale",documentType:scenario.name==="comprobante pagado"?"SalesReceipt":"SalesInvoice",documentNumber:"FV-CREDITO",customerName:"Cliente a crédito",documentTotal:50000,creditAmount:scenario.credit,creditCollectibleAmount:scenario.collect?30000:scenario.credit,uncollectedPaymentAmount:scenario.keep&&scenario.credit===0?50000:0,collectionMode:scenario.keep?"Dispatch":"Session",sequence:1,deliveryStatus:"Pending",payments:[],returns:[],lines:[],notes:null};
    const execution={...dispatch,documents:[document],expenses:[],settlement:null,isAssignedDriver:scenario.assigned!==false};
    let moneyCatalogReads=0,uploads=0;const submissions:DeliveryResultInput[]=[];
    await page.route("**/api/**",async route=>{
      const request=route.request(),path=new URL(request.url()).pathname;let body:unknown=[];
      if(path.endsWith("/auth/me"))body=user;
      else if(path.endsWith("/execution-context/tenants"))body=[{tenantId,name:"Empresa"}];
      else if(path.endsWith("/execution-context/businesses"))body=[{tenantId,businessId,name:"Sede",timeZone:"America/Bogota"}];
      else if(path.endsWith("/execution-context/access"))body={tenantId,businessId,roles:[],permissions:user.permissions};
      else if(path.endsWith("/reference-options/payment-method")){moneyCatalogReads++;body=[{code:"Cash",label:"Efectivo"},{code:"DebitCard",label:"Tarjeta débito"},{code:"CreditCard",label:"Tarjeta crédito"},{code:"Transfer",label:"Transferencia"}];}
      else if(path.replace(/\/$/,"").endsWith("/payment-methods")){moneyCatalogReads++;const type=new URL(request.url()).searchParams.get("methodCode");body={items:[1,2].map(index=>({paymentMethodId:`${type}-${index}`,methodCode:type,name:`Forma ${type} ${index}`,code:`F-${index}`,accountCode:"110505",accountName:"Auxiliar configurado",bankAccountId:type==="Transfer"?"bank-id":null,version:1})),page:1,pageSize:10,totalCount:2};}
      else if(path.endsWith("/reference-options/card-franchise"))body=[{code:"Visa",label:"Visa"}];
      else if(path.endsWith("/delivery-reasons"))body=[{dispatchReasonId:"reason",reasonType:"NotDelivered",code:"Absent",name:"Cliente ausente"}];
      else if(path.endsWith("/execution"))body=execution;
      else if(path.endsWith("/evidence")){uploads++;body={url:"https://qa.invalid/factura-firmada.png"};}
      else if(path.endsWith("/delivery-results")){submissions.push(request.postDataJSON());body={...execution,documents:[{...document,deliveryStatus:submissions[0].deliveryStatus,payments:submissions[0].payments}]};}
      else if(path.endsWith("/dispatches"))body={items:[dispatch],page:1,pageSize:50,totalCount:1,totalPages:1};
      await route.fulfill({status:200,contentType:"application/json",body:JSON.stringify(body)});
    });
    await page.setViewportSize({width:scenario.width??390,height:844});await page.goto("/dashboard/deliveries");await page.getByRole("button",{name:/DSP-CREDITO/}).click();await page.getByRole("button",{name:"Entregar",exact:true}).click();
    const delivery=page.getByRole("dialog",{name:"Confirmar entrega",exact:true});
    if(scenario.credit>0) await expect(delivery.getByRole("button",{name:"Crédito",exact:true})).toBeDisabled();
    else if(!scenario.keep)await expect(delivery).toContainText("Pago ya registrado en la sesión original");
    if(!scenario.keep){await expect(delivery.getByRole("combobox",{name:"Forma de pago de la entrega"})).toHaveCount(0);
      await expect(delivery.getByRole("button",{name:/^(Efectivo|Tarjeta débito|Tarjeta crédito|Transferencia)$/})).toHaveCount(0);}
    if(scenario.credit>0) await expect(delivery).toContainText("Se conserva el saldo de cartera hasta confirmar su recaudo");
    const confirm=delivery.getByRole("button",{name:"Confirmar entrega",exact:true});
    if(scenario.notDelivered){await delivery.getByRole("button",{name:"No se entregó",exact:true}).click();await expect(confirm).toBeEnabled();}
    else if(scenario.credit>0){await expect(confirm).toBeDisabled();await delivery.locator('input[type="file"]').setInputFiles({name:"firmada.png",mimeType:"image/png",buffer:Buffer.from("firma de prueba")});await expect(confirm).toBeEnabled();}
    if(scenario.keep){
      await delivery.getByRole("button",{name:"Mantener forma de pago actual",exact:true}).click();
      await expect(delivery).toContainText("no se mueve tu sesión de dinero");
      await expect(confirm).toBeEnabled();
      await expect(delivery.getByRole("combobox",{name:"Forma de pago de la entrega"})).toHaveCount(0);
      expect(await delivery.evaluate(element=>element.scrollWidth<=element.clientWidth+1)).toBe(true);
      await page.screenshot({path:test.info().outputPath(`keep-current-${scenario.credit}-${scenario.width}.png`)});
    }
    if(scenario.collect){
      await delivery.getByRole("checkbox",{name:/Recaudar saldo de crédito/}).check();
      const names:Record<string,string>={Cash:"Efectivo",DebitCard:"Tarjeta débito",CreditCard:"Tarjeta crédito",Transfer:"Transferencia"};
      if(scenario.collect!=="Cash")await delivery.getByRole("button",{name:names[scenario.collect],exact:true}).click();
      const editor=scenario.collect==="Cash"?delivery:page.getByRole("dialog").last();
      await editor.getByRole("combobox",{name:"Forma de pago de la entrega"}).click();
      await page.getByRole("option",{name:new RegExp(`Forma ${scenario.collect} 2`)}).click();
      if(scenario.collect==="Transfer")await editor.getByLabel("Referencia").fill("TRF-REAL");
      else if(scenario.collect!=="Cash"){
        await editor.getByRole("combobox",{name:"Franquicia"}).click();await page.getByRole("option",{name:"Visa",exact:true}).click();
        await editor.getByLabel(/aprobación/i).fill("APR-REAL");
      }
      if(scenario.collect!=="Cash")await editor.getByRole("button",{name:/^Guardar/}).click();
      await expect(confirm).toBeEnabled();
      expect(await delivery.evaluate(element=>element.scrollWidth<=element.clientWidth+1)).toBe(true);
      await page.screenshot({path:test.info().outputPath(`credit-delivery-${scenario.collect}-${scenario.width}.png`)});
    }
    await confirm.click();await expect(delivery).toHaveCount(0);expect(submissions).toHaveLength(1);if(!scenario.collect&&!scenario.keep)expect(moneyCatalogReads).toBe(0);expect(uploads).toBe(scenario.notDelivered||scenario.credit===0?0:1);
    if(scenario.keep){
      expect(submissions[0].payments).toContainEqual({applicationType:"KeepCurrent",paymentMethod:null,amount:0,reference:null,evidenceUrl:null});
      expect(submissions[0].payments.filter(payment=>payment.amount>0)).toHaveLength(0);
    }else if(scenario.collect){
      expect(submissions[0].payments).toHaveLength(2);
      expect(submissions[0].payments).toContainEqual(expect.objectContaining({applicationType:"CreditAdvance",amount:30000,paymentMethod:scenario.collect,paymentMethodId:`${scenario.collect}-2`}));
    }else expect(submissions[0].payments).toEqual(scenario.notDelivered||scenario.credit===0?[]:[{applicationType:"CreditDocument",paymentMethod:null,amount:0,reference:null,evidenceUrl:"https://qa.invalid/factura-firmada.png"}]);
    expect(submissions[0].deliveryStatus).toBe(scenario.notDelivered?"NotDelivered":"Delivered");
    await expect(page.getByRole("button",{name:"Cerrar despacho",exact:true})).toHaveCount(scenario.assigned===false?0:1);
  });
}


test("ordena dos veces con la versión autoritativa y refresca sólo por acción del usuario",async({page,baseURL})=>{
  const tenantId="11111111-1111-1111-1111-111111111111",businessId="22222222-2222-2222-2222-222222222222";
  const user={userId:"33333333-3333-3333-3333-333333333333",tenantId,tenantKey:"@test",username:"driver",firstName:"Transportador",lastName:"Prueba",roles:[],permissions:["dispatches.read","dispatches.delivery.execute"]};
  await page.context().addCookies([{name:"auth_token",value:"e2e",url:baseURL!,httpOnly:true}]);
  await page.addInitScript(({user,businessId})=>{localStorage.setItem("selected_tenant_id",user.tenantId);localStorage.setItem("selected_business_id",businessId);localStorage.setItem("auth-state",JSON.stringify({state:{isAuthenticated:true,user},version:0}))},{user,businessId});
  const dispatch={dispatchId:"route-order",dispatchNumber:"DSP-ORDEN",scheduledDate:"2026-11-18",driverName:"Transportador",status:"InDelivery",documentCount:3,lineCount:3,expectedQuantity:3,verifiedQuantity:3,shortageQuantity:0,rowVersion:"version-1"};
  let documents=Array.from({length:3},(_,index)=>({dispatchSourceDocumentId:`source-${index}`,sourceDocumentId:`sale-${index}`,documentType:"SalesReceipt",documentNumber:`CVI-${index}`,customerName:`Cliente ${index}`,documentTotal:10000,creditAmount:0,collectionMode:"Dispatch",sequence:index+1,deliveryStatus:"Pending",payments:[],returns:[],lines:[],notes:null}));
  let version="version-1",listReads=0,executionReads=0;
  const changes:{rowVersion:string;orderedDocumentIds:string[]}[]=[];
  const execution=()=>({...dispatch,rowVersion:version,documents,expenses:[],settlement:null});
  await page.route("**/api/**",async route=>{
    const request=route.request(),path=new URL(request.url()).pathname;let body:unknown=[];
    if(path.endsWith("/auth/me"))body=user;
    else if(path.endsWith("/execution-context/tenants"))body=[{tenantId,name:"Empresa"}];
    else if(path.endsWith("/execution-context/businesses"))body=[{tenantId,businessId,name:"Sede",timeZone:"America/Bogota"}];
    else if(path.endsWith("/execution-context/access"))body={tenantId,businessId,roles:[],permissions:user.permissions};
    else if(path.endsWith("/execution")){executionReads++;body=execution();}
    else if(path.endsWith("/dispatches")){listReads++;body={items:[{...dispatch,rowVersion:version}],page:1,pageSize:50,totalCount:1,totalPages:1};}
    else if(path.endsWith("/delivery-order")){
      const change=request.postDataJSON() as typeof changes[number];
      expect(change.rowVersion).toBe(version);changes.push(change);
      documents=change.orderedDocumentIds.map(id=>documents.find(document=>document.dispatchSourceDocumentId===id)!);
      version=`version-${changes.length+1}`;body=execution();
    }
    await route.fulfill({status:200,contentType:"application/json",body:JSON.stringify(body)});
  });
  await page.setViewportSize({width:390,height:844});await page.goto("/dashboard/deliveries");
  await page.getByRole("button",{name:/DSP-ORDEN/}).click();
  for(let change=1;change<=2;change++){
    const down=page.getByRole("button",{name:"Bajar",exact:true}).first();
    await expect(down).toBeEnabled();await down.click();await expect.poll(()=>changes.length).toBe(change);
    await expect(down).toBeEnabled();
  }
  expect(executionReads).toBe(1);expect(changes[0].rowVersion).toBe("version-1");expect(changes[1].rowVersion).toBe("version-2");
  await page.getByRole("button",{name:"Volver a mis despachos"}).click();
  await expect(page.getByRole("button",{name:/DSP-ORDEN/})).toBeVisible();expect(listReads).toBe(1);
  await page.getByRole("button",{name:"Actualizar mis despachos"}).click();await expect.poll(()=>listReads).toBe(2);
  await page.getByRole("button",{name:/DSP-ORDEN/}).click();
  await page.getByRole("button",{name:"Actualizar despacho",exact:true}).click();await expect.poll(()=>executionReads).toBe(2);
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);
});
