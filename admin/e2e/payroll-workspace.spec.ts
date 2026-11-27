import { expect, test, type Page } from "@playwright/test";
import type { PayrollOptions, PayrollRun, PayrollProfile, SaveConcept, SaveEmployment, PayrollEmployment, PayrollParty, PayrollRuleSet, PayrollNovelty, PayrollPaymentBatch, ElectronicPayrollPeriod, PayrollDeductionAgreement } from "../src/services/api/payroll";

const tenantId = "11111111-1111-1111-1111-111111111111";
const businessId = "22222222-2222-2222-2222-222222222222";
const secondBusinessId = "33333333-3333-3333-3333-333333333333";
const permissions = ["dashboard.read", "payroll.read", "payroll.manage", "payroll.calculate", "payroll.approve", "payroll.pay", "payroll.fiscal", "payroll.configure", "accounting.read", "accounting.configure"];
const version = "AAAAAAAAAAE=";

type PayrollFixtureOptions=PayrollOptions&{employments:PayrollEmployment[];parties:PayrollParty[];ruleSets:PayrollRuleSet[];novelties:PayrollNovelty[];paymentBatches:PayrollPaymentBatch[];electronicPeriods:ElectronicPayrollPeriod[];deductionAgreements:PayrollDeductionAgreement[]};

async function selectNoveltyLiquidation(page:Page,period:RegExp) {
  await page.getByRole("combobox",{name:"Buscar liquidación",exact:true}).click();
  await page.getByRole("option").filter({hasText:period}).first().click();
}

function fixture(): PayrollFixtureOptions {
  const catalogs: PayrollOptions["catalogs"] = {};
  for (const [catalogCode, code, label] of [
    ["payroll-run-kind", "Regular", "Liquidación regular"],
    ["payroll-run-kind", "Adjustment", "Ajuste por diferencias"],
    ["payroll-workflow-status", "Draft", "Borrador"],
    ["payroll-workflow-status", "Calculated", "Calculada"],
    ["payroll-workflow-status", "Approved", "Aprobada"],
    ["payroll-workflow-status", "Queued", "En procesamiento fiscal"],
    ["payroll-workflow-status", "Generated", "Generado"],
    ["payroll-pay-frequency", "Monthly", "Mensual"],
    ["payroll-contract-type", "Indefinite", "Término indefinido"],
    ["payroll-salary-type", "Ordinary", "Salario ordinario"],
    ["payroll-risk-class", "I", "Clase I"],
    ["payroll-worker-type", "01", "Dependiente"],
    ["payroll-worker-subtype", "00", "No aplica subtipo"],
    ["payroll-worker-subtype", "01", "Dependiente pensionado por vejez activo"],
    ["payroll-payment-method", "Cash", "Efectivo"],
    ["payroll-concept-nature", "Deduction", "Deducción"],
    ["payroll-calculation-method", "FixedAmount", "Valor fijo"],
    ["payroll-concept-treatment", "AuthorizedDeduction", "Deducción autorizada"],
    ["payroll-accounting-category", "EmployeeLoansReceivable", "Préstamos a empleados"],
    ["payroll-deduction-authority", "WrittenAuthorization", "Autorización escrita"],
    ["payroll-novelty-type", "Amount", "Valor"],
    ["payroll-novelty-type", "Hours", "Horas"],
    ["payroll-rule-parameter", "MonthlyDays", "Días del mes"],
  ]) {
    (catalogs[catalogCode] ??= []).push({ optionId: `${catalogCode}-${code}`, catalogCode, code, label,
      description: null, metadataCode: null, dianCode: null, isActive: true, sortOrder: 10 });
  }
  return {
    catalogs, parties: [], employments: [],
    concepts: [{ conceptId: "loan", code: "PRESTAMO", name: "Préstamo empleado", natureCode: "Deduction",
      calculationMethodCode: "FixedAmount", treatmentCode: "AuthorizedDeduction", dianConceptCode: null,
      accountingCategoryCode: "EmployeeLoansReceivable", systemRoleCode: null, isSalaryBase: false,
      isSocialSecurityBase: false, isBenefitsBase: false, isTaxWithholdingBase: false,
      requiresDeductionAgreement: true, effectiveFrom: "2026-01-01", effectiveTo: null, isActive: true, rowVersion: version }],
    ruleSets: [{ isGlobal:false, ruleSetId: "rules", countryCode: "CO", code: "CO-2026", name: "Reglas vigentes", effectiveFrom: "2026-01-01",
      effectiveTo: null, sourceReference: "Fuente de prueba", status: "Approved", parameters: [{ code: "MonthlyDays", numericValue: 30, unitCode: "Days", description: null }], rowVersion: version }],
    settings: { isEmployerExemptFromHealthSenaIcbf: false, electronicPayrollEnabled: true, rowVersion: version },
    electronicConfiguration: null, fiscalIssuers: [], deductionAgreements: [], novelties: [], paymentBatches: [], electronicPeriods: [],
  };
}

