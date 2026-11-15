import { expect, test } from "@playwright/test";
import { financialVoucherScenario, installFinancialVoucherContext } from "./support/financial-voucher";

test.use({timezoneId:"Pacific/Kiritimati"});

for (const supplier of [false, true]) test(`cartera administrativa ${supplier ? "proveedor" : "cliente"}: destino, páginas y filtros conservan selección sin detalle adicional`, async ({page,baseURL},testInfo) => {
  await page.clock.install({ time: new Date("2026-11-12T01:30:00Z") });
  const scenario=financialVoucherScenario(), id=(n:number)=>`bbbbbbbb-1111-4111-8111-${String(n).padStart(12,"0")}`;
  scenario.user.permissions.push("receivables.read","receivables.payments.create","payables.read","payables.payments.create");
  const roleId=id(1),siteId=id(2),targetId=id(141),resource=supplier?"payables":"receivables",key=supplier?"payableId":"receivableId",roleKey=supplier?"supplierId":"customerId";
  const rows=Array.from({length:41},(_,index)=>({[key]:id(101+index),[roleKey]:roleId,
    documentNumber:index===40?"TARGET-41":`FV-${index+1}`,currencyCode:supplier&&index===1?"USD":"COP",originalAmount:index===40?216748321.12:1000,
    paidAmount:100,adjustmentAmount:50,outstandingAmount:index===40?216748171.12:850,
    sourceDocumentType:"ManualAccountingVoucher",sourceDocumentId:id(999),
    customerName:"Tercero selección",supplierName:"Tercero selección",customerIdentification:"1001",supplierIdentification:"1001",
    status:"PartiallyPaid",isOverdue:true,overdueDays:35,agingBandCode:"Overdue31To60",
    dueDate:"2026-10-05T12:00:00-05:00",issuedAt:"2026-09-01T12:00:00-05:00",createdAt:"2026-09-01T12:00:00-05:00",
    dueOn:"2026-10-05",issuedOn:"2026-09-01",
    partySiteId:siteId,partySiteName:"Principal",transactions:[{transactionId:id(600+index),type:"Opening",balanceChange:1000,occurredAt:"2026-11-12T01:30:00Z"}]}));
  const target=rows[40],requests:URL[]=[];let detailReads=0,failedOnce=false;
  await installFinancialVoucherContext(page,baseURL!,scenario);
  await page.route("**/api/**",async route=>{
    const url=new URL(route.request().url()),path=url.pathname;
    if(path==="/api/commerce/v1/accounting/settlement-configuration"){
      await route.fulfill({json:{isAccountingEnabled:false,bankAccounts:[],paymentMethods:{contractVersion:2,forms:[]}}});return;
    }
    if(path==="/api/commerce/v1/portfolio/parties/site-options"){
      await route.fulfill({json:{items:[{partySiteId:siteId,roleId,partyId:id(3),displayName:"Tercero selección",identification:"1001",siteName:"Principal",isPrimary:true}],page:1,pageSize:20,totalCount:1,totalPages:1}});return;
    }
    if(path===`/api/commerce/v1/${resource}/${targetId}`){detailReads++;await route.fulfill({json:target});return;}
    if(path===`/api/commerce/v1/${resource}`){
      const modal=url.searchParams.has(roleKey);if(modal)requests.push(url);
      const search=url.searchParams.get("search")??"";
      if(modal&&search==="fallo"&&!failedOnce){failedOnce=true;rows[0].outstandingAmount=25;rows[0].paidAmount=925;await route.fulfill({status:503,json:{title:"Temporal"}});return;}
      const filtered=!modal?[target]:url.searchParams.get(key)?[target]:search&&search!=="fallo"?rows.filter(row=>row.documentNumber.includes(search)):rows;
      const original=filtered.reduce((sum,row)=>sum+row.originalAmount,0),paid=filtered.reduce((sum,row)=>sum+row.paidAmount,0),
        outstanding=filtered.reduce((sum,row)=>sum+row.outstandingAmount,0),pageNumber=Number(url.searchParams.get("page")??1),pageSize=Number(url.searchParams.get("pageSize")??20);
      const currencyTotals=[...new Set(filtered.map(row=>row.currencyCode))].map(currencyCode=>{
        const sameCurrency=filtered.filter(row=>row.currencyCode===currencyCode);
        const originalAmount=sameCurrency.reduce((sum,row)=>sum+row.originalAmount,0),paidAmount=sameCurrency.reduce((sum,row)=>sum+row.paidAmount,0),
          outstandingAmount=sameCurrency.reduce((sum,row)=>sum+row.outstandingAmount,0);
        return {currencyCode,originalAmount,paidAmount,adjustmentAmount:originalAmount-paidAmount-outstandingAmount,outstandingAmount,overdueAmount:outstandingAmount};
      });
      await route.fulfill({json:{items:filtered.slice((pageNumber-1)*pageSize,pageNumber*pageSize),page:pageNumber,pageSize,totalCount:filtered.length,totalPages:Math.ceil(filtered.length/pageSize),
        referenceDate:"2026-11-09",totalOriginal:original,totalPaid:paid,totalAdjustments:original-paid-outstanding,totalOutstanding:outstanding,totalOverdue:outstanding,
        currencyTotals}});return;
    }
    await route.fallback();
  });
  await page.goto(`/dashboard/${resource}`);
  await page.getByRole("tab",{name:"Facturas",exact:true}).click();
  await page.getByRole("row",{name:/TARGET-41/}).click();
  const detail=page.getByRole("dialog",{name:"TARGET-41",exact:true});await expect(detail).toBeVisible();
  const dueLabel = new Intl.DateTimeFormat("es-CO", {timeZone:"America/Bogota",year:"numeric",month:"short",day:"numeric"}).format(new Date(target.dueDate));
  const movementLabel = new Intl.DateTimeFormat("es-CO", {timeZone:"America/Bogota",year:"numeric",month:"short",day:"numeric",hour:"2-digit",minute:"2-digit"}).format(new Date("2026-11-12T01:30:00Z"));
  await expect(detail.getByText(dueLabel,{exact:true})).toBeVisible();
  await expect(detail.getByText(movementLabel,{exact:true})).toBeVisible();
  await detail.getByRole("button",{name:supplier?"Registrar pago":"Registrar abono",exact:true}).click();
  const modal=page.getByRole("dialog",{name:supplier?"Pago a proveedores":"Abono a cartera",exact:true});
  await expect(modal.getByLabel("Abono para TARGET-41")).toBeVisible();
  expect(detailReads).toBe(1);expect(requests).toHaveLength(1);expect(requests[0].searchParams.get(key)).toBe(targetId);
  await expect(modal.getByText("31–60 días",{exact:true})).toBeVisible();
  await expect(modal.getByText("Comprobante manual",{exact:true})).toBeVisible();
  await expect(modal.getByText("05/10/2026",{exact:true})).toBeVisible();
  await expect(modal.getByText("01/09/2026",{exact:true})).toBeVisible();
  await expect(modal.getByRole("button",{name:"Aplicar filtros",exact:true})).toHaveCount(0);
  const targetRow=modal.getByRole("group",{name:"Documento TARGET-41",exact:true});
  expect(await targetRow.evaluate(element=>element.scrollWidth<=element.clientWidth)).toBe(true);
  const original=targetRow.locator("span").filter({hasText:/216[.,]748[.,]321/}).first();
  expect(await original.evaluate(element=>element.scrollWidth<=element.clientWidth)).toBe(true);
  const band=await targetRow.getByText("31–60 días",{exact:true}).boundingBox();
  const originalBox=await original.boundingBox();
  expect(band!.x).toBeLessThan(originalBox!.x);
  await page.setViewportSize({width:390,height:844});
  expect(await modal.evaluate(element=>element.scrollWidth<=element.clientWidth)).toBe(true);
  expect(await targetRow.evaluate(element=>element.scrollWidth<=element.clientWidth)).toBe(true);
  await targetRow.scrollIntoViewIfNeeded();
  await page.screenshot({path:testInfo.outputPath("administrative-selection-mobile.png"),fullPage:true});
  await page.setViewportSize({width:1366,height:900});
  await targetRow.scrollIntoViewIfNeeded();
  await page.screenshot({path:testInfo.outputPath("administrative-selection-target.png"),fullPage:true});
  await modal.getByLabel("Abono para TARGET-41").fill("125");
  await modal.getByRole("button",{name:"Mostrar todas las facturas",exact:true}).click();
  await expect(modal.getByLabel("Abono para FV-1",{exact:true})).toBeVisible();
  expect(requests).toHaveLength(2);expect(requests[1].searchParams.has(key)).toBe(false);
  if(supplier){
    await expect(modal.getByRole("checkbox",{name:"Seleccionar FV-2",exact:true})).toBeDisabled();
    await expect(modal.getByLabel("Abono para FV-2",{exact:true})).toBeDisabled();
    await expect(modal.getByText("Pago no disponible en USD en este formulario.",{exact:true})).toBeVisible();
  }
  await modal.getByLabel("Abono para FV-1",{exact:true}).fill("50");
  await modal.getByRole("button",{name:"Siguiente",exact:true}).click();
  await expect(modal.getByLabel("Abono para FV-21",{exact:true})).toBeVisible();
  await modal.getByLabel("Abono para FV-21",{exact:true}).fill("75");
  await modal.getByRole("button",{name:"Siguiente",exact:true}).click();
  await expect(modal.getByLabel("Abono para TARGET-41")).toHaveValue("125");
  const amount=modal.getByRole("checkbox",{name:"Seleccionar TARGET-41",exact:true}).locator("..").locator("span").filter({hasText:/216[.,]748[.,]321/}).first();
  await expect(amount).toBeVisible();
  expect(await amount.evaluate(element=>element.scrollWidth<=element.clientWidth)).toBe(true);
  await expect(modal.getByText("3 facturas · 2 fuera de esta página",{exact:true})).toBeVisible();
  expect(requests).toHaveLength(4);
  const filter=modal.getByRole("group",{name:"Filtros de facturas para pago"});
  await filter.getByLabel("Documento o referencia",{exact:true}).fill("FV-");
  await filter.getByLabel("Documento o referencia",{exact:true}).fill("FV-1");expect(requests).toHaveLength(4);
  await expect(modal.getByRole("button",{name:"Ir a pagar",exact:true})).toBeDisabled();
  await page.clock.runFor(300);
  await expect(modal.getByLabel("Abono para FV-1",{exact:true})).toHaveValue("50");
  expect(requests).toHaveLength(5);expect(requests[4].searchParams.get("page")).toBe("1");expect(requests[4].searchParams.get("search")).toBe("FV-1");
  await modal.getByRole("button",{name:"Ir a pagar",exact:true}).click();
  await modal.getByRole("button",{name:"Atrás",exact:true}).click();
  await expect(modal.getByLabel("Abono para FV-1",{exact:true})).toHaveValue("50");
  expect(requests).toHaveLength(5);
  await filter.getByLabel("Documento o referencia",{exact:true}).fill("sin coincidencias");
  await page.clock.runFor(300);
  await expect(modal.getByText("No hay facturas pendientes que coincidan con los filtros.",{exact:true})).toBeVisible();
  await expect(modal.getByText("3 facturas · 3 fuera de esta página",{exact:true})).toBeVisible();
  expect(requests).toHaveLength(6);
  await filter.getByLabel("Documento o referencia",{exact:true}).fill("fallo");
  await page.clock.runFor(300);
  await expect(modal.getByRole("button",{name:"Ir a pagar",exact:true})).toBeDisabled();
  await modal.getByRole("button",{name:"Reintentar cartera",exact:true}).click();
  await expect(modal.getByLabel("Abono para FV-1",{exact:true})).toHaveValue("50");
  expect(requests).toHaveLength(8);expect(detailReads).toBe(1);
  await modal.getByRole("button",{name:"Ir a pagar",exact:true}).click();
  await expect(page.getByText("Ningún abono puede superar el saldo de la factura.",{exact:true})).toBeVisible();
  for (const label of ["Fecha del pago", "Registro desde", "Registro hasta"]) {
    const date = modal.getByRole("button", {name:label,exact:true});
    await date.click();
    await modal.getByRole("button", {name:"Elegir hoy",exact:true}).click();
    await expect(date).toContainText("11 de noviembre de 2026");
    if(label!=="Fecha del pago")await expect.poll(()=>requests.at(-1)?.searchParams.get(label==="Registro desde"?"from":"to")).toBe("2026-11-11");
  }
  await expect.poll(()=>requests.length).toBe(10);expect(detailReads).toBe(1);
  await filter.getByRole("checkbox",{name:"Sólo vencidas",exact:true}).check();
  await expect.poll(()=>requests.length).toBe(11);
  expect(requests.at(-1)!.searchParams.get("overdue")).toBe("true");
  await modal.getByRole("button",{name:"Registro desde",exact:true}).click();
  await modal.getByRole("button",{name:"12 de noviembre de 2026",exact:true}).click();
  await page.clock.runFor(300);
  expect(requests).toHaveLength(11);
  await expect(modal.getByRole("alert")).toContainText("La fecha inicial no puede ser posterior");
  await expect(modal.getByRole("button",{name:"Ir a pagar",exact:true})).toBeDisabled();
  await modal.getByRole("button",{name:"Registro desde",exact:true}).click();
  await modal.getByRole("button",{name:"10 de noviembre de 2026",exact:true}).click();
  await expect.poll(()=>requests.length).toBe(12);
  await expect(modal.getByRole("alert")).toHaveCount(0);
  await page.screenshot({path:testInfo.outputPath("administrative-selection.png"),fullPage:true});
});
