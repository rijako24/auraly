import { expect, test } from "@playwright/test";
import { financialVoucherScenario, installFinancialVoucherContext } from "./support/financial-voucher";

const id = (value:number) => `bcbcbcbc-1111-4222-8333-${String(value).padStart(12,"0")}`;

for(const viewport of [{width:1440,height:1000},{width:1366,height:768},{width:390,height:844}])
test(`selección paginada: teclado, búsqueda, reintento y cálculo sin refetch (${viewport.width}px)`, async ({page,baseURL},testInfo) => {
    await page.setViewportSize(viewport);
    const scenario = financialVoucherScenario();
    scenario.user.permissions.push("payroll.read","payroll.calculate","payroll.approve");
    await installFinancialVoucherContext(page,baseURL!,scenario);
    let run = {payrollRunId:id(1),businessId:scenario.businessId,runKind:"Regular",periodStart:"2026-01-01",periodEnd:"2026-01-31",paymentDate:"2026-01-31",
        status:"Calculated",calculationVersion:1,employeeCount:0,totalEarnings:0,totalDeductions:0,totalEmployerContributions:0,totalProvisions:0,netPayable:0,
        employees:[] as Array<Record<string,unknown>>,payableEmployees:[],rowVersion:"AAAAAAAAAAE="};
    const workers = Array.from({length:51},(_,index)=>({employmentId:id(index+100),employeeName:`Trabajador ${String(index).padStart(2,"0")}`,
        identification:String(index),contractNumber:`C-${index}`,startDate:"2026-01-01",endDate:null,payFrequencyName:"Mensual",isSelected:true,
        costCenterId:index<2?id(950):null,workSite:index===0?"Zona_%[A]~ Norte":"Zona otra Norte"}));
    let optionsReads=0,runsReads=0,detailReads=0,selectionReads=0,calculations=0,failPage=true,failCalculation=true;
    let centerReads=0;
    await page.route("**/api/commerce/v1/accounting/cost-centers?**",async route=>{centerReads++;await route.fulfill({json:{
        items:[{costCenterId:id(950),code:"CC",name:"Centro de selección",isActive:true,isDefault:false}],page:1,pageSize:20,totalCount:1,totalPages:1}});});
    let release:()=>void=()=>{};
    const released = new Promise<void>(resolve=>{release=resolve;});
    await page.route("**/api/commerce/v1/payroll/**",async route=>{
        const url=new URL(route.request().url()),path=url.pathname;
        if(path.endsWith("/options")){optionsReads++;await route.fulfill({json:{catalogs:{},concepts:[],employments:[],parties:[],ruleSets:[],settings:null,
            electronicConfiguration:null,fiscalIssuers:[],deductionAgreements:[],novelties:[],paymentBatches:[],electronicPeriods:[]}});return;}
        if(path.endsWith("/runs")){runsReads++;await route.fulfill({json:{items:[run],page:1,pageSize:20,totalCount:1,totalPages:1}});return;}
        if(path.endsWith(`/runs/${id(1)}`)){detailReads++;await route.fulfill({json:run});return;}
        if(path.endsWith("/employments")){
            expect(run.status).not.toBe("Approved");
            selectionReads++;
            if(failPage){await route.fulfill({status:503,json:{detail:"No disponible"}});return;}
            const number=Number(url.searchParams.get("page")),search=url.searchParams.get("search");
            const contract=url.searchParams.get("contractNumber");
            const center=url.searchParams.get("costCenterId"),site=url.searchParams.get("workSite");
            const filtered=workers.filter(worker=>(!search||worker.employeeName.includes(search))&&(!contract||worker.contractNumber.includes(contract))
                &&(!center||worker.costCenterId===center)&&(!site||worker.workSite.includes(site)));
            if(url.searchParams.get("sortDirection")==="desc")filtered.reverse();
            await route.fulfill({json:{items:filtered.slice((number-1)*50,number*50),page:number,pageSize:50,totalCount:filtered.length,
                eligibleCount:51,selectedCount:workers.filter(worker=>worker.isSelected).length,rowVersion:run.rowVersion}});return;
        }
        if(path.endsWith("/calculate")){
            calculations++;
            const body=route.request().postDataJSON();
            expect(body.rowVersion).toBe("AAAAAAAAAAE=");expect(body.selectAll).toBe(false);
            expect(body.selectionChanges).toEqual([{employmentId:id(100),isSelected:true},{employmentId:id(150),isSelected:true}]);
            if(failCalculation){failCalculation=false;await route.fulfill({status:503,json:{detail:"Cálculo no disponible"}});return;}
            await released;
            workers.forEach(worker=>{worker.isSelected=body.selectionChanges.some((change:{employmentId:string})=>change.employmentId===worker.employmentId);});
            run={...run,rowVersion:"AAAAAAAAAAI=",calculationVersion:2,employeeCount:2,employees:workers.filter(worker=>worker.isSelected).map((worker,index)=>({
                payrollRunEmployeeId:id(300+index),employmentId:worker.employmentId,partyId:id(400+index),employeeName:worker.employeeName,workedDays:30,
                earnings:0,deductions:0,employerContributions:0,provisions:0,netPayable:0,lines:[]}))};
            await route.fulfill({json:run});return;
        }
        if(path.endsWith("/approve")){
            expect(route.request().postDataJSON().rowVersion).toBe("AAAAAAAAAAM=");
            run={...run,status:"Approved",rowVersion:"AAAAAAAAAAQ="};
            await route.fulfill({status:202,json:{payrollRunId:run.payrollRunId,status:"Approved",runKind:"Regular",accountingPostingJobId:id(900),idempotentReplay:false}});return;
        }
        throw new Error(`Unexpected payroll request ${path}`);
    });
    await page.goto("/dashboard/payroll?section=runs");
    await page.getByRole("button",{name:/2026-01-01 — 2026-01-31/}).click();
    await expect(page.getByText("No fue posible cargar los trabajadores de la liquidación.",{exact:true})).toBeVisible();
    await expect(page.getByRole("button",{name:"Recalcular",exact:true})).toBeDisabled();
    failPage=false;await page.getByRole("button",{name:"Reintentar trabajadores",exact:true}).click();
    const selection=page.getByRole("region",{name:"Trabajadores de la liquidación",exact:true});
    await expect(selection.locator("tbody").getByRole("checkbox")).toHaveCount(50);
    const bounds=await selection.evaluate(element=>element.getBoundingClientRect().toJSON());
    expect(bounds.left).toBeGreaterThanOrEqual(0);expect(bounds.right).toBeLessThanOrEqual(viewport.width);
    const calculate = await selection.getByRole("button", { name: "Recalcular", exact: true }).evaluate(element => element.getBoundingClientRect().toJSON());
    const filters = await selection.getByRole("textbox", { name: "Buscar trabajador de la liquidación" }).evaluate(element => element.getBoundingClientRect().toJSON());
    expect(calculate.left).toBeGreaterThanOrEqual(0);
    expect(calculate.right).toBeLessThanOrEqual(viewport.width);
    expect(calculate.bottom).toBeLessThanOrEqual(filters.top);
    for(const name of ["Buscar","Siguiente trabajadores"]){
        const control=await selection.getByRole("button",{name,exact:true}).evaluate(element=>element.getBoundingClientRect().toJSON());
        expect(control.left).toBeGreaterThanOrEqual(0);expect(control.right).toBeLessThanOrEqual(viewport.width);
    }
    if(viewport.width===390){
        const table=await selection.getByRole("table").evaluate(element=>({viewport:element.parentElement!.clientWidth,content:element.parentElement!.scrollWidth}));
        expect(table.content).toBeGreaterThan(table.viewport);
    }
    await expect(selection.getByRole("checkbox",{name:"Incluir todos los trabajadores elegibles",exact:true})).toBeChecked();
    await selection.getByRole("checkbox",{name:"Incluir todos los trabajadores elegibles",exact:true}).uncheck();
    await expect(page.getByRole("button",{name:"Aprobar y contabilizar",exact:true})).toBeDisabled();
    const first=selection.getByRole("checkbox",{name:"Incluir Trabajador 00, contrato C-0",exact:true});
    await first.focus();await page.keyboard.press("Space");await expect(first).toBeChecked();await expect(first).toBeFocused();
    await selection.getByRole("button",{name:"Siguiente trabajadores",exact:true}).click();
    await selection.getByRole("checkbox",{name:"Incluir Trabajador 50, contrato C-50",exact:true}).check();
    await expect(selection.getByText("2 seleccionados de 51 elegibles",{exact:true})).toBeVisible();
    const readsBeforeTyping=selectionReads;
    await selection.getByRole("textbox",{name:"Buscar trabajador de la liquidación",exact:true}).fill("No existe");
    expect(selectionReads).toBe(readsBeforeTyping);
    await selection.getByRole("button",{name:"Buscar",exact:true}).click();
    await expect(selection.getByText("No hay trabajadores que coincidan con la búsqueda.",{exact:true})).toBeVisible();
    await selection.getByRole("textbox",{name:"Buscar trabajador de la liquidación",exact:true}).fill("");
    await selection.getByRole("button",{name:"Buscar",exact:true}).click();
    await expect(first).toBeChecked();
    await selection.getByRole("button",{name:"Recalcular",exact:true}).click();
    await expect(page.getByText("Cálculo no disponible",{exact:true})).toBeVisible();
    await expect(first).toBeChecked();
    const readsBeforeCalculation=selectionReads;
    await selection.getByRole("button",{name:"Recalcular",exact:true}).click();
    await expect(first).toBeDisabled();await expect(selection.getByRole("button",{name:"Calculando…",exact:true})).toBeDisabled();
    release();
    await expect(page.locator("summary")).toHaveCount(2);
    await expect(first).toBeChecked();
    await expect(selection.getByRole("checkbox",{name:"Incluir Trabajador 01, contrato C-1",exact:true})).not.toBeChecked();
    await expect(page.getByRole("button",{name:"Aprobar y contabilizar",exact:true})).toBeEnabled();
    await selection.getByRole("heading",{name:"Seleccionar trabajadores",exact:true}).scrollIntoViewIfNeeded();
    await page.screenshot({path:testInfo.outputPath("seleccion-liquidacion.png")});
    expect(calculations).toBe(2);expect(optionsReads).toBe(1);expect(runsReads).toBe(1);expect(detailReads).toBe(1);
    expect(selectionReads).toBe(readsBeforeCalculation);
    await selection.getByRole("button",{name:"Siguiente trabajadores",exact:true}).click();
    await expect(selection.getByRole("checkbox",{name:"Incluir Trabajador 50, contrato C-50",exact:true})).toBeChecked();
    expect(selectionReads).toBe(readsBeforeCalculation);
    await selection.getByRole("button",{name:"Ordenar por Trabajador",exact:true}).click();
    await expect(selection.locator("tbody").getByRole("checkbox").first()).toHaveAccessibleName("Incluir Trabajador 50, contrato C-50");
    await selection.getByRole("textbox",{name:"Filtrar contrato",exact:true}).fill("C-0");
    await selection.getByRole("button",{name:"Buscar",exact:true}).click();
    await expect(selection.locator("tbody").getByRole("checkbox")).toHaveCount(1);
    await expect(first).toBeChecked();
    await selection.getByRole("textbox",{name:"Buscar trabajador de la liquidación",exact:true}).fill("Trabajador 50");
    await selection.getByRole("button",{name:"Buscar",exact:true}).click();
    await expect(selection.getByText("No hay trabajadores que coincidan con la búsqueda.",{exact:true})).toBeVisible();
    await expect(selection.getByText("2 seleccionados de 51 elegibles",{exact:true})).toBeVisible();
    await selection.getByRole("textbox",{name:"Buscar trabajador de la liquidación",exact:true}).fill("");
    await selection.getByRole("button",{name:"Buscar",exact:true}).click();
    await selection.getByRole("textbox",{name:"Filtrar contrato",exact:true}).fill("");
    await selection.getByRole("button",{name:"Buscar",exact:true}).click();
    await expect(selection.locator("tbody").getByRole("checkbox")).toHaveCount(50);
    expect(centerReads).toBe(0);
    const beforeCenter=selectionReads;
    await selection.getByRole("combobox",{name:"Filtrar trabajadores por centro de costo",exact:true}).click();
    await page.getByRole("option",{name:/CC · Centro de selección/}).click();
    await expect(selection.locator("tbody").getByRole("checkbox")).toHaveCount(2);
    expect(centerReads).toBe(1);expect(selectionReads).toBe(beforeCenter+1);
    const beforeSite=selectionReads;
    await selection.getByRole("textbox",{name:"Filtrar sitio de trabajo",exact:true}).fill("_%[A]~");
    expect(selectionReads).toBe(beforeSite);
    await selection.getByRole("button",{name:"Buscar",exact:true}).click();
    await expect(selection.locator("tbody").getByRole("checkbox")).toHaveCount(1);await expect(first).toBeChecked();
    expect(selectionReads).toBe(beforeSite+1);
    expect(centerReads).toBe(1);
    await selection.getByRole("heading",{name:"Seleccionar trabajadores",exact:true}).scrollIntoViewIfNeeded();
    await page.screenshot({path:testInfo.outputPath(`seleccion-filtros-${viewport.width}.png`)});
    await first.uncheck();
    run={...run,rowVersion:"AAAAAAAAAAM="};
    await page.getByRole("button",{name:"Actualizar liquidación",exact:true}).click();
    await expect(selection.getByText("La liquidación cambió. Descarta la selección anterior antes de continuar.",{exact:true})).toBeVisible();
    await expect(first).not.toBeChecked();
    await expect(selection.getByRole("button",{name:"Recalcular",exact:true})).toBeDisabled();
    await selection.getByRole("button",{name:"Descartar selección",exact:true}).click();
    await expect(first).toBeChecked();
    await expect(selection.getByRole("button",{name:"Recalcular",exact:true})).toBeEnabled();
    const readsBeforeApprove=selectionReads;
    await page.getByRole("button",{name:"Aprobar y contabilizar",exact:true}).click();
    await expect(selection).toHaveCount(0);
    expect(selectionReads).toBe(readsBeforeApprove);
});

