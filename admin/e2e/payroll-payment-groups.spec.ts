import { expect,test } from "@playwright/test";
import { financialVoucherScenario,installFinancialVoucherContext } from "./support/financial-voucher";

test.use({viewport:{width:1440,height:740}});

for(const {completeBank,emptyForms,catalogError} of [{completeBank:true,emptyForms:false,catalogError:false},{completeBank:false,emptyForms:false,catalogError:false},{completeBank:true,emptyForms:true,catalogError:false},{completeBank:true,emptyForms:false,catalogError:true}])test(`nómina: ${catalogError?"recuperar consulta del catálogo":emptyForms?"catálogo vacío":`grupos congelados y destino bancario ${completeBank?"completo":"incompleto"}`}`,async({page,baseURL},testInfo)=>{
  const scenario=financialVoucherScenario(),id=(n:number)=>`aaaaaaaa-bbbb-4ccc-8ddd-${String(n).padStart(12,"0")}`;
  scenario.user.permissions.push("payroll.read","payroll.pay");
  await installFinancialVoucherContext(page,baseURL!,scenario);
  const runId=id(1),transfer=id(2),cash=id(3);
  let pending=[{payrollRunEmployeeId:id(10),paymentMethodOptionId:transfer,paymentMethodName:"Transferencia",amount:200,bankDestinationComplete:completeBank,employeeName:"Trabajador transferencia"},
    {payrollRunEmployeeId:id(11),paymentMethodOptionId:cash,paymentMethodName:"Efectivo",amount:100,bankDestinationComplete:true,employeeName:"Trabajador efectivo"}];
  const run={payrollRunId:runId,businessId:scenario.businessId,ruleSetId:id(4),runKind:"Regular",originalPayrollRunId:null,
    periodStart:"2026-08-01",periodEnd:"2026-08-31",paymentDate:"2026-08-31",status:"Approved",calculationVersion:1,
    employeeCount:2,totalEarnings:300,totalDeductions:0,totalEmployerContributions:0,totalProvisions:0,netPayable:300,employees:[],rowVersion:"AAAAAAAAAAE="};
  const catalogs={"payroll-payment-method":[{optionId:transfer,catalogCode:"payroll-payment-method",code:"Transfer",label:"Transferencia",isActive:true,sortOrder:1},
    {optionId:cash,catalogCode:"payroll-payment-method",code:"Cash",label:"Efectivo",isActive:true,sortOrder:2}]};
  const forms=[{paymentMethodId:id(20),name:"Banco nómina A",methodCode:"Transfer",accountId:id(30),accountCode:"111001",accountName:"Banco A",code:"BANCO-A"},
    {paymentMethodId:id(21),name:"Banco nómina B",methodCode:"Transfer",accountId:id(31),accountCode:"111002",accountName:"Banco B",code:"BANCO-B"},
    {paymentMethodId:id(22),name:"Caja nómina",methodCode:"Cash",accountId:id(32),accountCode:"110501",accountName:"Caja",code:"CAJA"}];
  let detailReads=0,optionsReads=0,runsReads=0,formReads=0;
  let failCatalog=catalogError;
  await page.route("**/api/commerce/v1/payment-methods/**",async route=>{
    const query=new URL(route.request().url()).searchParams;
    expect(query.get("usage")).toBe("Payroll");formReads++;
    if(failCatalog){await route.fulfill({status:503,json:{title:"Catálogo no disponible"}});return;}
    const pageNumber=Number(query.get("page")),pageSize=Number(query.get("pageSize"));
    await route.fulfill({json:{items:emptyForms?[]:forms,page:pageNumber,pageSize,totalCount:emptyForms?0:forms.length}});
  });
  const confirmations:Array<Record<string,unknown>>=[],history:Array<Record<string,unknown>>=[];let ledgerPosted=false;
  await page.route("**/api/commerce/v1/payroll/**",async route=>{
    const path=new URL(route.request().url()).pathname;
    if(path.endsWith("/options")){
      optionsReads++;await route.fulfill({json:{catalogs,concepts:[],employments:[],parties:[],ruleSets:[],settings:null,
        electronicConfiguration:null,fiscalIssuers:[],deductionAgreements:[],novelties:[],paymentBatches:history.map(item=>({...item,isPaid:ledgerPosted,accountingStatus:ledgerPosted?"Posted":"Pending"})),electronicPeriods:[]}});return;
    }
    if(path.endsWith(`/runs/${runId}`)){detailReads++;await route.fulfill({json:{...run,payableEmployees:pending}});return;}
    if(path.endsWith("/runs")){runsReads++;await route.fulfill({json:[run]});return;}
    if(path.endsWith("/payments")){
      const body=route.request().postDataJSON() as Record<string,unknown>;confirmations.push(body);
      const paid=pending.filter(employee=>employee.paymentMethodOptionId===body.paymentMethodOptionId);
      pending=pending.filter(employee=>employee.paymentMethodOptionId!==body.paymentMethodOptionId);
      const result={...body,paymentMethodName:forms.find(item=>item.paymentMethodId===body.paymentMethodId)!.name,status:"Confirmed",accountingStatus:"Pending",isPaid:false,employeeCount:paid.length,
        totalAmount:paid.reduce((sum,employee)=>sum+employee.amount,0),rowVersion:"AAAAAAAAAAI=",remainingEmployees:pending};
      history.push(result);await route.fulfill({json:result});return;
    }
    await route.fallback();
  });
  await page.goto("/dashboard/payroll?section=payments");
  await page.getByRole("button",{name:"Confirmar pago",exact:true}).click();
  const modal=page.getByRole("dialog",{name:"Confirmar lote de pago",exact:true});
  async function selectRun(){await modal.getByRole("combobox",{name:"Liquidación"}).click();await page.getByRole("option",{name:/2026-08-01/}).click();}
  async function selectMethod(name:string){await modal.getByRole("combobox",{name:"Forma de pago",exact:true}).click();await page.getByRole("option",{name:new RegExp(name)}).click();}
  await selectRun();
  if(catalogError){
    await modal.getByRole("combobox",{name:"Forma de pago",exact:true}).click();
    await expect(modal.getByText("No fue posible consultar.",{exact:false})).toBeVisible({timeout:15000});
    failCatalog=false;await modal.getByRole("button",{name:"Reintentar",exact:true}).click();
    await page.getByRole("option",{name:/Banco nómina B/}).click();
    await expect(modal.getByRole("region",{name:"Trabajadores del grupo de pago"}).getByText("Trabajador transferencia",{exact:true})).toBeVisible();
    expect(confirmations).toHaveLength(0);return;
  }
  if(emptyForms){
    await expect(modal.getByRole("button",{name:"Confirmar pago del grupo",exact:true})).toBeDisabled();
    await modal.getByRole("combobox",{name:"Forma de pago",exact:true}).click();
    await expect(modal.getByText("No hay formas habilitadas para nómina.",{exact:true})).toBeVisible();
    expect(confirmations).toHaveLength(0);return;
  }
  await selectMethod("Banco nómina A");
  const group=modal.getByRole("region",{name:"Trabajadores del grupo de pago"});
  await expect(group.getByText("Trabajador transferencia",{exact:true})).toBeVisible();
  await expect(group.getByText("Trabajador efectivo",{exact:true})).toHaveCount(0);
  await selectMethod("Banco nómina B");
  await expect(modal.getByText(/Cuenta: 111002 · Banco B/)).toBeVisible();
  await expect(group.getByText("Trabajador transferencia",{exact:true})).toBeVisible();
  await modal.getByRole("button",{name:"Fecha de pago",exact:true}).click();await modal.getByRole("button",{name:"Elegir hoy",exact:true}).click();
  await modal.getByLabel("Referencia bancaria o de caja",{exact:true}).fill("REF-TRANSFER");
  if(!completeBank){
    await expect(group.getByRole("alert")).toContainText("destino bancario completo");
    await expect(modal.getByRole("button",{name:"Confirmar pago del grupo",exact:true})).toBeDisabled();
    expect(confirmations).toHaveLength(0);return;
  }
  await modal.getByRole("button",{name:"Confirmar pago del grupo",exact:true}).click();await expect(modal).not.toBeVisible();
  expect(confirmations).toHaveLength(1);expect(confirmations[0].paymentMethodOptionId).toBe(transfer);expect(confirmations[0].paymentMethodId).toBe(id(21));
  const paidRow=page.getByRole("row").filter({hasText:"REF-TRANSFER"});
  await expect(paidRow).toContainText("Confirmado");await expect(paidRow).toContainText("Pendiente de contabilización");
  await page.getByRole("button",{name:"Confirmar pago",exact:true}).click();await selectRun();await selectMethod("Caja nómina");
  await expect(group.getByText("Trabajador efectivo",{exact:true})).toBeVisible();
  await expect(group.getByText("Trabajador transferencia",{exact:true})).toHaveCount(0);
  expect(detailReads).toBe(1);expect(optionsReads).toBe(1);expect(runsReads).toBe(1);expect(formReads).toBe(1);
  await page.screenshot({path:testInfo.outputPath("payroll-cash-pending.png"),fullPage:true});
  await page.keyboard.press("Escape");await expect(modal).not.toBeVisible();
  ledgerPosted=true;await page.getByRole("button",{name:"Actualizar nómina",exact:true}).click();
  await expect(paidRow).toContainText("Pagado");await expect(paidRow).toContainText("Contabilizado");
  expect(optionsReads).toBe(2);expect(runsReads).toBe(2);expect(detailReads).toBe(1);
  // Explicit refresh marks the hidden detail stale without loading it. Its sole
  // owner reads once when the user opens that detail again.
  await page.getByRole("button",{name:"Confirmar pago",exact:true}).click();await selectRun();await selectMethod("Caja nómina");
  await expect(group.getByText("Trabajador efectivo",{exact:true})).toBeVisible();
  expect(detailReads).toBe(2);expect(optionsReads).toBe(2);expect(runsReads).toBe(2);
});