async function setup(page: Page, baseURL: string, granted = permissions) {
  const options = fixture();
  const runs: PayrollRun[] = [];
  const profiles:PayrollProfile[]=[];
  const employees: PayrollEmployment[] = [];
  const writes: Array<{ path: string; body: Record<string, unknown> }> = [];
  const reads: Array<{ path: string; business: string | undefined }> = [];
  const failures = { options: false, detail: false, calculation:false };
  const controls={noveltyResponse:null as Promise<void>|null};
  const user = { userId: "44444444-4444-4444-4444-444444444444", tenantId, tenantKey: "TEST", username: "test", firstName: "Ana", lastName: "Prueba", roles: ["ACCOUNTANT"], permissions: granted };
  await page.context().addCookies([{ name: "auth_token", value: "e2e", url: baseURL, httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ tenantId, businessId, user }) => {
    // API-fixture journeys do not install a worker; preserve register's browser contract.
    Object.defineProperty(navigator.serviceWorker,"register",{value:async()=>({update:async()=>{}})});
    localStorage.setItem("auraly:pwa-install-dismissed","1");
    localStorage.setItem("selected_tenant_id", tenantId);
    localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
  }, { tenantId, businessId, user });
  await page.route("**/api/**", async route => {
    const request = route.request(), path = new URL(request.url()).pathname;
    const business = request.headers()["x-business-id"];
    let body: unknown = [];
    if (path.endsWith("/payroll/options") && failures.options || /\/payroll\/runs\/[^/]+$/.test(path) && failures.detail) {
      await route.fulfill({ status: 500, contentType: "application/json", body: JSON.stringify({ message: "Consulta no disponible" }) }); return;
    }
    if (request.method() !== "GET") {
      const data = request.postDataJSON(); writes.push({ path, body: data });
      if (path.endsWith("/payroll/runs")) {
        const run: PayrollRun = { ...data, status: "Draft", employeeCount: 0, totalEarnings: 0, totalDeductions: 0, netPayable: 0,
          rowVersion: version, calculationVersion: 0, totalEmployerContributions: 0, totalProvisions: 0, employees: [], payableEmployees: [] };
        runs.push(run); body = run;
      } else if (path.endsWith("/calculate") && data.recalculateEmploymentIds) {
        if(failures.calculation){failures.calculation=false;await route.fulfill({status:503,json:{detail:"Recálculo temporalmente no disponible"}});return;}
        const novelty=options.novelties[0];
        const amount=novelty.status==="Voided"?0:novelty.totalAmount;
        const updated={...runs[0].employees[0],novelties:[novelty],payrollRunEmployeeId:crypto.randomUUID(),earnings:1000000,deductions:130000,netPayable:870000,
          lines:amount?[{lineNumber:1,conceptId:"loan",conceptCode:"PRESTAMO",conceptName:"Préstamo empleado",natureCode:"Deduction",dianConceptCode:null,
            notes:novelty.notes,evidenceUrl:novelty.evidenceUrl,costCenterId:novelty.costCenterId,costCenterName:novelty.costCenterId?"Centro de prueba":null,
            accountingCategoryCode:"EmployeeLoansReceivable",quantity:0,rate:null,baseAmount:null,amount,isEmployerCost:false,isSalaryBase:false,
            noveltyId:novelty.noveltyId}]:[]};
        Object.assign(runs[0],{employees:[updated],rowVersion:"AAAAAAAAAAI=",totalDeductions:130000,netPayable:870000,calculationVersion:2});
        body={...runs[0],isPartialCalculation:true};
      } else if (path.endsWith("/calculate")) {
        if(!employees.length){await route.fulfill({status:400,json:{detail:"No hay contratos elegibles para el cálculo."}});return;}
        const run=runs.find(item=>path.endsWith(`/${item.payrollRunId}/calculate`))!;
        Object.assign(run, { status: "Calculated", employeeCount: 1, totalEarnings: 1000000, totalDeductions: 80000, netPayable: 920000,
          employees: [{ payrollRunEmployeeId: "run-employee", employmentId: employees[0].employmentId, partyId: employees[0].partyId,
            employeeName: "Elena Prueba", workedDays: 30, baseWorkedDays:30, earnings: 1000000, deductions: 80000, employerContributions: 0, provisions: 0, netPayable: 920000, lines: [] }] }); body = run; }
      else if (path.endsWith("/approve")) { if (path.includes("/rule-sets/")) {options.ruleSets.at(-1)!.status = "Approved"; body=options.ruleSets.at(-1)!;} else { runs[0].status = "Approved"; runs[0].payableEmployees = [{ payrollRunEmployeeId: "run-employee",
        paymentMethodOptionId: "payroll-payment-method-Cash", paymentMethodName: "Efectivo", amount: 920000, bankDestinationComplete: true, employeeName: "Elena Prueba" }]; } }
      else if (path.endsWith("/payroll/payments")) { body = { ...data, status: "Confirmed", employeeCount: 1, totalAmount: 920000, paymentMethodName: "Caja nómina", accountingStatus: "Pending", isPaid: false, remainingEmployees: [], rowVersion: version }; runs[0].payableEmployees = []; options.paymentBatches.push(body as PayrollPaymentBatch); }
      else if (path.includes("/employments/")) { body = { ...data as SaveEmployment, employeeName: "Elena Prueba", rowVersion: version }; const old = employees.findIndex(item => item.employmentId === data.employmentId); if (old >= 0) employees[old] = body as typeof employees[number]; else employees.push(body as typeof employees[number]); }
      else if (path.includes("/deduction-agreements/")) { body = { ...data, employeeName: "Elena Prueba", conceptName: "Préstamo empleado", authorityName: "Autorización escrita", deductedToDate: 0, rowVersion: version }; const old = options.deductionAgreements.findIndex(item => item.deductionAgreementId === data.deductionAgreementId); if (old >= 0) options.deductionAgreements[old] = body as typeof options.deductionAgreements[number]; else options.deductionAgreements.push(body as typeof options.deductionAgreements[number]); }
      else if (path.endsWith("/novelties")) {
        const novelty={...data,employeeName:"Elena Prueba",conceptName:"Préstamo empleado",noveltyTypeName:"Valor",status:"Approved",rowVersion:version};
        options.novelties.push(novelty);body=novelty;
      } else if(path.includes("/novelties/")) {
        const id=path.split("/novelties/")[1].split("/")[0];
        const index=options.novelties.findIndex(item=>item.noveltyId===id);
        const previous=options.novelties[index];
        const novelty={...previous,...(path.endsWith("/void")?{status:"Voided"}:data),rowVersion:"AAAAAAAAAAI="};
        options.novelties[index]=novelty;body=novelty;
      }
      else if (path.includes("/concepts/")) {
        const form=data as SaveConcept;
        const concept={...options.concepts.find(item=>item.conceptId===form.conceptId)??options.concepts[0],...form,rowVersion:version};
        options.concepts=[...options.concepts.filter(item=>item.conceptId!==form.conceptId),concept];body=concept;
      }
      else if (path.includes("/rule-sets/")) { body = { ...data, isGlobal:false, status: "Draft", rowVersion: version }; options.ruleSets.push(body as typeof options.ruleSets[number]); }
      else if (path.endsWith("/electronic-periods")) { body = { ...data, status: "Generated", documents: [{ electronicPayrollDocumentId: "electronic", partyId: "party", employeeName: "Elena Prueba", documentKind: "Regular", fiscalDocumentId: "fiscal", status: "Queued", sourceHashHex: "hash" }], rowVersion: version }; options.electronicPeriods.push(body as typeof options.electronicPeriods[number]); }
      else if (path.includes("/payroll/profiles/")) {
        const createdRuleSet=data.configuration?{...data.configuration,status:"Approved",isGlobal:false,rowVersion:version}:null;
        if(createdRuleSet)options.ruleSets.push(createdRuleSet);
        const profile={...data,code:data.code||String(data.profileId).replaceAll("-","").toUpperCase(),rowVersion:version,createdRuleSet};profiles.push(profile);body=profile;
      }
      else if (path.endsWith("/settings")) { options.settings = { ...data, rowVersion: version }; body = options.settings; }
      else if (path.endsWith("/electronic-configuration")) { options.electronicConfiguration = { ...data, rowVersion: version }; body = options.electronicConfiguration; }
    } else {
      reads.push({ path, business });
      if (path === "/api/auth/me") body = user;
      else if (path.endsWith("/tenants/branding/print")) body = {tenantId,displayName:"Empresa nómina",legalName:"Empresa nómina",nit:"900123456",verificationDigit:"7",logoUrl:null};
      else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Empresa prueba" }];
      else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Sede principal", timeZone: "America/Bogota" }, { tenantId, businessId: secondBusinessId, name: "Sede norte", timeZone: "Asia/Tokyo" }];
      else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId: business, roles: user.roles, permissions: granted };
      else if (path.endsWith("/payroll/options")) body = {catalogs:options.catalogs,concepts:options.concepts,settings:options.settings,electronicConfiguration:options.electronicConfiguration,fiscalIssuers:options.fiscalIssuers};
      else if (path.endsWith("/payroll/profiles")) body = {items:profiles,page:1,pageSize:20,totalCount:profiles.length,totalPages:1};
      else if (path.includes("/payroll/profiles/")&&path.endsWith("/rule-sets")) body={items:options.ruleSets,page:1,pageSize:20,totalCount:options.ruleSets.length};
      else if (path.includes("/payroll/profiles/")) body=profiles.find(item=>path.endsWith(item.profileId));
      else if (path.endsWith("/payroll/runs")) {
        const url=new URL(request.url()),pageNumber=Number(url.searchParams.get("page")??1),size=Number(url.searchParams.get("pageSize")??20);
        const items=runs.filter(run=>(!url.searchParams.get("status")||run.status===url.searchParams.get("status"))&&(!url.searchParams.get("kind")||run.runKind===url.searchParams.get("kind"))&&(!url.searchParams.get("paymentFrom")||run.paymentDate>=url.searchParams.get("paymentFrom")!)&&(!url.searchParams.get("paymentTo")||run.paymentDate<=url.searchParams.get("paymentTo")!)&&(!url.searchParams.get("workerSearch")||run.employees.some(worker=>worker.employeeName.toLowerCase().includes(url.searchParams.get("workerSearch")!.toLowerCase()))));
        body={items:items.slice((pageNumber-1)*size,pageNumber*size),page:pageNumber,pageSize:size,totalCount:items.length,totalPages:Math.ceil(items.length/size)};
      }
      else if(path.endsWith("/payroll/deduction-agreements")) {
        const url=new URL(request.url()),pageNumber=Number(url.searchParams.get("page")??1),size=Number(url.searchParams.get("pageSize")??20);
        const items=options.deductionAgreements.filter(item=>(!url.searchParams.get("employmentId")||item.employmentId===url.searchParams.get("employmentId"))&&(!url.searchParams.get("conceptId")||item.conceptId===url.searchParams.get("conceptId"))&&(!url.searchParams.has("active")||item.isActive===(url.searchParams.get("active")==="true")));
        body={items:items.slice((pageNumber-1)*size,pageNumber*size),page:pageNumber,pageSize:size,totalCount:items.length,totalPages:Math.ceil(items.length/size)};
      }
      else if(path.endsWith("/payroll/payments")) {
        const url=new URL(request.url()),pageNumber=Number(url.searchParams.get("page")??1),size=Number(url.searchParams.get("pageSize")??20);
        body={items:options.paymentBatches.slice((pageNumber-1)*size,pageNumber*size),page:pageNumber,pageSize:size,totalCount:options.paymentBatches.length,totalPages:Math.ceil(options.paymentBatches.length/size)};
      }
      else if(/\/payroll\/electronic-periods\/[^/]+\/documents$/.test(path)) {
        const url=new URL(request.url()),pageNumber=Number(url.searchParams.get("page")??1),size=Number(url.searchParams.get("pageSize")??20);
        const items=options.electronicPeriods.find(period=>path.includes(period.electronicPeriodId))?.documents??[];
        body={items:items.slice((pageNumber-1)*size,pageNumber*size),page:pageNumber,pageSize:size,totalCount:items.length,totalPages:Math.ceil(items.length/size)};
      }
      else if(path.endsWith("/payroll/electronic-periods")) {
        const url=new URL(request.url()),pageNumber=Number(url.searchParams.get("page")??1),size=Number(url.searchParams.get("pageSize")??20);
        body={items:options.electronicPeriods.slice((pageNumber-1)*size,pageNumber*size).map(period=>({...period,documentCount:period.documents.length,documents:[]})),page:pageNumber,pageSize:size,totalCount:options.electronicPeriods.length,totalPages:Math.ceil(options.electronicPeriods.length/size)};
      }
      else if(path.endsWith("/payroll/calculated-employees")) {
        const url=new URL(request.url()),pageNumber=Number(url.searchParams.get("page")??1),size=Number(url.searchParams.get("pageSize")??20),search=(url.searchParams.get("search")??"").toLowerCase();
        const items=runs.filter(run=>run.status!=="Draft"&&(!url.searchParams.get("runId")||run.payrollRunId===url.searchParams.get("runId"))).flatMap(run=>run.employees.map(employee=>({...employee,payrollRunId:run.payrollRunId,periodStart:run.periodStart,periodEnd:run.periodEnd,status:run.status,rowVersion:run.rowVersion}))).filter(employee=>employee.employeeName.toLowerCase().includes(search));
        body={items:items.slice((pageNumber-1)*size,pageNumber*size),totalCount:items.length,page:pageNumber,pageSize:size};
      }
      else if (/\/payroll\/runs\/[^/]+\/employments$/.test(path)) body = {
        items: employees.map(employee => ({ ...employee, identification: "123456", payFrequencyName: "Mensual", isSelected: true })),
        page: 1, pageSize: 50, totalCount: employees.length, eligibleCount: employees.length, selectedCount: employees.length, rowVersion: version };
      else if (path.includes("/payment-methods/")) body = { items: [{ paymentMethodId: "cash-form", code: "CAJA", name: "Caja nómina", methodCode: "Cash", accountId: "cash-account", accountCode: "110501", accountName: "Caja" }], page: 1, pageSize: 20, totalCount: 1 };
      else if (path.includes("/payroll/runs/")) {
        const run=runs.find(run => path.endsWith(run.payrollRunId));
        const worker=new URL(request.url()).searchParams.get("employmentId");
        body=run?{...run,isPartialCalculation:Boolean(worker),employees:run.employees.filter(employee=>!worker||employee.employmentId===worker).map(employee=>({...employee,novelties:options.novelties.filter(novelty=>novelty.employmentId===employee.employmentId && novelty.startDate<=run.periodEnd && novelty.endDate>=run.periodStart)}))}:null;
      }
      else if (path.endsWith("/payroll/employments")) body = { items: business === secondBusinessId ? [] : employees, page: 1, pageSize: 25, totalPages: 1, totalCount: business === secondBusinessId ? 0 : employees.length };
      else if (path.endsWith("/parties/role-options")) body = { items: [{ partyId: "party", employeeId: "employee", roleId: "employee", role: "Employee", name: "Elena Prueba", displayName: "Elena Prueba", identification: "123456" }], page: 1, pageSize: 10, totalPages: 1, totalCount: 1 };
      else if (path.endsWith("/subscription")) body = { dianDocumentMonthlyLimit: 10, dianDocumentsUsed: 0 };
      else if (path.endsWith("/reports/definitions")) body = [{ code: "payroll-summary", name: "Resumen de nómina", description: "Nóminas aprobadas", dataset: "Summary", columns: [{ key: "netPayable", label: "Neto", format: "currency", align: "right" }], sortOrder: 1 }];
      else if (path.endsWith("/reports/payroll-summary")) body = { rows: [{ netPayable: 920000 }] };
    }
    if(request.method()==="POST" && path.endsWith("/novelties") && controls.noveltyResponse)await controls.noveltyResponse;
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
  });
  return { options, runs, employees, writes, reads, failures, controls };
}

async function choose(page: Page, label: string, option: string) {
  await (label === "Empleado" ? page.getByRole("combobox", { name: "Seleccionar empleado" }) : label === "Trabajador" ? page.locator("label").filter({ has: page.locator("span", { hasText: /^Trabajador$/ }) }).getByRole("combobox") : page.getByLabel(label, { exact: true })).click();
  await page.getByRole("option", { name: option, exact: label !== "Empleado" && label !== "Trabajador" && label !== "Acuerdo de descuento (si aplica)" }).click();
}

for(const width of [1366,390])test(`contrato conserva el scroll al abrir y cerrar combos inferiores a ${width}`,async({page,baseURL},info)=>{
  await page.setViewportSize({width,height:width===390?844:768});
  await setup(page,baseURL!);
  await page.route("**/api/commerce/v1/payroll/profiles?**",route=>route.fulfill({json:{items:Array.from({length:10},(_,index)=>({profileId:`profile-${index}`,code:`P${index}`,name:`Perfil de prueba ${index}`,isActive:true,mappings:[]})),page:1,pageSize:10,totalCount:10,totalPages:1}}));
  await page.route("**/api/commerce/v1/accounting/cost-centers?**",route=>route.fulfill({json:{items:[],page:1,pageSize:10,totalCount:0,totalPages:0}}));
  const errors:string[]=[];page.on("pageerror",error=>{errors.push(error.message);console.error(error.stack);});
  await page.goto("/dashboard/payroll");
  await page.getByRole("tab",{name:"Trabajadores",exact:true}).click();
  await page.getByRole("button",{name:"Nuevo contrato",exact:true}).click();
  const dialog=page.getByRole("dialog",{name:"Nueva relación laboral",exact:true});
  const scroller=await dialog.locator("form").evaluateHandle(element=>{
    for(let parent=element.parentElement;parent;parent=parent.parentElement)
      if(["auto","scroll"].includes(getComputedStyle(parent).overflowY)&&parent.scrollHeight>parent.clientHeight)return parent;
    throw new Error("El contrato necesita un contenedor de desplazamiento");
  });
  const observations=[];
  for(const name of ["Medio de pago","Caja de compensación","Perfil de nómina","EPS","Centro de costo contractual"]){
    const trigger=dialog.getByRole("combobox",{name,exact:true});
    await trigger.scrollIntoViewIfNeeded();
    const before=await scroller!.evaluate(element=>element.scrollTop);
    await trigger.click();
    const list=page.getByRole("listbox").last();await expect(list).toBeVisible();
    await expect(list.getByText("Buscando…",{exact:true})).toBeHidden();
    await expect.poll(()=>list.evaluate(element=>element.closest("[data-state='open']")?.getAnimations().some(animation=>animation.playState==="running")??false)).toBe(false);
    const after=await scroller!.evaluate(element=>element.scrollTop);
    const bounds=await list.boundingBox();
    expect(await list.evaluate(element=>{
      const bounds=element.getBoundingClientRect();
      for(let parent=element.parentElement;parent&&parent!==document.body;parent=parent.parentElement){
        const style=getComputedStyle(parent),clip=parent.getBoundingClientRect();
        if(["hidden","clip","auto","scroll"].includes(style.overflowY)&&(bounds.top<clip.top-1||bounds.bottom>clip.bottom+1))return false;
      }
      return true;
    }),`${name} no debe recortarse por un contenedor del modal`).toBe(true);
    observations.push({name,before,after,bounds});
    console.info("Contract dropdown",JSON.stringify(observations.at(-1)));
    await page.screenshot({path:info.outputPath(`contract-${name.replaceAll(" ","-")}-${width}.png`)});
    if(name==="Perfil de nómina"){
      const lastOption=list.getByRole("option").last();await lastOption.scrollIntoViewIfNeeded();await expect(lastOption).toBeInViewport();
    }
    expect(Math.abs(after-before),`${name} debe conservar la posición del contrato`).toBeLessThanOrEqual(3);
    expect(bounds!.y).toBeGreaterThanOrEqual(0);expect(bounds!.y+bounds!.height).toBeLessThanOrEqual(width===390?844:768);
    await page.keyboard.press("Escape");await expect(list).toBeHidden();
    expect(Math.abs(await scroller!.evaluate(element=>element.scrollTop)-before)).toBeLessThanOrEqual(3);
    await trigger.click();await expect(list).toBeVisible();
    await page.getByRole("option").first().click();
    await expect(list).toBeHidden();
    await expect(trigger).toBeFocused();
    expect(Math.abs(await scroller!.evaluate(element=>element.scrollTop)-before)).toBeLessThanOrEqual(3);
  }
  await info.attach("contract-dropdown-geometry",{body:JSON.stringify(observations),contentType:"application/json"});
  expect(errors).toEqual([]);
});

