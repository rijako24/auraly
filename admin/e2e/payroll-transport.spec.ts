import { expect,test } from "@playwright/test";
import { financialVoucherScenario,installFinancialVoucherContext } from "./support/financial-voucher";

const id=(n:number)=>`abababab-1111-4222-8333-${String(n).padStart(12,"0")}`;
for(const initialFlag of [true,false])test(`contrato: guardar auxilio ${initialFlag} sin recargar el workspace`,async({page,baseURL},testInfo)=>{
  const scenario=financialVoucherScenario();scenario.user.permissions.push("payroll.read","payroll.manage");
  await installFinancialVoucherContext(page,baseURL!,scenario);
  let employment={employmentId:id(1),partyId:id(2),employeeId:id(3),businessId:scenario.businessId,employeeName:"Trabajadora auxilio",
    contractNumber:"AUX-1",monthlySalary:1500000,isActive:true,startDate:"2026-01-01",endDate:null,contractTypeOptionId:id(10),
    salaryTypeOptionId:id(11),payFrequencyOptionId:id(12),riskClassOptionId:id(13),workerTypeOptionId:id(14),workerSubtypeOptionId:null,
    paymentMethodOptionId:id(15),integralSalaryPercentage:null,bankAccountReference:null,bankOptionId:null,bankAccountTypeOptionId:null,
    bankAccountNumber:null,rowVersion:"AAAAAAAAAAE=",appliesTransportAllowance:initialFlag};
  const definitions=[['payroll-contract-type','Indefinite','Indefinido'],['payroll-salary-type','Ordinary','Ordinario'],
    ['payroll-pay-frequency','Monthly','Mensual'],['payroll-risk-class','I','Riesgo I'],['payroll-worker-type','01','Trabajador'],
    ['payroll-payment-method','Cash','Efectivo']];
  const catalogs=Object.fromEntries(definitions.map(([catalogCode,code,label],index)=>[catalogCode,[{optionId:id(index+10),catalogCode,code,label,isActive:true,sortOrder:1}]]));
  let optionsReads=0,runsReads=0,employmentReads=0,saves=0;
  let rejectSave=!initialFlag;
  let completeSave:()=>void=()=>{};
  const saveReleased=new Promise<void>(resolve=>{completeSave=resolve;});
  await page.route("**/api/commerce/v1/payroll/**",async route=>{
    const path=new URL(route.request().url()).pathname;
    if(path.endsWith("/options")){optionsReads++;await route.fulfill({json:{catalogs,concepts:[],employments:[],parties:[],ruleSets:[],settings:null,
      electronicConfiguration:null,fiscalIssuers:[],deductionAgreements:[],novelties:[],paymentBatches:[],electronicPeriods:[]}});return;}
    if(path.endsWith("/runs")){runsReads++;await route.fulfill({json:[]});return;}
    if(path.endsWith("/employments")){employmentReads++;await route.fulfill({json:{items:[employment],page:1,pageSize:25,totalCount:1,totalPages:1}});return;}
    if(path.endsWith(`/employments/${id(1)}`)&&route.request().method()==="PUT"){
      saves++;
      if(rejectSave){rejectSave=false;await route.fulfill({status:503,json:{detail:"No fue posible guardar el contrato."}});return;}
      await saveReleased;
      employment={...employment,...route.request().postDataJSON(),rowVersion:"AAAAAAAAAAI="};
      await route.fulfill({json:employment});return;
    }
    await route.fallback();
  });
  await page.goto("/dashboard/payroll?section=employments");
  await page.getByRole("row").filter({hasText:"Trabajadora auxilio"}).getByRole("button",{name:"Editar",exact:true}).click();
  const modal=page.getByRole("dialog",{name:"Editar relación laboral",exact:true});
  const flag=modal.getByRole("checkbox",{name:"Aplica auxilio de transporte",exact:true});
  await expect(flag).toBeChecked({checked:initialFlag});
  await flag.focus();await page.keyboard.press("Space");await expect(flag).toBeFocused();
  const save=modal.getByRole("button",{name:"Guardar contrato",exact:true});
  if(!initialFlag){
    await save.click();await expect(page.getByText("No fue posible guardar el contrato.",{exact:true})).toBeVisible();
    await expect(modal).toBeVisible();await expect(flag).toBeEnabled();
    expect(employment.appliesTransportAllowance).toBe(initialFlag);
  }
  await save.click();await expect(flag).toBeDisabled();await expect(save).toBeDisabled();
  completeSave();await expect(modal).not.toBeVisible();
  expect(saves).toBe(initialFlag?1:2);expect(employment.appliesTransportAllowance).toBe(!initialFlag);
  await page.getByRole("row").filter({hasText:"Trabajadora auxilio"}).getByRole("button",{name:"Editar",exact:true}).click();
  await expect(flag).toBeChecked({checked:!initialFlag});
  await modal.screenshot({path:testInfo.outputPath("contrato-auxilio.png")});
  expect(optionsReads).toBe(1);expect(runsReads).toBe(1);expect(employmentReads).toBe(1);
});

