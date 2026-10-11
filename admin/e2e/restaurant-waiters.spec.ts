import { expect, test, type Locator, type Page, type Route } from "@playwright/test";

const tenantId="11111111-1111-1111-1111-111111111111";
const businessId="22222222-2222-2222-2222-222222222222";
const userId="33333333-3333-3333-3333-333333333333";
const partyId="44444444-4444-4444-4444-444444444444";
const waiterId="55555555-5555-5555-5555-555555555555";
const countryId="66666666-6666-6666-6666-666666666666";
const divisionId="77777777-7777-7777-7777-777777777777";
const cityId="88888888-8888-8888-8888-888888888888";
const permissions=["dashboard.read","parties.read","parties.update","parties.waiters.create","parties.waiters.configure","users.create"];
test.use({serviceWorkers:"block"});

const json=(route:Route,body:unknown,status=200)=>route.fulfill({status,contentType:"application/json",body:JSON.stringify(body)});
const field=(scope:Locator,label:string)=>scope.getByText(label,{exact:true}).locator("..");

async function setup(page:Page){
  await page.context().addCookies([{name:"auth_token",value:"waiter-browser-fixture",url:"http://127.0.0.1:3000",httpOnly:true,sameSite:"Lax"}]);
  await page.addInitScript(({tenantId,businessId,userId,permissions})=>{
    localStorage.setItem("selected_tenant_id",tenantId);localStorage.setItem("selected_business_id",businessId);
    localStorage.setItem("theme","light");
    localStorage.setItem("auth-state",JSON.stringify({state:{isAuthenticated:true,user:{userId,tenantId,tenantKey:"E2E",username:"waiter-test",email:"waiter@test.local",firstName:"Prueba",lastName:"Meseros",roles:["Administrador"],permissions}},version:0}));
  },{tenantId,businessId,userId,permissions});
  const user={userId,tenantId,tenantKey:"E2E",username:"waiter-test",email:"waiter@test.local",firstName:"Prueba",lastName:"Meseros",roles:["Administrador"],permissions};
  let created=false;
  let waiter={waiterId,code:"MES-001",shortName:"Lu",isActive:true};
  let rowVersion="AAAAAAAAAAA=";
  let listReads=0,detailReads=0,catalogReads=0,userWrites=0;
  const creates:Record<string,unknown>[]=[];
  const updates:Record<string,unknown>[]=[];
  const common=()=>({partyId,partyType:"NaturalPerson",identificationTypeCode:"CC",identification:"100000001",verificationDigit:null,displayName:"Lucía Sala",legalName:null,firstName:"Lucía",lastName:"Sala",email:null,phone:null,roles:["Waiter"],rowVersion,waiter});
  const item=()=>({...common(),primarySiteName:"Principal",cityName:"Ciudad",isActive:true,completionStatus:"Complete",customerId:null,supplierId:null,sellerId:null,carrierId:null,employeeId:null,userId:null,supplierPurchaseEvidencePolicy:null,supplierDefaultPaymentDueDays:null});
  const detail=()=>({...common(),identificationCountryId:countryId,primarySite:null,sites:[],customer:null,supplier:null,seller:null,carrier:null,employee:null,user:null});
  await page.route("**/api/**",route=>{
    const path=new URL(route.request().url()).pathname;
    const method=route.request().method();
    if(path.endsWith("/auth/me"))return json(route,user);
    if(path.endsWith("/execution-context/tenants"))return json(route,[{tenantId,name:"Empresa de prueba"}]);
    if(path.endsWith("/execution-context/businesses"))return json(route,[{tenantId,businessId,name:"Restaurante prueba"}]);
    if(path.endsWith("/execution-context/access"))return json(route,{tenantId,businessId,roles:user.roles,permissions});
    if(path.endsWith(`/businesses/${businessId}`))return json(route,{businessId,tenantId,name:"Restaurante prueba",pointOfSaleMode:"RestaurantBar"});
    if(path.endsWith("/reference-options/party-role")){
      catalogReads++;
      return json(route,[{id:"waiter-role",code:"Waiter",label:"Atención de sala",description:"Meseros del salón",sortOrder:10},{id:"user-role",code:"User",label:"Usuario",description:"Acceso manual",sortOrder:20}]);
    }
    if(path.endsWith("/reference-options/tenant-identification-type"))return json(route,[{id:"cc",code:"CC",label:"Cédula de ciudadanía",description:"NaturalPerson",sortOrder:10}]);
    if(path.endsWith("/masters/geography/countries"))return json(route,[{countryId,code:"CO",name:"Colombia",isActive:true}]);
    if(path.endsWith(`/countries/${countryId}/divisions`))return json(route,[{administrativeDivisionId:divisionId,countryId,code:"TEST",name:"Departamento",divisionType:"Department",isActive:true}]);
    if(path.endsWith(`/divisions/${divisionId}/cities`))return json(route,[{cityId,administrativeDivisionId:divisionId,code:"CITY",name:"Ciudad",isActive:true}]);
    if(path.endsWith("/parties/identity"))return json(route,{exists:false,hasRequestedRole:false,party:null});
    if(path.endsWith("/commerce/v1/waiters")&&method==="POST"){
      creates.push(route.request().postDataJSON());
      if(creates.length===1)return json(route,{message:"Fallo temporal de prueba"},503);
      created=true;
      return json(route,{roleId:waiterId,partyId,role:"Waiter",idempotentReplay:false},201);
    }
    if(path.endsWith("/commerce/v1/parties")){
      listReads++;
      return json(route,{items:created?[item()]:[],page:1,pageSize:25,totalCount:created?1:0,totalPages:created?1:0});
    }
    if(path.endsWith(`/commerce/v1/parties/${partyId}`)){
      if(method==="PUT"){
        const body=route.request().postDataJSON();updates.push(body);
        waiter={...waiter,...body.waiter};rowVersion="AAAAAAAAAAE=";
        return json(route,item());
      }
      detailReads++;return json(route,detail());
    }
    if(method==="POST"&&path.endsWith("/users"))userWrites++;
    return json(route,[]);
  });
  return {creates,updates,counts:()=>({listReads,detailReads,catalogReads,userWrites})};
}

