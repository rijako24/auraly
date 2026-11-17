import { expect, test } from "@playwright/test";
import { financialVoucherScenario, installFinancialVoucherContext } from "./support/financial-voucher";

test.use({ timezoneId: "Pacific/Kiritimati" });

for (const reopening of [false, true]) test(`compras: fechas de sede al ${reopening ? "reabrir un instante preciso" : "crear y guardar una orden"}`, async ({page,baseURL}) => {
  await page.clock.install({time:new Date("2026-03-07T15:30:00Z")});
  const scenario=financialVoucherScenario();scenario.timeZone="America/New_York";
  scenario.user.permissions.push("purchasing.purchase-orders.read","purchasing.purchase-orders.create");
  await installFinancialVoucherContext(page,baseURL!,scenario);
  const original="2026-03-07T10:30:19.1234567-05:00", expected="2026-03-14T10:30:22.7654321-04:00";
  let saved:Record<string,unknown>|null=reopening?{purchaseOrderId:scenario.documentId,businessId:scenario.businessId,
    orderedAt:original,expectedAt:expected,status:"Draft",documentNumber:"BORRADOR-1",supplierName:"Proveedor",
    warehouseName:"Principal",grandTotal:0,fulfillmentPercent:0,concurrencyToken:"version-1",lines:[]}:null;
  const writes:Array<Record<string,unknown>>=[];let contextReads=0;
  await page.route("**/api/**",async route=>{
    const request=route.request(),path=new URL(request.url()).pathname;
    if(path.endsWith("/execution-context/businesses"))contextReads++;
    if(path.endsWith(`/businesses/${scenario.businessId}`)){await route.fulfill({json:{businessId:scenario.businessId,name:"Sede",pointOfSaleMode:"Retail"}});return;}
    if(path.endsWith("/goods-receipts/options")){await route.fulfill({json:{warehouses:[],suppliers:[],purchaseEvidenceTypes:[]}});return;}
    if(path.endsWith("/purchase-orders")||path.endsWith("/draft")){
      if(request.method()==="GET")await route.fulfill({json:{items:saved?[saved]:[],page:1,pageSize:25,totalCount:saved?1:0,totalPages:saved?1:0}});
      else{
        const body=request.postDataJSON() as Record<string,unknown>;writes.push(body);
        saved={...body,status:"Draft",documentNumber:"BORRADOR-1",supplierName:"Proveedor",warehouseName:"Principal",
          grandTotal:0,fulfillmentPercent:0,concurrencyToken:"version-1"};
        await route.fulfill({json:saved});
      }return;
    }
    if(path.endsWith(`/purchase-orders/${scenario.documentId}`)||saved&&path.endsWith(`/purchase-orders/${saved.purchaseOrderId}`)){
      await route.fulfill({json:saved});return;
    }
    await route.fallback();
  });
  await page.goto("/dashboard/purchasing/purchase-orders");
  if(reopening)await page.getByRole("row").filter({hasText:"BORRADOR-1"}).click();
  else await page.getByRole("button",{name:"Nueva orden",exact:true}).click();
  const dialog=page.getByRole("dialog");
  const field=(name:string)=>dialog.getByText(name,{exact:true}).locator("..").getByRole("button").first();
  await expect(field("Fecha de orden")).toContainText("7 de marzo de 2026");
  await expect(field("Entrega esperada")).toContainText("14 de marzo de 2026");
  await dialog.getByRole("button",{name:"Guardar borrador",exact:true}).click();
  await expect.poll(()=>writes.length).toBe(1);
  expect(writes[0].orderedAt).toBe(reopening?original:"2026-03-07T10:30:00-05:00");
  expect(writes[0].expectedAt).toBe(reopening?expected:"2026-03-14T10:30:00-04:00");
  await dialog.getByRole("button",{name:"Cerrar",exact:true}).click();
  await page.getByRole("row").filter({hasText:"BORRADOR-1"}).click();
  await expect(field("Fecha de orden")).toContainText("7 de marzo de 2026");
  await expect(field("Entrega esperada")).toContainText("14 de marzo de 2026");
  await dialog.getByRole("button",{name:"Guardar borrador",exact:true}).click();
  await expect.poll(()=>writes.length).toBe(2);
  expect(writes[1].orderedAt).toBe(writes[0].orderedAt);
  expect(writes[1].expectedAt).toBe(writes[0].expectedAt);
  expect(contextReads).toBe(1);
});

