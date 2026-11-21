import { expect, test } from "@playwright/test";
import { financialVoucherScenario, installFinancialVoucherContext } from "./support/financial-voucher";

test.use({serviceWorkers:"block",timezoneId:"America/Los_Angeles"});
for(const viewport of [{width:1366,height:768},{width:390,height:844}]) for(const canCorrect of [true,false]) {
 test(`Facturas comparte historial, muestra pagos e iconos y cabe en ${viewport.width} ${canCorrect?"administrativo":"cajero sin permiso"}`,async({page,baseURL})=>{
  test.setTimeout(120_000);await page.setViewportSize(viewport);
  const scenario=financialVoucherScenario();scenario.user.permissions.push("sales.reprint","work-sessions.closures.reconcile","work-sessions.differences.read");
  if(canCorrect)scenario.user.permissions.push("sales.payments.correct");
  await installFinancialVoucherContext(page,baseURL!,scenario);
  let searches=0,details=0,prints=0,audits=0,brands=0,roleReads=0;
  const searchBodies:Array<Record<string,unknown>>=[];
  const receipt={documentId:scenario.documentId,documentType:"SalesReceipt",documentNumber:"CVI00-00000001",fiscalNumber:null,
    issuedAt:"2026-11-20T21:30:00Z",customerIdentification:"100200",customerName:"Cliente de prueba",
    lines:[{lineNumber:1,productCode:"P001",description:"Producto de prueba",quantity:1,unitPrice:100000,discount:0,tax:0,total:100000,taxCode:"01",taxRate:0}],
    payments:[{paymentMethodCode:"Cash",amount:100000}],untaxedAmount:100000,taxAmount:0,payableAmount:100000,netPayableAmount:100000,
    cufe:null,qrPayload:null,fiscalStatus:"NotRequired",withholdingTotal:0,withholdings:[]};
  await page.route("**/api/**",async route=>{
    const path=new URL(route.request().url()).pathname;
    if(path.endsWith("/sales/search")) {
      searches++;searchBodies.push(route.request().postDataJSON());expect(route.request().postDataJSON()).toMatchObject({context:{businessId:scenario.businessId},dateRangeVersion:2,take:20});
      await route.fulfill({json:{items:[{...receipt,total:100000,closureId:"closure-1",sellerName:"Vendedora seleccionada",
        payments:[{paymentNumber:1,methodCode:"Cash",amount:40000,formName:"Caja principal"},
          {paymentNumber:2,methodCode:"Transfer",amount:60000,formName:"Banco transferencia con nombre largo para reconocer la cuenta"}]},
        {...receipt,documentId:"88888888-8888-8888-8888-888888888888",documentType:"SalesInvoice",documentNumber:"VTA00-00000002",fiscalNumber:"FV-123",
          total:100000,payments:[{paymentNumber:1,methodCode:"DebitCard",amount:100000,formName:"Datáfono de la empresa"}]}],hasMore:false,nextOffset:null}});return;
    }
    if(path.endsWith("/parties/role-options")){
      roleReads++;const role=new URL(route.request().url()).searchParams.get("role");
      await route.fulfill({json:{items:[{partyId:scenario.draft.mainPartyId,roleId:role==="Seller"?"seller-filter":"user-filter",role,
        displayName:role==="Seller"?"Vendedor para filtrar":"Usuario que facturó",identification:"1001"}],page:1,pageSize:10,totalCount:1,totalPages:1}});return;
    }
    if(path.endsWith("/tenants/branding/print")){brands++;await route.fulfill({json:{tenantId:scenario.tenantId,displayName:"Empresa de prueba",legalName:"Empresa de prueba",logoUrl:null,nit:"900100200",verificationDigit:"1"}});return;}
    if(path.endsWith("/receipt")){details++;await route.fulfill({json:receipt});return;}
    if(path.endsWith("/receipts/render")){prints++;expect(route.request().postDataJSON().receipts[0].documentId).toBe(scenario.documentId);await route.fulfill({json:{html:"<html><body>Comprobante original</body></html>"}});return;}
    if(path.endsWith("/reprint-audit")){audits++;await route.fulfill({status:204});return;}
    await route.fallback();
  });
  await page.goto("/dashboard/invoices");
  const panel=page.getByRole("region",{name:"Facturas y comprobantes",exact:true});
  await expect(panel.getByText("CVI00-00000001",{exact:true})).toBeVisible();
  await expect(panel.getByText("Banco transferencia con nombre largo para reconocer la cuenta")).toBeVisible();
  await expect(panel.getByText("Vendedor: Vendedora seleccionada")).toBeVisible();
  if(canCorrect)await expect(panel.getByRole("link",{name:"Corregir pago de CVI00-00000001"})).toHaveAttribute("href","/dashboard/cash-differences?closure=closure-1");
  else await expect(panel.getByRole("link",{name:"Corregir pago de CVI00-00000001"})).toHaveCount(0);
  await expect(panel.getByRole("link",{name:"Corregir pago de VTA00-00000002"})).toHaveCount(0);
  await expect(panel.getByRole("button",{name:"Ver detalle de CVI00-00000001"})).toBeEnabled();
  await expect(panel.getByRole("button",{name:"Imprimir CVI00-00000001"})).toBeEnabled();
  await expect(panel).toContainText(/4:30|16:30/);
  expect(searches).toBe(1);expect(roleReads).toBe(0);
  await panel.getByRole("button",{name:"Filtros",exact:true}).click();
  await expect(panel.getByText("Cliente y sede",{exact:true})).toBeVisible();
  expect(roleReads).toBe(0);
  await panel.getByRole("combobox",{name:"Filtrar facturas por vendedor"}).click();
  await page.getByRole("option",{name:/Vendedor para filtrar/}).click();
  await expect.poll(()=>searches).toBe(2);
  await panel.getByRole("combobox",{name:"Filtrar facturas por usuario que facturó"}).click();
  await page.getByRole("option",{name:/Usuario que facturó/}).click();
  await expect.poll(()=>searches).toBe(3);
  expect(searchBodies.at(-1)).toMatchObject({sellerId:"seller-filter",billedByUserId:"user-filter"});
  expect(roleReads).toBe(2);
  await page.screenshot({path:test.info().outputPath("facturas-filtros.png"),fullPage:true});
  await panel.getByRole("button",{name:"Filtros",exact:true}).click();
  expect(searches).toBe(3);
  const dimensions=await page.evaluate(()=>({width:document.documentElement.clientWidth,scroll:document.documentElement.scrollWidth}));
  expect(dimensions.scroll).toBeLessThanOrEqual(dimensions.width+1);
  await page.screenshot({path:test.info().outputPath("facturas-pagos.png"),fullPage:true});
  await panel.getByRole("button",{name:"Ver detalle de CVI00-00000001"}).click();
  await expect(panel.getByRole("heading",{name:"Detalle de factura"})).toBeVisible();
  await panel.getByRole("button",{name:"Volver",exact:true}).click();
  await panel.getByRole("button",{name:"Imprimir CVI00-00000001"}).click();
  await expect.poll(()=>audits).toBe(1);expect(details).toBe(1);expect(prints).toBe(1);expect(brands).toBe(1);
  await panel.getByRole("button",{name:"Actualizar facturas"}).click();await expect.poll(()=>searches).toBe(4);
  expect(brands).toBe(1);
 });
}
