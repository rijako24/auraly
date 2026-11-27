import { expect, test, type Page } from "@playwright/test";
import { financialVoucherScenario, installFinancialVoucherContext } from "./support/financial-voucher";

const id=(number:number)=>`cdcdcdcd-1111-4222-8333-${String(number).padStart(12,"0")}`;
const options={catalogs:{"payroll-concept-nature":[{optionId:id(900),code:"Earning",label:"Devengado",isActive:true},{optionId:id(901),code:"Deduction",label:"Deducido",isActive:true}]},
    concepts:[],employments:[],parties:[],ruleSets:[],settings:null,electronicConfiguration:null,fiscalIssuers:[],deductionAgreements:[],novelties:[],paymentBatches:[],electronicPeriods:[]};
function worker(number:number,name:string,days:number){return{payrollRunEmployeeId:id(number+100),employmentId:id(number),partyId:id(number+200),employeeName:name,
    identification:String(number),contractNumber:`N-${number}`,workedDays:days,baseWorkedDays:30,maximumContractDays:30,periodDays:30,
    earnings:days*50000,deductions:days*4000,employerContributions:0,provisions:0,netPayable:days*46000,
    conceptWorkedDays:[{conceptId:id(500),baseDays:30,workedDays:days}],
    lines:[{lineNumber:1,conceptId:id(500),conceptCode:"SALARY",conceptName:"Salario",natureCode:"Earning",quantity:days,baseAmount:1500000,rate:null,amount:days*50000,isEmployerCost:false,isSalaryBase:true}]};}
function run(businessId:string){return{payrollRunId:id(1),businessId,ruleSetId:id(2),runKind:"Regular",periodStart:"2026-03-01",periodEnd:"2026-03-31",paymentDate:"2026-03-31",
    status:"Calculated",calculationVersion:1,employeeCount:2,totalEarnings:2900000,totalDeductions:232000,totalEmployerContributions:0,totalProvisions:0,netPayable:2668000,
    rowVersion:"AAAAAAAAAAE=",employees:[worker(10,"Ana días",28),worker(11,"Bruno completo",30)],payableEmployees:[]};}
function calculatedPage(...runs:ReturnType<typeof run>[]){
    const items=runs.filter(value=>value.status!=="Draft").flatMap(value=>value.employees.map(employee=>({...employee,
        payrollRunId:value.payrollRunId,periodStart:value.periodStart,periodEnd:value.periodEnd,status:value.status,rowVersion:value.rowVersion})));
    return{items,page:1,pageSize:20,totalCount:items.length,totalPages:items.length?1:0};
}
function scopedDetail(value:ReturnType<typeof run>,requestUrl:string){
    const employment=new URL(requestUrl).searchParams.get("employmentId");
    return{...value,isPartialCalculation:Boolean(employment),employees:employment?value.employees.filter(employee=>employee.employmentId===employment):value.employees};
}
async function openWorker(page:Page){
    await page.goto("/dashboard/payroll?section=novelties");
    await page.getByRole("combobox",{name:"Buscar liquidación",exact:true}).click();
    await page.getByRole("option",{name:/2026-03-01 — 2026-03-31/}).click();
    await page.getByRole("article").filter({hasText:"2026-03-01 — 2026-03-31"}).getByRole("button",{name:"Ver trabajador Ana días",exact:true}).click();
}

