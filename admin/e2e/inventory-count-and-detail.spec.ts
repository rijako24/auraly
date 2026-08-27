import { expect, test, type Page } from "@playwright/test";

const user = process.env.AURALY_E2E_USERNAME ?? "admin";
const password = process.env.AURALY_E2E_PASSWORD ?? "Admin123!";
const tenantKey = process.env.AURALY_E2E_TENANT_KEY ?? "AURALY";

async function login(page: Page) {
  await page.goto(`/login?tenant=${encodeURIComponent(tenantKey)}`);
  await page.locator("#username").fill(user);
  await page.locator("#password").fill(password);
  await page.getByRole("button", { name: /Iniciar sesi.n/ }).click();
  await expect(page).toHaveURL(/\/dashboard(?:\/|$)/, { timeout: 60_000 });
}

test.describe("conteo y detalle de inventario", () => {
  test.use({ serviceWorkers: "block" });
  test.beforeEach(async ({ page }) => login(page));
  test.setTimeout(120_000);

  test("conciliacion selecciona borradores y suma productos repetidos", async ({ page }) => {
    const countId = "11111111-1111-1111-1111-111111111111";
    const firstDraft = "22222222-2222-2222-2222-222222222222";
    const secondDraft = "33333333-3333-3333-3333-333333333333";
    const productId = "44444444-4444-4444-4444-444444444444";
    await page.route(/\/api\/commerce\/v1\/inventory\/physical-count-drafts(?:\?.*)?$/, async (route) => {
      await route.fulfill({
        contentType: "application/json",
        body: JSON.stringify({items:[
          {inventoryPhysicalCountId:countId,draftId:firstDraft,name:"Pasillo A",warehouseId:"66666666-6666-6666-6666-666666666666",warehouseName:"Principal",scopeType:"General",ownerUserId:"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",status:"Ready",version:2,productCount:1,countedProductCount:1,updatedAt:"2026-08-25T12:10:00Z"},
          {inventoryPhysicalCountId:countId,draftId:secondDraft,name:"Pasillo B",warehouseId:"66666666-6666-6666-6666-666666666666",warehouseName:"Principal",scopeType:"General",ownerUserId:"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",status:"Ready",version:2,productCount:1,countedProductCount:1,updatedAt:"2026-08-25T12:12:00Z"}
        ],page:1,pageSize:20,totalCount:2,totalPages:1}),
      });
    });
    await page.route(`**/api/commerce/v1/inventory/physical-counts/${countId}/reconciliations`, async (route) => route.fulfill({contentType:"application/json",body:JSON.stringify({reconciliationId:"55555555-5555-5555-5555-555555555555",inventoryPhysicalCountId:countId,snapshotInventorySequence:30,status:"Active",createdAt:"2026-08-25T12:15:00Z",createdByUserId:"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",isStale:false,countedApplicationStatus:null,countedDocumentId:null,countedDocumentNumber:null,uncountedApplicationStatus:null,uncountedDocumentId:null,uncountedDocumentNumber:null,drafts:[{draftId:firstDraft,name:"Pasillo A",ownerUserId:"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",version:2,countedProducts:1,pendingProducts:0},{draftId:secondDraft,name:"Pasillo B",ownerUserId:"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",version:2,countedProducts:1,pendingProducts:0}],products:[{productId,productCode:"PRD-1",productName:"Arroz",status:"Counted",proposedQuantity:8,systemQuantity:7,unitCost:2000,averageUnitCost:1800,sources:[{draftId:firstDraft,draftName:"Pasillo A",ownerUserId:"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",initialQuantity:3,verificationQuantity:null,finalQuantity:3},{draftId:secondDraft,draftName:"Pasillo B",ownerUserId:"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",initialQuantity:5,verificationQuantity:null,finalQuantity:5}]}]})}));

    await page.goto("/dashboard/inventory");
    await page.getByRole("button", { name: "Conciliación de inventario" }).click();
    const dialog=page.getByRole("dialog",{name:"Conciliación de inventario"});
    await dialog.getByRole("checkbox",{name:"Seleccionar Pasillo A"}).check();
    await dialog.getByRole("checkbox",{name:"Seleccionar Pasillo B"}).check();
    await dialog.getByRole("button",{name:"Conciliar seleccionados"}).click();
    await expect(dialog.getByRole("tab",{name:/Contados · 1/})).toBeVisible();
    await expect(dialog.getByText("Pasillo A",{exact:true})).toBeVisible();
    await expect(dialog.getByText("Pasillo B",{exact:true})).toBeVisible();
    await expect(dialog.getByText("Contado: 3",{exact:true})).toBeVisible();
    await expect(dialog.getByText("Contado: 5",{exact:true})).toBeVisible();
    await expect(dialog.getByText("8",{exact:true})).toBeVisible();
  });

  test("conteo abre buscador y grilla, mueve el foco y pide el nombre solo al guardar", async ({ page }) => {
    const countId = "11111111-1111-1111-1111-111111111111";
    const firstDraft = "22222222-2222-2222-2222-222222222222";
    const productId = "44444444-4444-4444-4444-444444444444";
    const secondProductId = "55555555-5555-5555-5555-555555555555";
    const warehouseId = "66666666-6666-6666-6666-666666666666";
    const draftLine = {
      productId, productCode: "PRD-1", productName: "Arroz", systemQuantity: 7,
      initialQuantity: null, verificationQuantity: null, pendingReason: null,
      initialCountedAt: null, verifiedAt: null,
    };
    const detail = (draft: Record<string, unknown>) => ({
      inventoryPhysicalCountId: countId,
      warehouseId,
      warehouseName: "Principal",
      scopeType: "General",
      reasonCode: "PHYSICAL_COUNT",
      notes: null,
      baseInventorySequence: 30,
      status: "Open",
      createdByUserId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      createdAt: "2026-08-25T12:00:00Z",
      startedAt: "2026-08-25T12:00:00Z",
      reviewStartedAt: null,
      closedAt: null,
      finalInventoryOperationId: null,
      finalDocumentNumber: null,
      drafts: [draft],
    });
    const initialDraft = {
      draftId: firstDraft, name: "Conteo principal", ownerUserId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      status: "InProgress", version: 1, createdAt: "2026-08-25T12:00:00Z",
      updatedAt: "2026-08-25T12:10:00Z", captureStage: "Count", lines: [draftLine],
    };
    const resumedDraft = {
      ...initialDraft,
      status: "InProgress",
      version: 3,
      captureStage: "Recount",
      lines: [
        {...draftLine,initialQuantity:8,verificationQuantity:9},
        {productId:secondProductId,productCode:"PRD-2",productName:"Aceite",systemQuantity:5,initialQuantity:5,verificationQuantity:null,pendingReason:null,initialCountedAt:"2026-08-25T12:12:00Z",verifiedAt:null},
      ],
    };
    await page.route(/\/api\/commerce\/v1\/inventory\/warehouses(?:\?.*)?$/, route => route.fulfill({contentType:"application/json",body:JSON.stringify([{warehouseId,code:"MAIN",name:"Principal"}])}));
    await page.route(/\/api\/commerce\/v1\/inventory\/reasons(?:\?.*)?$/, route => route.fulfill({contentType:"application/json",body:JSON.stringify([{inventoryReasonId:"77777777-7777-7777-7777-777777777777",operationType:"StockCount",code:"PHYSICAL_COUNT",name:"Conteo físico",isSystem:true,isActive:true,displayOrder:1}])}));
    await page.route(/\/api\/commerce\/v1\/inventory\/products(?:\?.*)?$/, route => route.fulfill({contentType:"application/json",body:JSON.stringify({items:[{productId,productCode:"PRD-1",reference:"7701",productName:"Arroz",unitCode:"EA",quantityOnHand:7,averageUnitCost:1800,saleUnitPrice:2500}],page:1,pageSize:50,totalCount:1,totalPages:1})}));
    await page.route("**/api/commerce/v1/inventory/physical-counts", async route => {
      if (route.request().method() !== "POST") return route.continue();
      const request=route.request().postDataJSON() as {initialDraftName:string;scopeType:string;productIds:string[]};
      expect(request.initialDraftName).toBe("Conteo principal");
      expect(request.scopeType).toBe("General");
      expect(request.productIds).toEqual([]);
      await route.fulfill({status:201,contentType:"application/json",body:JSON.stringify(detail(initialDraft))});
    });
    await page.route(`**/api/commerce/v1/inventory/physical-counts/${countId}/drafts/${firstDraft}`, async route => {
      const request=route.request().postDataJSON() as {name:string;readyForReconciliation:boolean;captureStage:string;lines:Array<{productId:string;initialQuantity:number|null;verificationQuantity:number|null}>};
      expect(request.name).toBe("Conteo principal");
      expect(request.readyForReconciliation).toBe(true);
      expect(request.captureStage).toBe("Recount");
      expect(request.lines).toEqual([{productId,initialQuantity:8,verificationQuantity:9,pendingReason:null}]);
      await route.fulfill({contentType:"application/json",body:JSON.stringify(detail({...initialDraft,status:"Ready",version:2,captureStage:"Recount",lines:[{...draftLine,initialQuantity:8,verificationQuantity:9}]}))});
    });
    await page.route(/\/api\/commerce\/v1\/inventory\/physical-count-drafts(?:\?.*)?$/, route => route.fulfill({contentType:"application/json",body:JSON.stringify({items:[{inventoryPhysicalCountId:countId,draftId:firstDraft,name:"Conteo principal",warehouseId,warehouseName:"Principal",scopeType:"General",ownerUserId:"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",status:"InProgress",version:3,productCount:2,countedProductCount:2,updatedAt:"2026-08-25T12:15:00Z"}],page:1,pageSize:20,totalCount:1,totalPages:1})}));
    await page.route(`**/api/commerce/v1/inventory/physical-counts/${countId}`, route => route.fulfill({contentType:"application/json",body:JSON.stringify(detail(resumedDraft))}));

    await page.goto("/dashboard/inventory");
    await page.getByRole("button", { name: "Nueva operación" }).click();
    const operation=page.getByRole("dialog",{name:"Nueva operación"});
    await expect(operation.getByRole("textbox",{name:"Nombre del borrador"})).toHaveCount(0);
    await operation.getByRole("combobox",{name:"Motivo"}).click();
    await page.getByRole("option",{name:"Conteo físico"}).click();
    const search=operation.getByRole("textbox",{name:"Buscar producto"});
    await search.fill("Arroz");
    await operation.getByRole("option",{name:/Arroz/}).click();
    const count=operation.getByRole("textbox",{name:"Contar Arroz",exact:true});
    await expect(count).toBeFocused();
    await count.fill("8");
    await count.press("Enter");
    await expect(search).toBeFocused();
    await expect(operation.getByRole("textbox",{name:"Recontar Arroz",exact:true})).toBeDisabled();
    await operation.getByRole("button",{name:"Recontar"}).click();
    const recount=operation.getByRole("textbox",{name:"Recontar Arroz",exact:true});
    await expect(count).toBeDisabled();
    await expect(recount).toBeFocused();
    await recount.fill("9");
    await operation.getByRole("button",{name:"Guardar borrador"}).click();
    const saveDialog=page.getByRole("dialog",{name:"Guardar borrador"});
    await saveDialog.getByRole("textbox",{name:"Nombre del borrador"}).fill("Conteo principal");
    await saveDialog.getByRole("button",{name:"Guardar",exact:true}).click();
    await expect(saveDialog).toBeHidden();
    await page.getByRole("button",{name:"Editar"}).click();
    const editDialog=page.getByRole("dialog",{name:"Editar borrador"});
    const pendingRecount=editDialog.getByRole("textbox",{name:"Recontar Aceite",exact:true});
    await expect(editDialog.getByRole("textbox",{name:"Contar Aceite",exact:true})).toBeDisabled();
    await expect(editDialog.getByRole("textbox",{name:"Recontar Arroz",exact:true})).toBeEnabled();
    await expect(pendingRecount).toBeFocused();
  });

  test("historial abre detalle con motivo y todas las lineas", async ({ page }) => {
    const documentId = "55555555-5555-5555-5555-555555555555";
    await page.route(/\/api\/commerce\/v1\/inventory\/operations\?.*/, async (route) => {
      await route.fulfill({
        contentType: "application/json",
        body: JSON.stringify({
          items: [{
            documentId,
            documentType: "StockCount",
            documentNumber: "CTI-000001",
            warehouseId: "66666666-6666-6666-6666-666666666666",
            warehouseName: "Principal",
            destinationWarehouseId: null,
            destinationWarehouseName: null,
            reasonCode: "Conteo físico",
            status: "Processed",
            occurredAt: "2026-08-14T16:00:00Z",
            lineCount: 2,
            totalValueChange: 15000,
          }],
          page: 1, pageSize: 50, totalCount: 1, totalPages: 1,
        }),
      });
    });
    await page.route(`**/api/commerce/v1/inventory/operations/${documentId}`, async (route) => {
      await route.fulfill({
        contentType: "application/json",
        body: JSON.stringify({
          documentId,
          documentType: "StockCount",
          documentNumber: "CTI-000001",
          warehouseId: "66666666-6666-6666-6666-666666666666",
          warehouseName: "Principal",
          destinationWarehouseId: null,
          destinationWarehouseName: null,
          reasonCode: "PHYSICAL_COUNT",
          reasonDescription: "Conteo físico programado",
          conversionType: null,
          baseInventorySequence: 25,
          notes: "Conteo de dos líneas",
          status: "Processed",
          occurredAt: "2026-08-14T16:00:00Z",
          createdAt: "2026-08-14T16:00:00Z",
          acceptedAt: "2026-08-14T16:01:00Z",
          processedAt: "2026-08-14T16:02:00Z",
          totalValueChange: 15000,
          lines: [
            { lineNumber: 1, direction: "COUNT", productId: "77777777-7777-7777-7777-777777777777", productCode: "PRD-000001", productName: "Aceite vegetal 3000 ml", quantity: 7, systemQuantityAtBase: 10, explicitUnitCost: null, allocationWeight: null, processedUnitCost: 5000, processedValue: -15000 },
            { lineNumber: 2, direction: "COUNT", productId: "88888888-8888-8888-8888-888888888888", productCode: "PRD-000002", productName: "Aceite vegetal 1000 ml", quantity: 12, systemQuantityAtBase: 8, explicitUnitCost: null, allocationWeight: null, processedUnitCost: 5000, processedValue: 20000 },
          ],
        }),
      });
    });

    await page.goto("/dashboard/inventory");
    await page.getByRole("tab", { name: "Operaciones" }).click();
    await page.getByRole("button", { name: "Inventarios" }).click();
    await page.getByText("CTI-000001", { exact: true }).click();

    const detail = page.getByRole("dialog", { name: "CTI-000001" });
    await expect(detail).toBeVisible();
    await expect(detail.getByText("Conteo físico programado")).toBeVisible();
    await expect(detail.getByText("Aceite vegetal 3000 ml")).toBeVisible();
    await expect(detail.getByText("Aceite vegetal 1000 ml")).toBeVisible();
    await detail.getByRole("button", { name: "Cerrar" }).click();
    await expect(detail).toBeHidden();
  });
});