test("sólo lectura no carga el selector de trabajadores",async({page,baseURL})=>{
    const scenario=financialVoucherScenario();scenario.user.permissions.push("payroll.read");
    await installFinancialVoucherContext(page,baseURL!,scenario);
    const run={payrollRunId:id(1),periodStart:"2026-01-01",periodEnd:"2026-01-31",status:"Draft",employeeCount:0,totalEarnings:0,totalDeductions:0,netPayable:0,employees:[]};
    let detailUnavailable=true;
    await page.route("**/api/commerce/v1/payroll/**",async route=>{
        const path=new URL(route.request().url()).pathname;
        if(path.endsWith("/options")){await route.fulfill({json:{catalogs:{},concepts:[],employments:[],parties:[],ruleSets:[],settings:null,deductionAgreements:[],novelties:[],paymentBatches:[],electronicPeriods:[]}});return;}
        if(path.endsWith("/runs")){await route.fulfill({json:{items:[run],page:1,pageSize:20,totalCount:1,totalPages:1}});return;}
        if(path.endsWith(`/runs/${id(1)}`)){
            if(detailUnavailable){await route.fulfill({status:503,json:{detail:"Liquidación no disponible"}});return;}
            await route.fulfill({json:run});return;
        }
        throw new Error(`Unexpected request ${path}`);
    });
    await page.goto("/dashboard/payroll");await page.getByRole("button",{name:/2026-01-01 — 2026-01-31/}).click();
    await expect(page.getByRole("button",{name:"Reintentar detalle",exact:true})).toBeVisible();
    detailUnavailable=false;await page.getByRole("button",{name:"Reintentar detalle",exact:true}).click();
    await expect(page.getByRole("button",{name:"Reintentar detalle",exact:true})).toHaveCount(0);
    await expect(page.getByText("Seleccionar trabajadores",{exact:true})).toHaveCount(0);
    await expect(page.getByRole("button",{name:"Calcular nómina",exact:true})).toHaveCount(0);
});

