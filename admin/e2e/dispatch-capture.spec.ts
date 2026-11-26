import { expect, test } from "@playwright/test";

test.use({ timezoneId: "Asia/Tokyo" });

for (const viewport of [{width:1366,height:768},{width:390,height:844}]) {
test(`despacho ${viewport.width}: carga transportadores y documentos por sede y fechas, con varias bodegas y sin consultas ocultas`, async ({ page, baseURL }, testInfo) => {
  await page.setViewportSize(viewport);
  test.setTimeout(120_000);
  await page.clock.setFixedTime(new Date("2026-11-17T01:30:00Z"));
  const tenantId = "11111111-1111-1111-1111-111111111111", businessId = "22222222-2222-2222-2222-222222222222";
  const user = { userId: "33333333-3333-3333-3333-333333333333", tenantId, tenantKey: "AURALY", username: "dispatch-test",
    firstName: "Prueba", lastName: "Despachos", roles: [], permissions: ["dispatches.read", "dispatches.read-all", "dispatches.create", "dispatches.attach-documents", "dispatches.prepare", "dispatches.verify", "dispatches.release"] };
  await page.context().addCookies([{ name: "auth_token", value: "e2e", url: baseURL!, httpOnly: true }]);
  await page.addInitScript(({ tenantId, businessId, user }) => {
    localStorage.setItem("selected_tenant_id", tenantId); localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
  }, { tenantId, businessId, user });
  const candidateQueries: URLSearchParams[] = [], driverQueries: URLSearchParams[] = [];
  let optionsReads = 0, listReads = 0, createBody: Record<string, unknown> | null = null;
  const documents = Array.from({length:10},(_,index) => ({ documentId: `source-${index}`, documentType: index === 1 ? "SalesInvoice" : "SalesReceipt",
    documentNumber: index === 1 ? "FV-200" : `CV-${100+index}`, issuedAt: "2026-11-16T15:00:00Z", warehouseId: `warehouse-${index}`,
    warehouseName: `Bodega ${index + 1}`, customerName: "Cliente del despacho", sellerName: "Vendedor", lineCount: 1, documentTotal: 1000 }));
  let detailReads=0,dispatchStatus="Draft";
  const verificationWrites:Array<{dispatchLineId:string;quantityDelta:number}>=[];
  const lines=[20.5,30,0.3].map((amount,index)=>({dispatchLineId:`line-${index}`,dispatchSourceDocumentId:`source-${index%2}`,
    sourceLineNumber:index===2?2:1,productId:`product-${index}`,productCode:`PRD-${index+1}`,description:`Producto ${index+1}`,
    assignedQuantity:amount,verifiedQuantity:index===2?0.1:0,shortageQuantity:0,status:index===2?"PartiallyVerified":"Pending",rowVersion:"AAAAAAAAAAA="}));
  const summary=()=>({dispatchId:"dispatch-created",dispatchNumber:"DSP-TEST",status:dispatchStatus,rowVersion:"AAAAAAAAAAA=",
    documentCount:2,lineCount:3,expectedQuantity:50.8,verifiedQuantity:lines.reduce((sum,line)=>sum+line.verifiedQuantity,0),shortageQuantity:0,
    pendingLineCount:lines.filter(line=>line.verifiedQuantity<line.assignedQuantity).length});
  await page.route("**/api/**", async route => {
    const url = new URL(route.request().url()), path = url.pathname;
    let body: unknown = [];
    if (path.endsWith("/auth/me")) body = user;
    else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Prueba" }];
    else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Sede seleccionada", timeZone: "America/Bogota" }];
    else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, roles: [], permissions: user.permissions };
    else if (path.endsWith("/dispatches/options")) { optionsReads++; body = { items: [{routeId:"route-1",name:"Ruta Centro",code:"CENTRO",sellerName:"Vendedor"}],page:1,pageSize:10,totalCount:1,totalPages:1 }; }
    else if (path.endsWith("/parties/role-options")) {
      driverQueries.push(url.searchParams);
      body = { items: [{ roleId: "carrier-party", partyId:"party-1",role:"Carrier",displayName: "Transportador habilitado" }], page: 1, pageSize: 10, totalCount: 1, totalPages: 1 };
    } else if (path.endsWith("/dispatches/candidates")) {
      candidateQueries.push(url.searchParams);
      expect(url.searchParams.get("warehouseId")).toBeNull();
      expect(url.searchParams.get("dateRangeVersion")).toBe("2");
      expect(url.searchParams.get("pageSize")).toBe("10");
      body = { items: url.searchParams.get("page") === "2" ? [] : documents, page: Number(url.searchParams.get("page")), pageSize: 10, totalCount: 12, totalPages: 2 };
    } else if (path.endsWith("/dispatches") && route.request().method() === "POST") {
      createBody = route.request().postDataJSON();
      body = summary();
    } else if (path.endsWith("/dispatches")) {
      listReads++; body = { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 };
    } else if (path.endsWith("/dispatches/dispatch-created")) {
      detailReads++;
      expect(url.searchParams.get("documentPageSize")).toBe("10");expect(url.searchParams.get("linePageSize")).toBe("10");
      body = { dispatchId: "dispatch-created", businessId, dispatchNumber: "DSP-TEST", scheduledDate: "2026-11-16", driverName: "Transportador habilitado",
        status: dispatchStatus, documents: documents.slice(0,2).map(row=>({...row,dispatchSourceDocumentId:row.documentId,sourceDocumentId:row.documentId})),
        lines, shortages: [], rowVersion: "AAAAAAAAAAA=",summary:summary(),pagination:{documentPage:1,documentPageSize:10,linePage:1,linePageSize:10} };
    } else if(path.endsWith("/dispatch-created/prepare")) {dispatchStatus="Prepared";body=summary();}
    else if(path.endsWith("/dispatch-created/start-verification")) {dispatchStatus="InVerification";body=summary();}
    else if(path.endsWith("/dispatch-created/verification-events")) {
      const command=route.request().postDataJSON();verificationWrites.push(command);
      const line=lines.find(row=>row.dispatchLineId===command.dispatchLineId)!;
      line.verifiedQuantity=Number((line.verifiedQuantity+command.quantityDelta).toFixed(6));line.status=line.verifiedQuantity===line.assignedQuantity?"Verified":"PartiallyVerified";
      body={...summary(),updatedLine:line};
    }
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
  });
  await page.goto("/dashboard/dispatches");
  await expect(page.getByRole("button", { name: "Crear despacho", exact: true })).toBeEnabled();
  expect(optionsReads).toBe(0); expect(candidateQueries).toHaveLength(0); expect(driverQueries).toHaveLength(0);
  await page.getByRole("button", { name: "Crear despacho", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Nuevo despacho de mercancía" });
  await expect(dialog.getByText("CV-100",{exact:true})).toBeVisible();
  await expect(dialog.getByText("Cliente del despacho",{exact:true}).first()).toBeVisible();
  await expect(dialog.getByRole("button",{name:/Filtros/})).toHaveAttribute("aria-expanded","false");
  await expect(dialog.getByLabel("Documentos desde")).toHaveCount(0);
  expect(optionsReads).toBe(0);
  expect(candidateQueries[0].get("from")).toBe("2026-11-16"); expect(candidateQueries[0].get("to")).toBe("2026-11-16");
  await expect(dialog.getByText("Bodega", { exact: true })).toHaveCount(0);
  await dialog.getByRole("combobox", { name: "Transportador del despacho" }).click();
  await page.getByRole("option", { name: "Transportador habilitado", exact: true }).click();
  expect(driverQueries).toHaveLength(1); expect(driverQueries[0].get("role")).toBe("Carrier");
  await dialog.getByRole("checkbox").nth(0).check(); await dialog.getByRole("checkbox").nth(1).check();
  await expect(dialog.getByText("2 seleccionados", { exact: false })).toBeVisible();
  expect(candidateQueries).toHaveLength(1);
  await page.screenshot({path:testInfo.outputPath("despacho-filtros-contraidos.png"),fullPage:true});
  await dialog.getByRole("button",{name:/Filtros/}).click();
  await expect(dialog.getByRole("button",{name:/Filtros/})).toHaveAttribute("aria-expanded","true");
  await dialog.getByRole("combobox",{name:"Ruta de los clientes"}).click();
  await page.getByRole("option",{name:/Ruta Centro/}).click();
  await expect.poll(()=>candidateQueries.at(-1)?.get("routeId")).toBe("route-1");
  await expect(dialog.getByText("2 seleccionados",{exact:false})).toBeVisible();
  await page.screenshot({path:testInfo.outputPath("despacho-filtros-expandidos.png"),fullPage:true});
  const beforeCollapse=candidateQueries.length;
  await dialog.getByRole("button",{name:/Filtros/}).click();
  await expect(dialog.getByLabel("Documentos desde")).toHaveCount(0);
  await expect(dialog.getByPlaceholder("Buscar factura o cliente")).toBeVisible();
  await dialog.getByRole("button",{name:/Filtros/}).click();
  await expect(dialog.getByRole("combobox",{name:"Ruta de los clientes"})).toContainText("Ruta Centro");
  expect(candidateQueries).toHaveLength(beforeCollapse);
  expect(optionsReads).toBe(1);
  await dialog.getByLabel("Documentos desde").click();
  await page.getByRole("button", { name: "15 de noviembre de 2026", exact: true }).click();
  await expect.poll(() => candidateQueries.at(-1)?.get("from")).toBe("2026-11-15");
  await expect(dialog.getByText("0 seleccionados", { exact: false })).toBeVisible();
  await dialog.getByRole("checkbox").nth(0).check(); await dialog.getByRole("checkbox").nth(1).check();

  expect(await dialog.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
  await dialog.getByRole("button", { name: "Crear despacho", exact: true }).click();
  await expect.poll(() => createBody).not.toBeNull();
  expect(createBody).toEqual(expect.objectContaining({ businessId, driverCarrierId: "carrier-party", sourceDocumentIds: ["source-0", "source-1"] }));
  expect(createBody).not.toHaveProperty("warehouseId");
  expect(createBody).not.toHaveProperty("routeId");
  expect(listReads).toBe(1); expect(optionsReads).toBe(1); expect(driverQueries).toHaveLength(1);
  expect(candidateQueries).toHaveLength(3);
  const workspace=page.getByRole("dialog",{name:"DSP-TEST",exact:true});
  await expect(workspace.getByRole("button",{name:"Cancelar despacho",exact:true})).toBeVisible();
  await workspace.getByRole("button",{name:"Preparar despacho",exact:true}).click();
  await expect(workspace.getByRole("button",{name:"Cancelar despacho",exact:true})).toBeVisible();
  await workspace.getByRole("button",{name:"Verificar mercancía",exact:true}).click();
  const firstQuantity=workspace.getByRole("spinbutton",{name:"Cantidad de PRD-1",exact:true});
  await expect(firstQuantity).toBeEnabled();await expect(firstQuantity).toHaveValue("20.5");
  await expect(workspace.getByRole("button",{name:"Cancelar despacho",exact:true})).toHaveCount(0);
  expect(detailReads).toBe(1);
  const readsBeforeVerification=detailReads;
  await workspace.getByRole("button",{name:"Verificar 20,5",exact:true}).click();
  await expect(firstQuantity).toHaveValue("0");
  const partial=workspace.getByRole("spinbutton",{name:"Cantidad de PRD-2",exact:true});
  await partial.fill("12.5");
  await workspace.getByRole("button",{name:"Verificar 12,5",exact:true}).click();
  await expect(partial).toHaveValue("17.5");
  const fractional=workspace.getByRole("spinbutton",{name:"Cantidad de PRD-3",exact:true});
  await expect(fractional).toHaveValue("0.2");
  await workspace.getByRole("button",{name:"Verificar 0,2",exact:true}).click();
  await expect(fractional).toHaveValue("0");
  expect(verificationWrites.map(value=>value.quantityDelta)).toEqual([20.5,12.5,0.2]);
  expect(detailReads).toBe(readsBeforeVerification);
  await expect(workspace.getByRole("button",{name:"Cerrar verificación y liberar",exact:true})).toBeDisabled();
  expect(await workspace.evaluate(element=>element.scrollWidth<=element.clientWidth)).toBe(true);
  const titleBox=await workspace.getByRole("heading",{name:"DSP-TEST",exact:true}).boundingBox();
  if(titleBox && titleBox.y<0) console.log(JSON.stringify(await workspace.evaluate(element=>{
    const dimensions=(node:Element)=>({tag:node.tagName,role:node.getAttribute("role"),top:node.getBoundingClientRect().top,
      height:node.getBoundingClientRect().height,scrollTop:node.scrollTop,scrollHeight:node.scrollHeight,clientHeight:node.clientHeight,
      overflow:getComputedStyle(node).overflow,shrink:getComputedStyle(node).flexShrink});
    return {dialog:dimensions(element),children:[...element.children].map(dimensions),body:dimensions(document.body)};
  })));
  expect(titleBox).not.toBeNull();expect(titleBox!.y).toBeGreaterThanOrEqual(0);
  expect(titleBox!.y+titleBox!.height).toBeLessThan(viewport.height);
  await page.screenshot({path:testInfo.outputPath("despacho-verificacion-cantidad.png"),fullPage:true});

});

}
