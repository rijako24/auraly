import { expect, test } from "@playwright/test";
import type { FinancialConcept, SaveFinancialConcept } from "../src/services/api/financial-concepts";

for(const width of [1366,390])test(`conceptos financieros crea, edita y cancela en modal ${width}`,async({page,baseURL},info)=>{
  await page.setViewportSize({width,height:844});
  const tenantId="11111111-1111-1111-1111-111111111111",businessId="22222222-2222-2222-2222-222222222222";
  const user={userId:"33333333-3333-3333-3333-333333333333",tenantId,tenantKey:"TEST",username:"e2e",firstName:"Ana",lastName:"Prueba",roles:["Administrator"],permissions:["catalog.read","catalog.update","dashboard.read","accounting.read"]};
  await page.context().addCookies([{name:"auth_token",value:"e2e",url:baseURL!,httpOnly:true}]);
  await page.addInitScript(({tenantId,businessId,user})=>{
    localStorage.setItem("selected_tenant_id",tenantId);localStorage.setItem("selected_business_id",businessId);
    localStorage.setItem("auraly:pwa-install-dismissed","1");
    localStorage.setItem("auth-state",JSON.stringify({state:{isAuthenticated:true,user},version:0}));
  },{tenantId,businessId,user});
  const account={accountId:"44444444-4444-4444-4444-444444444444",code:"280505",name:"Anticipos recibidos",allowsPosting:true,isActive:true};
  const side={id:"55555555-5555-5555-5555-555555555555",code:"Credit",label:"Crédito"};
  let items:FinancialConcept[]=[],reads=0,sides=0,writes=0;
  const unrelated:string[]=[];
  await page.route("**/api/**",async route=>{
    const path=new URL(route.request().url()).pathname;let body:unknown=[];
    if(/\/accounting\/(accounts|readiness|periods|mappings|category-definitions|opening-balance)$/.test(path))unrelated.push(path);
    if(path.endsWith("/auth/me"))body=user;
    else if(path.endsWith("/execution-context/tenants"))body=[{tenantId,name:"Empresa prueba"}];
    else if(path.endsWith("/execution-context/businesses"))body=[{tenantId,businessId,name:"Sede",timeZone:"America/Bogota"}];
    else if(path.endsWith("/execution-context/access"))body={tenantId,businessId,roles:user.roles,permissions:user.permissions};
    else if(path.endsWith("/reference-options/accounting-balance-side")){sides++;body=[side];}
    else if(path.endsWith("/account-options"))body={items:[account],page:1,pageSize:20,totalCount:1,totalPages:1};
    else if(path.includes("/financial-concepts")){
      if(route.request().method()==="PUT"){
        writes++;const draft=route.request().postDataJSON() as SaveFinancialConcept;
        items=[{...draft,accountCode:account.code,accountName:account.name,financialFunction:"CustomerAdvance",sideCode:side.code,sideLabel:side.label,rowVersion:`VERSION-${writes}`}];
        body={concept:items[0],page:{items,page:1,pageSize:50,totalCount:1}};
      }else{reads++;body={items,page:1,pageSize:50,totalCount:items.length};}
    }
    await route.fulfill({json:body});
  });
  await page.goto("/dashboard/accounting?section=concepts");
  await expect(page.getByRole("heading",{name:"Conceptos contables",exact:true})).toBeVisible();
  await expect(page.getByText("No hay conceptos financieros para este filtro.")).toBeVisible();
  expect(sides).toBe(0);
  await page.getByRole("button",{name:"Nuevo concepto",exact:true}).click();
  const dialog=page.getByRole("dialog",{name:/^(Nuevo|Editar) concepto financiero$/});
  await expect(dialog).toBeVisible();
  await dialog.getByLabel("Código",{exact:true}).fill("ANTICIPO");
  await dialog.getByLabel("Nombre",{exact:true}).fill("Anticipo recibido del cliente");
  await dialog.getByRole("combobox",{name:"Seleccionar cuenta contable"}).click();
  await page.getByRole("option",{name:"280505 · Anticipos recibidos",exact:true}).click();
  await dialog.getByRole("combobox",{name:"Naturaleza",exact:true}).click();
  await page.getByRole("option",{name:"Crédito",exact:true}).click();
  await dialog.getByRole("checkbox",{name:"Recibos de caja",exact:true}).check();
  const save=dialog.getByRole("button",{name:"Guardar concepto",exact:true});
  await expect(save).toBeInViewport();
  const bounds=await dialog.boundingBox();expect(bounds!.y).toBeGreaterThanOrEqual(0);expect(bounds!.y+bounds!.height).toBeLessThanOrEqual(844);
  expect(await dialog.evaluate(element=>element.scrollWidth<=element.clientWidth)).toBe(true);
  await page.screenshot({path:info.outputPath(`financial-concept-modal-${width}.png`)});
  await save.click();await expect(dialog).toBeHidden();
  await expect(page.getByRole("cell",{name:"Anticipo recibido del cliente",exact:true})).toBeVisible();
  expect(reads).toBe(1);expect(writes).toBe(1);expect(unrelated).toEqual([]);
  await page.getByRole("button",{name:"Editar",exact:true}).click();
  await expect(dialog.getByRole("heading",{name:"Editar concepto financiero",exact:true})).toBeVisible();
  await expect(dialog.getByLabel("Código",{exact:true})).toBeDisabled();
  await dialog.getByLabel("Nombre",{exact:true}).fill("No guardar");
  await dialog.getByRole("button",{name:"Cancelar",exact:true}).click();
  await expect(dialog).toBeHidden();expect(reads).toBe(1);expect(writes).toBe(1);expect(unrelated).toEqual([]);
});
