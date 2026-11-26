import { randomUUID } from "node:crypto";
import { expect, test } from "@playwright/test";
import { realAccountingWorkspace, realPartyRequest, waitForRealPosting } from "./support/real-accounting-workspace";
import type { PayrollEmployment, PayrollEmploymentPage, PayrollOptions, PayrollProfile, PayrollRun, PayrollRunPage, PayrollProfilePage, PayrollRuleSet, PayrollNovelty } from "../src/services/api/payroll";
import type { AccountingEntry } from "../src/services/api/accounting";

test.use({ actionTimeout: 30_000 });

test("nómina real: perfil contractual, selección, días por concepto y causación desde la vista", async ({ page }) => {
  test.setTimeout(360_000);
  const context = await realAccountingWorkspace(page);
  const { scopedJson } = context;
  const today = new Intl.DateTimeFormat("sv-SE", { timeZone: context.timeZone }).format(new Date());
  const path = "/api/commerce/v1/payroll";
  const options = await scopedJson<PayrollOptions>("GET", `${path}/options`);
  const existingRuns = (await scopedJson<PayrollRunPage>("GET", `${path}/runs?page=1&pageSize=50&kind=Regular`)).items;
  const periods = Array.from({ length: 12 }, (_, offset) => {
    const month = Date.UTC(Number(today.slice(0, 4)), Number(today.slice(5, 7)) - 1 + offset, 1);
    return { start: new Date(month).toISOString().slice(0, 10),
      end: new Date(Date.UTC(new Date(month).getUTCFullYear(), new Date(month).getUTCMonth() + 1, 0)).toISOString().slice(0, 10) };
  });
  const period = periods.find(candidate => candidate.start>=today && !existingRuns.some(run => run.runKind === "Regular" && run.periodStart === candidate.start && run.periodEnd === candidate.end));
  expect(period, "Se necesita un período regular libre en el entorno de pruebas.").toBeTruthy();
  const periodStart = period!.start;
  const accountingPeriods = await scopedJson<Array<{ startsOn: string; endsOn: string; status: string }>>("GET", "/api/commerce/v1/accounting/periods");
  if (!accountingPeriods.some(value => value.status === "Open" && value.startsOn <= period!.end && value.endsOn >= period!.end)) {
    await scopedJson("POST", "/api/commerce/v1/accounting/periods", { periodId: randomUUID(), tenantId: process.env.AURALY_E2E_TENANT_ID,
      startsOn: periodStart, endsOn: period!.end, name: `Período QA nómina ${periodStart}` });
  }
  const contracts = (await scopedJson<PayrollEmploymentPage>("GET", `${path}/employments?page=1&pageSize=50`)).items;
  const baseProfileId=contracts.find(value=>value.configuration?.values.accountingProfileId)?.configuration?.values.accountingProfileId
    ??(await scopedJson<PayrollProfilePage>("GET",`${path}/profiles?page=1&pageSize=1&isActive=true`)).items[0]?.profileId;
  expect(baseProfileId,"DEV necesita un perfil de prueba configurado.").toBeTruthy();
  const rules=(await scopedJson<{items:PayrollRuleSet[]}>("GET",`${path}/profiles/${baseProfileId}/rule-sets?page=1&pageSize=20`)).items.find(rule=>rule.status==="Approved");
  expect(rules).toBeTruthy();
  const tag = `QA-${randomUUID().slice(0, 8)}`;
  const centerId=randomUUID();
  await scopedJson("POST","/api/commerce/v1/accounting/cost-centers",{
    costCenterId:centerId,tenantId:process.env.AURALY_E2E_TENANT_ID,code:tag,name:`Centro ${tag}`,parentCostCenterId:null,isDefault:false,
  });

  await page.goto("/dashboard/payroll?section=configuration");
  await page.getByRole("button", { name: "Nuevo perfil", exact: true }).click();
  const profileCard = page.getByRole("dialog", { name: "Nuevo perfil de nómina", exact: true });
  await expect(profileCard.getByRole("textbox", { name: "Código", exact: true })).toHaveCount(0);
  await profileCard.getByRole("textbox", { name: "Nombre", exact: true }).fill(`Perfil ${tag}`);
  await profileCard.getByLabel("Fuente normativa",{exact:true}).fill(rules!.sourceReference);
  for(const definition of options.catalogs["payroll-rule-parameter"]){
    const parameter=rules!.parameters.find(value=>value.code===definition.code);
    expect(parameter,`El perfil base necesita el parámetro ${definition.label}`).toBeTruthy();
    const unit=definition.metadataCode==="Rate"?"%":definition.metadataCode==="COP"?"COP":definition.metadataCode==="Days"?"Días":definition.metadataCode==="Multiple"?"Veces":"Valor";
    const value=definition.metadataCode==="Rate"?Number((parameter!.numericValue*100).toFixed(6)):parameter!.numericValue;
    await profileCard.getByLabel(`${definition.label} · ${unit}`,{exact:true}).fill(String(value));
  }
  const savingProfile = page.waitForResponse(response => response.request().method() === "PUT" && response.url().includes("/payroll/profiles/"));
  await profileCard.getByRole("button", { name: "Guardar perfil", exact: true }).click();
  const profileResponse = await savingProfile;
  expect(profileResponse.ok(), await profileResponse.text()).toBe(true);
  let profile = await profileResponse.json() as PayrollProfile;
  expect(profile.code).toBe(profile.profileId.replaceAll("-", "").toUpperCase());

  // Seven independent synthetic masters; geography is read once for the batch.
  const prototype = await realPartyRequest(context, `Entidad ${tag}`);
  const institutions: string[] = [];
  for (const label of ["EPS", "Pensión", "Cesantías", "ARL", "Caja", "SENA", "ICBF"]) {
    const name = `${label} ${tag}`;
    const created = await scopedJson<{ partyId: string }>("POST", "/api/commerce/v1/suppliers", {
      ...prototype, operationId: randomUUID(),
      party: { ...prototype.party, identification: String(800000000 + Math.floor(Math.random() * 100000000)), displayName: name, legalName: name },
    });
    institutions.push(created.partyId);
  }
  profile = await scopedJson<PayrollProfile>("PUT", `${path}/profiles/${profile.profileId}`, {
    ...profile, mappings: ["Sena", "Icbf"].map((role, index) => {
      const concept = options.concepts.find(value => value.systemRoleCode === role);
      expect(concept).toBeTruthy();
      return { mappingId: randomUUID(), conceptId: concept!.conceptId, debitAccountId: null, creditAccountId: null,
        beneficiaryPartyId: institutions[index + 5], effectiveFrom: rules!.effectiveFrom, effectiveTo: null };
    }),
  });
  const employees: PayrollEmployment[] = [];
  const methodOptions = options.catalogs["payroll-payment-method"];
  for (const method of ["Cash", "Transfer"]) {
    const methodOption = methodOptions.find(value => value.code === method)!;
    const template = contracts.find(value => value.paymentMethodOptionId === methodOption.optionId && value.isActive)!;
    expect(template).toBeTruthy();
    const name = `${tag} ${method}`;
    const identity = await scopedJson<{ partyId: string }>("POST", "/api/commerce/v1/parties/identity", {
      ...prototype, operationId: randomUUID(), targetRole: "Employee", party: { ...prototype.party,
        partyType: "NaturalPerson", identificationTypeCode: options.catalogs["payroll-identification-type"].find(value => value.code === "CC")!.code,
        identification: String(1000000000 + Math.floor(Math.random() * 900000000)),
        displayName: name, legalName: null, firstName: tag, lastName: method },
    });
    const employeeRole = await scopedJson<{ employeeId: string }>("POST", "/api/employees", {
      businessId: context.businessId, name, partyId: identity.partyId, serviceIds: [],
    });
    employees.push(await scopedJson<PayrollEmployment>("PUT", `${path}/employments/${identity.partyId}`, {
      ...template, employmentId: identity.partyId, partyId: identity.partyId, employeeId: employeeRole.employeeId,
      contractNumber: `${tag}-${method}`, startDate: periodStart, endDate: null, rowVersion: null, appliesTransportAllowance: true,
      configuration: { accountingProfileId: profile.profileId, healthPartyId: institutions[0], pensionPartyId: institutions[1],
        severancePartyId: institutions[2], riskPartyId: institutions[3], compensationPartyId: institutions[4], costCenterId: centerId,
        details:{...template.configuration?.values.details,workSite:method==="Cash"?`Zona_%[A]~ ${tag}`:`Zona ${tag}`} },
    }));
  }

  await page.goto("/dashboard/payroll");
  await page.getByRole("button", { name: "Nueva liquidación", exact: true }).click();
  const creating = page.getByRole("dialog", { name: "Nueva liquidación", exact: true });
  await creating.getByRole("combobox", { name: "Periodicidad", exact: true }).click();
  await page.getByRole("option", { name: "Mensual", exact: true }).click();
  const monthOffset = (Number(periodStart.slice(0, 4)) - Number(today.slice(0, 4))) * 12 + Number(periodStart.slice(5, 7)) - Number(today.slice(5, 7));
  for (const [label, day] of [["Inicio", periodStart], ["Fin", period!.end], ["Fecha de pago", period!.end]]) {
    if (monthOffset === 0 && label !== "Fecha de pago") continue;
    await creating.getByText(label, { exact: true }).locator("..").getByRole("button").click();
    for (let month = 0; month < monthOffset; month++) await creating.getByRole("button", { name: "Mes siguiente", exact: true }).click();
    const dateLabel = new Intl.DateTimeFormat("es-CO", { dateStyle: "long", timeZone: "UTC" }).format(new Date(`${day}T12:00:00Z`));
    await creating.getByRole("button", { name: dateLabel, exact: true }).click();
  }
  const creatingRun = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith("/payroll/runs"));
  await creating.getByRole("button", { name: "Crear borrador", exact: true }).click();
  const createdResponse = await creatingRun;
  expect(createdResponse.ok(), await createdResponse.text()).toBe(true);
  let run = await createdResponse.json() as PayrollRun;
  expect(run.periodStart).toBe(periodStart);
  await page.getByRole("button", { name: new RegExp(`^${run.periodStart} — ${run.periodEnd} Borrador`) }).click();
  const selection = page.getByRole("region", { name: "Trabajadores de la liquidación", exact: true });
  await expect(selection.getByRole("checkbox", { name: "Incluir todos los trabajadores elegibles", exact: true })).toBeChecked();
  await selection.getByRole("checkbox", { name: "Incluir todos los trabajadores elegibles", exact: true }).uncheck();
  await selection.getByRole("combobox",{name:"Filtrar trabajadores por centro de costo",exact:true}).click();
  await page.getByPlaceholder("Buscar centro por código o nombre…").fill(tag);
  await page.getByRole("option",{name:`${tag} · Centro ${tag}`,exact:true}).click();
  await expect(selection.locator("tbody").getByRole("checkbox")).toHaveCount(2);
  await selection.getByRole("textbox",{name:"Filtrar sitio de trabajo",exact:true}).fill("_%[A]~");
  await selection.getByRole("button",{name:"Buscar",exact:true}).click();
  await expect(selection.locator("tbody").getByRole("checkbox")).toHaveCount(1);
  await selection.getByRole("checkbox",{name:`Incluir ${employees[0].employeeName}, contrato ${employees[0].contractNumber}`,exact:true}).check();
  await page.setViewportSize({width:390,height:844});
  await selection.getByRole("heading",{name:"Seleccionar trabajadores",exact:true}).scrollIntoViewIfNeeded();
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=document.documentElement.clientWidth)).toBe(true);
  await page.screenshot({path:"../work/release-preparation-current/dev-native-payroll-mobile-selection.png",fullPage:true});
  await page.setViewportSize({width:1366,height:768});
  await selection.getByRole("textbox",{name:"Filtrar sitio de trabajo",exact:true}).fill("");
  await selection.getByRole("button",{name:"Buscar",exact:true}).click();
  await expect(selection.locator("tbody").getByRole("checkbox")).toHaveCount(2);
  await expect(selection.getByRole("checkbox",{name:`Incluir ${employees[0].employeeName}, contrato ${employees[0].contractNumber}`,exact:true})).toBeChecked();
  await selection.getByRole("checkbox",{name:`Incluir ${employees[1].employeeName}, contrato ${employees[1].contractNumber}`,exact:true}).check();
  const calculating = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith(`/runs/${run.payrollRunId}/calculate`));
  await selection.getByRole("button", { name: "Calcular nómina", exact: true }).click();
  const calculationResponse = await calculating;
  expect(calculationResponse.ok(), await calculationResponse.text()).toBe(true);
  run = await calculationResponse.json() as PayrollRun;
  expect(run.employees).toHaveLength(2);
  expect(run.employees.every(employee => employee.baseWorkedDays === 30)).toBe(true);
  await test.info().attach("native-payroll-fixture", { body: JSON.stringify({ profileId: profile.profileId,
    payrollRunId: run.payrollRunId, employmentIds: employees.map(employee => employee.employmentId), periodStart }), contentType: "application/json" });
  await page.getByRole("dialog",{name:"Detalle de liquidación",exact:true}).getByRole("button",{name:"Cerrar ventana",exact:true}).click();
  await page.getByRole("tab", { name: "Novedades", exact: true }).click();
  const calculatedWorkers=page.getByRole("region", { name: "Trabajadores liquidados", exact: true });
  const history=page.getByRole("region",{name:"Liquidaciones para novedades",exact:true});
  await history.getByRole("combobox",{name:"Buscar liquidación",exact:true}).click();
  await page.getByPlaceholder("Buscar período (AAAA-MM-DD)…",{exact:true}).fill(run.periodStart);
  await page.getByRole("option").filter({hasText:`${run.periodStart} — ${run.periodEnd}`}).click();
  await calculatedWorkers.getByRole("textbox",{name:"Buscar trabajador liquidado",exact:true}).fill(tag);
  await calculatedWorkers.getByRole("button",{name:"Buscar",exact:true}).click();
  await calculatedWorkers.getByRole("button", { name: `Ver trabajador ${employees[0].employeeName}`, exact: true }).click();
  const transport = options.concepts.find(concept => concept.systemRoleCode === "TransportAllowance")!;
  expect(transport).toBeTruthy();
  await page.getByRole("button", { name: `Editar días de ${transport.name}`, exact: true }).click();
  await page.getByRole("spinbutton", { name: `Días trabajados de ${transport.name}`, exact: true }).fill("25");
  const changingDays = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith(`/runs/${run.payrollRunId}/calculate`));
  await page.getByRole("button", { name: `Guardar días de ${transport.name}`, exact: true }).click();
  const daysResponse = await changingDays;
  expect(daysResponse.ok(), await daysResponse.text()).toBe(true);
  run = await scopedJson<PayrollRun>("GET", `${path}/runs/${run.payrollRunId}`);
  const worker = run.employees.find(employee => employee.employmentId === employees[0].employmentId)!;
  expect(worker.lines.find(line => line.conceptId === transport.conceptId)!.quantity).toBe(25);
  expect(worker.baseWorkedDays).toBe(30);
  const bonus=options.concepts.find(concept=>concept.isActive&&concept.natureCode==="Earning"&&!concept.systemRoleCode&&!concept.requiresDeductionAgreement&&concept.calculationMethodCode==="FixedAmount");
  expect(bonus,"DEV requiere un concepto sintético de bonificación para el ensayo.").toBeTruthy();
  await page.getByRole("button",{name:"Agregar novedad al trabajador",exact:true}).click();
  let noveltyForm=page.getByRole("region",{name:"Edición de novedad",exact:true});
  await noveltyForm.getByRole("combobox",{name:"Concepto",exact:true}).click();
  await page.getByRole("option",{name:`${bonus!.code} · ${bonus!.name}`,exact:true}).click();
  await noveltyForm.getByRole("combobox",{name:"Tipo de novedad",exact:true}).click();
  const amountType=options.catalogs["payroll-novelty-type"].find(option=>option.code==="Amount")!;
  await page.getByRole("option",{name:amountType.label,exact:true}).click();
  await noveltyForm.getByLabel("Valor total",{exact:true}).fill("10000");
  await noveltyForm.getByLabel("Notas",{exact:true}).fill(`Novedad ${tag}`);
  const addingNovelty=page.waitForResponse(response=>response.request().method()==="POST"&&response.url().endsWith("/payroll/novelties"));
  const calculatingNovelty=page.waitForResponse(response=>response.request().method()==="POST"&&response.url().endsWith(`/runs/${run.payrollRunId}/calculate`));
  await noveltyForm.getByRole("button",{name:"Registrar novedad",exact:true}).click();
  const addedResponse=await addingNovelty;expect(addedResponse.ok(),await addedResponse.text()).toBe(true);
  const novelty=await addedResponse.json() as PayrollNovelty;expect((await calculatingNovelty).ok()).toBe(true);
  await page.getByRole("button",{name:"Editar novedad",exact:true}).click();
  noveltyForm=page.getByRole("region",{name:"Edición de novedad",exact:true});await noveltyForm.getByLabel("Valor total",{exact:true}).fill("15000");
  const editingNovelty=page.waitForResponse(response=>response.request().method()==="PUT"&&response.url().endsWith(`/novelties/${novelty.noveltyId}`));
  const recalculatingNovelty=page.waitForResponse(response=>response.request().method()==="POST"&&response.url().endsWith(`/runs/${run.payrollRunId}/calculate`));
  await noveltyForm.getByRole("button",{name:"Guardar novedad",exact:true}).click();expect((await editingNovelty).ok()).toBe(true);expect((await recalculatingNovelty).ok()).toBe(true);
  run=await scopedJson<PayrollRun>("GET",`${path}/runs/${run.payrollRunId}`);
  expect(run.employees.find(worker=>worker.employmentId===employees[0].employmentId)!.novelties!.find(item=>item.noveltyId===novelty.noveltyId)!.totalAmount).toBe(15000);
  await page.reload();
  const reloadedHistory=page.getByRole("region",{name:"Liquidaciones para novedades",exact:true});
  await reloadedHistory.getByRole("combobox",{name:"Buscar liquidación",exact:true}).click();await page.getByPlaceholder("Buscar período (AAAA-MM-DD)…",{exact:true}).fill(run.periodStart);
  await page.getByRole("option").filter({hasText:`${run.periodStart} — ${run.periodEnd}`}).click();
  const reloadedWorkers=page.getByRole("region",{name:"Trabajadores liquidados",exact:true});
  await reloadedWorkers.getByRole("textbox",{name:"Buscar trabajador liquidado",exact:true}).fill(tag);await reloadedWorkers.getByRole("button",{name:"Buscar",exact:true}).click();
  await reloadedWorkers.getByRole("button",{name:`Ver trabajador ${employees[0].employeeName}`,exact:true}).click();
  await page.getByRole("button",{name:"Editar novedad",exact:true}).click();
  await expect(page.getByRole("region",{name:"Edición de novedad",exact:true}).getByLabel("Valor total",{exact:true})).toHaveValue("15 000");
  await page.getByRole("button",{name:"Cerrar edición",exact:true}).click();
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);
  await page.screenshot({ path: "../work/release-preparation-current/dev-native-payroll-mobile-days.png", fullPage: true });
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.getByRole("tab", { name: "Liquidaciones", exact: true }).click();
  await page.getByRole("button").filter({hasText:`${run.periodStart} — ${run.periodEnd}`}).click();
  const approving = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith(`/runs/${run.payrollRunId}/approve`));
  await page.getByRole("button", { name: "Aprobar y contabilizar", exact: true }).click();
  const approved = await approving;
  expect(approved.ok(), await approved.text()).toBe(true);
  await waitForRealPosting(context, run.payrollRunId);
  const entry = await scopedJson<AccountingEntry>("GET", `/api/commerce/v1/accounting/entries/by-document/${run.payrollRunId}`);
  expect(entry.debitTotal).toBe(entry.creditTotal);
  expect(entry.debitTotal).toBeGreaterThan(0);
  console.info("Perfil contractual, selección, días por concepto y causación real verificados.");
  await page.screenshot({ path: "../work/release-preparation-current/dev-native-payroll-posted.png", fullPage: true });
  await page.getByRole("dialog",{name:"Detalle de liquidación",exact:true}).getByRole("button",{name:"Cerrar ventana",exact:true}).click();
  await page.getByRole("tab", { name: "Pagos", exact: true }).click();
  await page.getByRole("button", { name: "Confirmar pago", exact: true }).click();
  const paying = page.getByRole("dialog", { name: "Confirmar lote de pago", exact: true });
  await paying.getByRole("combobox", { name: "Liquidación", exact: true }).click();
  const total = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 }).format(run.netPayable);
  await page.getByRole("option").filter({hasText:`${run.periodStart} — ${run.periodEnd}`}).filter({hasText:`Neto liquidado ${total}`}).click();
  const refreshingPending = page.waitForResponse(response => response.request().method() === "GET" && response.url().includes(`/payroll/runs/${run.payrollRunId}`));
  await paying.getByRole("button", { name: "Actualizar pendientes", exact: true }).click();
  expect((await refreshingPending).ok()).toBe(true);
  for (const method of ["Cash", "Transfer"]) {
    const definition = methodOptions.find(value => value.code === method)!;
    const group = paying.getByRole("region", { name: `Grupo ${definition.label}`, exact: true });
    await group.getByRole("button", { name: new RegExp(definition.label) }).click();
    await group.getByRole("combobox", { name: "Forma de pago", exact: true }).click();
    await page.getByPlaceholder("Buscar forma de pago…").fill(method === "Cash" ? "QA-RC299-CASH" : "QA-RC299-TRANSFER");
    await page.getByRole("option").filter({ hasText: method === "Cash" ? "QA-RC299-CASH" : "QA-RC299-TRANSFER" }).click();
    await group.getByRole("button", { name: "Fecha de pago", exact: true }).click();
    for (let month = 0; month < monthOffset; month++) await paying.getByRole("button", { name: "Mes siguiente", exact: true }).click();
    await paying.getByRole("button", { name: new Intl.DateTimeFormat("es-CO", { dateStyle: "long", timeZone: "UTC" }).format(new Date(`${period!.end}T12:00:00Z`)), exact: true }).click();
    await group.getByRole("textbox", { name: "Referencia bancaria o de caja", exact: true }).fill(`${tag}-${method}`);
    const paymentResponse = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith("/payroll/payments"));
    await group.getByRole("button", { name: "Confirmar pago del grupo", exact: true }).click();
    const accepted = await paymentResponse;
    expect(accepted.ok(), await accepted.text()).toBe(true);
    const batch = await accepted.json() as { paymentBatchId: string; employeeCount: number; totalAmount: number };
    expect(batch.employeeCount).toBe(1);
    await waitForRealPosting(context, batch.paymentBatchId);
    const paymentEntry = await scopedJson<AccountingEntry>("GET", `/api/commerce/v1/accounting/entries/by-document/${batch.paymentBatchId}`);
    expect(paymentEntry.debitTotal).toBe(batch.totalAmount);
    expect(paymentEntry.creditTotal).toBe(batch.totalAmount);
    const replay = await scopedJson<{ paymentBatchId: string }>("POST", `${path}/payments`, accepted.request().postDataJSON());
    expect(replay.paymentBatchId).toBe(batch.paymentBatchId);
    console.info(`Pago de nómina ${method}: asiento balanceado y replay sin duplicados verificados.`);
  }
  const paid = await scopedJson<PayrollRun>("GET", `${path}/runs/${run.payrollRunId}`);
  expect(paid.payableEmployees).toEqual([]);
  await page.screenshot({ path: "../work/release-preparation-current/dev-native-payroll-paid.png", fullPage: true });
});