test("recepción: fecha del proveedor y plazo usan la sede; la recepción conserva su instante",async({page,baseURL})=>{
  const now="2026-03-08T16:00:00.000Z";
  await page.clock.setFixedTime(new Date(now));
  const scenario=financialVoucherScenario();scenario.timeZone="America/New_York";
  scenario.user.permissions.push("purchasing.goods-receipts.read","purchasing.goods-receipts.create");
  await installFinancialVoucherContext(page,baseURL!,scenario);
  let submitted:Record<string,unknown>|null=null;
  await page.route("**/api/**",async route=>{
    const path=new URL(route.request().url()).pathname;
    if(path.includes("/goods-receipts/drafts/")&&route.request().method()==="PUT"){
      submitted=route.request().postDataJSON();
      await route.fulfill({json:{draftId:submitted!.draftId,concurrencyToken:"AAAAAAAAAAE="}});return;
    }
    if(path.endsWith("/goods-receipts")||path.endsWith("/purchase-orders")){
      await route.fulfill({json:{items:[],page:1,pageSize:25,totalCount:0,totalPages:0}});return;
    }
    if(path.endsWith("/tenant-commercial/subscription")){await route.fulfill({json:null});return;}
    if(path.endsWith("/parties/site-options")){await route.fulfill({json:{items:[],page:1,pageSize:10,totalCount:0,totalPages:0}});return;}
    if(path.endsWith("/goods-receipts/options")){
      await route.fulfill({json:{warehouses:[],suppliers:[],purchaseEvidenceTypes:[],withholdingConcepts:[],withholdingJurisdictions:[],
        purchaseCostEvidenceTypes:[],purchaseCostKinds:[],purchaseCostTreatments:[],purchaseCostAllocationMethods:[],
        purchaseTaxRates:[],purchaseTaxTreatments:[],purchaseCurrencies:[],exchangeRateSources:[],purchaseCostAssociationModes:[]}});return;
    }
    await route.fallback();
  });
  await page.goto("/dashboard/purchasing/goods-receipts");
  await page.getByRole("button",{name:"Nueva compra",exact:true}).click();
  const dialog=page.getByRole("dialog",{name:"Recepción de compra",exact:true});
  await expect(dialog.getByText("Fecha de emisión",{exact:true}).locator("..").getByRole("button").first()).toContainText("8 de marzo de 2026");
  await expect(dialog.getByText("Fecha de vencimiento",{exact:true}).locator("..").getByRole("button").first()).toContainText("7 de abril de 2026");
  await dialog.getByRole("button",{name:"Guardar borrador",exact:true}).click();
  await expect.poll(()=>submitted).not.toBeNull();
  expect(submitted).toMatchObject({supplierInvoiceDate:"2026-03-08T12:00:00-04:00",dueDate:"2026-04-07T12:00:00-04:00",receivedAt:now});
});

for(const {now,from,to} of [
  {now:"2026-03-08T16:00:00Z",from:"2026-03-08T05:00:00.000Z",to:"2026-03-09T04:00:00.000Z"},
  {now:"2026-11-01T17:00:00Z",from:"2026-11-01T04:00:00.000Z",to:"2026-11-02T05:00:00.000Z"},
])test(`inventario: filtros de sede para el día ${from.slice(0,10)}`,async({page,baseURL})=>{
  await page.clock.setFixedTime(new Date(now));
  const scenario=financialVoucherScenario();scenario.timeZone="America/New_York";
  scenario.user.permissions.push("inventory.read","inventory.stock-counts.read");
  await installFinancialVoucherContext(page,baseURL!,scenario);
  const reads:URL[]=[];let contextReads=0;
  await page.route("**/api/**",async route=>{
    const url=new URL(route.request().url()),path=url.pathname;
    if(path.endsWith("/execution-context/businesses"))contextReads++;
    if(path.endsWith("/inventory/warehouses")){await route.fulfill({json:[]});return;}
    if(path.endsWith("/inventory/operations")){reads.push(url);await route.fulfill({json:{items:[],page:1,pageSize:20,totalCount:0,totalPages:0}});return;}
    if(path.endsWith("/inventory/balances")){await route.fulfill({json:{items:[],page:1,pageSize:20,totalCount:0,totalPages:0}});return;}
    await route.fallback();
  });
  await page.goto("/dashboard/inventory");
  await page.getByRole("tab",{name:"Operaciones",exact:true}).click();
  await expect.poll(()=>reads.length).toBe(1);
  expect(reads[0].searchParams.get("from")).toBe(from);
  expect(reads[0].searchParams.get("to")).toBe(to);
  expect(contextReads).toBe(1);
});