test("novedades: recálculo individual conserva compañeros, evita refetch y bloquea contabilización en curso",async({page,baseURL},testInfo)=>{
    const scenario=financialVoucherScenario();scenario.user.permissions.push("payroll.read","payroll.calculate","payroll.approve");
    await installFinancialVoucherContext(page,baseURL!,scenario);
    let current=run(scenario.businessId);
    let optionsReads=0,runsReads=0,detailReads=0,candidateReads=0,calculatedReads=0,calculations=0,fail=true;
    let release:()=>void=()=>{};const released=new Promise<void>(resolve=>{release=resolve;});
    await page.route("**/api/commerce/v1/payroll/**",async route=>{
        const path=new URL(route.request().url()).pathname;
        if(path.endsWith("/deduction-agreements")){await route.fulfill({json:{items:[],page:1,pageSize:20,totalCount:0,totalPages:0}});return;}
        if(path.endsWith("/options")){optionsReads++;await route.fulfill({json:options});return;}
        if(path.endsWith("/runs")){runsReads++;await route.fulfill({json:{items:[current],page:1,pageSize:20,totalCount:1,totalPages:1}});return;}
        if(path.endsWith("/calculated-employees")){calculatedReads++;await route.fulfill({json:calculatedPage(current)});return;}
        if(path.endsWith(`/runs/${id(1)}`)){detailReads++;await route.fulfill({json:scopedDetail(current,route.request().url())});return;}
        if(path.endsWith("/employments")){
            candidateReads++;await route.fulfill({json:{page:1,pageSize:50,totalCount:2,eligibleCount:2,selectedCount:2,rowVersion:current.rowVersion,
                items:current.employees.map(employee=>({...employee,startDate:"2026-03-01",endDate:null,payFrequencyName:"Mensual",isSelected:true}))}});return;
        }
        if(path.endsWith("/calculate")){
            calculations++;
            expect(route.request().postDataJSON()).toEqual({rowVersion:"AAAAAAAAAAE=",workedDaysChanges:[{employmentId:id(10),conceptId:id(500),workedDays:25}]});
            if(fail){fail=false;await route.fulfill({status:503,json:{detail:"Días temporalmente no disponibles"}});return;}
            await released;
            const updated={...worker(10,"Ana días",25),baseWorkedDays:27,conceptWorkedDays:[{conceptId:id(500),baseDays:27,workedDays:25}],payrollRunEmployeeId:id(999)};
            current={...current,employees:[updated,current.employees[1]],rowVersion:"AAAAAAAAAAI=",totalEarnings:2750000,totalDeductions:220000,netPayable:2530000,calculationVersion:2};
            await route.fulfill({json:{...current,isPartialCalculation:true,employees:[updated]}});return;
        }
        throw new Error(`Unexpected request ${path}`);
    });
    await openWorker(page);
    const editor=page.getByRole("region",{name:"Liquidación de Ana días",exact:true});
    await expect(editor.getByRole("button",{name:"Recalcular trabajador",exact:true})).toHaveCount(0);
    await expect(editor.getByRole("button",{name:"Reintentar cálculo",exact:true})).toHaveCount(0);
    await editor.getByRole("button",{name:"Editar días de Salario",exact:true}).click();
    const days=editor.getByRole("spinbutton",{name:"Días trabajados de Salario",exact:true});
    await expect(days).toHaveValue("28");await expect(days).toHaveAttribute("max","28");
    await days.fill("");await editor.getByRole("button",{name:"Guardar días de Salario",exact:true}).click();
    await expect(days).toBeFocused();expect(calculations).toBe(0);
    await days.fill("25");await editor.getByRole("button",{name:"Guardar días de Salario",exact:true}).click();
    await expect(editor.getByRole("alert")).toHaveText("Días temporalmente no disponibles");await expect(days).toHaveValue("25");
    await editor.getByRole("button",{name:"Guardar días de Salario",exact:true}).click();
    await expect(days).toBeDisabled();await expect(page.getByRole("button",{name:"Volver a trabajadores",exact:true})).toBeDisabled();
    await expect(page.getByRole("button",{name:"Actualizar nómina",exact:true})).toBeDisabled();
    await page.getByRole("tab",{name:"Liquidaciones",exact:true}).click();
    const period=page.getByRole("button",{name:/2026-03-01 — 2026-03-31/});
    await expect(period).toBeDisabled();
    release();
    await period.click();
    const selection=page.getByRole("region",{name:"Trabajadores de la liquidación",exact:true});
    await expect(selection.locator("tbody").getByRole("checkbox")).toHaveCount(2);
    const readsDuringSave=candidateReads;
    await expect(page.locator("summary").filter({hasText:"Ana días"})).toContainText(/1[.]150[.]000/);
    await expect(page.locator("summary").filter({hasText:"Bruno completo"})).toContainText(/1[.]380[.]000/);
    await expect(page.getByRole("button",{name:"Aprobar y contabilizar",exact:true})).toBeEnabled();
    await page.getByRole("button",{name:"Ana días",exact:true}).click();
    await expect(page.getByRole("tab",{name:"Novedades",exact:true})).toHaveAttribute("aria-selected","true");
    await editor.getByRole("button",{name:"Editar días de Salario",exact:true}).click();await expect(days).toHaveValue("25");
    await expect(editor.getByText(/Ausencias registradas: 2/)).toBeVisible();
    await page.getByRole("button",{name:"Volver a trabajadores",exact:true}).click();
    await page.getByRole("button",{name:"Ver trabajador Bruno completo",exact:true}).click();
    await page.getByRole("button",{name:"Editar días de Salario",exact:true}).click();
    await expect(page.getByRole("region",{name:"Liquidación de Bruno completo",exact:true}).getByRole("spinbutton")).toHaveValue("30");
    await page.getByRole("button",{name:"Volver a trabajadores",exact:true}).click();
    await page.getByRole("button",{name:"Ver trabajador Ana días",exact:true}).click();
    await editor.getByRole("button",{name:"Editar días de Salario",exact:true}).click();
    await expect(days).toHaveValue("25");
    await expect(editor.getByText(/Ausencias registradas: 2/)).toBeVisible();
    await editor.getByRole("heading",{name:"Ana días",exact:true}).scrollIntoViewIfNeeded();
    await page.screenshot({path:testInfo.outputPath("novedades-dias.png")});
    expect(optionsReads).toBe(1);expect(runsReads).toBe(2);expect(detailReads).toBeGreaterThanOrEqual(3);expect(calculatedReads).toBe(1);expect(calculations).toBe(2);
    expect(candidateReads).toBe(readsDuringSave);
});

