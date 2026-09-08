import { expect, test } from "@playwright/test";

test("contador edita centros y reglas con versión y conserva el vínculo bancario visible", async ({ page, baseURL }) => {
  const tenantId="11111111-1111-1111-1111-111111111111", businessId="22222222-2222-2222-2222-222222222222";
  const permissions=["dashboard.read","accounting.read","accounting.configure","accounting.manual.create"];
  const user={userId:"44444444-4444-4444-4444-444444444444",tenantId,tenantKey:"TEST",username:"test",firstName:"Contador",lastName:"Prueba",roles:["ACCOUNTANT"],permissions};
  const centers=[
    {costCenterId:"55555555-5555-5555-5555-555555555555",businessId,code:"GENERAL",name:"Administración",parentCostCenterId:null,isDefault:true,isActive:true,rowVersion:"AAAAAAAAAAE="},
    {costCenterId:"66666666-6666-6666-6666-666666666666",businessId,code:"VENTAS",name:"Comercial",parentCostCenterId:null,isDefault:false,isActive:true,rowVersion:"AAAAAAAAAAI="},
  ];
  const rules=[{assignmentId:"77777777-7777-7777-7777-777777777777",businessId,costCenterId:centers[1].costCenterId,costCenterCode:"VENTAS",costCenterName:"Comercial",operationKind:"Sales",warehouseId:null,warehouseCode:null,warehouseName:null,isActive:true,rowVersion:"AAAAAAAAAAM="}];
  const writes:Array<{path:string;body:Record<string,unknown>}>=[];
  await page.context().addCookies([{name:"auth_token",value:"e2e",url:baseURL!,httpOnly:true,sameSite:"Lax"}]);
  await page.addInitScript(({tenantId,businessId,user})=>{
    localStorage.setItem("selected_tenant_id",tenantId);localStorage.setItem("selected_business_id",businessId);
    localStorage.setItem("auth-state",JSON.stringify({state:{isAuthenticated:true,user},version:0}));
  },{tenantId,businessId,user});
  await page.route("**/api/**",async route=>{
    const path=new URL(route.request().url()).pathname;
    let body:unknown=[];
    if(route.request().method()==="PUT"){
      const request=route.request().postDataJSON();writes.push({path,body:request});
      if(path.endsWith("/cost-center-assignments")){Object.assign(rules[0],request,{rowVersion:"AAAAAAAAAAQ="});body=rules[0]}
      else {Object.assign(centers[0],request,{rowVersion:"AAAAAAAAAAU="});body=centers[0]}
    } else if(path==="/api/auth/me")body=user;
    else if(path.endsWith("/execution-context/tenants"))body=[{tenantId,name:"Prueba"}];
    else if(path.endsWith("/execution-context/businesses"))body=[{tenantId,businessId,name:"Sede"}];
    else if(path.endsWith("/execution-context/access"))body={tenantId,businessId,roles:user.roles,permissions};
    else if(path.endsWith("/cost-centers"))body=centers;
    else if(path.endsWith("/cost-center-assignments"))body=rules;
    else if(path.includes("accounting-cost-center-operation"))body=[{id:"all",code:"All",label:"Todas las operaciones"},{id:"sales",code:"Sales",label:"Ventas"},{id:"expenses",code:"Expenses",label:"Gastos"}];
    else if(path.endsWith("/readiness"))body={status:"Ready",blockingIssues:[],canEditOpeningBalances:false};
    else if(path.endsWith("/bank-accounts"))body=[{bankAccountId:"bank",accountingAccountId:"account",accountingAccountCode:"11100501",accountingAccountName:"Banco principal",accountTypeName:"Ahorros",bankName:"Banco prueba",accountNumber:"123456",displayName:"Cuenta operativa",isPrimary:true,isActive:true}];
    await route.fulfill({status:200,contentType:"application/json",body:JSON.stringify(body)});
  });
  await page.goto("/dashboard/accounting");
  await page.getByRole("button",{name:"Cuentas bancarias",exact:true}).click();
  await expect(page.getByRole("row").filter({hasText:"Cuenta operativa"})).toContainText("11100501 · Banco principal");
  await page.getByRole("button",{name:"Centros de costo",exact:true}).click();
  await page.getByRole("button",{name:"Editar centro",exact:true}).first().click();
  await page.getByLabel("Nombre del centro",{exact:true}).fill("Administración central");
  await page.getByRole("button",{name:"Guardar centro",exact:true}).click();
  await expect.poll(()=>writes.length).toBe(1);
  expect(writes[0].body).toMatchObject({name:"Administración central",rowVersion:"AAAAAAAAAAE=",isDefault:true});
  await page.getByRole("button",{name:"Editar regla",exact:true}).click();
  await page.getByRole("combobox",{name:"Operación de la regla"}).click();
  await page.getByRole("option",{name:"Gastos",exact:true}).click();
  await page.getByRole("button",{name:"Guardar asignación",exact:true}).click();
  await expect.poll(()=>writes.length).toBe(2);
  expect(writes[1].body).toMatchObject({assignmentId:rules[0].assignmentId,operationKind:"Expenses",rowVersion:"AAAAAAAAAAM="});
  await page.getByRole("button",{name:"Desactivar regla",exact:true}).click();
  await expect.poll(()=>writes.length).toBe(3);
  expect(writes[2].body).toMatchObject({assignmentId:rules[0].assignmentId,isActive:false,rowVersion:"AAAAAAAAAAQ="});
  await expect(page.getByRole("button",{name:"Reactivar regla",exact:true})).toBeVisible();
  await page.screenshot({path:"test-results/accounting-cost-centers.png",fullPage:true});
});
