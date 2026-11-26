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
    expect(["Transfer","Cash"]).toContain(query.get("methodCode"));
    const filtered=emptyForms?[]:forms.filter(form=>form.methodCode===query.get("methodCode"));
    const pageNumber=Number(query.get("page")),pageSize=Number(query.get("pageSize"));
    await route.fulfill({json:{items:filtered,page:pageNumber,pageSize,totalCount:filtered.length}});
  });
  const confirmations:Array<Record<string,unknown>>=[],history:Array<Record<string,unknown>>=[];let ledgerPosted=false;
  let release:()=>void=()=>{};const released=new Promise<void>(resolve=>{release=resolve;});
  await page.route("**/api/commerce/v1/payroll/**",async route=>{
    const path=new URL(route.request().url()).pathname;
    if(path.endsWith("/options")){
      optionsReads++;await route.fulfill({json:{catalogs,concepts:[],employments:[],parties:[],ruleSets:[],settings:null,
        electronicConfiguration:null,fiscalIssuers:[],deductionAgreements:[],novelties:[],paymentBatches:history.map(item=>({...item,isPaid:ledgerPosted,accountingStatus:ledgerPosted?"Posted":"Pending"})),electronicPeriods:[]}});return;
    }
    if(path.endsWith(`/runs/${runId}`)){detailReads++;await route.fulfill({json:{...run,payableEmployees:pending}});return;}
    if(path.endsWith("/runs")){runsReads++;await route.fulfill({json:{items:[run],page:1,pageSize:20,totalCount:1,totalPages:1}});return;}
    if(path.endsWith("/payments")){
      if(route.request().method()==="GET"){
        await route.fulfill({json:{items:history.map(item=>({...item,isPaid:ledgerPosted,accountingStatus:ledgerPosted?"Posted":"Pending"})),page:1,pageSize:20,totalCount:history.length,totalPages:history.length?1:0}});return;
      }
      const body=route.request().postDataJSON() as Record<string,unknown>;confirmations.push(body);
      if(confirmations.length===1)await released;
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
  const transferGroup=modal.getByRole("region",{name:"Grupo Transferencia",exact:true});
  const cashGroup=modal.getByRole("region",{name:"Grupo Efectivo",exact:true});
  async function selectMethod(group:typeof transferGroup,name:string){await group.getByRole("combobox",{name:"Forma de pago",exact:true}).click();await page.getByRole("option",{name:new RegExp(name)}).click();}
  async function paymentFields(group:typeof transferGroup,reference:string){
    await group.getByRole("button",{name:"Fecha de pago",exact:true}).click();await modal.getByRole("button",{name:"Elegir hoy",exact:true}).click();
    await group.getByLabel("Referencia bancaria o de caja",{exact:true}).fill(reference);
  }
  await selectRun();
  await expect(transferGroup).toContainText("1 trabajador pendiente");await expect(transferGroup).toContainText("200");
  await expect(cashGroup).toContainText("1 trabajador pendiente");await expect(cashGroup).toContainText("100");
  await expect(modal.getByRole("combobox",{name:"Forma de pago",exact:true})).toHaveCount(0);
  expect(formReads).toBe(0);
  await transferGroup.getByRole("button",{expanded:false}).click();
  await expect(transferGroup.getByText("Trabajador transferencia",{exact:true})).toBeVisible();
  await expect(transferGroup.getByText("Trabajador efectivo",{exact:true})).toHaveCount(0);
  if(catalogError){
    await transferGroup.getByRole("combobox",{name:"Forma de pago",exact:true}).click();
    await expect(modal.getByText("No fue posible consultar.",{exact:false})).toBeVisible({timeout:15000});
    failCatalog=false;await modal.getByRole("button",{name:"Reintentar",exact:true}).click();
    await page.getByRole("option",{name:/Banco nómina B/}).click();
    await expect(transferGroup.getByText("Trabajador transferencia",{exact:true})).toBeVisible();
    expect(confirmations).toHaveLength(0);return;
  }
  if(emptyForms){
    await expect(transferGroup.getByRole("button",{name:"Confirmar pago del grupo",exact:true})).toBeDisabled();
    await transferGroup.getByRole("combobox",{name:"Forma de pago",exact:true}).click();
    await expect(modal.getByText("No hay formas habilitadas para nómina.",{exact:true})).toBeVisible();
    expect(confirmations).toHaveLength(0);return;
  }
  await selectMethod(transferGroup,"Banco nómina A");
  await expect(transferGroup.getByText("Trabajador transferencia",{exact:true})).toBeVisible();
  await selectMethod(transferGroup,"Banco nómina B");
  await expect(modal.getByText(/Cuenta: 111002 · Banco B/)).toBeVisible();
  await expect(transferGroup.getByText("Trabajador transferencia",{exact:true})).toBeVisible();
  await paymentFields(transferGroup,"REF-TRANSFER");
  if(!completeBank){
    await expect(transferGroup.getByRole("alert")).toContainText("destino bancario completo");
    await expect(transferGroup.getByRole("button",{name:"Confirmar pago del grupo",exact:true})).toBeDisabled();
    expect(confirmations).toHaveLength(0);return;
  }
  await cashGroup.getByRole("button",{expanded:false}).click();await selectMethod(cashGroup,"Caja nómina");
  await expect(page.getByRole("option",{name:/Banco nómina/})).toHaveCount(0);
  await paymentFields(cashGroup,"REF-CASH");
  await transferGroup.getByRole("button",{name:"Confirmar pago del grupo",exact:true}).click();
  await expect.poll(()=>confirmations.length).toBe(1);
  await expect(cashGroup.getByRole("button",{name:"Confirmar pago del grupo",exact:true})).toBeDisabled();
  await expect(modal.getByRole("combobox",{name:"Liquidación",exact:true})).toBeDisabled();
  await page.keyboard.press("Escape");await expect(modal).toBeVisible();
  release();
  await expect(transferGroup).toContainText("0 trabajadores pendientes");
  expect(confirmations).toHaveLength(1);expect(confirmations[0].paymentMethodOptionId).toBe(transfer);expect(confirmations[0].paymentMethodId).toBe(id(21));
  await expect(cashGroup.getByLabel("Referencia bancaria o de caja",{exact:true})).toHaveValue("REF-CASH");
  await expect(cashGroup.getByRole("combobox",{name:"Forma de pago",exact:true})).toContainText("Caja nómina");
  await expect(cashGroup.getByText("Trabajador efectivo",{exact:true})).toBeVisible();
  await expect(cashGroup.getByText("Trabajador transferencia",{exact:true})).toHaveCount(0);
  await page.screenshot({path:testInfo.outputPath("payroll-cash-pending.png"),fullPage:true});
  await cashGroup.getByRole("button",{name:"Confirmar pago del grupo",exact:true}).click();await expect(modal).not.toBeVisible();
  expect(confirmations).toHaveLength(2);expect(confirmations[1].paymentMethodOptionId).toBe(cash);expect(confirmations[1].paymentMethodId).toBe(id(22));
  expect(confirmations[0].paymentBatchId).not.toBe(confirmations[1].paymentBatchId);
  const paidRow=page.getByRole("row").filter({hasText:"REF-TRANSFER"});
  await expect(paidRow).toContainText("Confirmado");await expect(paidRow).toContainText("Pendiente de contabilización");
  expect(detailReads).toBe(1);expect(optionsReads).toBe(1);expect(runsReads).toBe(1);expect(formReads).toBe(2);
  ledgerPosted=true;await page.getByRole("button",{name:"Actualizar nómina",exact:true}).click();
  await expect(paidRow).toContainText("Pagado");await expect(paidRow).toContainText("Contabilizado");
  expect(optionsReads).toBe(2);expect(runsReads).toBe(1);expect(detailReads).toBe(1);
  // Explicit refresh marks the hidden detail stale without loading it. Its sole
  // owner reads once when the user opens that detail again.
  await page.getByRole("button",{name:"Confirmar pago",exact:true}).click();await selectRun();
  await expect(cashGroup).toContainText("0 trabajadores pendientes");
  expect(detailReads).toBe(2);expect(optionsReads).toBe(2);expect(runsReads).toBe(2);
});