test("días por concepto: transporte conserva salario y deducciones sin lecturas adicionales",async({page,baseURL})=>{
    const scenario=financialVoucherScenario();scenario.user.permissions.push("payroll.read","payroll.calculate");
    await installFinancialVoucherContext(page,baseURL!,scenario);
    const salary=worker(10,"Ana días",30);
    const transport={...salary.lines[0],lineNumber:2,conceptId:id(501),conceptCode:"TRANSPORT",conceptName:"Auxilio de transporte",quantity:30,amount:162000};
    let current={...run(scenario.businessId),employeeCount:1,totalEarnings:1662000,totalDeductions:120000,netPayable:1542000,employees:[{...salary,earnings:1662000,netPayable:1542000,
        conceptWorkedDays:[...salary.conceptWorkedDays,{conceptId:id(501),baseDays:30,workedDays:30}],lines:[...salary.lines,transport]}]};
    let reads=0,calculations=0;
    await page.route("**/api/commerce/v1/payroll/**",async route=>{
        const path=new URL(route.request().url()).pathname;
        if(path.endsWith("/deduction-agreements")){await route.fulfill({json:{items:[],page:1,pageSize:20,totalCount:0,totalPages:0}});return;}
        if(path.endsWith("/options")){reads++;await route.fulfill({json:options});return;}
        if(path.endsWith("/runs")){reads++;await route.fulfill({json:{items:[current],page:1,pageSize:20,totalCount:1,totalPages:1}});return;}
        if(path.endsWith("/calculated-employees")){reads++;await route.fulfill({json:calculatedPage(current)});return;}
        if(path.endsWith(`/runs/${id(1)}`)){reads++;await route.fulfill({json:scopedDetail(current,route.request().url())});return;}
        if(path.endsWith("/calculate")){
            calculations++;
            expect(route.request().postDataJSON()).toEqual({rowVersion:current.rowVersion,
                workedDaysChanges:[{employmentId:id(10),conceptId:id(501),workedDays:25}]});
            const updated={...current.employees[0],payrollRunEmployeeId:id(999),earnings:1635000,netPayable:1515000,
                conceptWorkedDays:[salary.conceptWorkedDays[0],{conceptId:id(501),baseDays:25,workedDays:25}],
                lines:[salary.lines[0],{...transport,quantity:25,amount:135000}]};
            current={...current,rowVersion:"AAAAAAAAAAI=",totalEarnings:1635000,netPayable:1515000,employees:[updated]};
            await route.fulfill({json:{...current,isPartialCalculation:true}});return;
        }
        throw new Error(`Unexpected request ${path}`);
    });
    await openWorker(page);
    const editor=page.getByRole("region",{name:"Liquidación de Ana días",exact:true});
    await expect(editor.getByRole("spinbutton")).toHaveCount(0);
    await editor.getByRole("button",{name:"Editar días de Auxilio de transporte",exact:true}).click();
    const days=editor.getByRole("spinbutton",{name:"Días trabajados de Auxilio de transporte",exact:true});
    await days.press("ArrowDown");
    await expect(days).toHaveValue("29");
    await days.fill("25.5");
    await editor.getByRole("button",{name:"Guardar días de Auxilio de transporte",exact:true}).click();
    expect(calculations).toBe(0);
    await editor.getByRole("spinbutton",{name:"Días trabajados de Auxilio de transporte",exact:true}).fill("25");
    await editor.getByRole("button",{name:"Guardar días de Auxilio de transporte",exact:true}).click();
    await expect(editor.getByRole("spinbutton")).toHaveValue("25");
    await expect(editor.getByRole("row").filter({hasText:"SALARY · Salario"})).toContainText("30");
    await editor.getByRole("button",{name:"Cancelar edición de días",exact:true}).click();
    await editor.getByRole("button",{name:"Editar días de Salario",exact:true}).click();
    await expect(editor.getByRole("spinbutton")).toHaveValue("30");
    expect(reads).toBe(4);expect(calculations).toBe(1);
});