async function createEmployment(page: Page) {
  await page.getByRole("button", { name: "Nuevo contrato" }).click();
  await choose(page, "Empleado", "Elena Prueba");
  await page.getByLabel("Número de contrato", { exact: true }).fill("CONTRATO-01");
  for (const [label, option] of [["Tipo de contrato", "Término indefinido"], ["Tipo de salario", "Salario ordinario"], ["Periodicidad", "Mensual"], ["Clase de riesgo", "Clase I"], ["Tipo de trabajador", "Dependiente"], ["Subtipo", "No aplica subtipo"], ["Medio de pago", "Efectivo"]]) await choose(page, label, option);
  await page.getByLabel("Salario mensual", { exact: true }).fill("1000000");
  await page.getByRole("button", { name: "Guardar", exact: false }).click();
  await expect(page.getByRole("row").filter({ hasText: "CONTRATO-01" })).toBeVisible();
}

test("liquidación, aprobación, pago, DIAN y reportes se recorren desde sus pestañas", async ({ page, baseURL }) => {
  await page.clock.install({ time: new Date("2026-11-12T01:30:00Z") });
  const state = await setup(page, baseURL!);
  const errors: string[] = []; page.on("pageerror", error => errors.push(error.message));
  await page.goto("/dashboard/payroll");
  await expect(page.getByRole("heading", { name: "Nómina clara y conectada" })).toBeVisible();
  await expect(page.getByRole("tab")).toHaveCount(8);
  await page.getByRole("tab", { name: "Trabajadores", exact: true }).click();
  await createEmployment(page);
  await page.getByRole("tab", { name: "Liquidaciones", exact: true }).click();
  await page.getByRole("button", { name: "Nueva liquidación", exact: true }).click();
  await choose(page, "Periodicidad", "Mensual");
  await expect(page.getByRole("combobox",{name:"Reglas aprobadas",exact:true})).toHaveCount(0);
  await page.getByRole("button", { name: "Generar liquidación", exact: true }).click();
  await expect.poll(() => state.runs[0]?.status).toBe("Calculated");
  expect(state.writes.filter(write=>write.path.endsWith("/calculate"))).toHaveLength(1);
  await page.getByRole("button", { name: "Aprobar y contabilizar" }).click();
  await expect.poll(() => state.runs[0].status).toBe("Approved");
  await expect(page.getByRole("button", { name: "Recalcular", exact: true })).toHaveCount(0);
  await page.getByRole("dialog",{name:"Detalle de liquidación",exact:true}).getByRole("button",{name:"Cerrar ventana",exact:true}).click();
  await page.getByRole("tab", { name: "Pagos", exact: true }).click();
  await page.getByRole("button", { name: "Confirmar pago", exact: true }).click();
  await page.getByRole("combobox", { name: "Liquidación", exact: true }).click();
  await page.getByRole("option").click();
  await page.getByRole("region",{name:"Grupo Efectivo",exact:true}).getByRole("button",{expanded:false}).click();
  await page.getByRole("combobox", { name: "Forma de pago", exact: true }).click();
  await page.getByRole("option", { name: /Caja nómina/ }).click();
  await page.getByRole("button", { name: "Fecha de pago", exact: true }).click();
  await page.getByRole("button", { name: "Elegir hoy", exact: true }).click();
  await page.getByLabel("Referencia bancaria o de caja").fill("PAGO-001");
  await page.getByRole("button", { name: "Confirmar pago del grupo" }).click();
  await expect(page.getByRole("row").filter({ hasText: "PAGO-001" })).toBeVisible();
  await page.getByRole("tab", { name: "Electrónica DIAN" }).click();
  await expect(page.getByRole("button", { name: "Consolidar mes", exact: true })).toHaveCount(1);
  await page.getByRole("button", { name: "Consolidar mes", exact: true }).click();
  await page.getByRole("button", { name: "Generar y enviar" }).click();
  await expect.poll(() => state.options.electronicPeriods.length).toBe(1);
  await page.locator("summary").click();
  await expect(page.getByText("En procesamiento fiscal", { exact: true })).toBeVisible();
  await page.getByRole("tab", { name: "Reportes", exact: true }).click();
  await page.getByRole("button", { name: "Generar reporte", exact: true }).click();
  await expect(page.getByText("$ 920.000", { exact: false }).first()).toBeVisible();
  await page.screenshot({ path: "test-results/payroll-reports.png", fullPage: true });
  expect(errors).toEqual([]);
});

test("crear y reabrir contrato, descuento y novedad conserva sus datos", async ({ page, baseURL }) => {
  const state = await setup(page, baseURL!);
  const centerId="44444444-4444-4444-4444-444444444444";let centerReads=0;
  await page.route("**/api/commerce/v1/accounting/cost-centers?**",async route=>{centerReads++;await route.fulfill({json:{items:[{costCenterId:centerId,code:"CC",name:"Centro de prueba",isActive:true,isDefault:false}],page:1,pageSize:20,totalCount:1,totalPages:1}});});
  await page.goto("/dashboard/payroll?section=employments");
  await createEmployment(page);
  await page.getByRole("button", { name: "Editar", exact: true }).click();
  await expect(page.getByLabel("Salario mensual", { exact: true })).toHaveValue("1 000 000");
  await page.keyboard.press("Escape");
  await page.getByRole("tab", { name: "Novedades", exact: true }).click();
  const employee=state.employees[0];
  state.runs.push({payrollRunId:"worker-run",businessId,ruleSetId:"rules",runKind:"Regular",originalPayrollRunId:null,periodStart:"2026-08-01",periodEnd:"2026-08-31",paymentDate:"2026-08-31",
    status:"Calculated",employeeCount:1,totalEarnings:1000000,totalDeductions:80000,netPayable:920000,totalEmployerContributions:0,totalProvisions:0,calculationVersion:1,rowVersion:version,payableEmployees:[],
    employees:[{payrollRunEmployeeId:"calculation-before",employmentId:employee.employmentId,partyId:employee.partyId,employeeName:"Elena Prueba",workedDays:30,baseWorkedDays:30,maximumContractDays:30,periodDays:30,
      earnings:1000000,deductions:80000,netPayable:920000,employerContributions:0,provisions:0,lines:[]}]});
  await page.getByRole("button", { name: "Actualizar nómina" }).click();
  await selectNoveltyLiquidation(page,/2026-08-01 — 2026-08-31/);
  await page.getByRole("button",{name:"Ver trabajador Elena Prueba",exact:true}).click();
  await page.getByRole("button",{name:"Agregar novedad",exact:true}).click();
  const noveltyEditor=page.getByRole("region",{name:"Edición de novedad",exact:true});
  await expect(noveltyEditor.getByRole("textbox").first()).toHaveValue("Elena Prueba");
  await choose(page, "Concepto", "PRESTAMO · Préstamo empleado");
  await choose(page, "Tipo de novedad", "Valor");
  await expect(page.getByRole("button", { name: "Registrar novedad" })).toBeDisabled();
  await page.getByRole("button",{name:"Registrar autorización aquí",exact:true}).click();
  const authority=page.getByRole("region",{name:"Autorización de esta novedad",exact:true});
  await choose(page,"Autoridad","Autorización escrita");
  await authority.getByLabel("Referencia",{exact:true}).fill("AUT-01");
  await authority.getByLabel("Evidencia (URL)",{exact:true}).fill("https://example.test/evidencia");
  await authority.getByRole("button",{name:"Guardar autorización",exact:true}).click();
  await expect.poll(()=>state.options.deductionAgreements.length).toBe(1);
  const acceptedBeneficiary=state.options.deductionAgreements[0].beneficiaryPartyId;
  await expect(page.getByRole("combobox",{name:"Acuerdo de descuento (si aplica)",exact:true})).toContainText("AUT-01");
  expect(centerReads).toBe(0);
  await page.getByRole("combobox",{name:"Centro de costo de la novedad",exact:true}).click();
  await page.getByRole("option",{name:"CC · Centro de prueba",exact:true}).click();
  expect(centerReads).toBe(1);
  await page.getByLabel("Valor total", { exact: true }).fill("50000");
  await expect(page.getByLabel("Cantidad",{exact:true})).toHaveValue("0");
  await expect(page.getByLabel("Cantidad",{exact:true})).toHaveAttribute("min","0");
  await choose(page,"Tipo de novedad","Horas");
  await expect(page.getByRole("button",{name:"Registrar novedad"})).toBeDisabled();
  await choose(page,"Tipo de novedad","Valor");
  const readsBefore=state.reads.length;
  await page.getByRole("button", { name: "Registrar novedad" }).click();
  await expect(noveltyEditor).toBeHidden();
  expect(state.options.novelties[0].quantity).toBe(0);
  expect(state.options.novelties[0].costCenterId).toBe(centerId);
  expect(state.reads.length).toBe(readsBefore);
  await expect(page.getByRole("button",{name:"Editar novedad",exact:true})).toHaveText("");
  await expect(page.getByRole("button",{name:"Retirar novedad",exact:true})).toHaveText("");
  await page.getByRole("button",{name:"Editar novedad",exact:true}).click();
  await expect(noveltyEditor.getByRole("textbox").first()).toHaveValue("Elena Prueba");
  await page.getByLabel("Valor total",{exact:true}).fill("60000");
  await page.getByLabel("Notas",{exact:true}).fill("Cuota corregida");
  await page.getByRole("button",{name:"Guardar novedad",exact:true}).click();
  await expect(noveltyEditor).toBeHidden();
  await expect(page.getByText("Cuota corregida",{exact:true})).toBeVisible();
  expect(state.options.novelties[0].totalAmount).toBe(60000);
  expect(state.reads.length).toBe(readsBefore);
  await page.getByRole("button",{name:"Retirar novedad",exact:true}).click();
  await expect(page.getByRole("button",{name:"Retirar novedad",exact:true})).toBeHidden();
  expect(state.options.novelties[0].status).toBe("Voided");
  expect(state.reads.length).toBe(readsBefore);
  await page.getByRole("tab",{name:"Autorizaciones",exact:true}).click();
  await page.getByRole("button", { name: "Desactivar", exact: true }).click();
  await expect.poll(() => state.options.deductionAgreements[0].isActive).toBe(false);
  expect(state.writes.at(-1)?.body.beneficiaryPartyId).toBe(acceptedBeneficiary);
  await page.screenshot({ path: "test-results/payroll-novelties.png", fullPage: true });
});

