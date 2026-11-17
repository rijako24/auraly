import { expect, test } from "@playwright/test";
import { financialVoucherScenario, installFinancialVoucherContext } from "./support/financial-voucher";

test.use({ serviceWorkers:"block" });

for (const viewport of [{width:1440,height:1000},{width:390,height:844}]) {
  test(`productos: pestañas bajo demanda y presentación compartida ${viewport.width}`, async({page,baseURL},testInfo)=>{
    await page.setViewportSize(viewport);
    const scenario=financialVoucherScenario();
    scenario.user.permissions.push("products.read");
    await installFinancialVoucherContext(page,baseURL!,scenario);
    const reads:string[]=[];
    const lookupReads:string[]=[];
    page.on("request",request=>{
      const path=new URL(request.url()).pathname;
      if(request.method()==="GET"&&(path.endsWith("/product-categories")||path.endsWith("/product-brands")||path.endsWith("/reference-options/product-composition-kind")))lookupReads.push(path);
    });
    await page.route("**/api/businesses/**",async route=>{
      const url=new URL(route.request().url());
      if(url.pathname.endsWith("/product-categories/browse")) {
        const depth=Number(url.searchParams.get("depth"));reads.push(`category:${depth}`);
        await route.fulfill({json:{items:[{productCategoryId:`category-${depth}`,parentProductCategoryId:null,name:`Clasificación ${depth}`,
          depth,path:`Ruta ${depth}`,displayOrder:0,isActive:true,isBrowsable:true}],page:1,pageSize:20,totalCount:1,totalPages:1}});
      } else if(url.pathname.endsWith("/products")) {
        reads.push(`products:${url.searchParams.get("areaId")??"all"}`);
        await route.fulfill({json:{items:[{productId:"product-1",businessId:scenario.businessId,productCode:"PRD-1",sku:"REF-1",name:"Producto de prueba",
          description:"Catálogo de prueba",unitPrice:12000,currency:"COP",isActive:true}],page:1,pageSize:20,totalCount:1,totalPages:1}});
      } else await route.fallback();
    });
    await page.goto("/dashboard/products");
    const tabs=page.getByRole("tablist",{name:"Explorar productos",exact:true});
    const products=tabs.getByRole("tab",{name:"Productos",exact:true});
    await expect(products).toHaveAttribute("aria-selected","true");
    await expect(page.getByText("Producto de prueba",{exact:true})).toBeVisible();
    expect(reads).toEqual(["products:all"]);
    expect(lookupReads).toEqual([]);
    await expect(page.getByRole("combobox",{name:"Explorar por"})).toHaveCount(0);
    const filters=page.locator("details").filter({has:page.locator("summary").filter({hasText:"Filtros"})});
    const tabBox=await tabs.boundingBox(),filterBox=await filters.boundingBox();
    expect(tabBox!.y).toBeGreaterThanOrEqual(filterBox!.y+filterBox!.height);
    const views=page.getByRole("group",{name:"Vista de los resultados",exact:true});
    await views.getByRole("button",{name:"Galería",exact:true}).click();
    expect(reads).toEqual(["products:all"]);
    for(const [depth,name] of ["Áreas","Líneas","Grupos","Subgrupos"].entries()) {
      const tab=tabs.getByRole("tab",{name,exact:true});
      await tab.click();
      await expect(tab).toHaveAttribute("aria-selected","true");
      await expect(page.getByText(`Clasificación ${depth}`,{exact:true})).toBeVisible();
      await expect(views.getByRole("button",{name:"Galería",exact:true})).toHaveAttribute("aria-pressed","true");
      expect(reads).toEqual(["products:all",...Array.from({length:depth+1},(_,index)=>`category:${index}`)]);
      const box=await tab.boundingBox();expect(box!.x).toBeGreaterThanOrEqual(0);expect(box!.x+box!.width).toBeLessThanOrEqual(viewport.width);
    }
    await testInfo.attach("consultas-al-abrir",{body:JSON.stringify(reads),contentType:"application/json"});
    expect(lookupReads).toEqual([]);
    await page.screenshot({path:`test-results/products-tabs-${viewport.width}.png`,fullPage:true});
    const last=tabs.getByRole("tab",{name:"Subgrupos",exact:true});
    await last.focus();await page.keyboard.press("Home");
    await expect(products).toBeFocused();await expect(products).toHaveAttribute("aria-selected","true");
    await expect(page.getByText("Producto de prueba",{exact:true})).toBeVisible();
    await tabs.getByRole("tab",{name:"Áreas",exact:true}).click();
    await page.getByText("Clasificación 0",{exact:true}).click();
    await expect(products).toHaveAttribute("aria-selected","true");
    await expect(page.getByRole("navigation",{name:"Categoría explorada"})).toContainText("Ruta 0");
    await expect.poll(()=>reads.at(-1)).toBe("products:category-0");
    await page.reload();
    await expect(products).toHaveAttribute("aria-selected","true");
    await expect(views.getByRole("button",{name:"Galería",exact:true})).toHaveAttribute("aria-pressed","true");
    await page.locator("summary").filter({hasText:"Filtros"}).click();
    await expect.poll(()=>lookupReads.length).toBe(3);
  });
}