test("novedades: lectura histórica no inventa entradas editables",async({page,baseURL})=>{
    const scenario=financialVoucherScenario();scenario.user.permissions.push("payroll.read");
    await installFinancialVoucherContext(page,baseURL!,scenario);
    const current=run(scenario.businessId);current.status="Approved";
    await page.route("**/api/commerce/v1/payroll/**",async route=>{
        expect(route.request().method()).toBe("GET");const path=new URL(route.request().url()).pathname;
        if(path.endsWith("/deduction-agreements")){await route.fulfill({json:{items:[],page:1,pageSize:20,totalCount:0,totalPages:0}});return;}
        if(path.endsWith("/options")){await route.fulfill({json:options});return;}
        if(path.endsWith("/runs")){await route.fulfill({json:{items:[current],page:1,pageSize:20,totalCount:1,totalPages:1}});return;}
        if(path.endsWith("/calculated-employees")){await route.fulfill({json:calculatedPage(current)});return;}
        if(path.endsWith(`/runs/${id(1)}`)){const detail=scopedDetail(current,route.request().url());await route.fulfill({json:{...detail,employees:detail.employees.map(employee=>({...employee,baseWorkedDays:null,maximumContractDays:null,periodDays:null}))}});return;}
        throw new Error(`Unexpected request ${path}`);
    });
    await openWorker(page);
    const editor=page.getByRole("region",{name:"Liquidación de Ana días",exact:true});
    await expect(editor.getByRole("spinbutton")).toHaveCount(0);
    await expect(editor.getByText("Los días de este cálculo se conservan en lectura.",{exact:true})).toBeVisible();
    await expect(editor.getByRole("button",{name:/Editar días/})).toHaveCount(0);
});

test("novedades lista solo los trabajadores de la liquidación elegida sin consultar por trabajador",async({page,baseURL},testInfo)=>{
    const scenario=financialVoucherScenario();scenario.user.permissions.push("payroll.read","payroll.calculate");
    await installFinancialVoucherContext(page,baseURL!,scenario);
    const current=run(scenario.businessId);
    current.employees.push(worker(12,"Clara completa",30));current.employeeCount=3;
    const draft={...current,payrollRunId:id(3),status:"Draft",periodStart:"2026-12-01",periodEnd:"2026-12-15",employees:[],employeeCount:0};
    let optionsReads=0,runsReads=0,detailReads=0,calculatedReads=0;
    await page.route("**/api/commerce/v1/payroll/**",async route=>{
        expect(route.request().method()).toBe("GET");
        const path=new URL(route.request().url()).pathname;
        if(path.endsWith("/deduction-agreements")){await route.fulfill({json:{items:[],page:1,pageSize:20,totalCount:0,totalPages:0}});return;}
        if(path.endsWith("/options")){optionsReads++;await route.fulfill({json:options});return;}
        if(path.endsWith("/runs")){runsReads++;await route.fulfill({json:{items:[draft,current],page:1,pageSize:20,totalCount:2,totalPages:1}});return;}
        if(path.endsWith("/calculated-employees")){calculatedReads++;await route.fulfill({json:calculatedPage(draft,current)});return;}
        if(path.endsWith(`/runs/${id(1)}`)){detailReads++;await route.fulfill({json:scopedDetail(current,route.request().url())});return;}
        throw new Error(`Unexpected request ${path}`);
    });
    await page.goto("/dashboard/payroll?section=novelties");
    const list=page.getByRole("region",{name:"Trabajadores liquidados",exact:true});
    await expect(list).toHaveCount(0);
    expect(calculatedReads).toBe(0);
    await page.getByRole("combobox",{name:"Buscar liquidación",exact:true}).click();
    await page.getByRole("option",{name:/2026-03-01 — 2026-03-31/}).click();
    await expect(list.getByRole("article")).toHaveCount(3);
    await expect(list.getByText(/2026-12-01/)).toHaveCount(0);
    await expect(list.getByText(/Contrato N-12/)).toBeVisible();
    expect(detailReads).toBe(0);expect(runsReads).toBe(1);
    await page.screenshot({path:testInfo.outputPath("novedades-trabajadores.png")});
    await list.getByRole("button",{name:"Ver trabajador Clara completa",exact:true}).click();
    await expect(list).toHaveCount(0);
    await expect(page.getByRole("region",{name:"Liquidación de Clara completa",exact:true})).toBeVisible();
    await page.getByRole("button",{name:"Volver a trabajadores",exact:true}).click();
    await expect(list.getByRole("article")).toHaveCount(3);
    expect(optionsReads).toBe(1);expect(runsReads).toBe(1);expect(detailReads).toBe(1);expect(calculatedReads).toBe(1);
});