test("configuración permite crear conceptos y perfiles y enlaza centros de costo", async ({ page, baseURL }) => {
  const state = await setup(page, baseURL!);
  state.options.catalogs["payroll-rule-parameter"].push(
    {optionId:"health-rate",catalogCode:"payroll-rule-parameter",code:"EmployeeHealthRate",label:"Salud del trabajador",description:null,metadataCode:"Rate",dianCode:null,isActive:true,sortOrder:20},
    {optionId:"minimum-salary",catalogCode:"payroll-rule-parameter",code:"MinimumMonthlySalary",label:"Salario mínimo mensual",description:null,metadataCode:"COP",dianCode:null,isActive:true,sortOrder:30});
  await page.goto("/dashboard/payroll?section=configuration");
  await expect(page.getByRole("link", { name: "Ver centros y asignaciones" })).toHaveAttribute("href", "/dashboard/accounting?section=centers");
  await page.getByRole("button", { name: "Agregar", exact: true }).first().click();
  await page.getByLabel("Código", { exact: true }).fill("OTRO");
  await page.getByLabel("Nombre", { exact: true }).fill("Otro descuento");
  for (const [label, option] of [["Naturaleza", "Deducción"], ["Método", "Valor fijo"], ["Tratamiento", "Deducción autorizada"], ["Categoría contable", "Préstamos a empleados"]]) await choose(page, label, option);
  await page.getByRole("button", { name: "Guardar concepto" }).click();
  await expect.poll(() => state.options.concepts.length).toBe(2);
  await expect(page.getByText("OTRO · Otro descuento",{exact:true})).toBeVisible();
  await page.getByRole("checkbox",{name:/^Nómina electrónica/}).check();
  await expect.poll(()=>state.options.settings?.electronicPayrollEnabled).toBe(true);
  await expect(page.getByRole("checkbox",{name:/^Nómina electrónica/})).toBeChecked();
  expect(state.reads.filter(read=>read.path.endsWith("/payroll/options"))).toHaveLength(1);
  await page.getByRole("button",{name:"Nuevo perfil",exact:true}).click();
  const profileDialog=page.getByRole("dialog",{name:"Nuevo perfil de nómina",exact:true});
  await expect(profileDialog.getByRole("combobox",{name:/Porcentajes|configuración aprobada/})).toHaveCount(0);
  await expect(profileDialog.getByLabel("Código",{exact:true})).toHaveCount(0);
  await profileDialog.getByLabel("Nombre",{exact:true}).fill("Operativo");
  await profileDialog.getByLabel("Fuente normativa",{exact:true}).fill("Fuente verificada de prueba");
  await profileDialog.getByLabel("Días del mes · Valor",{exact:true}).fill("30");
  await profileDialog.getByLabel("Salud del trabajador · %",{exact:true}).fill("4");
  await profileDialog.getByLabel("Salario mínimo mensual · COP",{exact:true}).fill("1000000");
  for(const width of [1366,390]){
    await page.setViewportSize({width,height:844});
    await expect(profileDialog.getByRole("button",{name:"Guardar perfil",exact:true})).toBeInViewport();
    expect(await profileDialog.evaluate(element=>element.scrollWidth<=element.clientWidth)).toBe(true);
    await page.screenshot({path:`test-results/payroll-new-profile-native-${width}.png`});
  }
  await profileDialog.getByRole("button",{name:"Guardar perfil",exact:true}).click();
  await expect(profileDialog).toBeHidden();
  await expect(page.getByRole("button",{name:"Abrir perfil Operativo",exact:true})).toBeVisible();
  expect(state.reads.filter(read=>/\/payroll\/profiles\/[^/]+\/rule-sets$/.test(read.path))).toHaveLength(0);
  expect(state.options.ruleSets).toHaveLength(2);expect(state.options.ruleSets[1].status).toBe("Approved");
  const command=state.writes.find(write=>write.path.includes("/payroll/profiles/"))!.body;
  expect(command.configuration).toMatchObject({parameters:[{code:"MonthlyDays",numericValue:30},{code:"EmployeeHealthRate",numericValue:.04},{code:"MinimumMonthlySalary",numericValue:1000000}]});
  await page.screenshot({ path: "test-results/payroll-configuration.png", fullPage: true });
});

test("los errores no aparentan una nómina vacía y permiten reintentar", async ({ page, baseURL }) => {
  const state = await setup(page, baseURL!); state.failures.options = true;
  await page.goto("/dashboard/payroll");
  await expect(page.getByRole("button", { name: "Reintentar", exact: true })).toBeVisible();
  await expect(page.getByText("Todavía no hay liquidaciones.")).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Nueva liquidación" })).toBeDisabled();
  state.failures.options = false;
  await page.getByRole("button", { name: "Reintentar", exact: true }).click();
  await expect(page.getByText("Todavía no hay liquidaciones.")).toBeVisible();
});

test("solo lectura no ofrece mutaciones y móvil conserva pestañas accesibles", async ({ page, baseURL }) => {
  await setup(page, baseURL!, ["dashboard.read", "payroll.read"]);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/dashboard/payroll");
  await expect(page.getByText("Todavía no hay liquidaciones.")).toBeVisible();
  await expect(page.getByRole("button", { name: "Nueva liquidación" })).toHaveCount(0);
  await page.getByRole("tab", { name: "Liquidaciones", exact: true }).focus();
  await page.keyboard.press("ArrowRight");
  await expect(page.getByRole("tab", { name: "Trabajadores", exact: true })).toBeFocused();
  await page.getByRole("tab", { name: "Configuración", exact: true }).click();
  await expect(page.getByRole("button", { name: "Agregar", exact: true })).toHaveCount(0);
  await expect(page.getByRole("checkbox").first()).toBeDisabled();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: "test-results/payroll-mobile.png", fullPage: true });
});

test("cambiar de sede descarta el detalle y consulta los trabajadores en el nuevo contexto", async ({ page, baseURL }) => {
  const state = await setup(page, baseURL!);
  await page.goto("/dashboard/payroll?section=employments");
  await expect.poll(() => state.reads.some(item => item.path.endsWith("/payroll/employments") && item.business === businessId)).toBe(true);
  const beforeRefresh = state.reads.filter(item => item.path.endsWith("/payroll/employments")).length;
  await page.getByRole("button", { name: "Actualizar nómina" }).click();
  await expect.poll(() => state.reads.filter(item => item.path.endsWith("/payroll/employments")).length).toBeGreaterThan(beforeRefresh);
  await page.getByRole("banner").getByRole("combobox").click();
  await page.getByRole("button", { name: "Sede norte Espacio de trabajo" }).click();
  await expect.poll(() => state.reads.some(item => item.path.endsWith("/payroll/employments") && item.business === secondBusinessId)).toBe(true);
  await expect(page.getByText("Crea el primer contrato laboral.")).toBeVisible();
});

test("sin permiso de lectura no consulta el módulo", async ({ page, baseURL }) => {
  const state = await setup(page, baseURL!, ["dashboard.read"]);
  await page.goto("/dashboard/payroll");
  await expect(page.getByText("No tienes permiso para consultar nómina.")).toBeVisible();
  expect(state.reads.filter(item => item.path.includes("/payroll/"))).toHaveLength(0);
});

test("editar serie conserva software, PIN y habilitación propios de nómina", async ({ page, baseURL }) => {
  const state = await setup(page, baseURL!);
  state.options.fiscalIssuers = [{ fiscalIssuerConfigurationId: "issuer", version: 1, legalName: "Empleador de prueba", softwareIdentificationCode: "invoice-software", softwarePinSecretReference: "invoice-pin-reference", environment: 2, testSetId: "invoice-test-set", isActive: true }];
  state.options.electronicConfiguration = { businessId, fiscalIssuerConfigurationId: "issuer", softwareIdentificationCode: "payroll-software", softwarePinSecretReference: "payroll-pin-reference", testSetId: "payroll-test-set", prefix: "NIE", nextConsecutive: 4, qrValidationUrl: "https://example.test/payroll", isActive: true, rowVersion: version };
  // The adjacent fiscal cards are independent of editing the existing payroll series.
  await page.route("**/fiscal/configuration/**", route => route.fulfill({ status: 503, contentType: "application/json", body: JSON.stringify({ message: "Configuración fiscal no disponible en esta prueba" }) }));
  await page.goto("/dashboard/settings/fiscal");
  await page.getByLabel("Prefijo de nómina", { exact: true }).fill("NOM");
  await page.getByRole("button", { name: "Guardar serie de nómina" }).click();
  await expect.poll(() => state.writes.some(item => item.path.endsWith("/electronic-configuration"))).toBe(true);
  expect(state.writes.find(item => item.path.endsWith("/electronic-configuration"))?.body).toMatchObject({ prefix: "NOM", softwareIdentificationCode: "payroll-software", softwarePinSecretReference: "payroll-pin-reference", testSetId: "payroll-test-set", rowVersion: version });
  await expect(page.getByLabel("Prefijo de nómina", { exact: true })).toHaveValue("NOM");
});


test.describe("fechas iniciales de nómina", () => {
  test.use({ timezoneId: "Pacific/Kiritimati" });
  test("crear liquidación toma el día de cada sede", async ({ page, baseURL }) => {
    await page.clock.install({ time: new Date("2026-11-12T01:30:00Z") });
    const state = await setup(page, baseURL!);
    await page.goto("/dashboard/payroll");
    for (const [day, expectedBusiness] of [["2026-11-11", businessId], ["2026-11-12", secondBusinessId]]) {
      await page.getByRole("button", { name: "Nueva liquidación", exact: true }).click();
      const dialog = page.getByRole("dialog");
      const paymentDate = dialog.locator("label").filter({has:page.locator("span",{hasText:/^Fecha de pago$/})}).getByRole("button");
      await paymentDate.click();
      await page.getByRole("button", {name:"Elegir hoy",exact:true}).click();
      await choose(page, "Periodicidad", "Mensual");
      await expect(page.getByRole("combobox",{name:"Reglas aprobadas",exact:true})).toHaveCount(0);
      const count = state.writes.length;
      await page.getByRole("button", { name: "Generar liquidación", exact: true }).click();
      await expect.poll(() => state.writes.length).toBe(count + 2);
      expect(state.writes[count].body).toMatchObject({ businessId: expectedBusiness,
        periodStart: "2026-11-01", periodEnd: "2026-11-30", paymentDate: day });
      const detail=page.getByRole("dialog",{name:"Detalle de liquidación",exact:true});
      await expect(detail.getByText("No hay contratos elegibles para el cálculo.",{exact:true})).toBeVisible();
      await expect(detail.getByRole("button",{name:"Aprobar y contabilizar",exact:true})).toHaveCount(0);
      await detail.getByRole("button",{name:"Cerrar ventana",exact:true}).click();
      if (expectedBusiness === businessId) {
        await page.getByRole("banner").getByRole("combobox").click();
        await page.getByRole("button", { name: "Sede norte Espacio de trabajo" }).click();
      }
    }
    expect(state.reads.filter(item => item.path.endsWith("/execution-context/businesses"))).toHaveLength(1);
  });
});


test.describe("fechas de calendario independientes del navegador", () => {
  test.use({ timezoneId: "Pacific/Apia" });
  test("un día existente en la sede no se desplaza por un día omitido en la zona del navegador", async ({ page, baseURL }) => {
    await page.clock.install({ time: new Date("2011-12-30T15:00:00Z") });
    await setup(page, baseURL!);
    await page.goto("/dashboard/payroll");
    await page.getByRole("button", {name:"Nueva liquidación",exact:true}).click();
    const dialog = page.getByRole("dialog");
    const end = dialog.locator("label").filter({has:page.locator("span",{hasText:/^Fin$/})}).getByRole("button");
    // New regular drafts propose the complete calendar month.
    await expect(end).toContainText("31 de diciembre de 2011");
    await end.click();
    await expect(page.locator('button[aria-label="30 de diciembre de 2011"]')).toBeVisible();
    await page.getByRole("button", {name:"Elegir hoy",exact:true}).click();
    await expect(end).toContainText("30 de diciembre de 2011");
  });
});


