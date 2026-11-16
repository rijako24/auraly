import { expect, test, type Page } from "@playwright/test";
import type { PayrollOptions, PayrollRun, SaveConcept, SaveEmployment } from "../src/services/api/payroll";

const tenantId = "11111111-1111-1111-1111-111111111111";
const businessId = "22222222-2222-2222-2222-222222222222";
const secondBusinessId = "33333333-3333-3333-3333-333333333333";
const permissions = ["dashboard.read", "payroll.read", "payroll.manage", "payroll.calculate", "payroll.approve", "payroll.pay", "payroll.fiscal", "payroll.configure", "accounting.read", "accounting.configure"];
const version = "AAAAAAAAAAE=";

function fixture(): PayrollOptions {
  const catalogs: PayrollOptions["catalogs"] = {};
  for (const [catalogCode, code, label] of [
    ["payroll-run-kind", "Regular", "Liquidación regular"],
    ["payroll-run-kind", "Adjustment", "Ajuste por diferencias"],
    ["payroll-workflow-status", "Draft", "Borrador"],
    ["payroll-workflow-status", "Approved", "Aprobada"],
    ["payroll-workflow-status", "Queued", "En procesamiento fiscal"],
    ["payroll-pay-frequency", "Monthly", "Mensual"],
    ["payroll-contract-type", "Indefinite", "Término indefinido"],
    ["payroll-salary-type", "Ordinary", "Salario ordinario"],
    ["payroll-risk-class", "I", "Clase I"],
    ["payroll-worker-type", "01", "Dependiente"],
    ["payroll-worker-subtype", "00", "No aplica subtipo"],
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
    ruleSets: [{ ruleSetId: "rules", countryCode: "CO", code: "CO-2026", name: "Reglas vigentes", effectiveFrom: "2026-01-01",
      effectiveTo: null, sourceReference: "Fuente de prueba", status: "Approved", parameters: [{ code: "MonthlyDays", numericValue: 30, unitCode: "Days", description: null }], rowVersion: version }],
    settings: { isEmployerExemptFromHealthSenaIcbf: false, electronicPayrollEnabled: true, rowVersion: version },
    electronicConfiguration: null, fiscalIssuers: [], deductionAgreements: [], novelties: [], paymentBatches: [], electronicPeriods: [],
  };
}

async function setup(page: Page, baseURL: string, granted = permissions) {
  const options = fixture();
  const runs: PayrollRun[] = [];
  const employees: PayrollOptions["employments"] = [];
  const writes: Array<{ path: string; body: Record<string, unknown> }> = [];
  const reads: Array<{ path: string; business: string | undefined }> = [];
  const failures = { options: false, detail: false, calculation:false };
  const controls={noveltyResponse:null as Promise<void>|null};
  const user = { userId: "44444444-4444-4444-4444-444444444444", tenantId, tenantKey: "TEST", username: "test", firstName: "Ana", lastName: "Prueba", roles: ["ACCOUNTANT"], permissions: granted };
  await page.context().addCookies([{ name: "auth_token", value: "e2e", url: baseURL, httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ tenantId, businessId, user }) => {
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
            accountingCategoryCode:"EmployeeLoansReceivable",quantity:0,rate:null,baseAmount:null,amount,isEmployerCost:false,isSalaryBase:false,
            noveltyId:novelty.noveltyId}]:[]};
        Object.assign(runs[0],{employees:[updated],rowVersion:"AAAAAAAAAAI=",totalDeductions:130000,netPayable:870000,calculationVersion:2});
        body={...runs[0],isPartialCalculation:true};
      } else if (path.endsWith("/calculate")) {
        Object.assign(runs[0], { status: "Calculated", employeeCount: 1, totalEarnings: 1000000, totalDeductions: 80000, netPayable: 920000,
          employees: [{ payrollRunEmployeeId: "run-employee", employmentId: employees[0].employmentId, partyId: employees[0].partyId,
            employeeName: "Elena Prueba", workedDays: 30, earnings: 1000000, deductions: 80000, employerContributions: 0, provisions: 0, netPayable: 920000, lines: [] }] }); body = runs[0]; }
      else if (path.endsWith("/approve")) { if (path.includes("/rule-sets/")) options.ruleSets.at(-1)!.status = "Approved"; else { runs[0].status = "Approved"; runs[0].payableEmployees = [{ payrollRunEmployeeId: "run-employee",
        paymentMethodOptionId: "payroll-payment-method-Cash", paymentMethodName: "Efectivo", amount: 920000, bankDestinationComplete: true, employeeName: "Elena Prueba" }]; } }
      else if (path.endsWith("/payroll/payments")) { body = { ...data, status: "Confirmed", employeeCount: 1, totalAmount: 920000, paymentMethodName: "Caja nómina", accountingStatus: "Pending", isPaid: false, remainingEmployees: [], rowVersion: version }; runs[0].payableEmployees = []; options.paymentBatches.push(body as PayrollOptions["paymentBatches"][number]); }
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
      else if (path.includes("/concepts/")) { const form = data as SaveConcept; options.concepts.push({ ...options.concepts[0], conceptId: form.conceptId, code: form.code, name: form.name }); }
      else if (path.includes("/rule-sets/")) { body = { ...data, status: "Draft", rowVersion: version }; options.ruleSets.push(body as typeof options.ruleSets[number]); }
      else if (path.endsWith("/electronic-periods")) { body = { ...data, status: "Generated", documents: [{ electronicPayrollDocumentId: "electronic", partyId: "party", employeeName: "Elena Prueba", documentKind: "Regular", fiscalDocumentId: "fiscal", status: "Queued", sourceHashHex: "hash" }], rowVersion: version }; options.electronicPeriods.push(body as typeof options.electronicPeriods[number]); }
      else if (path.endsWith("/settings")) { options.settings = { ...data, rowVersion: version }; body = options.settings; }
      else if (path.endsWith("/electronic-configuration")) { options.electronicConfiguration = { ...data, rowVersion: version }; body = options.electronicConfiguration; }
    } else {
      reads.push({ path, business });
      if (path === "/api/auth/me") body = user;
      else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Empresa prueba" }];
      else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Sede principal", timeZone: "America/Bogota" }, { tenantId, businessId: secondBusinessId, name: "Sede norte", timeZone: "Asia/Tokyo" }];
      else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId: business, roles: user.roles, permissions: granted };
      else if (path.endsWith("/payroll/options")) body = {...options,novelties:[]};
      else if (path.endsWith("/payroll/runs")) body = runs;
      else if (/\/payroll\/runs\/[^/]+\/employments$/.test(path)) body = {
        items: employees.map(employee => ({ ...employee, identification: "123456", payFrequencyName: "Mensual", isSelected: true })),
        page: 1, pageSize: 50, totalCount: employees.length, eligibleCount: employees.length, selectedCount: employees.length, rowVersion: version };
      else if (path.includes("/payment-methods/")) body = { items: [{ paymentMethodId: "cash-form", code: "CAJA", name: "Caja nómina", methodCode: "Cash", accountId: "cash-account", accountCode: "110501", accountName: "Caja" }], page: 1, pageSize: 20, totalCount: 1 };
      else if (path.includes("/payroll/runs/")) {
        const run=runs.find(run => path.endsWith(run.payrollRunId));
        body=run?{...run,employees:run.employees.map(employee=>({...employee,novelties:options.novelties.filter(novelty=>novelty.employmentId===employee.employmentId && novelty.startDate<=run.periodEnd && novelty.endDate>=run.periodStart)}))}:null;
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
  await (label === "Empleado" ? page.getByRole("combobox", { name: "Seleccionar employee" }) : label === "Trabajador" ? page.locator("label").filter({ has: page.locator("span", { hasText: /^Trabajador$/ }) }).getByRole("combobox") : page.getByLabel(label, { exact: true })).click();
  await page.getByRole("option", { name: option, exact: label !== "Empleado" && label !== "Trabajador" }).click();
}

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
  await expect(page.getByRole("tab")).toHaveCount(7);
  await page.getByRole("tab", { name: "Trabajadores", exact: true }).click();
  await createEmployment(page);
  await page.getByRole("tab", { name: "Liquidaciones", exact: true }).click();
  await page.getByRole("button", { name: "Nueva liquidación", exact: true }).click();
  await choose(page, "Periodicidad", "Mensual");
  await choose(page, "Reglas aprobadas", "CO-2026 · Reglas vigentes");
  await page.getByRole("button", { name: "Crear borrador" }).click();
  await expect.poll(() => state.runs.length).toBe(1);
  await page.getByRole("button").filter({ hasText: /Borrador/ }).click();
  await page.getByRole("button", { name: "Calcular nómina", exact: true }).click();
  await page.getByRole("button", { name: "Aprobar y contabilizar" }).click();
  await expect.poll(() => state.runs[0].status).toBe("Approved");
  await expect(page.getByRole("button", { name: "Recalcular", exact: true })).toHaveCount(0);
  await page.getByRole("tab", { name: "Pagos", exact: true }).click();
  await page.getByRole("button", { name: "Confirmar pago", exact: true }).click();
  await page.getByRole("combobox", { name: "Liquidación", exact: true }).click();
  await page.getByRole("option").click();
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
  await page.goto("/dashboard/payroll?section=employments");
  await createEmployment(page);
  await page.getByRole("button", { name: "Editar", exact: true }).click();
  await expect(page.getByLabel("Salario mensual", { exact: true })).toHaveValue("1000000");
  await page.keyboard.press("Escape");
  await page.getByRole("tab", { name: "Novedades", exact: true }).click();
  await page.getByRole("button", { name: "Agregar", exact: true }).first().click();
  await choose(page, "Trabajador", "Elena Prueba");
  await choose(page, "Concepto", "PRESTAMO · Préstamo empleado");
  await choose(page, "Autoridad", "Autorización escrita");
  await page.getByLabel("Referencia", { exact: true }).fill("AUT-01");
  await page.getByLabel("Evidencia (URL)", { exact: true }).fill("https://example.test/evidencia");
  await page.getByRole("button", { name: "Guardar autorización" }).click();
  await expect.poll(() => state.options.deductionAgreements.length).toBe(1);
  state.options.deductionAgreements[0].beneficiaryPartyId = "beneficiary";
  const employee=state.employees[0];
  state.runs.push({payrollRunId:"worker-run",businessId,ruleSetId:"rules",runKind:"Regular",originalPayrollRunId:null,periodStart:"2026-08-01",periodEnd:"2026-08-31",paymentDate:"2026-08-31",
    status:"Calculated",employeeCount:1,totalEarnings:1000000,totalDeductions:80000,netPayable:920000,totalEmployerContributions:0,totalProvisions:0,calculationVersion:1,rowVersion:version,payableEmployees:[],
    employees:[{payrollRunEmployeeId:"calculation-before",employmentId:employee.employmentId,partyId:employee.partyId,employeeName:"Elena Prueba",workedDays:30,baseWorkedDays:30,maximumContractDays:30,periodDays:30,
      earnings:1000000,deductions:80000,netPayable:920000,employerContributions:0,provisions:0,lines:[]}]});
  await page.getByRole("button", { name: "Actualizar nómina" }).click();
  await choose(page,"Liquidación","2026-08-01 — 2026-08-31");
  await page.getByRole("button",{name:"Elena Prueba",exact:true}).click();
  await page.getByRole("button",{name:"Agregar novedad al trabajador",exact:true}).click();
  const noveltyEditor=page.getByRole("region",{name:"Edición de novedad",exact:true});
  await expect(noveltyEditor.getByRole("textbox").first()).toHaveValue("Elena Prueba");
  await choose(page, "Concepto", "PRESTAMO · Préstamo empleado");
  await choose(page, "Tipo de novedad", "Valor");
  await expect(page.getByRole("button", { name: "Registrar novedad" })).toBeDisabled();
  await choose(page, "Acuerdo de descuento (si aplica)", "AUT-01");
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
  expect(state.reads.length).toBe(readsBefore);
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
  await page.getByRole("button", { name: "Desactivar", exact: true }).click();
  await expect.poll(() => state.options.deductionAgreements[0].isActive).toBe(false);
  expect(state.writes.at(-1)?.body.beneficiaryPartyId).toBe("beneficiary");
  await page.screenshot({ path: "test-results/payroll-novelties.png", fullPage: true });
});