test("cambiar liquidación desde Liquidaciones cierra la captura del período anterior",async({page,baseURL})=>{
    const scenario=financialVoucherScenario();scenario.user.permissions.push("payroll.read","payroll.calculate","payroll.manage");
    await installFinancialVoucherContext(page,baseURL!,scenario);
    const first=run(scenario.businessId),second={...first,payrollRunId:id(4),periodStart:"2026-06-01",periodEnd:"2026-06-30"};
    await page.route("**/api/commerce/v1/payroll/**",async route=>{
        expect(route.request().method()).toBe("GET");const path=new URL(route.request().url()).pathname;
        if(path.endsWith("/deduction-agreements")){await route.fulfill({json:{items:[],page:1,pageSize:20,totalCount:0,totalPages:0}});return;}
        if(path.endsWith("/options")){await route.fulfill({json:options});return;}
        if(path.endsWith("/runs")){await route.fulfill({json:{items:[first,second],page:1,pageSize:20,totalCount:2,totalPages:1}});return;}
        if(path.endsWith("/calculated-employees")){await route.fulfill({json:calculatedPage(first,second)});return;}
        if(path.endsWith(`/runs/${id(1)}`)){await route.fulfill({json:scopedDetail(first,route.request().url())});return;}
        if(path.endsWith(`/runs/${id(4)}`)){await route.fulfill({json:scopedDetail(second,route.request().url())});return;}
        if(path.endsWith("/employments")){await route.fulfill({json:{items:[],page:1,pageSize:50,totalCount:0,eligibleCount:0,selectedCount:0,rowVersion:first.rowVersion}});return;}
        throw new Error(`Unexpected request ${path}`);
    });
    await openWorker(page);
    await page.getByRole("button",{name:"Agregar novedad",exact:true}).click();
    await page.getByRole("textbox",{name:"Notas",exact:true}).fill("Entrada sin guardar de marzo");
    await page.getByRole("tab",{name:"Liquidaciones",exact:true}).click();
    await page.getByRole("button",{name:/2026-06-01 — 2026-06-30/}).click();
    await page.getByRole("dialog",{name:"Detalle de liquidación",exact:true}).getByRole("button",{name:"Cerrar ventana",exact:true}).click();
    await page.getByRole("tab",{name:"Novedades",exact:true}).click();
    await page.getByRole("combobox",{name:"Buscar liquidación",exact:true}).click();
    await page.getByRole("option",{name:/2026-06-01 — 2026-06-30/}).click();
    await page.getByRole("article").filter({hasText:"2026-06-01 — 2026-06-30"}).getByRole("button",{name:"Ver trabajador Ana días",exact:true}).click();
    const editor=page.getByRole("region",{name:"Edición de novedad",exact:true});
    await expect(editor).toHaveCount(0);
    await page.getByRole("button",{name:"Agregar novedad",exact:true}).click();
    await expect(editor.getByRole("textbox",{name:"Notas",exact:true})).toHaveValue("");
    await expect(editor.getByRole("button",{name:"Inicio",exact:true})).toContainText("1 de junio de 2026");
});