for(const switchScope of [false,true]) test(`novedad del trabajador guarda una vez y recupera el cálculo; cambio de sede=${switchScope}`,async({page,baseURL})=>{
  const state=await setup(page,baseURL!);
  await page.goto("/dashboard/payroll?section=employments");await createEmployment(page);
  const employee=state.employees[0];
  state.options.deductionAgreements.push({deductionAgreementId:"agreement",employmentId:employee.employmentId,employeeName:"Elena Prueba",conceptId:"loan",conceptName:"Préstamo empleado",
    authorityOptionId:"payroll-deduction-authority-WrittenAuthorization",beneficiaryPartyId:null,authorityName:"Autorización escrita",referenceNumber:"AUT-WORKER",evidenceUrl:"https://example.test/evidence",
    effectiveFrom:"2026-01-01",effectiveTo:null,authorizedTotal:500000,installmentAmount:50000,deductedToDate:0,priority:1,mustProtectMinimumNetPay:true,isActive:true,rowVersion:version});
  state.runs.push({payrollRunId:"worker-run",businessId,ruleSetId:"rules",runKind:"Regular",originalPayrollRunId:null,periodStart:"2026-08-01",periodEnd:"2026-08-31",paymentDate:"2026-08-31",
    status:"Calculated",employeeCount:1,totalEarnings:1000000,totalDeductions:80000,netPayable:920000,totalEmployerContributions:0,totalProvisions:0,calculationVersion:1,rowVersion:version,payableEmployees:[],
    employees:[{payrollRunEmployeeId:"calculation-before",employmentId:employee.employmentId,partyId:employee.partyId,employeeName:"Elena Prueba",workedDays:30,baseWorkedDays:30,maximumContractDays:30,periodDays:30,
      earnings:1000000,deductions:80000,netPayable:920000,employerContributions:0,provisions:0,lines:[]}]});
  await page.goto("/dashboard/payroll?section=novelties");
  await selectNoveltyLiquidation(page,/2026-08-01 — 2026-08-31/);
  await page.getByRole("button",{name:"Ver trabajador Elena Prueba",exact:true}).click();
  const editor=page.getByRole("region",{name:"Liquidación de Elena Prueba",exact:true});
  await expect(editor.getByRole("button",{name:"Recalcular trabajador",exact:true})).toHaveCount(0);
  await expect(editor.getByRole("button",{name:"Reintentar cálculo",exact:true})).toHaveCount(0);
  await editor.getByRole("button",{name:"Agregar novedad",exact:true}).click();
  const dialog=page.getByRole("region",{name:"Edición de novedad",exact:true});
  await expect(dialog.getByRole("textbox").first()).toHaveValue("Elena Prueba");
  await choose(page,"Concepto","PRESTAMO · Préstamo empleado");await choose(page,"Tipo de novedad","Valor");
  await choose(page,"Acuerdo de descuento (si aplica)","AUT-WORKER");await page.getByLabel("Valor total",{exact:true}).fill("50000");
  const readsBefore=state.reads.length;
  if(switchScope){
    let release:()=>void=()=>{};state.controls.noveltyResponse=new Promise<void>(resolve=>{release=resolve;});
    const saved=page.waitForResponse(response=>response.request().method()==="POST" && response.url().endsWith("/payroll/novelties"));
    await page.getByRole("button",{name:"Registrar novedad",exact:true}).click();
    try {
      await expect.poll(()=>state.options.novelties.length).toBe(1);
      await page.getByRole("button",{name:"Cerrar edición",exact:true}).click();
      await page.getByRole("banner").getByRole("combobox").click();
      await page.getByRole("button",{name:"Sede norte Espacio de trabajo"}).click();
      await expect(page.getByRole("banner").getByRole("combobox")).toContainText("Sede norte");
    } finally {release();}
    await (await saved).finished();
    await page.evaluate(()=>new Promise<void>(resolve=>requestAnimationFrame(()=>requestAnimationFrame(()=>resolve()))));
    expect(state.writes.filter(item=>item.path.endsWith("/calculate"))).toHaveLength(0);
    expect(state.writes.filter(item=>item.path.endsWith("/novelties"))).toHaveLength(1);return;
  }
  state.failures.calculation=true;
  let release:()=>void=()=>{};state.controls.noveltyResponse=new Promise<void>(resolve=>{release=resolve;});
  await page.getByRole("button",{name:"Registrar novedad",exact:true}).click();
  try {
    await expect.poll(()=>state.options.novelties.length).toBe(1);
    await page.getByRole("button",{name:"Cerrar edición",exact:true}).click();
    await editor.getByRole("button",{name:"Agregar novedad",exact:true}).click();
    await dialog.getByRole("textbox",{name:"Notas",exact:true}).fill("Segunda captura sin guardar");
  } finally {release();}
  await expect(page.getByText(/Novedad guardada. No fue posible actualizar el cálculo/)).toBeVisible();
  await expect(dialog.getByRole("textbox",{name:"Notas",exact:true})).toHaveValue("Segunda captura sin guardar");
  await page.getByRole("button",{name:"Cerrar edición",exact:true}).click();
  await expect(dialog).toBeHidden();
  expect(state.options.novelties).toHaveLength(1);
  expect(state.reads.length).toBe(readsBefore);
  await page.getByRole("button",{name:"Volver a trabajadores",exact:true}).click();
  await page.getByRole("button",{name:"Ver trabajador Elena Prueba",exact:true}).click();
  await expect(editor.getByRole("button",{name:"Reintentar cálculo",exact:true})).toBeEnabled();
  const readsBeforeRetry=state.reads.length;
  await editor.getByRole("button",{name:"Reintentar cálculo",exact:true}).click();
  await expect(editor.getByRole("row").filter({hasText:"PRESTAMO"})).toContainText("50.000");
  await expect(editor.getByRole("button",{name:"Reintentar cálculo",exact:true})).toHaveCount(0);
  const calculations=state.writes.filter(item=>item.path.endsWith("/calculate"));
  expect(calculations).toHaveLength(2);
  expect(calculations[0].body).toEqual({rowVersion:version,recalculateEmploymentIds:[employee.employmentId]});
  expect(state.writes.filter(item=>item.path.endsWith("/novelties"))).toHaveLength(1);
  expect(state.reads.length).toBe(readsBeforeRetry);
});


for(const configurationStatus of ["Approved","Retired"] as const){
test(`el perfil reúne toda su configuración editable sin pantallas ni selectores de porcentajes (${configurationStatus})`, async ({page,baseURL})=>{
  await page.clock.install({time:new Date("2027-01-01T15:00:00Z")});
  const state=await setup(page,baseURL!);
  state.options.catalogs["payroll-rule-parameter"].push({optionId:"health-rate",catalogCode:"payroll-rule-parameter",code:"EmployeeHealthRate",label:"Salud del trabajador",description:null,metadataCode:"Rate",dianCode:null,isActive:true,sortOrder:20});
  state.options.ruleSets[0].parameters.push({code:"EmployeeHealthRate",numericValue:.04,unitCode:"Rate",description:null});
  state.options.ruleSets[0].status=configurationStatus;
  if(configurationStatus==="Retired")state.options.ruleSets.push({...state.options.ruleSets[0],ruleSetId:"earlier-approval",status:"Approved",effectiveFrom:"2025-01-01",
    parameters:state.options.ruleSets[0].parameters.map(parameter=>parameter.code==="EmployeeHealthRate"?{...parameter,numericValue:.02}:parameter)});
  let profile={profileId:"profile",code:"OPER",name:"Operativo",ruleSetId:"rules",isActive:true,rowVersion:version,
    mappings:[{mappingId:"net",conceptId:null,debitAccountId:null,creditAccountId:"net-account",creditAccountLabel:"250505 · Nómina por pagar",effectiveFrom:"2026-01-01",effectiveTo:null}]};
  const reads:string[]=[],writes:Record<string,unknown>[]=[];
  await page.route("**/accounting/account-options?**",route=>route.fulfill({json:{items:Array.from({length:10},(_,index)=>({accountId:`account-${index}`,code:`2505${index}`,name:`Auxiliar de perfil ${index}`,allowsPosting:true,isActive:true})),page:1,pageSize:10,totalCount:10,totalPages:1}}));
  await page.route("**/payroll/profiles**",async route=>{
    const path=new URL(route.request().url()).pathname;
    if(route.request().method()==="PUT"){
      const data=route.request().postDataJSON();writes.push(data);
      const createdRuleSet=data.configuration?{...data.configuration,status:"Approved",rowVersion:version,isGlobal:false}:null;
      if(createdRuleSet)state.options.ruleSets.unshift(createdRuleSet);
      profile={...data,rowVersion:"AAAAAAAAAAI="};
      await route.fulfill({json:{...profile,createdRuleSet}});return;
    }
    reads.push(path);
    await route.fulfill({json:path.endsWith("/rule-sets")?{items:state.options.ruleSets,page:1,pageSize:20,totalCount:state.options.ruleSets.length}:path.endsWith("/profiles")?{items:[profile],page:1,pageSize:20,totalCount:1,totalPages:1}:profile});
  });
  await page.goto("/dashboard/payroll?section=employments");
  await expect(page.getByRole("button",{name:"Nuevo contrato",exact:true})).toBeVisible();
  expect(reads).toEqual([]);
  await page.getByRole("tab",{name:"Configuración",exact:true}).click();
  await page.getByRole("button",{name:"Abrir perfil Operativo",exact:true}).click();
  const dialog=page.getByRole("dialog",{name:"Editar perfil de nómina",exact:true});
  await expect(dialog.getByLabel("Salud del trabajador · %",{exact:true})).toHaveValue("4");
  if(configurationStatus==="Retired")await expect(dialog.getByText("Configuración retirada.",{exact:false})).toBeVisible();
  await expect(dialog.getByRole("combobox",{name:"Seleccionar cuenta contable"})).toContainText("250505 · Nómina por pagar");
  await expect(page.getByRole("button",{name:"Nueva vigencia",exact:true})).toHaveCount(0);
  await expect(dialog.getByRole("combobox",{name:/Porcentajes|configuración aprobada/})).toHaveCount(0);
  await dialog.getByLabel("Nombre",{exact:true}).fill("Operativo actualizado");
  await dialog.getByLabel("Salud del trabajador · %",{exact:true}).fill("4.13");
  for(const width of [1366,390]){
    await page.setViewportSize({width,height:844});
    await expect(dialog.getByRole("button",{name:"Guardar perfil",exact:true})).toBeInViewport();
    expect(await dialog.evaluate(element=>element.scrollWidth<=element.clientWidth+1)).toBe(true);
    const account=dialog.getByRole("combobox",{name:"Seleccionar cuenta contable",exact:true});
    await account.scrollIntoViewIfNeeded();
    const scroller=account.locator("xpath=ancestor::div[contains(@class,'overflow-y-auto')][1]");const before=await scroller.evaluate(element=>element.scrollTop);
    await account.click();const menu=page.getByRole("listbox");await expect(menu.getByRole("option")).toHaveCount(11);
    const available=menu.getByRole("option").filter({hasText:/Auxiliar de perfil/});await expect(available).toHaveCount(10);
    await available.last().scrollIntoViewIfNeeded();await expect(available.last()).toBeInViewport();
    const bounds=await menu.boundingBox(),parent=await dialog.boundingBox();expect(bounds!.y).toBeGreaterThanOrEqual(parent!.y-1);expect(bounds!.y+bounds!.height).toBeLessThanOrEqual(parent!.y+parent!.height+1);
    await page.keyboard.press("Escape");expect(Math.abs(await scroller.evaluate(element=>element.scrollTop)-before)).toBeLessThanOrEqual(3);
    await page.screenshot({path:`test-results/payroll-unified-profile-${width}.png`});
  }
  await dialog.getByRole("button",{name:"Guardar perfil",exact:true}).click();
  await expect(dialog).toBeHidden();
  expect(writes).toHaveLength(1);
  expect(writes[0].configuration).toMatchObject({effectiveFrom:"2026-01-01",parameters:[{code:"MonthlyDays",numericValue:30},{code:"EmployeeHealthRate",numericValue:.0413}]});
  expect(reads.filter(path=>path.endsWith("/profiles"))).toHaveLength(2);
  expect(reads.filter(path=>path.endsWith("/rule-sets"))).toHaveLength(1);
  await page.getByRole("button",{name:"Abrir perfil Operativo actualizado",exact:true}).click();
  await expect(dialog.getByLabel("Salud del trabajador · %",{exact:true})).toHaveValue("4.13");
  expect(reads.filter(path=>path.endsWith("/rule-sets"))).toHaveLength(2);
  expect(state.writes.filter(write=>write.path.includes("/rule-sets/"))).toEqual([]);
});
}

test("perfiles filtra, ordena y pagina en servidor, también en móvil y sin permiso de edición",async({page,baseURL})=>{
  await setup(page,baseURL!,["dashboard.read","payroll.read"]);
  const requests:URL[]=[];
  await page.route("**/payroll/profiles**",async route=>{
    const url=new URL(route.request().url());requests.push(url);
    const second=url.searchParams.get("page")==="2",filtered=Boolean(url.searchParams.get("search"));
    const items=Array.from({length:second||filtered?1:20},(_,index)=>({profileId:`profile-${second?21:index}`,code:`PER-${second?21:index}`,
      name:filtered?"Administrativo":"Perfil "+(second?21:index),ruleSetId:"rules",isActive:true,rowVersion:version,mappings:[]}));
    await route.fulfill({json:{items,page:second?2:1,pageSize:20,totalCount:filtered?1:21,totalPages:filtered?1:2}});
  });
  await page.setViewportSize({width:390,height:844});
  await page.goto("/dashboard/payroll?section=configuration");
  const profiles=page.getByRole("region",{name:"Perfiles de nómina",exact:true});
  await expect(page.getByRole("button",{name:/Abrir perfil/})).toHaveCount(20);
  expect(requests).toHaveLength(1);
  await expect(page.getByRole("button",{name:"Nuevo perfil",exact:true})).toHaveCount(0);
  await page.getByRole("button",{name:"Nombre",exact:false}).filter({hasText:"Nombre"}).click();
  await expect.poll(()=>requests.at(-1)?.searchParams.get("sortBy")).toBe("name");
  expect(requests.at(-1)?.searchParams.get("sortDirection")).toBe("desc");
  await page.getByRole("button",{name:"Siguiente perfiles",exact:true}).click();
  await expect(page.getByRole("button",{name:"Abrir perfil Perfil 21",exact:true})).toBeVisible();
  expect(requests.at(-1)?.searchParams.get("page")).toBe("2");
  await page.getByRole("textbox",{name:"Buscar perfil de nómina"}).fill("Administrativo");
  await profiles.getByRole("button",{name:"Buscar",exact:true}).click();
  await expect(page.getByRole("button",{name:"Abrir perfil Administrativo",exact:true})).toBeVisible();
  expect(requests.at(-1)?.searchParams.get("search")).toBe("Administrativo");
  expect(requests.at(-1)?.searchParams.get("page")).toBe("1");
  expect(requests.at(-1)?.searchParams.get("pageSize")).toBe("20");
  expect(await profiles.evaluate(element=>element.scrollWidth<=element.clientWidth+1)).toBe(true);
  await page.screenshot({path:"test-results/payroll-profiles-mobile.png",fullPage:true});
});