test("cálculo: muestra la respuesta autoritativa sin un segundo GET",async({page,baseURL})=>{
  const scenario=financialVoucherScenario();scenario.user.permissions.push("payroll.read","payroll.calculate");
  await installFinancialVoucherContext(page,baseURL!,scenario);
  const run={payrollRunId:id(20),businessId:scenario.businessId,runKind:"Regular",periodStart:"2026-01-01",periodEnd:"2026-01-31",paymentDate:"2026-01-31",
    status:"Draft",employeeCount:0,totalEarnings:0,totalDeductions:0,totalEmployerContributions:0,totalProvisions:0,netPayable:0,employees:[],payableEmployees:[],rowVersion:"AAAAAAAAAAE="};
  let optionsReads=0,runsReads=0,detailReads=0,calculations=0;
  await page.route("**/api/commerce/v1/payroll/**",async route=>{
    const path=new URL(route.request().url()).pathname;
    if(path.endsWith("/options")){optionsReads++;await route.fulfill({json:{catalogs:{},concepts:[],employments:[],parties:[],ruleSets:[],settings:null,
      electronicConfiguration:null,fiscalIssuers:[],deductionAgreements:[],novelties:[],paymentBatches:[],electronicPeriods:[]}});return;}
    if(path.endsWith("/runs")){runsReads++;await route.fulfill({json:[run]});return;}
    if(path.endsWith(`/runs/${id(20)}`)){detailReads++;await route.fulfill({json:run});return;}
    if(path.endsWith(`/runs/${id(20)}/employments`)){await route.fulfill({json:{items:[{employmentId:id(23),employeeName:"Trabajadora calculada",identification:"123",contractNumber:"AUX",startDate:"2026-01-01",endDate:null,payFrequencyName:"Mensual",isSelected:true}],page:1,pageSize:50,totalCount:1,eligibleCount:1,selectedCount:1,rowVersion:run.rowVersion}});return;}
    if(path.endsWith("/calculate")){calculations++;await route.fulfill({json:{...run,status:"Calculated",calculationVersion:1,totalEarnings:1662000,totalDeductions:120000,
      netPayable:1542000,employees:[{payrollRunEmployeeId:id(21),partyId:id(22),employmentId:id(23),employeeName:"Trabajadora calculada",workedDays:30,
        earnings:1662000,deductions:120000,employerContributions:0,provisions:0,netPayable:1542000,lines:[]}]}});return;}
    await route.fallback();
  });
  await page.goto("/dashboard/payroll?section=runs");await page.getByRole("button",{name:/2026-01-01 — 2026-01-31/}).click();
  await page.getByRole("button",{name:"Calcular nómina",exact:true}).click();
  await expect(page.locator("summary").filter({hasText:"Trabajadora calculada"})).toBeVisible();
  await expect(page.getByRole("button",{name:"Recalcular",exact:true})).toBeVisible();
  expect(calculations).toBe(1);expect(optionsReads).toBe(1);expect(runsReads).toBe(1);expect(detailReads).toBe(1);
});

test("contratos: sólo lectura, vacío y recuperación explícita de la consulta",async({page,baseURL})=>{
  const scenario=financialVoucherScenario();scenario.user.permissions.push("payroll.read");
  await installFinancialVoucherContext(page,baseURL!,scenario);
  let unavailable=true;
  await page.route("**/api/commerce/v1/payroll/**",async route=>{
    const path=new URL(route.request().url()).pathname;
    expect(route.request().method()).toBe("GET");
    if(path.endsWith("/options")){
      await route.fulfill({json:{catalogs:{},concepts:[],employments:[],parties:[],ruleSets:[],settings:null,
        electronicConfiguration:null,fiscalIssuers:[],deductionAgreements:[],novelties:[],paymentBatches:[],electronicPeriods:[]}});return;
    }
    if(path.endsWith("/runs")){await route.fulfill({json:[]});return;}
    if(path.endsWith("/employments")){
      if(unavailable){await route.fulfill({status:503,json:{detail:"Contratos no disponibles"}});return;}
      await route.fulfill({json:{items:[],page:1,pageSize:25,totalCount:0,totalPages:0}});return;
    }
    await route.fallback();
  });
  await page.goto("/dashboard/payroll?section=employments");
  await expect(page.getByText("No fue posible cargar los contratos.",{exact:false})).toBeVisible();
  unavailable=false;await page.getByRole("button",{name:"Reintentar",exact:true}).click();
  await expect(page.getByText("Crea el primer contrato laboral.",{exact:true})).toBeVisible();
  await expect(page.getByRole("button",{name:"Nuevo contrato",exact:true})).toHaveCount(0);
  await expect(page.getByRole("button",{name:"Editar",exact:true})).toHaveCount(0);
  await expect(page.getByRole("button",{name:"Anterior",exact:true})).toBeDisabled();
  await expect(page.getByRole("button",{name:"Siguiente",exact:true})).toBeDisabled();
});
