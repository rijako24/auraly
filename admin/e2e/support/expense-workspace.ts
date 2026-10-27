import { expect, type Page } from "@playwright/test";

const tenantId = "11111111-1111-1111-1111-111111111111", businessId = "22222222-2222-2222-2222-222222222222";
const accountId = "44444444-4444-4444-4444-444444444444", supplierId = "55555555-5555-5555-5555-555555555555";
const conceptId = "66666666-6666-6666-6666-666666666666";
async function openExpenses(page: Page, baseURL: string, enterSupplierInvoice = true,
  behavior: {holdPreview?: boolean; manageWithholdings?: boolean; rules?: object[];
    supplierSearch?: string; supplierPolicy?: "InternalReceiptVoucher"} = {}) {
  const user = { userId: "33333333-3333-3333-3333-333333333333", tenantId, tenantKey: "@expense",
    username: "accountant", firstName: "Prueba", lastName: "Gastos", roles: [], permissions: ["expenses.read", "expenses.create", "expenses.configure", "expenses.cancel",
      ...(behavior.manageWithholdings ? ["commerce.taxation.withholdings.manage"] : [])] };
  await page.context().addCookies([{ name: "auth_token", value: "expenses", url: baseURL, httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ user, businessId }) => {
    localStorage.setItem("selected_tenant_id", user.tenantId); localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
  }, { user, businessId });
  const state = { preview: [] as Record<string, unknown>[], confirm: [] as Record<string, unknown>[], savedRules: [] as Record<string, unknown>[], supplierSearches: [] as string[], list: 0, accountReads: 0, conflict: false, canConfirm: true };
  let releasePreview = () => {};
  const previewGate = new Promise<void>(resolve => { releasePreview = resolve; });
  await page.route("**/api/**", async route => {
    const request = route.request(), path = new URL(request.url()).pathname;
    let body: unknown = [], status = 200;
    if (path === "/api/auth/me") body = user;
    else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Empresa" }];
    else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Sede" }];
    else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, permissions: user.permissions, roles: [] };
    else if (path.endsWith("/reference-options/accounting-withholding-kind")) body = [
      { id: "kind-income", code: "IncomeTax", label: "Retefuente", isActive: true, sortOrder: 10 },
      { id: "kind-vat", code: "Vat", label: "ReteIVA", isActive: true, sortOrder: 20 },
      { id: "kind-ica", code: "IndustryCommerce", label: "ReteICA", isActive: true, sortOrder: 30 },
    ];
    else if (path.endsWith("/accounting/readiness")) body = { status: "Ready", blockingIssues: [] };
    else if (path.endsWith("/expenses/options")) body = { concepts: [{ conceptId, businessId, code: "SERV", name: "Servicio frecuente", expenseAccountId: accountId,
      expenseAccountCode: "519595", expenseAccountName: "Servicios", defaultCostCenterId: null, defaultCostCenterName: null, withholdingConceptCode: "SERVICIOS", isActive: true }],
      suppliers: [], expenseAccounts: [], costCenters: [], taxes: [{ taxProfileId: "77777777-7777-7777-7777-777777777777", name: "IVA", rate: 19 }],
      taxTreatments: [{ code: "DeductibleInputVat", label: "IVA descontable" }, { code: "CapitalizedCost", label: "Mayor valor del gasto" }],
      withholdingConceptCodes: ["SERVICIOS"], purchaseEvidenceTypes: [{ code: "SupplierElectronicInvoice", label: "Factura electrónica del proveedor" }, { code: "InternalReceiptVoucher", label: "Comprobante interno" }, { code: "BuyerElectronicSupportDocument", label: "Documento soporte" }] };
    else if (path.endsWith("/parties/role-options")) body = { items: [{ partyId: supplierId, roleId: supplierId, role: "Supplier", displayName: "Proveedor de prueba", identification: "900123456", supplierPurchaseEvidencePolicy: null, supplierDefaultPaymentDueDays: 30, supplierId }], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 };
    else if (path.endsWith("/parties/site-options")) {
      state.supplierSearches.push(new URL(request.url()).searchParams.get("search") ?? "");
      body = { items: [
      { partyId: supplierId, roleId: supplierId, partySiteId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", siteName: "Sede principal", displayName: "Proveedor de prueba", identification: "900123456", isPrimary: true, supplierPurchaseEvidencePolicy: null, supplierDefaultPaymentDueDays: 30 },
      { partyId: supplierId, roleId: supplierId, partySiteId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", siteName: "Sede norte", displayName: "Proveedor de prueba", identification: "900123456", isPrimary: false, supplierPurchaseEvidencePolicy: null, supplierDefaultPaymentDueDays: 30 },
    ].map(site => ({...site,supplierPurchaseEvidencePolicy:behavior.supplierPolicy??null})), page: 1, pageSize: 20, totalCount: 2, totalPages: 1 };
    }
    else if (path.endsWith("/taxation/withholding-rules")) {
      if (request.method() === "POST") {
        const value = request.postDataJSON();state.savedRules.push(value);
        body = {...value,ruleId:crypto.randomUUID(),version:1};
      } else body = [...(behavior.rules ?? []),...state.savedRules];
    }
    else if (path.endsWith("/accounting/account-options")) { state.accountReads++; const liability = new URL(request.url()).searchParams.get("liabilityOnly") === "true"; body = { items: liability
      ? [{ accountId: "88888888-8888-8888-8888-888888888888", code: "236570", name: "Retenciones por pagar", accountType: "Liability", allowsPosting: true, isActive: true }]
      : [{ accountId: "99999999-9999-9999-9999-999999999999", code: "519596", name: "Servicios varios", accountType: "Expense", allowsPosting: true, isActive: true }], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 }; }
    else if (path.endsWith("/expenses/preview")) {
      if (behavior.holdPreview) await previewGate;
      const value = request.postDataJSON(); state.preview.push(value);
      const base = value.lines.reduce((sum: number, line: { taxExclusiveAmount: number }) => sum + line.taxExclusiveAmount, 0);
      const manual = (value.withholdingAdjustments ?? []).filter((item: {action:string}) => item.action === "Manual");
      const manualTotal = manual.reduce((sum:number,item:{amount:number}) => sum + item.amount, 0);
      body = { lines: value.lines.map((line: object, index: number) => ({ ...line, expenseAccountId: accountId, lineNumber: index + 1, accountCode: "519595", accountName: "Servicios", vatAmount: 0, taxRate: 0 })),
        calculationHash: `review-${state.preview.length}`, canConfirm: state.canConfirm,
        diagnostics: state.canConfirm ? [] : ["Falta el perfil tributario del proveedor."],
        withholding: { grossAmount: base, withholdingTotal: base * .025 + manualTotal, netAmount: base * .975 - manualTotal,
          lines: [{ ruleId: "rule", ruleVersion: 1, ruleCode: "RF", name: "Retefuente servicios", kind:"IncomeTax", baseKind:"TaxExclusiveAmount", taxableBase: base, rate: 2.5, amount: base * .025, jurisdictionCode:null },
            ...manual.map((item:object) => ({...item,ruleVersion:0,ruleCode:"MANUAL",baseKind:"TaxExclusiveAmount"}))] } };
    } else if (path.endsWith("/expenses/confirm")) {
      state.confirm.push(request.postDataJSON());
      if (state.conflict) { status = 409; body = { detail: "El cálculo cambió. Recalcula las retenciones." }; }
      else { status = 202; body = { expenseId: request.postDataJSON().expenseId, status: "Accepted" }; }
    } else if (path.endsWith("/expenses")) { state.list++; body = { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0, grossTotal: 0, withholdingTotal: 0, netPayableTotal: 0 }; }
    await route.fulfill({ status, contentType: "application/json", body: JSON.stringify(body) });
  });
  await page.goto("/dashboard/expenses");
  await page.getByRole("button", { name: "Nuevo gasto", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Registrar gasto" });
  await expect(dialog.getByRole("combobox", { name: "Seleccionar proveedor y sede" })).toHaveCount(1);
  await dialog.getByRole("combobox", { name: "Seleccionar proveedor y sede" }).click();
  if (behavior.supplierSearch) {
    await page.getByPlaceholder("Buscar por nombre o identificación…").fill(behavior.supplierSearch);
    await expect.poll(() => state.supplierSearches).toContain(behavior.supplierSearch);
  }
  await expect(page.getByRole("option", { name: /Sede principal/ })).toBeVisible();
  await expect(page.getByRole("option", { name: /Sede norte/ })).toBeVisible();
  await page.getByRole("option", { name: /Sede principal/ }).click();
  if (enterSupplierInvoice) await dialog.getByLabel("Número de factura del proveedor").fill("FV-100");
  await dialog.getByRole("button", { name: "Agregar gasto", exact: true }).click();
  const line = page.getByRole("dialog", { name: "Agregar gasto", exact: true });
  await expect(line.getByRole("button", { name: "Agregar a la grilla" })).toBeDisabled();
  await expect(line.getByLabel("Concepto de gasto")).toContainText("Seleccionar concepto");
  await expect(line.getByRole("button", { name: "Quitar selección de concepto" })).toHaveCount(0);
  await line.getByLabel("Concepto de gasto").click();
  await page.getByRole("option", { name: "Servicio frecuente" }).click();
  await line.getByRole("button", { name: "Quitar selección de concepto" }).click();
  await expect(line.getByLabel("Concepto de gasto")).toContainText("Seleccionar concepto");
  await expect(line.getByRole("combobox", { name: "Seleccionar cuenta contable" })).toHaveCount(0);
  await line.getByLabel("Concepto de gasto").click();
  await page.getByRole("option", { name: "Servicio frecuente" }).click();
  await line.getByLabel("Base antes de IVA").fill("120000");
  await line.getByLabel("Base antes de IVA").blur();
  await line.getByRole("button", { name: "Agregar a la grilla" }).click();
  await expect(line).toHaveCount(0);
  return { state, dialog, releasePreview };
}

export { openExpenses, tenantId, businessId, accountId, supplierId, conceptId };
