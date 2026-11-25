import {expect,test} from "@playwright/test";
import {financialVoucherScenario,installFinancialVoucherContext} from "./support/financial-voucher";

for(const width of [1366,390]) {
 test(`histórico ${width}: distingue importes desconocidos y no ofrece un original inexistente`,async({page,baseURL},info)=>{
  await page.setViewportSize({width,height:width===390?844:768});
  const scenario=financialVoucherScenario();scenario.user.permissions.push("sales.reports.read","sales.reports.read-all","accounting.read");
  await installFinancialVoucherContext(page,baseURL!,scenario);
  const documentId="88888888-8888-8888-8888-888888888888";
  const measures={documents:1,customers:1,quantity:0,grossSales:100,discounts:0,salesBeforeReturns:100,returns:100,netUntaxed:0,tax:0,netTotal:0,recognizedCost:null,profit:null,marginPercent:null,missingCostLines:2,returnsWithTax:119,returnedTax:19,salesWithTax:119,salesTax:19};
  let contexts=0,explores=0;
  await page.route("**/sales-reports/**",async route=>{
    if(new URL(route.request().url()).pathname.endsWith("/context")){
      contexts++;await route.fulfill({json:{businessDate:"2026-11-20",monthStart:"2026-11-01",timeZoneId:"America/Bogota",isRestaurant:false,hasPendingSources:false,hasFailedSources:false,historicalReturnsWithoutOfficialTotal:1,
        groupings:[{code:"document",label:"Comprobante"}],comparisons:[{code:"none",label:"Sin comparación"}],documentTypes:[],orders:[],customerActivities:[],paymentMethods:[],productReturnActivities:[],productCompositions:[]}});return;
    }
    if(new URL(route.request().url()).pathname.endsWith("/explore")){
      explores++;await route.fulfill({json:{items:[],page:1,pageSize:25,totalCount:0,totals:measures,projectedThrough:"2026-11-20T12:00:00Z",hasLineScope:false,officialTotal:null,roundingAdjustment:null,restaurantTip:null,
        taxes:[{taxCode:null,taxRate:null,base:-100,tax:-19,total:-119}],payments:[],comparison:null,products:null,supplier:null,impact:null,
        document:{documentId,documentType:"SalesInvoice",documentNumber:"VTA00-00000234",customerName:"Cliente histórico",sellerName:"Sin vendedor histórico",sourceDocumentAvailable:false}}});return;
    }
    await route.fallback();
  });
  await page.goto(`/dashboard/reports/sales?document=${documentId}`);
  await expect(page.getByText(/Histórico importado: se conservan/)).toBeVisible();
  await expect(page.getByText(/Tarifa sin documentar/)).toBeVisible();
  await expect(page.getByRole("button",{name:"Abrir trazabilidad financiera del original"})).toHaveCount(0);
  expect(await page.getByRole("main").evaluate(element=>element.scrollWidth<=element.clientWidth)).toBe(true);
  expect(contexts).toBe(1);expect(explores).toBe(1);
  await page.getByText(/Histórico importado: se conservan/).scrollIntoViewIfNeeded();
  await page.screenshot({path:info.outputPath(`historical-report-${width}.png`),fullPage:true});
 });
 test(`reportes ${width}: encabezados ordenan en servidor, fecha visible y una carga por cambio`,async({page,baseURL},info)=>{
  test.setTimeout(120_000);await page.setViewportSize({width,height:width===390?844:768});
  const scenario=financialVoucherScenario();scenario.user.permissions.push("sales.reports.read","sales.reports.read-all");
  await installFinancialVoucherContext(page,baseURL!,scenario);
  const requests:Array<{orderBy:string;page:number}>=[];let contextReads=0;
  const measures={documents:1,customers:1,quantity:2,grossSales:110,discounts:10,salesBeforeReturns:100,returns:0,netUntaxed:100,tax:19,netTotal:119,recognizedCost:60,profit:40,marginPercent:40,missingCostLines:0,returnsWithTax:0,returnedTax:0,salesWithTax:119,salesTax:19};
  await page.route("**/sales-reports/**",async route=>{
    const path=new URL(route.request().url()).pathname;
    if(path.endsWith("/context")){contextReads++;await route.fulfill({json:{businessDate:"2026-11-20",monthStart:"2026-11-01",timeZoneId:"America/Bogota",isRestaurant:false,hasPendingSources:false,hasFailedSources:false,
      groupings:[{code:"document",label:"Comprobante"},{code:"product",label:"Producto"},{code:"customer",label:"Cliente"},{code:"supplier",label:"Proveedor"}],comparisons:[{code:"none",label:"Sin comparación"}],documentTypes:[],orders:[],customerActivities:[],paymentMethods:[],productReturnActivities:[],productCompositions:[]}});return;}
    if(path.endsWith("/explore")){const request=route.request().postDataJSON();requests.push(request);await route.fulfill({json:{items:[{key:"one",label:"CVI00-00000001",measures,occurredAt:"2026-11-20T12:00:00Z",boughtInPeriod:null,document:null,supplier:null}],page:request.page,pageSize:25,totalCount:30,totals:measures,projectedThrough:"2026-11-20T12:00:00Z",hasLineScope:false,officialTotal:null,roundingAdjustment:null,restaurantTip:null,taxes:[],payments:[],comparison:null,products:null,supplier:null,impact:null,document:null}});return;}
    await route.fallback();
  });
  for(const reportKind of ["sales","impact","products","supplier-impact"]){
    await page.goto(`/dashboard/reports/${reportKind}?page=2`);
    const grid=page.getByRole("table");await expect(grid).toBeVisible();
    const initialContexts=contextReads,initialRequests=requests.length;
    const heading=grid.getByRole("columnheader",{name:/^Bruto sin impuestos/});
    await heading.getByRole("button").click();
    await expect.poll(()=>requests.at(-1)?.orderBy).toBe("gross-asc");
    await expect(heading).toHaveAttribute("aria-sort","ascending");expect(requests.at(-1)?.page).toBe(1);
    await expect.poll(()=>requests.length).toBe(initialRequests+1);expect(contextReads).toBe(initialContexts);
    await heading.getByRole("button").click();await expect.poll(()=>requests.at(-1)?.orderBy).toBe("gross-desc");
    await expect(heading).toHaveAttribute("aria-sort","descending");expect(requests.length).toBe(initialRequests+2);
    if(reportKind==="sales") {const date=grid.getByRole("columnheader",{name:/^Fecha/});await expect(date).toBeAttached();await date.getByRole("button").click();await expect.poll(()=>requests.at(-1)?.orderBy).toBe("date-asc");}
    expect(await page.getByRole("main").evaluate(element=>element.scrollWidth<=element.clientWidth)).toBe(true);
    await page.screenshot({path:info.outputPath(`${reportKind}-${width}.png`),fullPage:true});
  }
 });
}
