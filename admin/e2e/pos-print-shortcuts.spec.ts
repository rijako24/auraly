import { expect, test, type Route } from "@playwright/test";
import { login } from "./support/auth";

for (const mode of ["browser", "installed-online", "prepared"] as const) {
  for (const catalogState of ["ready", "credit", "credit-multiple", "credit-missing", "cards", "missing", "previous", "rejected"] as const) {
  const configuredCredit = catalogState === "credit" || catalogState === "credit-multiple";
  test(`${catalogState === "rejected" ? "rejected confirmation shows its error inside checkout without printing" : catalogState === "cards" ? "native card capture opens only after selecting a form and preserves approval" : catalogState === "missing" || catalogState === "previous" ? `unavailable ${catalogState} catalog blocks new capture without legacy fallback` : catalogState === "credit-missing" ? "credit without a configured form cannot be selected by button or shortcut" : configuredCredit ? `configured money and ${catalogState} form keep the existing checkout` : "printing letters preserve amounts and a failed logo never blocks sales"} (${mode})`, async ({ page, baseURL }) => {
    test.setTimeout(150_000);
    if (process.env.AURALY_E2E_TENANT_KEY) await login(page, undefined, 90_000);
    else await page.context().addCookies([{ name: "auth_token", value: "printing-test", url: baseURL!, httpOnly: true }]);
    const tenantId = "11111111-1111-1111-1111-111111111111";
    const businessId = "22222222-2222-2222-2222-222222222222";
    const warehouseId = "33333333-3333-3333-3333-333333333333";
    const user = { userId: "44444444-4444-4444-4444-444444444444", tenantId, tenantKey: "TEST",
      username: "test", firstName: "Prueba", lastName: "Impresión", roles: [], permissions: ["pos.sales.create"] };
    const workspace = { businessId, warehouseId, businessName: "Empresa Prueba", warehouseName: "Principal",
      warehouseCode: "B01", warehouseAllowsNegativeStockSales: true, hasActiveEdgeEnrollment: false,
      fiscalReadyForOnlineSales: true, hasDianDocumentQuota: true, fiscalWarningMessages: [], workSessionId: "session" };
    const line = { lineId: "line", productId: "product", productCode: "PRD-1", description: "Producto prueba",
      unitCode: "EA", taxCode: "01", taxRate: 0, quantity: 1, baseUnitPrice: 1000, unitPrice: 1000,
      currencyCode: "COP", priceSource: "Public", discount: 0, documentUnitCost: 500,
      net: 1000, tax: 0, total: 1000 };
    const draft = { draftId: "draft", ...workspace, userId: user.userId, status: "Active", version: 1,
      customerId: configuredCredit || catalogState === "credit-missing" ? "customer" : null, lines: [line], untaxedAmount: 1000, taxAmount: 0, payableAmount: 1000 };
    const nextDraft = { ...draft, draftId: "next", lines: [], untaxedAmount: 0, payableAmount: 0 };
    const toEdge = (value: typeof draft | typeof nextDraft) => ({ ...value, draftId: { value: value.draftId },
      lines: value.lines.map(item => ({ ...item, productId: { value: item.productId } })) });
    const receipt = { documentId: "sale", documentType: "SalesReceipt", documentNumber: "CV-TEST",
      companyName: "Empresa Prueba", issuedAt: "2026-10-01T15:00:00Z", lines: [line], payments: [],
      untaxedAmount: 1000, taxAmount: 0, payableAmount: 1000 };
    const configuration = { receiptMode: "WindowsRaw", orderMode: "WindowsPrint", posOutputFormat: "Letter",
      posPrinterName: "Printer-Letter", receiptPaperWidthMillimeters: 80,
      templateRoutes: ["Receipt", "HalfLetter", "HalfLegal", "Letter"].map(format =>
        ({ documentType: "SalesInvoice", format, printerName: `Printer-${format}` })) };
    await page.addInitScript(({ user, workspace, mode }) => {
      localStorage.setItem("selected_tenant_id", user.tenantId);
      localStorage.setItem("selected_business_id", workspace.businessId);
      localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
      localStorage.setItem(`auraly.pos.sales-workspace:${user.tenantId}:${user.userId}`, `${workspace.businessId}:${workspace.warehouseId}`);
      localStorage.setItem("auraly.pos.document-type", "SalesReceipt");
      localStorage.setItem("auraly.printing.configuration.v1", JSON.stringify({ posOutputFormat: "Letter", receiptPaperWidthMillimeters: 80 }));
      if (mode !== "browser") sessionStorage.setItem("auraly.pos.edge-token", "printing-test");
    }, { user, workspace, mode });
    // Exercise rendering while replacing the OS print dialog, which otherwise
    // blocks prepared-preview navigation and browser-context teardown.
    await page.context().addInitScript(() => {
      window.print = () => {
        const owner = (window.opener ?? window.top ?? window) as Window & { testNativePrintCalls?: number };
        owner.testNativePrintCalls = (owner.testNativePrintCalls ?? 0) + 1;
        window.dispatchEvent(new Event("afterprint"));
      };
    });

    let brandingReads = 0;
    let confirmed = false;
    const completions: Array<{ payments: Array<{ amount: number; methodCode: string; paymentMethodId?: string }>; credit?: { amount: number; paymentMethodId?: string | null; accountId?: string | null } }> = [];
    const forms = ["Cash", "DebitCard", "CreditCard", "Transfer", ...(configuredCredit || catalogState === "ready" ? ["Credit"] : [])].map((methodCode, index) => ({
      paymentMethodId: `form-${index}`, version: 1, code: `FORM-${index}`, name: `Forma ${methodCode}`,
      methodCode, accountId: `account-${index}`, useForSales: true, useForReceipts: true,
      useForSupplierPayments: true, useForPayroll: true, isActive: true,
    }));
    if (catalogState === "cards") forms.push({ ...forms[1], paymentMethodId: "form-other-debit", code: "OTHER-DEBIT", name: "Otra tarjeta débito" });
    if (catalogState === "credit-multiple") forms.push({ ...forms[4], paymentMethodId: "form-other-credit", accountId: "account-other-credit", code: "OTHER-CREDIT", name: "Otra financiación" });
    const printed: string[] = [];
    const handle = async (route: Route) => {
      const request = route.request(), url = new URL(request.url()), path = url.pathname;
      let body: unknown = [];
      if (path.endsWith("/tenants/branding/print")) {
        brandingReads++;
        await route.fulfill({ status: 500, contentType: "application/json", body: JSON.stringify({ title: "Error interno del servidor." }) });
        return;
      }
      if (path === "/api/auth/me") body = user;
      else if (path.endsWith("/health")) body = { ...workspace, status: mode === "prepared" ? "Ready" : "EnrollmentRequired",
        identityReady: true, serverConnected: true, pushConnected: true, catalogStatus: "Ready", userId: user.userId,
        userDisplayName: "Prueba", permissions: user.permissions, fiscalReady: true, fiscalWarnings: [], deviceSeriesCode: "01" };
      else if (path.endsWith("/workspace/bootstrap")) body = { tenantId, tenantName: "Empresa Prueba", userId: user.userId,
        userDisplayName: "Prueba", options: [workspace], canEnrollPosDevice: false };
      else if (path.endsWith("/workspace/options")) body = [workspace];
      else if (path.endsWith("/workspace/select")) body = workspace;
      else if (path.endsWith("/work-sessions/current")) body = { workSessionId: "session" };
      else if (path.endsWith("/drafts/active")) body = mode === "prepared" ? toEdge(draft) : draft;
      else if (path.endsWith("/customers/get") || path.endsWith("/customers/customer")) body = {
        customerId: "customer", identification: "1001", name: "Cliente con crédito", isActive: true,
        requiresElectronicInvoice: false, isCreditEnabled: true, availableCredit: 10000,
      };
      else if (path.endsWith("/inventory-validation")) body = { isValid: true, wasValidated: true, issues: [] };
      else if (path.endsWith("/settlement")) body = { grossAmount: 1000, withholdingTotal: 0, netAmount: 1000, lines: [] };
      else if (path.endsWith("/settlement-configuration")) body = { isAccountingEnabled: false, bankAccounts: [],
        ...(catalogState === "missing" ? {} : { paymentMethods: { contractVersion: catalogState === "previous" ? 1 : 2, forms } }) };
      else if (path.endsWith("/reference-options/card-franchise")) body = [{ code: "Visa", label: "Visa" }];
      else if (path.endsWith("/reference-options/payment-method")) body = configuredCredit || catalogState === "credit-missing" || catalogState === "cards"
        ? (["Cash", "DebitCard", "CreditCard", "Transfer", "Credit"] as const).map(code => ({ code,
          label: { Cash: "Efectivo", DebitCard: "Tarjeta débito", CreditCard: "Tarjeta crédito", Transfer: "Transferencia", Credit: "Crédito cliente" }[code] }))
        : [{ code: "Cash", label: "Efectivo" }, { code: "Transfer", label: "Transferencia" }, { code: "Credit", label: "Crédito cliente" }];
      else if (path.endsWith("/configuration/printers")) body = { configuration };
      else if (path.endsWith("/complete")) {
        completions.push(request.postDataJSON());
        if (catalogState === "rejected") return route.fulfill({ status: 400, contentType: "application/json",
          body: JSON.stringify({ message: "La identificación del adquirente no es válida." }) });
        confirmed = true;
        body = mode === "prepared" ? { receipt, nextDraft: toEdge(nextDraft), printedDirectly: false,
          issuedSale: { documentId: { value: "sale" }, documentNumber: "CV-TEST", total: 1000, wasAlreadyIssued: false } }
          : { receipt, nextDraft, isDuplicate: false };
      }
      else if (path.endsWith("/receipts/render") || path.endsWith("/print/receipt")) {
        expect(confirmed).toBe(true);
        const print = request.postDataJSON();
        printed.push(path.endsWith("/receipts/render") ? print.format : url.searchParams.get("format") ?? "Letter");
        if (path.endsWith("/receipts/render")) expect(print.receipts[0].companyName).toBe("Empresa Prueba");
        body = { html: "<html><body>Comprobante prueba</body></html>" };
      }
      else if (path.includes("/orders")) body = { items: [], totalCount: 0, totalPages: 0, page: 1, pageSize: 25 };
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
    };
    await page.route("**/api/**", handle);
    await page.route("**/edge/v1/**", handle);

    if (catalogState === "rejected") {
      await page.goto("/pos");
      await expect(page.locator("#pos-scanner")).toBeEnabled();
      await page.getByRole("button", { name: /^Cobrar/ }).click();
      const dialog = page.getByRole("dialog", { name: "Finalizar venta" });
      const amount = dialog.getByRole("textbox", { name: "Valor recibido en Efectivo" });
      await amount.fill("1000");
      await dialog.getByRole("button", { name: "Sin imprimir", exact: true }).click();
      await expect(dialog.getByRole("alert")).toContainText("La identificación del adquirente no es válida.");
      await expect(amount).toHaveValue("1.000");
      await dialog.getByRole("button", { name: /^Emitir e imprimir/ }).click();
      await expect.poll(() => completions.length).toBe(2);
      await expect(dialog.getByRole("alert")).toContainText("La identificación del adquirente no es válida.");
      expect(confirmed).toBe(false);
      expect(printed).toHaveLength(0);
      expect(await page.evaluate(() => (window as Window & { testNativePrintCalls?: number }).testNativePrintCalls ?? 0)).toBe(0);
      return;
    }

    if (catalogState === "cards") {
      await page.goto("/pos");
      await expect(page.locator("#pos-scanner")).toBeEnabled();
      await page.getByRole("button", { name: /^Cobrar/ }).click();
      const dialog = page.getByRole("dialog", { name: "Finalizar venta" });
      await expect(dialog.getByText("Fecha del pago", { exact: true })).toHaveCount(0);
      const cash = dialog.getByRole("textbox", { name: "Valor recibido en Efectivo" });
      await cash.fill("1200");
      await expect(dialog.getByText("Cambio a entregar", { exact: true })).toBeVisible();
      const details = page.getByRole("dialog", { name: "Datos de la tarjeta", exact: true });
      const picker = dialog.getByRole("combobox", { name: "Medio de pago" });
      for (const [shortcut, form] of [["F2", "Otra tarjeta débito"], ["F3", "Forma CreditCard"]] as const) {
        await page.keyboard.press(shortcut);
        if (shortcut === "F2") {
          await expect(details).toHaveCount(0);
          await picker.click(); await page.getByRole("option", { name: form, exact: true }).click();
        } else await expect(picker).toContainText(form);
        await expect(details).toBeVisible();
        await details.getByPlaceholder("Aprobación del datáfono").fill("APROBACION-VENTA");
        await details.getByRole("button", { name: "Guardar datos · Enter", exact: true }).click();
        await picker.click(); await page.getByRole("option", { name: form, exact: true }).click();
        await expect(details).toHaveCount(0);
        await dialog.getByRole("button", { name: /Visa · APROBACION-VENTA/ }).click();
        await expect(details.getByPlaceholder("Aprobación del datáfono")).toHaveValue("APROBACION-VENTA");
        await details.getByRole("button", { name: "Cerrar datos de tarjeta", exact: true }).click();
      }
      await dialog.getByRole("textbox", { name: "Valor recibido en Tarjeta crédito" }).fill("1000");
      await dialog.getByRole("button", { name: "Sin imprimir", exact: true }).click();
      await expect.poll(() => completions.length).toBe(1);
      expect(completions[0].payments).toMatchObject([{ methodCode: "CreditCard", paymentMethodId: "form-2", amount: 1000,
        approvalNumber: "APROBACION-VENTA", cardFranchiseCode: "Visa" }]);
      return;
    }

    if (catalogState === "missing" || catalogState === "previous") {
      await page.goto("/pos");
      await expect(page.locator("#pos-scanner")).toBeEnabled();
      await page.getByRole("button", { name: /^Cobrar/ }).click();
      const dialog = page.getByRole("dialog", { name: "Finalizar venta" });
      await expect(dialog.getByRole("alert")).toContainText("catálogo vigente");
      await expect(dialog.getByRole("button", { name: "Sin imprimir", exact: true })).toBeDisabled();
      await dialog.getByRole("textbox", { name: "Valor recibido en Efectivo" }).press("Enter");
      expect(completions).toHaveLength(0);
      expect(printed).toHaveLength(0);
      return;
    }
    if (catalogState === "credit-missing") {
      await page.goto("/pos");
      await expect(page.locator("#pos-scanner")).toBeEnabled();
      await page.getByRole("button", { name: /^Cobrar/ }).click();
      const dialog = page.getByRole("dialog", { name: "Finalizar venta" });
      await expect(dialog.getByRole("button", { name: /Crédito cliente/ })).toBeDisabled();
      const cash = dialog.getByRole("textbox", { name: "Valor recibido en Efectivo" });
      await cash.fill("500");
      await cash.press("F5");
      await expect(dialog.getByRole("textbox", { name: "Valor recibido en Crédito cliente" })).toHaveCount(0);
      await expect(dialog.getByRole("button", { name: "Sin imprimir", exact: true })).toBeDisabled();
      expect(completions).toHaveLength(0);
      expect(printed).toHaveLength(0);
      return;
    }
    if (configuredCredit) {
      await page.goto("/pos");
      await expect(page.locator("#pos-scanner")).toBeEnabled();
      await page.getByRole("button", { name: /^Cobrar/ }).click();
      const dialog = page.getByRole("dialog", { name: "Finalizar venta" });
      const cash = dialog.getByRole("textbox", { name: "Valor recibido en Efectivo" });
      await expect(dialog.getByRole("combobox", { name: "Medio de pago" })).toContainText("Forma Cash");
      await cash.fill("500");
      await cash.press("F5");
      const creditAmount = dialog.getByRole("textbox", { name: "Valor recibido en Crédito cliente" });
      await expect(creditAmount).toHaveValue("500");
      const creditForm = dialog.getByRole("combobox", { name: "Medio de pago" }).last();
      if (catalogState === "credit-multiple") {
        await expect(dialog.getByRole("button", { name: "Sin imprimir", exact: true })).toBeDisabled();
        await creditForm.click();
        await page.getByRole("option", { name: "Otra financiación", exact: true }).click();
        await expect(creditForm).toContainText("Otra financiación");
      } else await expect(creditForm).toContainText("Forma Credit");
      await expect(dialog.getByRole("alert")).toHaveCount(0);
      const amountBox = await creditAmount.boundingBox();
      const currencyBox = await creditAmount.locator("..").getByText("$", { exact: true }).boundingBox();
      expect(amountBox).not.toBeNull();
      expect(currencyBox).not.toBeNull();
      expect(currencyBox!.y).toBeGreaterThanOrEqual(amountBox!.y);
      expect(currencyBox!.y + currencyBox!.height).toBeLessThanOrEqual(amountBox!.y + amountBox!.height);
      if (mode === "browser") {
        for (const viewport of [{ width: 1366, height: 768 }, { width: 390, height: 844 }]) {
          await page.setViewportSize(viewport);
          await creditAmount.scrollIntoViewIfNeeded();
          await expect(creditForm).toBeVisible();
          expect(await dialog.evaluate(element => element.scrollWidth <= element.clientWidth + 1)).toBe(true);
          await dialog.screenshot({ path: test.info().outputPath(`configured-credit-${viewport.width}.png`) });
        }
      }
      await dialog.getByRole("button", { name: "Sin imprimir", exact: true }).click();
      await expect.poll(() => completions.length).toBe(1);
      expect(completions[0].payments).toMatchObject([{ methodCode: "Cash", amount: 500, paymentMethodId: "form-0" }]);
      expect(completions[0].credit).toMatchObject({ amount: 500 });
      expect(completions[0].credit?.paymentMethodId).toBe(catalogState === "credit-multiple" ? "form-other-credit" : "form-4");
      expect(completions[0].credit?.accountId).toBe(catalogState === "credit-multiple" ? "account-other-credit" : "account-4");
      await expect(dialog).toBeHidden();
      expect(printed).toHaveLength(0);
      return;
    }

    for (const [key, format] of [["s", null], ["t", "Receipt"], ["m", "HalfLetter"], ["o", "HalfLegal"], ["c", "Letter"], ["Enter", "Letter"]] as const) {
      confirmed = false;
      const readsAtEntry = brandingReads;
      await page.goto("/pos");
      await expect(page.locator("#pos-scanner")).toBeEnabled();
      await page.getByRole("button", { name: /^Cobrar/ }).click({ timeout: 10_000 });
      const dialog = page.getByRole("dialog", { name: "Finalizar venta" });
      for (const label of ["Sin imprimir", "Tirilla", "Media carta", "Oficio", "Carta"])
        await expect(dialog.getByRole("button", { name: label, exact: true })).toBeVisible();
      if (key === "s" && mode === "browser")
        await dialog.screenshot({ path: test.info().outputPath("print-shortcuts.png") });
      const cash = dialog.getByRole("textbox", { name: "Valor recibido en Efectivo" });
      await expect(cash).toBeVisible();
      if (key === "s") {
        // Printing shortcuts must preserve the existing payment-row shortcut.
        await cash.fill("500");
        await cash.press("F3");
        const voucher = dialog.getByRole("textbox", { name: "Valor recibido en Crédito cliente" });
        await expect(voucher).toBeVisible();
        await voucher.press("e");
        await expect(voucher).toHaveCount(0);
        await expect(cash).toHaveValue("500");
        expect(completions).toHaveLength(0);
      }
      if (mode !== "prepared") await expect.poll(() => brandingReads).toBe(readsAtEntry + 1);
      await cash.fill("100");
      await cash.press("End");
      await cash.press("0");
      await expect(cash).toHaveValue("1.000");
      const before = completions.length, printsBefore = printed.length, readsBefore = brandingReads;
      await cash.press(key);
      await expect.poll(() => completions.length).toBe(before + 1);
      expect(completions.at(-1)?.payments).toMatchObject([{ methodCode: "Cash", amount: 1000 }]);
      await expect(dialog).toBeHidden();
      if (format) {
        await expect.poll(() => printed.length).toBe(printsBefore + 1);
        expect(printed.at(-1)).toBe(format);
      }
      else {
        expect(printed).toHaveLength(printsBefore);
        await expect(page.locator('body > iframe[aria-hidden="true"]')).toHaveCount(0);
      }
      expect(brandingReads).toBe(readsBefore);
      if (mode === "prepared") expect(brandingReads).toBe(0);
    }
  });
  }
}