test("crea mesero sin usuario, conserva el reintento y edita su ficha sin habilitación por sede ni refetch",async({page})=>{
  const state=await setup(page);
  await page.goto("/dashboard/parties");
  await page.getByRole("button",{name:"Nuevo tercero"}).click();
  let dialog=page.getByRole("dialog");
  await dialog.getByRole("button",{name:/^Atención de sala/}).click();
  await field(dialog,"Identificación").locator("input").fill("100000001");
  await field(dialog,"Nombre visible").locator("input").fill("Lucía Sala");
  await field(dialog,"Nombres").locator("input").fill("Lucía");
  await field(dialog,"Apellidos").locator("input").fill("Sala");
  await field(dialog,"Departamento").getByRole("combobox").click();
  await page.getByRole("option",{name:"Departamento",exact:true}).click();
  await field(dialog,"Ciudad").getByRole("combobox").click();
  await page.getByRole("option",{name:"Ciudad",exact:true}).click();
  await field(dialog,"Dirección").locator("input").fill("Calle 1");
  await field(dialog,"Nombre corto (opcional)").locator("input").fill("Lu");
  await dialog.getByRole("button",{name:"Guardar tercero"}).click();
  await expect.poll(()=>state.creates.length).toBe(1);
  await expect(dialog.getByRole("button",{name:"Guardar tercero"})).toBeEnabled();
  await dialog.getByRole("button",{name:"Guardar tercero"}).click();
  await expect(dialog).toBeHidden();
  expect(state.creates).toHaveLength(2);
  expect(state.creates[0].operationId).toEqual(state.creates[1].operationId);
  expect(state.counts().userWrites).toBe(0);
  expect(state.counts().catalogReads).toBe(1);
  const row=page.getByRole("row").filter({hasText:"Lucía Sala"});
  await expect(row).toBeVisible();
  await row.getByRole("button",{name:"Editar"}).click();
  dialog=page.getByRole("dialog");
  await dialog.getByRole("tab",{name:/Atención de sala/}).click();
  await expect(dialog.getByText("Sin usuario vinculado",{exact:true})).toBeVisible();
  await expect(dialog.getByText("Puede atender en esta sede",{exact:true})).toHaveCount(0);
  await dialog.getByLabel("Nombre corto (opcional)").fill("Lucía");
  await dialog.getByRole("button",{name:"Guardar tercero"}).click();
  await expect(dialog.getByRole("button",{name:"Editar información"})).toBeVisible();
  expect(state.updates).toHaveLength(1);
  expect(state.updates[0].waiter).toEqual({shortName:"Lucía"});
  expect(state.counts().detailReads).toBe(1);
  expect(state.counts().catalogReads).toBe(1);
  await expect(dialog.getByText("Lucía",{exact:true})).toBeVisible();
  await page.screenshot({path:"../tmp/restaurant-waiter-light.png",fullPage:true});
  await page.evaluate(()=>document.documentElement.classList.add("dark"));
  await page.screenshot({path:"../tmp/restaurant-waiter-dark.png",fullPage:true});
  await page.setViewportSize({width:390,height:844});
  await page.screenshot({path:"../tmp/restaurant-waiter-mobile.png",fullPage:true});
});
