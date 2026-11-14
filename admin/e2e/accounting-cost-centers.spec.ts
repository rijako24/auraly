import { expect, test } from "@playwright/test";

test("contador edita centros y reglas con versión y conserva el vínculo bancario visible", async ({ page, baseURL }) => {
  const tenantId="11111111-1111-1111-1111-111111111111", businessId="22222222-2222-2222-2222-222222222222";
  const otherBusinessId="88888888-8888-8888-8888-888888888888";
  const permissions=["dashboard.read","accounting.read","accounting.configure","accounting.manual.create"];
  const user={userId:"44444444-4444-4444-4444-444444444444",tenantId,tenantKey:"TEST",username:"test",firstName:"Contador",lastName:"Prueba",roles:["ACCOUNTANT"],permissions};
  const centers=[
    {costCenterId:"55555555-5555-5555-5555-555555555555",tenantId,code:"GENERAL",name:"Administración",parentCostCenterId:null,isDefault:true,isActive:true,rowVersion:"AAAAAAAAAAE="},
    {costCenterId:"66666666-6666-6666-6666-666666666666",tenantId,code:"VENTAS",name:"Comercial",parentCostCenterId:null,isDefault:false,isActive:true,rowVersion:"AAAAAAAAAAI="},
  ];
  const rules=[{assignmentId:"77777777-7777-7777-7777-777777777777",businessId,costCenterId:centers[1].costCenterId,costCenterCode:"VENTAS",costCenterName:"Comercial",operationKind:"Sales",isActive:true,rowVersion:"AAAAAAAAAAM="}];
  const writes:Array<{path:string;body:Record<string,unknown>}>=[];
  const reads:URL[]=[];
  await page.context().addCookies([{name:"auth_token",value:"e2e",url:baseURL!,httpOnly:true,sameSite:"Lax"}]);
  await page.addInitScript(({tenantId,businessId,user})=>{
    localStorage.setItem("selected_tenant_id",tenantId);localStorage.setItem("selected_business_id",businessId);
    localStorage.setItem("auth-state",JSON.stringify({state:{isAuthenticated:true,user},version:0}));
  },{tenantId,businessId,user});
  await page.route("**/api/**",async route=>{
    const url=new URL(route.request().url()),path=url.pathname;
    if(route.request().method()==="GET")reads.push(url);
    let body:unknown=[];
    if(route.request().method()==="POST"&&path.endsWith("/cost-centers")){
      const request=route.request().postDataJSON();writes.push({path,body:request});
      const row={...request,isActive:true,rowVersion:"AAAAAAAAAAY="};centers.push(row);body=row;
    }else if(route.request().method()==="PUT"){
      const request=route.request().postDataJSON();writes.push({path,body:request});
      if(path.endsWith("/cost-center-assignments")){Object.assign(rules[0],request,{rowVersion:"AAAAAAAAAAQ="});body=rules[0]}
      else {Object.assign(centers[0],request,{rowVersion:"AAAAAAAAAAU="});body=centers[0]}
    } else if(path==="/api/auth/me")body=user;
    else if(path.endsWith("/execution-context/tenants"))body=[{tenantId,name:"Prueba"}];
    else if(path.endsWith("/execution-context/businesses"))body=[{tenantId,businessId,name:"Sede"},{tenantId,businessId:otherBusinessId,name:"Otra sede"}];
    else if(path.endsWith("/execution-context/access"))body={tenantId,businessId:route.request().headers()["x-business-id"]??businessId,roles:user.roles,permissions};
    else if(path.endsWith("/cost-centers")){
      const search=url.searchParams.get("search")?.toLowerCase()??"",ids=url.searchParams.getAll("ids");
      const filtered=centers.filter(row=>(!search||`${row.code} ${row.name}`.toLowerCase().includes(search))&&
        (!ids.length||ids.includes(row.costCenterId))&&(!url.searchParams.has("isDefault")||row.isDefault));
      const page=Number(url.searchParams.get("page")??1),pageSize=Number(url.searchParams.get("pageSize")??20);
      body={items:filtered.slice((page-1)*pageSize,page*pageSize),page,pageSize,totalCount:filtered.length,totalPages:Math.ceil(filtered.length/pageSize)};
    }
    else if(path.endsWith("/cost-center-assignments"))body=route.request().headers()["x-business-id"]===otherBusinessId?[]:rules;
    else if(path.includes("accounting-cost-center-operation"))body=[{id:"all",code:"All",label:"Todas las operaciones"},{id:"sales",code:"Sales",label:"Ventas"},{id:"expenses",code:"Expenses",label:"Gastos"}];
    else if(path.endsWith("/readiness"))body={status:"Ready",blockingIssues:[],canEditOpeningBalances:false};
    else if(path.endsWith("/bank-accounts"))body=[{bankAccountId:"bank",accountingAccountId:"account",accountingAccountCode:"11100501",auxiliaryCount:1,accountingAccountName:"Banco principal",accountTypeName:"Ahorros",bankName:"Banco prueba",accountNumber:"123456",displayName:"Cuenta operativa",isPrimary:true,isActive:true}];
    await route.fulfill({status:200,contentType:"application/json",body:JSON.stringify(body)});
  });
  await page.goto("/dashboard/accounting");
  await page.getByRole("button",{name:"Cuentas bancarias",exact:true}).click();
  await expect(page.getByRole("row").filter({hasText:"Cuenta operativa"})).toContainText("11100501 · Banco principal");
  await page.getByRole("button",{name:"Centros de costo",exact:true}).click();
  await expect(page.getByText("Centros de toda la empresa",{exact:true})).toBeVisible();
  await expect(page.getByRole("combobox",{name:"Bodega de la regla"})).toHaveCount(0);
  await expect(page.getByText("Bodega opcional",{exact:true})).toHaveCount(0);
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
  expect(writes[1].body).not.toHaveProperty("warehouseId");
  await page.getByRole("button",{name:"Desactivar regla",exact:true}).click();
  await expect.poll(()=>writes.length).toBe(3);
  expect(writes[2].body).toMatchObject({assignmentId:rules[0].assignmentId,isActive:false,rowVersion:"AAAAAAAAAAQ="});
  expect(writes[2].body).not.toHaveProperty("costCenterCode");
  await expect(page.getByRole("button",{name:"Reactivar regla",exact:true})).toBeVisible();
  expect(reads.filter(url=>url.pathname.endsWith("/cost-center-assignments"))).toHaveLength(1);
  expect(reads.filter(url=>url.pathname.endsWith("/warehouses"))).toHaveLength(0);
  await page.getByPlaceholder("Buscar centro por código o nombre",{exact:true}).fill("VENTAS");
  await expect.poll(()=>reads.some(url=>url.searchParams.get("search")==="VENTAS")).toBe(true);
  await expect(page.getByRole("row").filter({hasText:"Administración central"})).toHaveCount(0);
  await expect(page.getByRole("row").filter({hasText:"Comercial"})).toHaveCount(1);
  await page.getByLabel("Código del centro",{exact:true}).fill("PROYECTO");
  await page.getByLabel("Nombre del centro",{exact:true}).fill("Centro compartido nuevo");
  await page.getByRole("button",{name:"Crear centro",exact:true}).click();
  await expect.poll(()=>writes.length).toBe(4);
  expect(writes[3].body).toMatchObject({tenantId,code:"PROYECTO",name:"Centro compartido nuevo"});
  expect(writes[3].body).not.toHaveProperty("businessId");
  expect(writes[3].body).not.toHaveProperty("warehouseId");
  const beforeDirectLink=reads.length;
  await page.goto("/dashboard/accounting?section=centers");
  await expect(page.getByText("Centros de toda la empresa",{exact:true})).toBeVisible();
  await expect(page.getByRole("row").filter({hasText:"Administración central"})).toHaveCount(1);
  expect(reads.slice(beforeDirectLink).filter(url=>["/accounts","/periods","/mappings","/readiness","/category-definitions"].some(suffix=>url.pathname.endsWith(suffix)))).toHaveLength(0);
  await expect(page.getByText("Iniciando Auraly",{exact:true})).toHaveCount(0);
  await page.screenshot({path:"test-results/accounting-cost-centers.png",fullPage:true});
  const beforeContextSwitch=reads.filter(url=>url.pathname.endsWith("/cost-center-assignments")).length;
  await page.getByRole("button",{name:"Editar centro",exact:true}).first().click();
  await page.getByLabel("Nombre del centro",{exact:true}).fill("Cambio sin guardar");
  await page.getByRole("button",{name:"Editar regla",exact:true}).click();
  await page.getByRole("banner").getByRole("combobox").first().click();
  await page.getByRole("button",{name:"Otra sede Espacio de trabajo",exact:true}).click();
  await expect(page.getByLabel("Nombre del centro",{exact:true})).toHaveValue("");
  await expect(page.getByRole("combobox",{name:"Centro de la regla",exact:true})).toContainText("Seleccionar centro");
  await expect(page.getByText("Sin reglas específicas. Se usa el predeterminado de la empresa.",{exact:true})).toBeVisible();
  await expect(page.getByRole("row").filter({hasText:"Administración central"})).toHaveCount(1);
  expect(writes).toHaveLength(4);
  expect(reads.filter(url=>url.pathname.endsWith("/cost-center-assignments"))).toHaveLength(beforeContextSwitch+1);
});