for (const receipt of [false, true]) test(`${receipt ? "recibos de caja" : "trazabilidad"}: filtro, listado y comprobante usan el calendario de la sede`, async ({page,baseURL}) => {
  await page.clock.setFixedTime(new Date("2027-01-01T04:00:00Z"));
  const scenario=financialVoucherScenario();scenario.timeZone="America/New_York";
  scenario.user.permissions=["accounting.read"];
  await installFinancialVoucherContext(page,baseURL!,scenario);
  const at="2027-01-01T04:30:00Z";
  const row={...scenario.draft.row,occurredAt:at,postedAt:at,hasManualDraft:false,status:"Posted",
    sourceDocumentType:"ReceivablePayment",sourceDocumentNumber:"RC-FECHA",voucherTypeCode:"CashReceipt",entryNumber:"CC-FECHA"};
  let listReads=0,typeReads=0,contextReads=0;
  await page.route("**/api/**",async route=>{
    const url=new URL(route.request().url()),path=url.pathname;
    if(path.endsWith("/execution-context/businesses"))contextReads++;
    if(path.endsWith("/reference-options/accounting-document-type")){
      typeReads++;
      expect(url.searchParams.get("dateRangeVersion")).toBe("2");
      expect(url.searchParams.get("from")).toBe("2026-01-01");
      expect(url.searchParams.get("to")).toBe("2026-12-31");
    }
    if(path.endsWith("/accounting/documents")){
      listReads++;
      expect(url.searchParams.get("dateRangeVersion")).toBe("2");
      expect(url.searchParams.get("from")).toBe("2026-01-01");
      expect(url.searchParams.get("to")).toBe("2026-12-31");
      await route.fulfill({json:{items:[row],page:1,pageSize:25,totalCount:1,totalPages:1}});return;
    }
    if(path.endsWith(`/accounting/postings/by-document/${scenario.documentId}`)){
      await route.fulfill({json:row});return;
    }
    if(path.endsWith(`/accounting/entries/by-document/${scenario.documentId}`)){
      await route.fulfill({json:{...row,description:"Pago recibido",debitTotal:100,creditTotal:100,
        lines:scenario.draft.lines.map((line,index)=>({...line.value,lineNumber:index+1,accountCode:line.accountCode,accountName:line.accountName}))}});return;
    }
    if(path.includes("/fiscal/documents/")){
      await route.fulfill({status:404,json:{message:"Sin documento fiscal"}});return;
    }
    await route.fallback();
  });
  await page.goto(receipt?"/dashboard/cash-receipts":"/dashboard/financial-traceability");
  const documentRow=page.getByRole("row").filter({hasText:"RC-FECHA"});
  await expect(documentRow.getByRole("cell").first()).toHaveText("31/12/2026");
  await documentRow.getByRole("button",{name:"Ver documento",exact:true}).click();
  const dialog=page.getByRole("dialog");
  await expect(dialog.getByText("Fecha del documento",{exact:true}).locator("..")).toContainText(/31.*dic.*2026.*11:30/);
  await dialog.getByRole("button",{name:"Abrir reporte",exact:true}).click();
  await expect(page.getByRole("dialog")).toContainText(/31.*dic.*2026/);
  expect(listReads).toBe(1);expect(typeReads).toBe(1);expect(contextReads).toBe(1);
});