test("los historiales cargan solo la pestaña visible y los documentos al desplegar su período",async({page,baseURL})=>{
  const state=await setup(page,baseURL!);
  for(let index=0;index<41;index++){
    state.runs.push({payrollRunId:`history-${index}`,businessId,ruleSetId:null,runKind:"Regular",originalPayrollRunId:null,
      periodStart:"2026-08-01",periodEnd:"2026-08-31",paymentDate:"2026-08-31",status:"Approved",employeeCount:0,
      totalEarnings:1000000,totalDeductions:0,netPayable:1000000,totalEmployerContributions:0,totalProvisions:0,calculationVersion:1,rowVersion:version,payableEmployees:[],employees:[]});
    state.options.paymentBatches.push({paymentBatchId:`batch-${index}`,payrollRunId:`history-${index}`,paymentDate:"2026-08-31",
      paymentMethodOptionId:"payroll-payment-method-Cash",paymentMethodName:"Efectivo",referenceNumber:`PAGO-${index}`,
      status:"Confirmed",employeeCount:1,totalAmount:1000000,rowVersion:version,remainingEmployees:[],accountingStatus:"Posted",isPaid:true});
    state.options.electronicPeriods.push({electronicPeriodId:`period-${index}`,year:2026,month:8,status:"Generated",rowVersion:version,
      documents:Array.from({length:25},(_,document)=>({electronicPayrollDocumentId:`doc-${index}-${document}`,partyId:`party-${document}`,
        employeeName:`Trabajador fiscal ${document}`,documentKind:"Individual",fiscalDocumentId:null,status:"Queued",sourceHashHex:"00"}))});
  }
  await page.setViewportSize({width:390,height:844});
  await page.goto("/dashboard/payroll?section=configuration");
  await expect(page.getByRole("button",{name:"Nuevo perfil",exact:true})).toBeVisible();
  const count=(suffix:string)=>state.reads.filter(read=>read.path.endsWith(suffix)).length;
  for(const suffix of ["/payroll/runs","/payroll/payments","/payroll/deduction-agreements","/payroll/electronic-periods"])expect(count(suffix)).toBe(0);
  await page.getByRole("tab",{name:"Liquidaciones",exact:true}).click();
  await expect(page.getByText("41 liquidaciones · Página 1",{exact:true})).toBeVisible();
  expect(count("/payroll/runs")).toBe(1);
  await page.getByRole("button",{name:"Siguiente liquidaciones",exact:true}).click();
  await expect(page.getByText("41 liquidaciones · Página 2",{exact:true})).toBeVisible();
  expect(count("/payroll/runs")).toBe(2);
  await page.getByRole("tab",{name:"Pagos",exact:true}).click();
  await expect(page.getByText("PAGO-0",{exact:true})).toBeVisible();await expect(page.getByText("PAGO-20",{exact:true})).toHaveCount(0);
  await page.getByRole("button",{name:"Siguiente pagos",exact:true}).click();
  await expect(page.getByText("PAGO-20",{exact:true})).toBeVisible();await expect(page.getByText("PAGO-0",{exact:true})).toHaveCount(0);
  expect(count("/payroll/payments")).toBe(2);expect(count("/payroll/electronic-periods")).toBe(0);
  await page.getByRole("tab",{name:"Electrónica DIAN",exact:true}).click();
  await expect(page.locator("details")).toHaveCount(20);
  expect(state.reads.filter(read=>read.path.endsWith("/documents"))).toHaveLength(0);
  await page.locator("details summary").first().click();
  await expect(page.getByText("Trabajador fiscal 0",{exact:true})).toBeVisible();
  await expect(page.getByText("Trabajador fiscal 20",{exact:true})).toHaveCount(0);
  await page.getByRole("button",{name:"Siguiente documentos",exact:true}).click();
  await expect(page.getByText("Trabajador fiscal 20",{exact:true})).toBeVisible();
  expect(state.reads.filter(read=>read.path.endsWith("/documents"))).toHaveLength(2);
  await page.locator("details summary").first().click();await page.locator("details summary").first().click();
  await expect(page.getByText("Trabajador fiscal 0",{exact:true})).toBeVisible();
  expect(state.reads.filter(read=>read.path.endsWith("/documents"))).toHaveLength(2);
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=document.documentElement.clientWidth)).toBe(true);
  await page.screenshot({path:"test-results/payroll-electronic-history-mobile.png",fullPage:true});
});

test("acciones por trabajador abren solo su desprendible en un modal móvil",async({page,baseURL})=>{
  const state=await setup(page,baseURL!);
  const workers=Array.from({length:2},(_,index)=>({payrollRunEmployeeId:`result-${index}`,employmentId:`employment-${index}`,partyId:`party-${index}`,
    employeeName:`Trabajador ${index}`,identification:String(index),contractNumber:`C-${index}`,monthlySalary:1000000,
    workedDays:30,baseWorkedDays:30,maximumContractDays:30,periodDays:30,earnings:1000000,deductions:0,netPayable:1000000,employerContributions:0,provisions:0,lines:[]}));
  state.runs.push({payrollRunId:"row-actions",businessId,ruleSetId:null,runKind:"Regular",originalPayrollRunId:null,periodStart:"2026-08-01",periodEnd:"2026-08-31",paymentDate:"2026-08-31",
    status:"Calculated",employeeCount:2,totalEarnings:2000000,totalDeductions:0,netPayable:2000000,totalEmployerContributions:0,totalProvisions:0,calculationVersion:1,rowVersion:version,payableEmployees:[],employees:workers});
  const receipts:URL[]=[];
  await page.route("**/payroll/reports/payroll-receipt?**",async route=>{
    const url=new URL(route.request().url());receipts.push(url);
    const worker=state.runs[0].employees.find(row=>row.payrollRunEmployeeId===url.searchParams.get("employeeResultId"))!;
    const header={...worker,runStatus:"Calculated",periodStart:"2026-08-01",periodEnd:"2026-08-31",paymentDate:"2026-08-31",paidDate:null,profileName:"Operativo",costCenterName:"Principal"};
    return route.fulfill({json:{from:header.periodStart,to:header.periodEnd,rows:[
      {...header,id:"salary",conceptCode:"SAL",conceptName:"Salario",nature:"Earning",quantity:30,amount:1000000},
      {...header,id:"bonus",conceptCode:"BON",conceptName:"Bonificación",nature:"Earning",quantity:1,amount:1000000},
      {...header,id:"health",conceptCode:"SALUD",conceptName:"Salud trabajador",nature:"Deduction",quantity:1,percentageRate:0.04,amount:80000},
      {...header,id:"absence",conceptCode:"AUS",conceptName:"Ausencia",nature:"Deduction",quantity:2,amount:0}]}});
  });
  await page.goto("/dashboard/payroll?section=novelties");
  await selectNoveltyLiquidation(page,/2026-08-01 — 2026-08-31/);
  const list=page.getByRole("region",{name:"Trabajadores liquidados",exact:true});
  await expect(list.getByRole("article")).toHaveCount(2);
  await expect(list.getByRole("button",{name:/^Recalcular a /})).toHaveCount(0);
  await expect(list.getByRole("button",{name:"Desprendible de Trabajador 0",exact:true})).toBeEnabled();
  await list.getByRole("button",{name:"Desprendible de Trabajador 0",exact:true}).click();
  const dialog=page.getByRole("dialog",{name:"Desprendible laboral",exact:true});
  await expect(dialog.getByText("Trabajador 0 · 0",{exact:true})).toBeVisible();
  await expect(dialog.getByRole("row").filter({hasText:"Salud trabajador"})).toContainText(/4\s*%/);
  await expect(dialog.getByRole("row").filter({hasText:"Ausencia"})).not.toContainText(/\d\s*%/);
  await expect(dialog.locator("#auraly-report-print-area").getByText(/Preliminar · Trabajador 0/)).toBeVisible();
  expect(receipts).toHaveLength(1);expect(receipts[0].searchParams.get("employeeResultId")).toBe("result-0");
  expect(state.reads.filter(read=>read.path.endsWith("/calculated-employees"))).toHaveLength(1);
  expect(state.reads.filter(read=>read.path.endsWith("/payroll/runs/row-actions"))).toHaveLength(0);
  await page.setViewportSize({width:390,height:844});
  const bounds=await dialog.boundingBox();expect(bounds!.width).toBeLessThanOrEqual(390);
  expect(await dialog.evaluate(element=>element.scrollWidth<=element.clientWidth)).toBe(true);
  await dialog.getByText("Trabajador 0 · 0",{exact:true}).scrollIntoViewIfNeeded();
  await page.screenshot({path:"test-results/payroll-worker-receipt-modal-mobile.png"});
  await dialog.getByRole("button",{name:"Cerrar",exact:true}).click();
  await expect(dialog).toBeHidden();await expect(list.getByRole("article")).toHaveCount(2);
  expect(state.reads.filter(read=>read.path.endsWith("/calculated-employees"))).toHaveLength(1);
});

test("contrato conserva perfil y afiliaciones al editar sin lecturas por cada entidad",async({page,baseURL})=>{
  const state=await setup(page,baseURL!);
  const config={accountingProfileId:"oper",healthPartyId:"eps",pensionPartyId:"afp",severancePartyId:"severance",riskPartyId:"arl",compensationPartyId:"ccf",costCenterId:null};
  const metadata={profileName:"Operativo",healthName:"EPS de prueba",pensionName:"Pensión de prueba",severanceName:"Cesantías de prueba",riskName:"ARL de prueba",compensationName:"Caja de prueba",costCenterName:null};
  state.employees.push({employmentId:"contract",partyId:"party",businessId,employeeId:"employee",employeeName:"Elena Prueba",contractTypeOptionId:"payroll-contract-type-Indefinite",
    salaryTypeOptionId:"payroll-salary-type-Ordinary",payFrequencyOptionId:"payroll-pay-frequency-Monthly",riskClassOptionId:"payroll-risk-class-I",workerTypeOptionId:"payroll-worker-type-01",workerSubtypeOptionId:"payroll-worker-subtype-00",
    paymentMethodOptionId:"payroll-payment-method-Cash",contractNumber:"PROFILE-01",startDate:"2026-01-01",endDate:null,monthlySalary:1000000,integralSalaryPercentage:null,bankAccountReference:null,bankOptionId:null,bankAccountTypeOptionId:null,bankAccountNumber:null,isActive:true,appliesTransportAllowance:true,rowVersion:version,configuration:{values:config,...metadata}});
  const institutions:string[]=[];const profiles:string[]=[];
  await page.route("**/payroll/profiles**",async route=>{
    profiles.push(route.request().url());
    await route.fulfill({json:{items:[{profileId:"oper",code:"OPER",name:"Operativo",ruleSetId:"rules",isActive:true,rowVersion:version,mappings:[]}],page:1,pageSize:10,totalCount:1,totalPages:1}});
  });
  await page.route("**/parties/role-options**",async route=>{
    institutions.push(route.request().url());
    await route.fulfill({json:{items:[{partyId:"eps-new",roleId:"eps-new",role:"Any",displayName:"Nueva EPS de prueba",identification:"900001"}],page:1,pageSize:10,totalCount:1,totalPages:1}});
  });
  await page.route("**/payroll/employments/contract",async route=>{
    const body=route.request().postDataJSON();
    const result={...state.employees[0],...body,configuration:{values:body.configuration,...metadata,healthName:"Nueva EPS de prueba"}};
    state.employees[0]=result;
    await route.fulfill({json:result});
  });
  await page.goto("/dashboard/payroll?section=employments");
  await page.getByRole("button",{name:"Editar",exact:true}).click();
  await expect(page.getByRole("combobox",{name:"Perfil de nómina",exact:true})).toContainText("Operativo");
  await expect(page.getByRole("combobox",{name:"EPS",exact:true})).toContainText("EPS de prueba");
  expect(institutions).toHaveLength(0);expect(profiles).toHaveLength(0);
  await page.getByRole("combobox",{name:"Perfil de nómina",exact:true}).click();
  await page.getByRole("option",{name:"Operativo",exact:true}).click();
  expect(profiles).toHaveLength(1);expect(new URL(profiles[0]).searchParams.get("isActive")).toBe("true");
  await page.getByRole("combobox",{name:"EPS",exact:true}).click();
  await page.getByRole("option",{name:/Nueva EPS de prueba/}).click();
  expect(institutions).toHaveLength(1);
  expect(new URL(institutions[0]).searchParams.get("partyType")).toBe("Organization");
  const before=state.reads.length;
  await page.getByRole("button",{name:"Guardar contrato",exact:true}).click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  expect(state.reads.length).toBe(before);
  await page.getByRole("button",{name:"Editar",exact:true}).click();
  await expect(page.getByRole("combobox",{name:"EPS",exact:true})).toContainText("Nueva EPS de prueba");
  expect(institutions).toHaveLength(1);
  await page.setViewportSize({width:390,height:844});
  await page.getByRole("combobox",{name:"Perfil de nómina",exact:true}).scrollIntoViewIfNeeded();
  await expect(page.getByRole("combobox",{name:"Fondo de pensiones",exact:true})).toBeVisible();
  await page.screenshot({path:"test-results/payroll-contract-profile-mobile.png",fullPage:true});
});

