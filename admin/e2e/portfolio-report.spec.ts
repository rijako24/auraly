import { expect, test } from "@playwright/test";
import { readFile } from "node:fs/promises";
import { unzipSync, strFromU8 } from "fflate";

test.use({timezoneId:"Pacific/Kiritimati"});

for (const direction of ["receivables", "payables"] as const) {
  test(`${direction} consulta el informe por página sin traer toda la cartera`, async ({ page, baseURL }) => {
    await page.clock.install({time:new Date("2026-11-11T14:00:00Z")});
    const tenantId = "11111111-1111-1111-1111-111111111111";
    const businessId = "22222222-2222-2222-2222-222222222222";
    const user = { userId: "33333333-3333-3333-3333-333333333333", tenantId,
      tenantKey: "@auraly", username: "report-e2e", firstName: "Prueba", lastName: "Informes",
      roles: [], permissions: ["receivables.read", "payables.read"] };
    await page.context().addCookies([{ name: "auth_token", value: "report-e2e", url: baseURL!, httpOnly: true, sameSite: "Lax" }]);
    await page.addInitScript(({ user, businessId }) => {
      localStorage.setItem("selected_tenant_id", user.tenantId);
      localStorage.setItem("selected_business_id", businessId);
      localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
    }, { user, businessId });
    const reportRequests: URL[] = [];
    const printRequests: URL[] = [];
    const exportRequests: URL[] = [];
    const logoSource = direction === "receivables" ? `data:image/png;base64,${(await readFile("public/brand/auraly-mark.png")).toString("base64")}` : null;
    const legalName = direction === "receivables"
      ? "COMERCIALIZADORA INTERNACIONAL DE PRODUCTOS DE CONSUMO MASIVO, IMPORTACIONES, EXPORTACIONES Y DISTRIBUCIONES INDUSTRIALES DEL NORTE Y DEL CARIBE COLOMBIANO SOCIEDAD POR ACCIONES SIMPLIFICADA"
      : "Auraly Distribuciones";
    let brandingReads = 0;
    let recoverReport = false;
    await page.route("**/api/**", async route => {
      const url = new URL(route.request().url());
      const path = url.pathname;
      if (direction === "receivables" && path.includes("/receivables/report"))
        expect(url.searchParams.get("templateVersion")).toBe("4");
      if ([`/api/commerce/v1/${direction}`, `/api/commerce/v1/${direction}/customers`,
        `/api/commerce/v1/${direction}/suppliers`, `/api/commerce/v1/${direction}/report`,
        `/api/commerce/v1/${direction}/report/print`, `/api/commerce/v1/${direction}/report/export`,
        "/api/commerce/v1/receivable-payments", "/api/commerce/v1/payable-payments"].includes(path))
        expect(url.searchParams.get("dateRangeVersion")).toBe("2");
      let body: unknown = [];
      if (path === "/api/auth/me") body = user;
      else if (path.endsWith("/tenants/branding/print")) { brandingReads++; body = {tenantId,displayName:"Auraly",legalName,logoUrl:logoSource,nit:"900100001"}; }
      else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Auraly" }];
      else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Auraly",timeZone:"America/Bogota" }];
      else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, roles: [], permissions: user.permissions };
      else if (path.endsWith(`/${direction}/report/print`)) {
        printRequests.push(url);
        return route.fulfill({ status: 200, contentType: "text/html",
          body: "<!doctype html><html lang=\"es\"><body><h1>Informe completo</h1></body></html>" });
      } else if (path.endsWith(`/${direction}/report`) || path.endsWith(`/${direction}/report/export`)) {
        const isExport = path.endsWith("/export");
        (isExport ? exportRequests : reportRequests).push(url);
        if (!isExport && url.searchParams.get("search") === "error-consulta" && !recoverReport)
          return route.fulfill({status:503,contentType:"application/problem+json",body:JSON.stringify({status:503,detail:"Fallo de consulta de prueba"})});
        const currentPage = Number(url.searchParams.get("page"));
        const isPayable = direction === "payables";
        body = {
          items: [{ ...(isPayable ? { supplierId: "sup-1", supplierName: "Proveedor A", payableId: `pay-${currentPage}` }
            : { customerId: "cus-1", customerName: "Cliente A", receivableId: `rec-${currentPage}` }),
            identification: "900100001", partySiteId: "site-1", partySiteName: "Centro", currencyCode: "COP",
            invoiceCount: 1, originalAmount: 10000, paidAmount: 2500, outstandingAmount: 7500, otherImpact: 0,
            overdueAmount: 7500, notDueAmount: 0, overdue1To30Amount: 7500,
            overdue31To60Amount: 0, overdue61To90Amount: 0, overdueOver90Amount: 0,
            documentNumber: url.searchParams.get("documentNumber") ?? `FAC-${currentPage}`, issuedAt: "2026-10-01T12:00:00Z",
            ...(!isPayable ? {termDays:19,ageDays:41,daysPastDue:22,address:"Calle de prueba 123",city:"Bogotá",phone:"3001234567",creditLimit:500000,
              groupTotals:{invoiceCount:51,originalAmount:510000,paidAmount:127500,otherImpact:0,outstandingAmount:382500,
                notDueAmount:0,overdue1To30Amount:382500,overdue31To60Amount:0,overdue61To90Amount:0,overdueOver90Amount:0}} : {}),
            dueDate: "2026-10-20T12:00:00Z", applications: [{ documentNumber: "AB-1", appliedAt: "2026-10-05T12:00:00Z", amount: 2500 }] }],
          page: currentPage, pageSize: 50, totalCount: 51, totalPages: 2,
          ...(isPayable ? { currencyTotals: [{ currencyCode: "COP", invoiceCount: 51,
            originalAmount: 510000, paidAmount: 127500, outstandingAmount: 382500, overdueAmount: 382500, otherImpact: 0,
            notDueAmount: 0, overdue1To30Amount: 382500, overdue31To60Amount: 0,
            overdue61To90Amount: 0, overdueOver90Amount: 0 }] }
            : { totalInvoiceCount: 51, totalOriginal: 510000, totalPaid: 127500,
              totalOutstanding: 382500, totalOverdue: 382500, totalOtherImpact: 0,
              totalNotDue: 0, totalOverdue1To30: 382500, totalOverdue31To60: 0,
              totalOverdue61To90: 0, totalOverdueOver90: 0 }),
        };
        if (!isPayable && url.searchParams.get("search") === "sin-resultados")
          body = {...body as Record<string,unknown>,items:[],totalCount:0,totalPages:0,totalInvoiceCount:0,
            totalOriginal:0,totalPaid:0,totalOutstanding:0,totalOverdue:0,totalOtherImpact:0,
            totalNotDue:0,totalOverdue1To30:0,totalOverdue31To60:0,totalOverdue61To90:0,totalOverdueOver90:0};
        const creditOnly=!isPayable && url.searchParams.get("search")==="saldo-a-favor";
        if (creditOnly) {
          const credit={customerId:"credit-only",partyId:"credit-party",customerName:"Cliente sólo con anticipo",
            identification:"12345",partySiteId:null,partySiteName:null,currencyCode:"COP",invoiceCount:0,
            receivableId:null,documentNumber:"Anticipos y otros saldos a favor",issuedAt:null,dueDate:null,applications:[],
            originalAmount:0,paidAmount:0,otherImpact:0,outstandingAmount:0,overdueAmount:0,
            notDueAmount:0,overdue1To30Amount:0,overdue31To60Amount:0,overdue61To90Amount:0,overdueOver90Amount:0,
            availableAdvances:15000,availableCredits:0,netAmount:-15000,isCreditBalance:true};
          body={items:[{...credit,groupTotals:credit}],page:1,pageSize:50,totalCount:1,totalPages:1,
            totalInvoiceCount:0,totalOriginal:0,totalPaid:0,totalOtherImpact:0,totalOutstanding:0,totalOverdue:0,
            totalNotDue:0,totalOverdue1To30:0,totalOverdue31To60:0,totalOverdue61To90:0,totalOverdueOver90:0,
            totalAvailableAdvances:15000,totalAvailableCredits:0,totalNetAmount:-15000};
        }
        if (!isPayable && url.searchParams.get("search") === "grupos") {
          const first=(body as {items:Record<string,unknown>[]}).items[0];
          const totals={invoiceCount:2,originalAmount:20000,paidAmount:5000,otherImpact:0,outstandingAmount:15000,
            notDueAmount:0,overdue1To30Amount:7500,overdue31To60Amount:7500,overdue61To90Amount:0,overdueOver90Amount:0,
            availableAdvances:2000,availableCredits:0,netAmount:13000};
          const a={...first,documentNumber:"FE-1",internalDocumentNumber:"BTAX-1",groupTotals:totals};
          const b={...first,customerId:"cus-2",customerName:"Cliente B",identification:"900200002",receivableId:"rec-b",documentNumber:"BTAX-2",
            originalAmount:10000.1234,outstandingAmount:7500.1234,overdueAmount:7500.1234,overdue1To30Amount:7500.1234,
            groupTotals:{...totals,invoiceCount:1,originalAmount:10000.1234,paidAmount:2500,outstandingAmount:7500.1234,
              overdue1To30Amount:7500.1234,
              overdue31To60Amount:0,availableAdvances:0,netAmount:7500.1234}};
          body={items:[a,b,{...a,receivableId:"rec-a2",documentNumber:"ASI-1",internalDocumentNumber:null,
            issuedAt:"2026-09-01T12:00:00Z",dueDate:"2026-09-30T12:00:00Z",termDays:29,ageDays:71,
            overdue1To30Amount:0,overdue31To60Amount:7500,daysPastDue:42},
            {...a,receivableId:null,documentNumber:"Anticipos y otros saldos a favor",isCreditBalance:true,invoiceCount:0,
              originalAmount:0,paidAmount:0,outstandingAmount:0,overdueAmount:0,overdue1To30Amount:0,availableAdvances:2000,netAmount:-2000}],
            page:1,pageSize:50,totalCount:4,totalPages:1,totalInvoiceCount:3,totalOriginal:30000.1234,totalPaid:7500,
            totalOutstanding:22500.1234,totalOverdue:22500.1234,totalOtherImpact:0,totalNotDue:0,totalOverdue1To30:15000.1234,
            totalOverdue31To60:7500,totalOverdue61To90:0,totalOverdueOver90:0,totalAvailableAdvances:2000,totalAvailableCredits:0,totalNetAmount:20500.1234};
        }
        if (isExport && !creditOnly && url.searchParams.get("search") !== "grupos") {
          const complete = body as { items: Record<string,unknown>[] };
          const first = complete.items[0];
          complete.items = Array.from({length:51},(_,index) => ({...first,documentNumber:`FAC-${index+1}`,
            [isPayable?"payableId":"receivableId"]:`document-${index+1}`}));
        }
      } else if (path.endsWith(`/${direction}/customers`) || path.endsWith(`/${direction}/suppliers`)) {
        body = { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0,
          totalOutstanding: 0, totalOverdue: 0 };
      } else if (path.endsWith(`/${direction}`) || path.endsWith("/receivable-payments") || path.endsWith("/payable-payments")) {
        body = { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 };
      } else if (path.endsWith("/portfolio/parties/site-options")) {
        body = { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 };
      } else if (path.endsWith("/portfolio/parties/role-options")) {
        body = {items:[{partyId:"party-2",roleId:direction === "receivables" ? "customer-2" : "supplier-2",
          role:direction === "receivables" ? "Customer" : "Supplier",displayName:direction === "receivables" ? "Cliente B" : "Proveedor B",
          identification:"900200002"}],page:1,pageSize:10,totalCount:1,totalPages:1};
      }
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
    });

    await page.goto(`/dashboard/${direction}`);
    await page.getByText("Filtros", { exact: true }).click();
    const filteredList = page.waitForResponse(response => response.url().includes(`/api/commerce/v1/${direction}`)
      && new URL(response.url()).searchParams.get("search") === "FAC");
    await page.getByPlaceholder("Número de documento o identificación").fill("FAC");
    await filteredList;
    await page.getByRole("button", { name: "Reportes" }).click();
    const dialog = page.getByRole("dialog", { name: direction === "receivables"
      ? "Informe de cuentas por cobrar" : "Informe de cuentas por pagar" });
    await expect(dialog.getByText("FAC-1", { exact: true })).toBeVisible();
    await expect(dialog.getByText(/AB-1/)).toHaveCount(0);
    expect(reportRequests[0].searchParams.get("includeApplications")).toBe("false");
    expect(reportRequests[0].searchParams.get("cutoff")).toBe("2026-11-11");
    await dialog.getByRole("button", { name: direction === "receivables" ? "Detallado con abonos" : "Detallado con pagos", exact:true }).click();
    await expect(dialog.getByText(/AB-1/)).toBeVisible();
    expect(reportRequests.at(-1)!.searchParams.get("includeApplications")).toBe("true");
    await expect(dialog.getByRole("heading", { name: direction === "receivables" ? "Cliente A" : "Proveedor A" })).toBeVisible();
    const document = dialog.locator("tbody").filter({ hasText: "FAC-1" });
    await expect(dialog.getByText(legalName,{exact:true})).toBeVisible();
    expect(await dialog.getByTestId("portfolio-report-header").evaluate(element=>element.getBoundingClientRect().height)).toBeLessThan(200);
    await expect(dialog.getByRole("columnheader", { name: "Saldo pendiente", exact: true })).toBeVisible();
    await expect(document.getByText(/AB-1/)).toBeVisible();
    await expect(document.getByText("1–30 días", { exact: true })).toBeVisible();
    await expect(dialog.getByRole("region", { name: "Edades de cartera" })).toBeVisible();
    await page.setViewportSize({ width: 390, height: 844 });
    await expect(document).toBeVisible();
    expect(await dialog.evaluate(element => element.getBoundingClientRect().right <= window.innerWidth)).toBe(true);
    expect(await dialog.getByTestId("portfolio-report-header").evaluate(element=>element.getBoundingClientRect().height)).toBeLessThan(300);
    const tableViewport = dialog.locator("div.overflow-x-auto").first();
    expect(await tableViewport.evaluate(element => element.scrollWidth > element.clientWidth)).toBe(true);
    await page.setViewportSize({ width: 1440, height: 1000 });
    expect(reportRequests).toHaveLength(2);
    expect(reportRequests[0].searchParams.get("pageSize")).toBe("50");
    expect(reportRequests[0].searchParams.get("search")).toBe("FAC");
    expect(reportRequests[0].searchParams.has("sortDirection")).toBe(false);
    await dialog.getByRole("button", { name: "Siguiente" }).click();
    await expect(dialog.getByText("FAC-2", { exact: true })).toBeVisible();
    expect(reportRequests).toHaveLength(3);
    expect(reportRequests[2].searchParams.get("page")).toBe("2");
    await dialog.getByRole("button", { name: "Consolidado general", exact:true }).click();
    await expect.poll(() => reportRequests.length).toBe(4);
    const summaryRequest = reportRequests.at(-1)!;
    expect(summaryRequest.searchParams.get("page")).toBe("1");
    expect(summaryRequest.searchParams.get("sortBy")).toBeNull();
    expect(summaryRequest.searchParams.has("sortDirection")).toBe(false);
    expect(summaryRequest.searchParams.get("consolidated")).toBe("true");
    let popups = 0;
    page.on("popup", () => popups++);
    await dialog.getByRole("button", { name: "Imprimir informe completo" }).click();
    await expect(dialog.frameLocator('iframe[title="Impresión del informe"]').getByRole("heading", { name: "Informe completo" })).toHaveText("Informe completo");
    expect(popups).toBe(0);
    expect(printRequests).toHaveLength(1);
    expect(printRequests[0].searchParams.has("pageSize")).toBe(false);
    await dialog.getByRole("button", { name: direction === "receivables" ? "Detallado con abonos" : "Detallado con pagos", exact:true }).click();
    await dialog.getByRole("combobox", { name: "Ordenar documentos por" }).click();
    await page.getByRole("option", { name: "Valor original" }).click();
    await expect.poll(() => reportRequests.at(-1)?.searchParams.get("sortBy")).toBe("originalAmount");
    await dialog.getByRole("button", { name: "Ascendente" }).click();
    await expect.poll(() => reportRequests.at(-1)?.searchParams.get("sortDirection")).toBe("desc");
    const excelDownload = page.waitForEvent("download");
    await dialog.getByRole("button", { name: "Exportar Excel", exact: true }).click();
    const workbook = await excelDownload;
    await workbook.saveAs(test.info().outputPath("cartera.xlsx"));
    expect(workbook.suggestedFilename()).toMatch(/\.xlsx$/);
    const archive = unzipSync(await readFile((await workbook.path())!));
    expect(archive["xl/worksheets/sheet1.xml"]).toBeTruthy();
    const workbookXml = Object.entries(archive).filter(([name])=>name.endsWith(".xml"))
      .map(([,value])=>strFromU8(value)).join("\n");
    expect(workbookXml).toContain("FAC-51");
    expect(workbookXml).toContain("10000");
    expect(workbookXml).toContain('formatCode="dd/mm/yyyy"');
    expect(exportRequests).toHaveLength(1);
    expect(exportRequests[0].searchParams.get("sortDirection")).toBe("desc");
    expect(exportRequests[0].searchParams.get("search")).toBe("FAC");
    expect(exportRequests[0].searchParams.get("includeApplications")).toBe("true");
    const pdfDownload = page.waitForEvent("download");
    await dialog.getByRole("button", { name: "Descargar PDF", exact: true }).click();
    const pdf = await pdfDownload;
    await pdf.saveAs(test.info().outputPath("cartera.pdf"));
    expect(pdf.suggestedFilename()).toMatch(/\.pdf$/);
    expect((await readFile((await pdf.path())!)).subarray(0,4).toString()).toBe("%PDF");
    expect(exportRequests).toHaveLength(2);
    expect(brandingReads).toBe(1);
    if (direction === "receivables") {
      await expect(dialog.getByRole("columnheader",{name:"Plazo (días)",exact:true})).toBeVisible();
      await expect(dialog.getByRole("columnheader",{name:"Edad (días)",exact:true})).toBeVisible();
      await expect(dialog.getByRole("columnheader",{name:"Mora (días)",exact:true})).toBeVisible();
      await expect(dialog.getByText(/Calle de prueba 123/)).toBeVisible();
      await expect(dialog.getByText("Subtotal cliente / sede · 51 documentos filtrados")).toBeVisible();
      await dialog.getByText("Más filtros",{exact:true}).click();
      const beforeDraft = reportRequests.length;
      await dialog.getByRole("combobox",{name:"Cliente del informe"}).click();
      await page.getByRole("option",{name:/Cliente B/}).click();
      await expect.poll(()=>reportRequests.at(-1)?.searchParams.get("customerId")).toBe("customer-2");
      await expect(dialog.getByRole("button",{name:"Aplicar filtros",exact:true})).toHaveCount(0);
      const beforeText=reportRequests.length;
      await dialog.getByRole("textbox",{name:"Factura exacta"}).fill("FAC-");
      await dialog.getByRole("textbox",{name:"Factura exacta"}).fill("FAC-42");
      expect(reportRequests).toHaveLength(beforeText);
      await expect(dialog.getByRole("button",{name:"Exportar Excel",exact:true})).toBeDisabled();
      await page.clock.runFor(300);
      await expect.poll(()=>reportRequests.length).toBe(beforeText+1);
      await dialog.getByRole("spinbutton",{name:"Edad desde (días)"}).fill("40");
      await dialog.getByRole("spinbutton",{name:"Edad hasta (días)"}).fill("42");
      await dialog.getByRole("spinbutton",{name:"Mora desde (días)"}).fill("20");
      await dialog.getByRole("spinbutton",{name:"Mora hasta (días)"}).fill("30");
      for (const label of ["Emisión desde","Emisión hasta"]) {
        await dialog.locator("label").filter({hasText:label}).getByRole("button").click();
        await dialog.getByRole("button",{name:"Mes anterior",exact:true}).click();
        await dialog.getByRole("button",{name:"1 de octubre de 2026",exact:true}).click();
      }
      await page.clock.runFor(300);
      await expect(dialog.getByText("FAC-42",{exact:true})).toBeVisible();
      expect(reportRequests.length).toBeGreaterThan(beforeDraft);
      const applied = reportRequests.at(-1)!;
      for (const [key,value] of Object.entries({customerId:"customer-2",documentNumber:"FAC-42",issuedFrom:"2026-10-01",issuedTo:"2026-10-01",
        minimumAgeDays:"40",maximumAgeDays:"42",minimumDaysPastDue:"20",maximumDaysPastDue:"30",page:"1"})) expect(applied.searchParams.get(key)).toBe(value);
      await page.screenshot({path:test.info().outputPath("cartera-detallada-filtros.png"),fullPage:true});
      await dialog.getByRole("button",{name:"Ver todos los clientes",exact:true}).click();
      await expect.poll(()=>reportRequests.at(-1)?.searchParams.has("documentNumber")).toBe(false);
      const all = reportRequests.at(-1)!;
      for (const key of ["customerId","partySiteId","search","status","issuedFrom","issuedTo","minimumAgeDays","maximumAgeDays","minimumDaysPastDue","maximumDaysPastDue"]) expect(all.searchParams.has(key)).toBe(false);
      const beforeInvalid = reportRequests.length;
      await dialog.getByRole("spinbutton",{name:"Edad desde (días)"}).fill("20");
      await dialog.getByRole("spinbutton",{name:"Edad hasta (días)"}).fill("10");
      await page.clock.runFor(300);
      await expect(dialog.getByRole("alert")).toContainText("Revisa los rangos");
      expect(reportRequests).toHaveLength(beforeInvalid);
      await dialog.getByRole("button",{name:"Consolidado con saldo",exact:true}).click();
      await expect(dialog.getByRole("alert")).toContainText("Revisa los rangos");
      expect(reportRequests).toHaveLength(beforeInvalid);
      await dialog.getByRole("spinbutton",{name:"Edad desde (días)"}).fill("");
      await dialog.getByRole("spinbutton",{name:"Edad hasta (días)"}).fill("");
      await dialog.getByRole("textbox",{name:"Búsqueda general del informe"}).fill("sin-resultados");
      await page.clock.runFor(300);
      await expect(dialog.getByText("No hay documentos para estos filtros.",{exact:true})).toBeVisible();
      await dialog.getByRole("textbox",{name:"Búsqueda general del informe"}).fill("error-consulta");
      await page.clock.runFor(300);
      await expect(dialog.getByRole("alert")).toContainText("Fallo de consulta de prueba");
      const beforeRetry=reportRequests.length;
      recoverReport=true;
      await dialog.getByRole("button",{name:"Reintentar",exact:true}).click();
      await expect(dialog.getByText("FAC-1",{exact:true})).toBeVisible();
      expect(reportRequests).toHaveLength(beforeRetry+1);
      const beforeCredit=reportRequests.length;
      await dialog.getByRole("textbox",{name:"Búsqueda general del informe"}).fill("saldo-a-favor");
      expect(reportRequests).toHaveLength(beforeCredit);
      await page.clock.runFor(300);
      await expect(dialog.getByText("Cliente sólo con anticipo",{exact:true})).toBeVisible();
      await expect(dialog.getByText(/Neto comercial:.*-.*15/).first()).toBeVisible();
      await expect(dialog.getByText(/Sin sede identificada/)).toBeVisible();
      expect(reportRequests).toHaveLength(beforeCredit+1);
      const creditDownload=page.waitForEvent("download");
      await dialog.getByRole("button",{name:"Exportar Excel",exact:true}).click();
      const creditWorkbook=unzipSync(await readFile((await (await creditDownload).path())!));
      expect(strFromU8(creditWorkbook["xl/worksheets/sheet1.xml"])).toContain("-15000");
      await page.screenshot({path:test.info().outputPath("cartera-solo-anticipo.png"),fullPage:true});
      await dialog.getByRole("textbox",{name:"Búsqueda general del informe"}).fill("grupos");
      await page.clock.runFor(300);
      const clientA=dialog.getByRole("region",{name:"Cliente A",exact:true});
      const clientB=dialog.getByRole("region",{name:"Cliente B",exact:true});
      await expect(clientA.getByRole("heading",{name:"Cliente A",exact:true})).toHaveCount(1);
      await expect(clientB.getByRole("heading",{name:"Cliente B",exact:true})).toHaveCount(1);
      await expect(clientA.getByRole("columnheader",{name:"Documento",exact:true})).toHaveCount(1);
      await expect(clientB.getByRole("columnheader",{name:"Documento",exact:true})).toHaveCount(1);
      await expect(clientA.getByRole("cell",{name:"FE-1 Referencia interna: BTAX-1",exact:true})).toBeVisible();
      await expect(clientA.getByText("ASI-1",{exact:true})).toBeVisible();
      await expect(clientA.getByText("Referencia interna: BTAX-1",{exact:true})).toBeVisible();
      await expect(clientB.getByText("BTAX-2",{exact:true})).toBeVisible();
      await expect(clientB.getByText(/^1–30 días:\s*\$\s*7\.500,1234$/)).toBeVisible();
      await expect(dialog.getByRole("region",{name:"Edades de cartera"}).getByText(/15\.000,1234/)).toBeVisible();
      await expect(clientB.getByText("FE-1",{exact:true})).toHaveCount(0);
      await expect(dialog.getByText("Anticipos y otros saldos a favor",{exact:true})).toHaveCount(0);
      await expect(clientA.getByRole("row").filter({hasText:"FE-1"}).getByText("1–30 días",{exact:true}))
        .toHaveCSS("background-color","rgb(255, 241, 194)");
      await expect(clientA.getByRole("row").filter({hasText:"ASI-1"}).getByText("31–60 días",{exact:true}))
        .toHaveCSS("background-color","rgb(255, 226, 204)");
      const groupedDownload=page.waitForEvent("download");
      await dialog.getByRole("button",{name:"Exportar Excel",exact:true}).click();
      const groupedWorkbook=await groupedDownload;
      await groupedWorkbook.saveAs(test.info().outputPath("cartera-por-cliente.xlsx"));
      const groupedArchive=unzipSync(await readFile((await groupedWorkbook.path())!));
      const groupedXml=Object.entries(groupedArchive).filter(([name])=>name.endsWith(".xml")).map(([,value])=>strFromU8(value)).join("\n");
      expect(strFromU8(groupedArchive["xl/worksheets/sheet1.xml"]).match(/<mergeCell ref=/g)).toHaveLength(3);
      expect(groupedXml).toContain("FFF1C2");expect(groupedXml).toContain("FFE2CC");
      const groupedPdfDownload=page.waitForEvent("download");
      await dialog.getByRole("button",{name:"Descargar PDF",exact:true}).click();
      await (await groupedPdfDownload).saveAs(test.info().outputPath("cartera-por-cliente.pdf"));
      await dialog.getByText("Más filtros",{exact:true}).click();
      await clientA.scrollIntoViewIfNeeded();
      await page.screenshot({path:test.info().outputPath("cartera-por-cliente-con-edades.png"),fullPage:true});
    }
    const beforeParty=reportRequests.length;
    await dialog.getByRole("combobox",{name:direction === "receivables" ? "Cliente del informe" : "Proveedor del informe"}).click();
    await page.getByRole("option",{name:direction === "receivables" ? /Cliente B/ : /Proveedor B/}).click();
    await expect.poll(()=>reportRequests.length).toBe(beforeParty+1);
    expect(reportRequests.at(-1)!.searchParams.get(direction === "receivables" ? "customerId" : "supplierId")).toBe(direction === "receivables" ? "customer-2" : "supplier-2");
    expect(reportRequests.at(-1)!.searchParams.has("partySiteId")).toBe(false);
    const shortcuts=[
      {label:"Consolidado general",summary:true,balance:false,applications:false,status:null},
      {label:"Consolidado con saldo",summary:true,balance:true,applications:false,status:null},
      {label:"Detallado con saldo",summary:false,balance:true,applications:false,status:null},
      {label:direction === "receivables" ? "Detallado con abonos" : "Detallado con pagos",summary:false,balance:false,applications:true,status:null},
      {label:"Detallado sin saldo",summary:false,balance:false,applications:false,status:"Paid"},
      {label:"Detallado general",summary:false,balance:false,applications:false,status:null},
    ];
    for(const shortcut of shortcuts) {
      const before=reportRequests.length;
      await dialog.getByRole("button",{name:shortcut.label,exact:true}).click();
      await expect.poll(()=>reportRequests.length).toBe(before+1);
      const requested=reportRequests.at(-1)!;
      expect(requested.searchParams.get(direction === "receivables" ? "customerId" : "supplierId"))
        .toBe(direction === "receivables" ? "customer-2" : "supplier-2");
      expect(requested.searchParams.get("consolidated")).toBe(String(shortcut.summary));
      expect(requested.searchParams.get("outstandingOnly")).toBe(String(shortcut.balance));
      expect(requested.searchParams.get("includeApplications")).toBe(String(shortcut.applications));
      expect(requested.searchParams.get("status")).toBe(shortcut.status);
      expect(requested.searchParams.get("cutoff")).toBe("2026-11-11");
      expect(requested.searchParams.get("page")).toBe("1");
      await expect(dialog.getByRole("button",{name:shortcut.label,exact:true})).toHaveAttribute("aria-pressed","true");
    }
    await page.screenshot({path:test.info().outputPath("cartera-reportes-listos.png"),fullPage:true});
  });
}