test("sin contratos elegibles explica periodicidad y vigencia antes de liquidar",async({page,baseURL})=>{
    const scenario=financialVoucherScenario();scenario.user.permissions.push("payroll.read","payroll.calculate");
    await installFinancialVoucherContext(page,baseURL!,scenario);
    const run={payrollRunId:id(1),periodStart:"2026-12-01",periodEnd:"2026-12-15",status:"Draft",employeeCount:0,
        totalEarnings:0,totalDeductions:0,netPayable:0,employees:[],rowVersion:"AAAAAAAAAAE="};
    let candidateReads=0;
    await page.route("**/api/commerce/v1/payroll/**",async route=>{
        expect(route.request().method()).toBe("GET");const path=new URL(route.request().url()).pathname;
        if(path.endsWith("/options")){await route.fulfill({json:{catalogs:{},concepts:[],employments:[],parties:[],ruleSets:[],settings:null,deductionAgreements:[],novelties:[],paymentBatches:[],electronicPeriods:[]}});return;}
        if(path.endsWith("/runs")){await route.fulfill({json:{items:[run],page:1,pageSize:20,totalCount:1,totalPages:1}});return;}
        if(path.endsWith(`/runs/${id(1)}`)){await route.fulfill({json:run});return;}
        if(path.endsWith("/employments")){candidateReads++;await route.fulfill({json:{items:[],page:1,pageSize:50,totalCount:0,eligibleCount:0,selectedCount:0,rowVersion:run.rowVersion}});return;}
        throw new Error(`Unexpected request ${path}`);
    });
    await page.goto("/dashboard/payroll");
    await page.getByRole("button",{name:/2026-12-01 — 2026-12-15/}).click();
    await expect(page.getByText("No hay contratos activos y vigentes que coincidan con la periodicidad de esta liquidación. Revisa la periodicidad y las fechas de los contratos.",{exact:true})).toBeVisible();
    await expect(page.getByRole("button",{name:"Calcular nómina",exact:true})).toBeDisabled();
    expect(candidateReads).toBe(1);
});