for(const width of [390,1366,1920])test(`bandeja de novedades pagina antes de abrir un trabajador a ${width}`,async({page,baseURL})=>{
  await page.setViewportSize({width,height:844});
  const state=await setup(page,baseURL!);
  state.runs.push({payrollRunId:"paged-run",businessId,ruleSetId:null,runKind:"Regular",originalPayrollRunId:null,periodStart:"2026-08-01",periodEnd:"2026-08-31",paymentDate:"2026-08-31",status:"Calculated",employeeCount:41,totalEarnings:41000000,totalDeductions:0,netPayable:41000000,totalEmployerContributions:0,totalProvisions:0,calculationVersion:1,rowVersion:version,payableEmployees:[],
    employees:Array.from({length:41},(_,index)=>({payrollRunEmployeeId:`result-${index}`,employmentId:`contract-${index}`,partyId:`party-${index}`,employeeName:`Trabajador ${String(index).padStart(2,"0")}`,identification:String(index),contractNumber:`C-${index}`,monthlySalary:1000000,workedDays:30,earnings:1000000,deductions:0,netPayable:1000000,employerContributions:0,provisions:0,lines:[]}))});
  const pages:string[]=[];const details:string[]=[];
  page.on("request",request=>{if(request.url().includes("/payroll/calculated-employees"))pages.push(request.url());if(request.method()==="GET"&&request.url().includes("/payroll/runs/paged-run"))details.push(request.url());});
  await page.goto("/dashboard/payroll?section=novelties");
  await selectNoveltyLiquidation(page,/2026-08-01 — 2026-08-31/);
  const list=page.getByRole("region",{name:"Trabajadores liquidados",exact:true});
  await expect(list.getByRole("article")).toHaveCount(20);expect(details).toHaveLength(0);
  await expect.poll(()=>page.getByRole("tab",{name:"Novedades",exact:true}).evaluate(element=>{
    const tab=element.getBoundingClientRect(),viewport=element.parentElement!.parentElement!.getBoundingClientRect();
    return tab.left>=viewport.left&&tab.right<=viewport.right;
  })).toBe(true);
  const listBounds=await list.boundingBox();
  for(const button of await list.getByRole("button",{name:/^(Anterior|Siguiente) trabajadores$/}).all()){
    const buttonBounds=await button.boundingBox();
    expect(buttonBounds!.x).toBeGreaterThanOrEqual(listBounds!.x);
    expect(buttonBounds!.x+buttonBounds!.width).toBeLessThanOrEqual(listBounds!.x+listBounds!.width);
  }
  await list.getByRole("button",{name:"Siguiente trabajadores"}).click();
  await expect(list.getByRole("button",{name:"Ver trabajador Trabajador 20",exact:true})).toBeVisible();
  expect(new URL(pages.at(-1)!).searchParams.get("page")).toBe("2");
  await expect(list.getByRole("button",{name:"Trabajador 20",exact:true})).toHaveCount(0);
  const workerRow=list.getByRole("article").filter({hasText:"Trabajador 20"});
  await workerRow.hover();
  await workerRow.click({position:{x:8,y:8}});

  await expect(page.getByRole("region",{name:"Liquidación de Trabajador 20",exact:true})).toBeVisible();
  expect(details).toHaveLength(1);expect(new URL(details[0]).searchParams.get("employmentId")).toBe("contract-20");
  await page.getByRole("button",{name:"Volver a trabajadores"}).click();
  await expect(list.getByRole("button",{name:"Ver trabajador Trabajador 20",exact:true})).toBeVisible();expect(pages).toHaveLength(2);
  const bounds=await list.boundingBox();expect(bounds!.x+bounds!.width).toBeLessThanOrEqual(width);
  await page.screenshot({path:`test-results/payroll-novelty-page-${width}.png`,fullPage:true});
});

test("novedades conserva visibles los históricos sin identidad y permite abrir los resultados completos",async({page,baseURL})=>{
  await page.setViewportSize({width:390,height:844});
  const state=await setup(page,baseURL!);
  state.runs.push({payrollRunId:"complete-run",businessId,ruleSetId:null,runKind:"Regular",originalPayrollRunId:null,
    periodStart:"2026-08-01",periodEnd:"2026-08-31",paymentDate:"2026-08-31",status:"Calculated",employeeCount:1,
    totalEarnings:1000000,totalDeductions:0,netPayable:1000000,totalEmployerContributions:0,totalProvisions:0,calculationVersion:1,rowVersion:version,payableEmployees:[],
    employees:[{payrollRunEmployeeId:"complete-worker",employmentId:"complete-contract",partyId:"complete-party",employeeName:"Elena Histórica",workedDays:30,earnings:1000000,deductions:0,netPayable:1000000,employerContributions:0,provisions:0,lines:[]}]});
  await page.route("**/api/commerce/v1/payroll/calculated-employees?*",async route=>{
    const common={rowVersion:version,identification:null,contractNumber:null,periodStart:"2026-08-01",periodEnd:"2026-08-31",status:"Calculated",workedDays:30,earnings:1000000,deductions:0,netPayable:1000000};
    await route.fulfill({json:{items:[{...common,payrollRunEmployeeId:"missing",payrollRunId:"legacy",employmentId:"legacy",partyId:"legacy",employeeName:null},
      {...common,payrollRunEmployeeId:"complete-worker",payrollRunId:"complete-run",employmentId:"complete-contract",partyId:"complete-party",employeeName:"Elena Histórica"}],page:1,pageSize:20,totalCount:2}});
  });
  await page.goto("/dashboard/payroll?section=novelties");
  await selectNoveltyLiquidation(page,/2026-08-01 — 2026-08-31/);
  const list=page.getByRole("region",{name:"Trabajadores liquidados",exact:true});
  await expect(list.getByRole("article")).toHaveCount(2);
  const historical=list.getByRole("article").filter({hasText:"Identidad histórica no disponible"});
  await expect(historical.getByText("Identidad histórica no disponible",{exact:true})).toBeVisible();
  await expect(historical.getByRole("button",{name:"Identidad histórica no disponible",exact:true})).toHaveCount(0);
  for(const button of await historical.getByRole("button").all())await expect(button).toBeDisabled();
  await expect(page.getByText("Iniciando Auraly",{exact:true})).toBeHidden();
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);
  await page.screenshot({path:test.info().outputPath("missing-historical-identity-mobile.png"),fullPage:true});
  expect(state.reads.filter(read=>read.path.includes("/payroll/runs/"))).toHaveLength(0);
  await list.getByRole("button",{name:"Ver trabajador Elena Histórica",exact:true}).click();
  await expect(page.getByRole("region",{name:"Liquidación de Elena Histórica",exact:true})).toBeVisible();
  expect(state.reads.filter(read=>read.path.includes("/payroll/runs/"))).toHaveLength(1);
});

test("desprendibles conservan el resultado, separan valores y exportan el mismo lote",async({page,baseURL},testInfo)=>{
  await setup(page,baseURL!);
  await page.route("**/api/commerce/v1/payroll/reports/**",async route=>{
    if(route.request().url().endsWith("/definitions"))return route.fulfill({json:[{code:"payroll-receipt",name:"Desprendible laboral",description:"Resultado aprobado",dataset:"PayrollReceipt",columns:[],sortOrder:1}]});
    const rows=["Ana","Beatriz"].flatMap((employeeName,index)=>{
      const worker={employeeName,identification:String(index+123),payrollRunEmployeeId:`approved-${index}`,contractNumber:`C-${index}`,periodStart:"2026-08-01",periodEnd:"2026-08-31",paymentDate:"2026-08-31",monthlySalary:3000000,profileName:"Operativo",costCenterName:"Principal",healthName:"EPS de prueba",pensionName:"AFP de prueba",jobTitle:"Auxiliar",workedDays:30,earnings:3000000,deductions:120000,netPayable:2880000};
      return [{...worker,id:`salary-${index}`,conceptCode:"SAL",conceptName:"Salario",nature:"Earning",quantity:30,amount:3000000},
        {...worker,id:`health-${index}`,conceptCode:"SALUD",conceptName:"Salud empleado",nature:"Deduction",quantity:1,amount:120000},
        {...worker,id:`employer-${index}`,conceptCode:"APORTE",conceptName:"Aporte del empleador",nature:"EmployerContribution",quantity:1,amount:300000}];
    });
    await route.fulfill({json:{rows}});
  });
  await page.goto("/dashboard/payroll?section=reports");
  await page.getByRole("button",{name:"Generar reporte",exact:true}).click();
  const report=page.locator("#auraly-report-print-area");
  await expect(report.getByText("Ana · 123",{exact:true})).toBeVisible();
  await expect(report.getByText("Beatriz · 124",{exact:true})).toBeVisible();
  await expect(report.getByText("Aporte del empleador")).toHaveCount(0);
  await expect(report.getByText("Neto a pagar",{exact:true})).toHaveCount(2);
  await page.getByRole("checkbox",{name:"Incluir espacio para firma"}).check();
  await expect(report.getByText(/^Firma del trabajador:/)).toHaveCount(2);
  const pdf=page.waitForEvent("download");await page.getByRole("button",{name:"Descargar PDF",exact:true}).click();
  await (await pdf).saveAs(testInfo.outputPath("desprendibles.pdf"));
  const excel=page.waitForEvent("download");await page.getByRole("button",{name:"Exportar Excel",exact:true}).click();
  await (await excel).saveAs(testInfo.outputPath("desprendibles.xlsx"));
  await page.setViewportSize({width:390,height:844});
  expect(await report.evaluate(element=>element.scrollWidth<=element.clientWidth)).toBe(true);
  const salary=report.getByRole("row").filter({hasText:"SAL · Salario"}).first();
  const amount=salary.getByRole("cell").nth(3);
  expect(await amount.evaluate(element=>element.scrollWidth<=element.clientWidth)).toBe(true);
  await expect(amount).toHaveCSS("white-space","nowrap");
  await report.getByText("Ana · 123",{exact:true}).scrollIntoViewIfNeeded();
  await page.screenshot({path:testInfo.outputPath("desprendible-mobile.png"),fullPage:true});
});