test("configuración permite crear conceptos y reglas y enlaza centros de costo", async ({ page, baseURL }) => {
  const state = await setup(page, baseURL!);
  await page.goto("/dashboard/payroll?section=configuration");
  await expect(page.getByRole("link", { name: "Ver centros y asignaciones" })).toHaveAttribute("href", "/dashboard/accounting?section=centers");
  await page.getByRole("button", { name: "Agregar", exact: true }).first().click();
  await page.getByLabel("Código", { exact: true }).fill("OTRO");
  await page.getByLabel("Nombre", { exact: true }).fill("Otro descuento");
  for (const [label, option] of [["Naturaleza", "Deducción"], ["Método", "Valor fijo"], ["Tratamiento", "Deducción autorizada"], ["Categoría contable", "Préstamos a empleados"]]) await choose(page, label, option);
  await page.getByRole("button", { name: "Guardar concepto" }).click();
  await expect.poll(() => state.options.concepts.length).toBe(2);
  await page.getByRole("button", { name: "Agregar", exact: true }).last().click();
  await page.getByLabel("Código", { exact: true }).fill("NUEVAS");
  await page.getByLabel("Nombre", { exact: true }).fill("Reglas revisadas");
  await page.getByLabel("Fuente normativa", { exact: true }).fill("Fuente verificada de prueba");
  await page.getByLabel("Días del mes · Valor", { exact: true }).fill("30");
  await page.getByRole("button", { name: "Guardar borrador", exact: true }).click();
  await expect.poll(() => state.options.ruleSets.length).toBe(2);
  await page.getByRole("button", { name: "Aprobar", exact: true }).click();
  await expect.poll(() => state.options.ruleSets[1].status).toBe("Approved");
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
      await choose(page, "Reglas aprobadas", "CO-2026 · Reglas vigentes");
      const count = state.writes.length;
      await page.getByRole("button", { name: "Crear borrador", exact: true }).click();
      await expect.poll(() => state.writes.length).toBe(count + 1);
      expect(state.writes.at(-1)?.body).toMatchObject({ businessId: expectedBusiness,
        periodStart: "2026-11-01", periodEnd: "2026-11-30", paymentDate: day });
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
  await page.getByRole("combobox",{name:"Liquidación",exact:true}).click();
  await page.getByRole("option",{name:"2026-08-01 — 2026-08-31",exact:true}).click();
  await page.getByRole("button",{name:"Elena Prueba",exact:true}).click();
  const editor=page.getByRole("region",{name:"Liquidación de Elena Prueba",exact:true});
  const readsBefore=state.reads.length;
  await editor.getByRole("button",{name:"Agregar novedad al trabajador",exact:true}).click();
  const dialog=page.getByRole("region",{name:"Edición de novedad",exact:true});
  await expect(dialog.getByRole("textbox").first()).toHaveValue("Elena Prueba");
  await choose(page,"Concepto","PRESTAMO · Préstamo empleado");await choose(page,"Tipo de novedad","Valor");
  await choose(page,"Acuerdo de descuento (si aplica)","AUT-WORKER");await page.getByLabel("Valor total",{exact:true}).fill("50000");
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
    await editor.getByRole("button",{name:"Agregar novedad al trabajador",exact:true}).click();
    await dialog.getByRole("textbox",{name:"Notas",exact:true}).fill("Segunda captura sin guardar");
  } finally {release();}
  await expect(page.getByText(/Novedad guardada. No fue posible actualizar el cálculo/)).toBeVisible();
  await expect(dialog.getByRole("textbox",{name:"Notas",exact:true})).toHaveValue("Segunda captura sin guardar");
  await page.getByRole("button",{name:"Cerrar edición",exact:true}).click();
  await expect(dialog).toBeHidden();
  expect(state.options.novelties).toHaveLength(1);
  await editor.getByRole("button",{name:"Recalcular trabajador",exact:true}).click();
  await expect(editor.getByRole("row").filter({hasText:"PRESTAMO"})).toContainText("50.000");
  const calculations=state.writes.filter(item=>item.path.endsWith("/calculate"));
  expect(calculations).toHaveLength(2);
  expect(calculations[0].body).toEqual({rowVersion:version,recalculateEmploymentIds:[employee.employmentId]});
  expect(state.writes.filter(item=>item.path.endsWith("/novelties"))).toHaveLength(1);
  expect(state.reads.length).toBe(readsBefore);
});
