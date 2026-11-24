import {expect,test} from "@playwright/test";

test.use({serviceWorkers:"block"});
for(const viewport of [{width:1366,height:768},{width:390,height:844}]) {
 test(`centro de costo: automático, catálogo paginado y selección recordada ${viewport.width}`,async({page,baseURL})=>{
  await page.setViewportSize(viewport);
  const tenantId="11111111-1111-1111-1111-111111111111";
  const businessId="22222222-2222-2222-2222-222222222222";
  const warehouseId="33333333-3333-3333-3333-333333333333";
  const centerId="55555555-5555-5555-5555-555555555555";
  const name="Caja principal de facturación con nombre suficientemente largo para móvil";
  const user={userId:"44444444-4444-4444-4444-444444444444",tenantId,tenantKey:"TEST",username:"cashier",firstName:"Caja",lastName:"Prueba",roles:["Cajero"],permissions:["sales.create","pos.workspace.change"]};
  const workspace={businessId,warehouseId,businessName:"Sede de prueba",warehouseName:"Bodega principal",warehouseCode:"VEN",warehouseAllowsNegativeStockSales:true,hasActiveEdgeEnrollment:false,pointOfSaleMode:"DirectSale"};
  let centerQueries=0,draftReads=0;
  const selections:Array<{costCenterId:string|null}>=[];
  await page.context().addCookies([{name:"auth_token",value:"e2e",url:baseURL!,httpOnly:true,sameSite:"Lax"}]);
  await page.addInitScript(({tenantId,businessId,user})=>{
   localStorage.setItem("selected_tenant_id",tenantId);localStorage.setItem("selected_business_id",businessId);
   localStorage.setItem("auth-state",JSON.stringify({state:{isAuthenticated:true,user},version:0}));
  },{tenantId,businessId,user});
  await page.route("**/api/**",async route=>{
   const url=new URL(route.request().url()),path=url.pathname;
   let body:unknown=[];
   if(path.endsWith("/auth/me"))body=user;
   else if(path.endsWith("/workspace/bootstrap"))body={tenantId,tenantName:"Prueba",userId:user.userId,userDisplayName:"Caja Prueba",options:[workspace],canEnrollPosDevice:false,activeEnrolledDeviceCount:0,maximumEnrolledDevices:0};
   else if(path.endsWith("/workspace/options"))body=[workspace];
   else if(path.endsWith("/workspace/select")||path.endsWith("/workspace/change")){
    const request=route.request().postDataJSON();selections.push(request);
    body={...workspace,costCenterId:request.costCenterId,costCenterName:request.costCenterId?`BOX · ${name}`:null};
   }else if(path.endsWith("/accounting/cost-centers")){
    centerQueries++;expect(url.searchParams.get("page")).toBe("1");expect(url.searchParams.get("pageSize")).toBe("10");expect(url.searchParams.get("isActive")).toBe("true");
    body={items:[{costCenterId:centerId,tenantId,code:"BOX",name,isActive:true,isDefault:false}],page:1,pageSize:10,totalCount:1,totalPages:1};
   }else if(path.endsWith("/work-sessions/current"))body={workSessionId:"session"};
   else if(path.endsWith("/drafts/active")){draftReads++;body={draftId:"draft",...workspace,userId:user.userId,workSessionId:"session",status:"Active",version:1,lines:[],untaxedAmount:0,taxAmount:0,payableAmount:0};}
   else if(path.includes("/orders"))body={items:[],totalCount:0,totalPages:1,page:1,pageSize:25};
   await route.fulfill({status:200,contentType:"application/json",body:JSON.stringify(body)});
  });
  await page.goto("/pos?workspace=change");
  const picker=page.getByRole("combobox",{name:"Centro de costo de facturación",exact:true});
  await expect(picker).toHaveText("Automático");expect(centerQueries).toBe(0);
  await picker.click();
  const option=page.getByRole("option",{name:`BOX · ${name}`,exact:true});
  await expect(option).toBeVisible();expect(centerQueries).toBe(1);
  await page.screenshot({path:test.info().outputPath("cost-center-options.png"),fullPage:true,animations:"disabled"});
  await option.click();await expect(picker).toContainText(name);
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
  if(viewport.width>=1000) expect(await page.evaluate(()=>document.documentElement.scrollHeight)).toBeLessThanOrEqual(viewport.height+1);
  await page.screenshot({path:test.info().outputPath("cost-center-selected.png"),fullPage:true,animations:"disabled"});
  await page.getByRole("button",{name:"Continuar a ventas",exact:true}).click();
  await expect(page.locator("#pos-scanner")).toBeEnabled();
  expect(selections.at(-1)?.costCenterId).toBe(centerId);expect(draftReads).toBe(1);expect(centerQueries).toBe(1);
  const saved=await page.evaluate(({tenantId,userId,businessId,warehouseId})=>JSON.parse(localStorage.getItem(`auraly.pos.sales-workspace:${tenantId}:${userId}:cost-center:${businessId}:${warehouseId}`)!),{tenantId,userId:user.userId,businessId,warehouseId});
  expect(saved.costCenterId).toBe(centerId);
  await page.goto("/pos?workspace=change");await expect(picker).toContainText(name);
  await picker.click();await page.getByRole("option",{name:"Automático",exact:true}).click();
  await page.getByRole("button",{name:"Continuar a ventas",exact:true}).click();
  await expect(page.locator("#pos-scanner")).toBeEnabled();expect(selections.at(-1)?.costCenterId).toBeNull();
 });
}