for(const width of [1366,390])test(`novedades exige liquidación y conserva estado, trabajador y fecha de pago ${width}`,async({page,baseURL},info)=>{
  const state=await setup(page,baseURL!);
  await page.clock.install({time:new Date("2026-08-20T15:00:00Z")});
  await page.setViewportSize({width,height:844});
  const base:PayrollRun={payrollRunId:"draft-filter",businessId,ruleSetId:null,originalPayrollRunId:null,runKind:"Regular",periodStart:"2026-08-01",periodEnd:"2026-08-31",paymentDate:"2026-08-15",status:"Draft",employeeCount:0,totalEarnings:0,totalDeductions:0,netPayable:0,totalEmployerContributions:0,totalProvisions:0,calculationVersion:0,rowVersion:version,payableEmployees:[],employees:[],payFrequencyName:"Mensual"};
  state.runs.push(base,{...base,payrollRunId:"calculated-filter",status:"Calculated",paymentDate:"2026-08-31",employeeCount:1,employees:[{payrollRunEmployeeId:"calc-worker",employmentId:"employment",partyId:"party",employeeName:"Elena Filtro",workedDays:30,earnings:1000000,deductions:0,netPayable:1000000,employerContributions:0,provisions:0,lines:[]}]},{...base,payrollRunId:"approved-filter",status:"Approved",paymentDate:"2026-08-31"});
  const requests:URL[]=[];page.on("request",request=>{if(request.url().includes("/payroll/runs?"))requests.push(new URL(request.url()));});
  await page.goto("/dashboard/payroll?section=novelties");
  const history=page.getByRole("region",{name:"Liquidaciones para novedades",exact:true});
  await expect(history.getByText("Selecciona una liquidación para ver sus trabajadores.",{exact:true})).toBeVisible();
  expect(requests).toHaveLength(0);
  expect(state.reads.filter(read=>read.path.endsWith("/calculated-employees"))).toHaveLength(0);
  await history.getByRole("combobox",{name:"Buscar liquidación",exact:true}).click();
  await page.getByRole("option").filter({hasText:"Pendiente de cálculo"}).click();
  await expect(page.getByText("Esta liquidación está en borrador. Calcula primero sus trabajadores para editar sus novedades.",{exact:true})).toBeVisible();
  expect(state.reads.filter(read=>read.path.endsWith("/calculated-employees"))).toHaveLength(0);
  await page.getByRole("button",{name:"Preparar liquidación",exact:true}).click();
  await expect(page).toHaveURL(/section=runs/);
  const detail=page.getByRole("dialog",{name:"Detalle de liquidación",exact:true});await expect(detail.getByText("Periodicidad: Mensual",{exact:true})).toBeVisible();
  const frequency=await detail.getByText("Periodicidad: Mensual",{exact:true}).boundingBox();
  const refresh=await detail.getByRole("button",{name:"Actualizar liquidación",exact:true}).boundingBox();
  expect(Math.abs(frequency!.y+frequency!.height/2-refresh!.y-refresh!.height/2)).toBeLessThanOrEqual(2);
  await page.screenshot({path:info.outputPath(`compact-run-detail-${width}.png`)});
  await detail.getByRole("button",{name:"Cerrar ventana",exact:true}).click();
  await page.getByRole("tab",{name:"Novedades",exact:true}).click();
  await choose(page,"Estado de liquidación","Calculada");
  const beforeFilters=requests.length;
  await history.locator("label").filter({hasText:"Pago programado desde"}).getByRole("button").click();
  await page.getByRole("button",{name:"16 de agosto de 2026",exact:true}).click();
  expect(requests).toHaveLength(beforeFilters);
  await history.getByRole("textbox",{name:"Buscar trabajador en liquidaciones",exact:true}).fill("Elena");
  await history.getByRole("button",{name:"Buscar",exact:true}).click();expect(requests).toHaveLength(beforeFilters);
  await selectNoveltyLiquidation(page,/2026-08-01 — 2026-08-31/);
  expect(requests.at(-1)?.searchParams.get("status")).toBe("Calculated");
  expect(requests.at(-1)?.searchParams.get("paymentFrom")).toBe("2026-08-16");
  expect(requests.at(-1)?.searchParams.get("workerSearch")).toBe("Elena");
  await expect(page.getByRole("button",{name:"Ver trabajador Elena Filtro",exact:true})).toBeVisible();
  const workerRequest=page.waitForRequest(request=>request.url().includes("/payroll/runs/calculated-filter")&&!request.url().includes("/employments"));
  await page.getByRole("button",{name:"Ver trabajador Elena Filtro",exact:true}).click();await workerRequest;
  await expect(page.getByRole("region",{name:"Liquidación de Elena Filtro",exact:true})).toBeVisible();
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
  await page.getByRole("button",{name:"Volver a trabajadores",exact:true}).click();await page.getByRole("button",{name:"Quitar selección de Buscar liquidación",exact:true}).click();
  await history.getByRole("button",{name:"Limpiar",exact:true}).click();
  await history.getByRole("combobox",{name:"Buscar liquidación",exact:true}).click();
  await page.getByRole("option").filter({hasText:/2026-08-01/}).last().click();
  await expect(page.getByText("Aprobada",{exact:true})).toBeVisible();
  await expect(page.getByRole("button",{name:"Preparar liquidación",exact:true})).toHaveCount(0);
  await page.getByRole("button",{name:"Quitar selección de Buscar liquidación",exact:true}).click();
  await page.screenshot({path:info.outputPath(`novelties-filters-${width}.png`),fullPage:true});
});

for(const width of [1366,390])test(`pagos descarga solo el desprendible contabilizado del trabajador y pagina a ${width}`,async({page,baseURL},info)=>{
  const state=await setup(page,baseURL!);
  await page.setViewportSize({width,height:844});
  state.options.paymentBatches.push({paymentBatchId:"paid-batch",payrollRunId:"approved-run",paymentDate:"2026-08-31",paymentMethodOptionId:"payroll-payment-method-Cash",paymentMethodName:"Caja nómina",referenceNumber:"PAGO-APROBADO",status:"Confirmed",employeeCount:21,totalAmount:21000000,rowVersion:version,remainingEmployees:[],accountingStatus:"Posted",isPaid:true});
  const roster:URL[]=[],receipts:URL[]=[];
  await page.route("**/payroll/payments/paid-batch/employees?**",async route=>{
    const url=new URL(route.request().url());roster.push(url);
    const pageNumber=Number(url.searchParams.get("page")),pageSize=Number(url.searchParams.get("pageSize"));
    const items=Array.from({length:21},(_,index)=>({payrollRunEmployeeId:`paid-result-${index}`,partyId:`paid-party-${index}`,employeeName:`Trabajadora ${index}`,identification:String(1000+index),periodStart:"2026-08-01",periodEnd:"2026-08-31",paymentDate:"2026-08-31",amount:1000000,isPaid:index!==1}));
    await route.fulfill({json:{items:items.slice((pageNumber-1)*pageSize,pageNumber*pageSize),page:pageNumber,pageSize,totalCount:21,totalPages:Math.ceil(21/pageSize)}});
  });
  await page.route("**/payroll/reports/payroll-receipt?**",async route=>{
    const url=new URL(route.request().url());receipts.push(url);
    const header={payrollRunEmployeeId:url.searchParams.get("employeeResultId"),partyId:url.searchParams.get("partyId"),employeeName:"Trabajadora 20",identification:"1020",contractNumber:"CONTRATO-20",periodStart:"2026-08-01",periodEnd:"2026-08-31",paymentDate:"2026-08-31",paidDate:"2026-08-31",monthlySalary:1150000,workedDays:28,earnings:1100000,deductions:100000,netPayable:1000000,profileName:"Operativo",costCenterName:"Principal",healthName:"EPS de prueba",pensionName:"AFP de prueba",jobTitle:"Auxiliar administrativa"};
    const lines=[
      {id:"salary",conceptCode:"SAL",conceptName:"Salario básico",nature:"Earning",quantity:28,amount:1100000},
      {id:"absence",conceptCode:"AUS",conceptName:"Ausencia",nature:"Deduction",quantity:2,amount:0},
      {id:"health",conceptCode:"SALUD",conceptName:"Salud trabajador",nature:"Deduction",quantity:1,percentageRate:0.04,amount:44000},
      {id:"pension",conceptCode:"PEN",conceptName:"Pensión trabajador",nature:"Deduction",quantity:1,percentageRate:0.04,amount:44000},
      {id:"other",conceptCode:"OTR",conceptName:"Otra deducción",nature:"Deduction",quantity:1,amount:12000},
    ];
    await route.fulfill({json:{from:header.periodStart,to:header.periodEnd,rows:lines.map(line=>({...header,...line}))}});
  });
  await page.goto("/dashboard/payroll?section=payments");
  const open=page.getByRole("button",{name:"Ver desprendibles del pago PAGO-APROBADO",exact:true});await expect(open).toBeVisible();expect(roster).toHaveLength(0);expect(receipts).toHaveLength(0);
  await open.click();const dialog=page.getByRole("dialog",{name:"Desprendibles del pago",exact:true});
  await expect(dialog.getByRole("button",{name:"Descargar desprendible de Trabajadora 1",exact:true})).toBeDisabled();
  await expect(dialog.getByRole("button",{name:/^Descargar desprendible de/})).toHaveCount(20);expect(roster).toHaveLength(1);expect(receipts).toHaveLength(0);
  expect(await dialog.evaluate(element=>element.scrollWidth<=element.clientWidth)).toBe(true);
  await dialog.getByRole("button",{name:"Siguiente página",exact:true}).click();
  const downloadButton=dialog.getByRole("button",{name:"Descargar desprendible de Trabajadora 20",exact:true});await expect(downloadButton).toBeVisible();expect(roster).toHaveLength(2);
  const downloaded=page.waitForEvent("download");await downloadButton.click();
  const pdf=await downloaded;expect(pdf.suggestedFilename()).toMatch(/desprendible.*\.pdf$/);await pdf.saveAs(info.outputPath(`paid-worker-${width}.pdf`));
  expect(receipts).toHaveLength(1);expect(receipts[0].searchParams.get("employeeResultId")).toBe("paid-result-20");expect(receipts[0].searchParams.get("partyId")).toBe("paid-party-20");expect(roster).toHaveLength(2);
  expect(state.writes).toHaveLength(0);await page.screenshot({path:info.outputPath(`paid-worker-${width}.png`)});
});

test("pagos no descarga un resultado que no coincide con el pago contabilizado",async({page,baseURL})=>{
  const state=await setup(page,baseURL!);
  state.options.paymentBatches.push({paymentBatchId:"guard-batch",payrollRunId:"approved-run",paymentDate:"2026-08-31",paymentMethodOptionId:"payroll-payment-method-Cash",paymentMethodName:"Caja nómina",referenceNumber:"PAGO-GUARD",status:"Confirmed",employeeCount:1,totalAmount:1000000,rowVersion:version,remainingEmployees:[],accountingStatus:"Posted",isPaid:true});
  await page.route("**/payroll/payments/guard-batch/employees?**",route=>route.fulfill({json:{items:[{payrollRunEmployeeId:"expected-result",partyId:"expected-party",employeeName:"Trabajadora",identification:"123",periodStart:"2026-08-01",periodEnd:"2026-08-31",paymentDate:"2026-08-31",amount:1000000,isPaid:true}],page:1,pageSize:20,totalCount:1,totalPages:1}}));
  await page.route("**/payroll/reports/payroll-receipt?**",route=>route.fulfill({json:{rows:[{payrollRunEmployeeId:"another-result",paidDate:"2026-08-31",netPayable:1000000}]}}));
  const downloads:string[]=[];page.on("download",download=>downloads.push(download.suggestedFilename()));
  await page.goto("/dashboard/payroll?section=payments");await page.getByRole("button",{name:"Ver desprendibles del pago PAGO-GUARD",exact:true}).click();
  const dialog=page.getByRole("dialog",{name:"Desprendibles del pago",exact:true});await dialog.getByRole("button",{name:"Descargar desprendible de Trabajadora",exact:true}).click();
  await expect(dialog.getByRole("alert")).toHaveText("El pago no tiene un resultado contabilizado coincidente. Actualiza los pagos antes de descargar el desprendible.");expect(downloads).toHaveLength(0);expect(state.writes).toHaveLength(0);
});

test("autorizaciones tiene lista propia y abre evidencia sin consultas por fila",async({page,baseURL})=>{
  const state=await setup(page,baseURL!);
  state.options.deductionAgreements.push({deductionAgreementId:"evidence",employmentId:"employment",employeeName:"Elena",conceptId:"loan",conceptName:"Préstamo",authorityOptionId:"authority",authorityName:"Autorización escrita",beneficiaryPartyId:null,referenceNumber:"AUT-EVIDENCE",evidenceUrl:"https://evidence.example.test/soporte",effectiveFrom:"2026-01-01",effectiveTo:null,authorizedTotal:100000,installmentAmount:10000,deductedToDate:0,priority:1,mustProtectMinimumNetPay:true,isActive:true,rowVersion:version});
  await page.goto("/dashboard/payroll?section=novelties");
  expect(state.reads.filter(read=>read.path.endsWith("/deduction-agreements"))).toHaveLength(0);
  await page.getByRole("tab",{name:"Autorizaciones",exact:true}).click();
  const link=page.getByRole("link",{name:"Ver evidencia",exact:true});await expect(link).toHaveAttribute("href","https://evidence.example.test/soporte");await expect(link).toHaveAttribute("target","_blank");
  await page.context().route("https://evidence.example.test/**",route=>route.fulfill({body:"Soporte de prueba"}));
  const popupPromise=page.waitForEvent("popup");await link.click();const popup=await popupPromise;await popup.waitForLoadState();expect(popup.url()).toBe("https://evidence.example.test/soporte");await popup.close();
  expect(state.reads.filter(read=>read.path.endsWith("/deduction-agreements"))).toHaveLength(1);
});
