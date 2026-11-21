import { expect, test } from "@playwright/test";
import { financialVoucherScenario, installFinancialVoucherContext } from "./support/financial-voucher";

test("navegar entre listas desplazadas mantiene el título debajo del encabezado", async ({page,baseURL},info) => {
  await page.setViewportSize({width:1366,height:768});
  const scenario=financialVoucherScenario();
  scenario.user.permissions.push("dispatches.read");
  await installFinancialVoucherContext(page,baseURL!,scenario);
  await page.route("**/api/**",async route=>{
    const path=new URL(route.request().url()).pathname;
    if(path.endsWith("/accounting/documents")) {
      await route.fulfill({json:{items:Array.from({length:25},(_,index)=>({...scenario.draft.row,
        sourceDocumentId:`receipt-${index}`,sourceDocumentNumber:`RC-${index}`,status:"Posted",hasManualDraft:false})),
        page:1,pageSize:25,totalCount:25,totalPages:1}});return;
    }
    if(path.endsWith("/dispatches")) {
      await route.fulfill({json:{items:Array.from({length:25},(_,index)=>({dispatchId:`dispatch-${index}`,
        dispatchNumber:`DSP00-${String(index+1).padStart(8,"0")}`,scheduledDate:"2026-11-20",driverName:"Transportador",
        vehiclePlate:"ABC123",status:"Draft",documentCount:2,lineCount:3,expectedQuantity:20.5,verifiedQuantity:0,shortageQuantity:0})),
        page:1,pageSize:25,totalCount:25,totalPages:1}});return;
    }
    await route.fallback();
  });
  await page.goto("/dashboard/cash-receipts");
  await expect(page.getByText("RC-24",{exact:true})).toBeAttached();
  const main=page.getByRole("main");
  await main.hover();await page.mouse.wheel(0,600);
  await expect.poll(()=>main.evaluate(element=>element.scrollTop)).toBeGreaterThan(100);
  await page.getByRole("link",{name:"Despachos",exact:true}).click();
  const title=main.getByRole("heading",{name:"Despachos",exact:true});
  await expect(title).toBeVisible();
  await expect.poll(()=>main.evaluate(element=>element.scrollTop)).toBe(0);
  const container=await main.boundingBox(),heading=await title.boundingBox();
  expect(heading!.y).toBeGreaterThanOrEqual(container!.y);
  expect(heading!.y+heading!.height).toBeLessThan(768);
  await page.screenshot({path:info.outputPath("encabezado-al-navegar.png"),fullPage:true});
});
