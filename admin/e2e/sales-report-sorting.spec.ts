import {expect,test} from "@playwright/test";
import {financialVoucherScenario,installFinancialVoucherContext} from "./support/financial-voucher";

for(const width of [1366,390]) {
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
