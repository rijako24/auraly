import { paymentCatalogFixture } from "./support/payment-catalog";
import { expect, test } from "@playwright/test";

test.use({serviceWorkers:"block"});
for(const viewport of [{width:1366,height:768},{width:390,height:844}]) {
 test(`vendedor opcional usa catálogo y respuesta del comando sin recargar borrador ${viewport.width}`,async({page,baseURL})=>{
  test.setTimeout(120_000);await page.setViewportSize(viewport);
  const tenantId = "11111111-1111-1111-1111-111111111111";
  const businessId = "22222222-2222-2222-2222-222222222222";
  const warehouseId = "33333333-3333-3333-3333-333333333333";
  const user = { userId: "44444444-4444-4444-4444-444444444444", tenantId, tenantKey: "TEST",
    username: "cajero", firstName: "Caja", lastName: "Prueba", roles: ["Cajero"], permissions: ["sales.create"] };
  const workspace = { businessId, warehouseId, businessName: "Tienda de prueba", warehouseName: "Principal",
    warehouseCode: "B01", warehouseAllowsNegativeStockSales: true, hasActiveEdgeEnrollment: false, pointOfSaleMode: "Store" };
  const products = ["S", "M"].map((size, index) => ({ productId: `product-${index}`, productCode: `PRD-${index}`,
    imageUrl: "/demo/products/shirt-black.svg", name: `Camiseta negra ${size}`, baseUnitCode: "EA", unitPrice: 49_900, taxCode: "IVA", taxRate: 0,
    currencyCode: "COP", isActive: true, isWeighable: false, allowsFractionalSale: false, priceSource: "Base",
    presentation: { familyProductId: "product-0", colorName: "Negro", sizeName: size } }));
  let lines: Array<Record<string, unknown>> = [];
  let version = 1;
  let selectedSeller: string | null = null;
  let sellerQueries=0;let draftReads=0;let sellerWrites=0;
  const draft = () => ({ draftId: "same-direct-sale-draft", ...workspace, workSessionId: "session", userId: user.userId,
    sellerId:selectedSeller, sellerName:selectedSeller ? "Vendedora de prueba con nombre largo para móvil" : null,
    status: "Active", version, lines, untaxedAmount: lines.length ? 49_900 * Number(lines[0].quantity) : 0,
    taxAmount: 0, payableAmount: lines.length ? 49_900 * Number(lines[0].quantity) : 0 });
  const writes: string[] = [];
  const browseRequests: Array<Record<string, unknown>> = [];
  let categoriesReads = 0;
  await page.context().addCookies([{ name: "auth_token", value: "e2e", url: baseURL!, httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ tenantId, businessId, warehouseId, user }) => {
    localStorage.setItem("selected_tenant_id", tenantId); localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
    localStorage.setItem(`auraly.pos.sales-workspace:${tenantId}:${user.userId}`, `${businessId}:${warehouseId}`);
  }, { tenantId, businessId, warehouseId, user });
  await page.route("**/api/**", async route => {
    const path = new URL(route.request().url()).pathname;
    let body: unknown = [];
    if (path.endsWith("/auth/me")) body = user;
    else if (path.endsWith("/tenants/branding/print")) body={tenantId,displayName:"Prueba",legalName:"Prueba",logoUrl:null};
    else if (path.endsWith("/workspace/bootstrap")) body = { tenantId, tenantName: "Prueba", userId: user.userId,
      userDisplayName: "Caja Prueba", options: [workspace], canEnrollPosDevice: false, activeEnrolledDeviceCount: 0, maximumEnrolledDevices: 0 };
    else if (path.endsWith("/workspace/options")) body = [workspace];
    else if (path.endsWith("/workspace/select")) body = workspace;
    else if (path.endsWith("/work-sessions/current")) {
      await new Promise(resolve => setTimeout(resolve, 500));
      body = { workSessionId: "session" };
    }
    else if(path.endsWith("/parties/role-options")) {
      sellerQueries++;const query=new URL(route.request().url());expect(query.searchParams.get("role")).toBe("Seller");
      body={items:[{roleId:"seller-1",partyId:"party-1",displayName:"Vendedora de prueba con nombre largo para móvil",identification:"100200"}],page:1,pageSize:20,totalCount:1,totalPages:1};
    }
    else if(path.endsWith("/same-direct-sale-draft/seller")) {
      sellerWrites++;const input=route.request().postDataJSON();expect(input.expectedVersion).toBe(version);
      selectedSeller=input.sellerId;version++;body=draft();
    }
    else if (path.endsWith("/drafts/active")) {draftReads++;body=draft();}
    else if (path.endsWith("/pos/catalog/categories")) { categoriesReads++; body = [{ categoryId: "shirts", name: "Camisetas" }]; }
    else if (path.endsWith("/pos/catalog/products/search")) {
      const request = route.request().postDataJSON(); browseRequests.push(request);
      body = { items: products, hasMore: false, nextOffset: null };
    } else if (path.endsWith("/same-direct-sale-draft/items")) {
      writes.push(path); const request = route.request().postDataJSON();
      expect(request).toMatchObject({ selector: "product-1", quantity: 1, expectedVersion: version });
      version++;
      lines = [{ lineId: "line-1", productId: "product-1", productCode: "PRD-1", description: "Camiseta negra M",
        unitCode: "EA", quantity: 1, untaxedAmount: 49_900, tax: 0, total: 49_900, unitPrice: 49_900,
        discount: 0, promotionDiscount: 0, taxCode: "IVA", taxRate: 0, priceSource: "Base", allowsFractionalSale: false }];
      body = draft();
    } else if (path.endsWith("/same-direct-sale-draft/lines/line-1/quantity")) {
      writes.push(path); const request = route.request().postDataJSON();
      expect(request).toMatchObject({ quantity: 2, expectedVersion: version });
      version++; lines[0] = { ...lines[0], quantity: 2, total: 99_800 }; body = draft();
    } else if (path.endsWith("/inventory-validation")) body = { isValid: true, wasValidated: true, issues: [] };
    else if (path.endsWith("/settlement-configuration")) body = { isAccountingEnabled: false, bankAccounts: [], paymentMethods: paymentCatalogFixture() };
    else if (path.endsWith("/settlement")) body = { grossAmount: 99_800, withholdingTotal: 0, netAmount: 99_800, lines: [] };
    else if (path.endsWith("/reference-options/payment-method")) body = [{ id: "cash", code: "Cash", label: "Efectivo", isActive: true, sortOrder: 1 }];
    else if (path.includes("/orders")) body = { items: [], totalCount: 0, totalPages: 0, page: 1, pageSize: 25 };
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
  });
  await page.goto("/pos");
  const choose=page.getByRole("button",{name:"Seleccionar vendedor",exact:true});
  await expect(choose).toBeEnabled();
  expect(sellerQueries).toBe(0);
  await expect(page.getByRole("region",{name:"Catálogo de tiendas"}).getByText("Camiseta negra S",{exact:true})).toBeVisible();
  const before=draftReads;
  const browseBefore=browseRequests.length;
  if(viewport.width>1000){await page.getByPlaceholder("Código o referencia",{exact:true}).focus();await page.keyboard.press("Control+v");}
  else await choose.click();
  const dialog=page.getByRole("dialog",{name:"Seleccionar vendedor"});
  await expect(dialog.getByRole("button",{name:/Vendedora de prueba/})).toBeVisible();
  await page.screenshot({path:test.info().outputPath("seller-picker.png"),fullPage:true});
  const bounds=await dialog.boundingBox();expect(bounds!.x).toBeGreaterThanOrEqual(0);expect(bounds!.y).toBeGreaterThanOrEqual(0);
  expect(bounds!.x+bounds!.width).toBeLessThanOrEqual(viewport.width+1);expect(bounds!.y+bounds!.height).toBeLessThanOrEqual(viewport.height+1);
  await dialog.getByRole("button",{name:/Vendedora de prueba/}).click();
  await expect(dialog).not.toBeVisible();
  await expect(choose).toContainText("Vendedora de prueba");
  await page.screenshot({path:test.info().outputPath("seller-selected.png"),fullPage:true});
  expect(sellerWrites).toBe(1);expect(draftReads).toBe(before);
  expect(categoriesReads).toBe(1);expect(browseRequests).toHaveLength(browseBefore);
  await page.reload();await expect(choose).toContainText("Vendedora de prueba");
  await choose.click();await page.getByRole("button",{name:"Vendedor automático",exact:true}).click();
  await expect(choose).toContainText("Automático");expect(sellerWrites).toBe(2);
  await choose.click();await page.keyboard.press("Escape");await expect(dialog).not.toBeVisible();expect(sellerWrites).toBe(2);
 });
}
