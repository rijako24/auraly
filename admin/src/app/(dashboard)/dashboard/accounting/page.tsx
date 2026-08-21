"use client";

import { useState, type FormEvent, type ReactNode } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { accountingApi } from "@/services/api/accounting";
import { useTenantContextStore } from "@/stores/tenant-context-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { DatePicker } from "@/components/ui/date-picker";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";

const categories: Record<string, string> = {
  Cash: "Caja", Bank: "Bancos", DebitCardClearing: "Tarjeta débito por conciliar",
  CreditCardClearing: "Tarjeta crédito por conciliar", TransferClearing: "Transferencias por conciliar",
  AccountsReceivable: "Clientes", AccountsPayable: "Proveedores", SalesRevenue: "Ingresos por ventas",
  SalesReturns: "Devoluciones en ventas", OutputVat: "IVA generado", InputVat: "IVA descontable",
  Inventory: "Inventarios", CostOfGoodsSold: "Costo de ventas", PurchasesExpense: "Gastos de compra",
  CustomerCreditsPayable: "Saldos a favor de clientes", SupplierCreditsReceivable: "Saldos a favor con proveedores",
  WithholdingIncomeTaxPayable: "Retefuente por pagar", WithholdingVatPayable: "ReteIVA por pagar",
  WithholdingIcaPayable: "ReteICA por pagar", WithholdingIncomeTaxReceivable: "Retefuente a favor",
  WithholdingVatReceivable: "ReteIVA a favor", WithholdingIcaReceivable: "ReteICA a favor",
  OtherIncome: "Otros ingresos de caja",
  OwnerContributions: "Aportes del propietario",
  OperatingExpense: "Gastos operativos",
  OtherExpense: "Otras salidas de caja",
};

export default function AccountingConfigurationPage() {
  const tenantId = useTenantContextStore((state) => state.selectedTenantId);
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const queryClient = useQueryClient();
  const key = ["accounting-configuration", tenantId, businessId];
  const accounts = useQuery({ queryKey: [...key, "accounts"], queryFn: accountingApi.accounts, enabled: Boolean(tenantId && businessId) });
  const centers = useQuery({ queryKey: [...key, "centers"], queryFn: accountingApi.costCenters, enabled: Boolean(tenantId && businessId) });
  const periods = useQuery({ queryKey: [...key, "periods"], queryFn: accountingApi.periods, enabled: Boolean(tenantId && businessId) });
  const mappings = useQuery({ queryKey: [...key, "mappings"], queryFn: accountingApi.mappings, enabled: Boolean(tenantId && businessId) });

  const refresh = async () => queryClient.invalidateQueries({ queryKey: key });
  const [accountCode, setAccountCode] = useState("");
  const [accountName, setAccountName] = useState("");
  const [accountType, setAccountType] = useState("Asset");
  const [requiresParty, setRequiresParty] = useState(false);
  const [centerCode, setCenterCode] = useState("");
  const [centerName, setCenterName] = useState("");
  const [defaultCenter, setDefaultCenter] = useState(false);
  const year = new Date().getFullYear();
  const [periodName, setPeriodName] = useState(String(year));
  const [startsOn, setStartsOn] = useState(`${year}-01-01`);
  const [endsOn, setEndsOn] = useState(`${year}-12-31`);
  const [category, setCategory] = useState("Cash");
  const [accountId, setAccountId] = useState("");
  const [mappingScope, setMappingScope] = useState<"tenant" | "business">("tenant");
  const [effectiveFrom, setEffectiveFrom] = useState(`${year}-01-01`);

  const accountMutation = useMutation({
    mutationFn: () => accountingApi.createAccount({ accountId: crypto.randomUUID(), tenantId: tenantId!, code: accountCode, name: accountName, accountType, allowsPosting: true, requiresParty }),
    onSuccess: async () => { setAccountCode(""); setAccountName(""); await refresh(); toast.success("Cuenta creada"); },
    onError: showError,
  });
  const centerMutation = useMutation({
    mutationFn: () => accountingApi.createCostCenter({ costCenterId: crypto.randomUUID(), businessId: businessId!, code: centerCode, name: centerName, parentCostCenterId: null, isDefault: defaultCenter }),
    onSuccess: async () => { setCenterCode(""); setCenterName(""); await refresh(); toast.success("Centro de costo creado"); },
    onError: showError,
  });
  const periodMutation = useMutation({
    mutationFn: () => accountingApi.createPeriod({ periodId: crypto.randomUUID(), tenantId: tenantId!, startsOn, endsOn, name: periodName }),
    onSuccess: async () => { await refresh(); toast.success("Periodo creado"); },
    onError: showError,
  });
  const mappingMutation = useMutation({
    mutationFn: () => accountingApi.setMapping({ tenantId: tenantId!, businessId: mappingScope === "business" ? businessId : null, category, accountId, effectiveFrom, effectiveTo: null }),
    onSuccess: async () => { await refresh(); toast.success("Mapeo contable guardado"); },
    onError: showError,
  });

  if (!tenantId || !businessId) return <p className="text-sm text-muted-foreground">Selecciona empresa y sede para configurar la contabilidad.</p>;
  const postingAccounts = (accounts.data ?? []).filter((item) => item.isActive && item.allowsPosting);
  const mappedAccount = new Map((accounts.data ?? []).map((item) => [item.accountId, `${item.code} — ${item.name}`]));

  return <div className="space-y-6">
    <div><h1 className="text-2xl font-semibold">Configuración contable</h1><p className="text-sm text-muted-foreground">PUC operativo, centros de costo, periodos y cuentas automáticas por concepto.</p></div>
    <div className="grid gap-5 xl:grid-cols-2">
      <Card><CardHeader><CardTitle>Nueva cuenta auxiliar</CardTitle></CardHeader><CardContent><form className="grid gap-4 sm:grid-cols-2" onSubmit={(event) => submit(event, accountMutation.mutate)}>
        <Field label="Código PUC"><Input value={accountCode} onChange={(e) => setAccountCode(e.target.value)} required /></Field>
        <Field label="Nombre"><Input value={accountName} onChange={(e) => setAccountName(e.target.value)} required /></Field>
        <Field label="Naturaleza"><Select value={accountType} onValueChange={setAccountType}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent>{["Asset", "Liability", "Equity", "Revenue", "Expense", "ContraRevenue"].map((value) => <SelectItem key={value} value={value}>{value}</SelectItem>)}</SelectContent></Select></Field>
        <label className="flex items-end gap-2 pb-2 text-sm"><Checkbox className="h-5 w-5 rounded-md" checked={requiresParty} onCheckedChange={(checked) => setRequiresParty(checked === true)} /> Exige tercero</label>
        <Button disabled={accountMutation.isPending}>Crear cuenta</Button>
      </form></CardContent></Card>
      <Card><CardHeader><CardTitle>Centro de costo</CardTitle></CardHeader><CardContent><form className="grid gap-4 sm:grid-cols-2" onSubmit={(event) => submit(event, centerMutation.mutate)}>
        <Field label="Código"><Input value={centerCode} onChange={(e) => setCenterCode(e.target.value)} required /></Field>
        <Field label="Nombre"><Input value={centerName} onChange={(e) => setCenterName(e.target.value)} required /></Field>
        <label className="flex items-center gap-2 text-sm"><Checkbox className="h-5 w-5 rounded-md" checked={defaultCenter} onCheckedChange={(checked) => setDefaultCenter(checked === true)} /> Centro predeterminado</label>
        <Button disabled={centerMutation.isPending}>Crear centro</Button>
      </form></CardContent></Card>
      <Card><CardHeader><CardTitle>Periodo contable</CardTitle></CardHeader><CardContent><form className="grid gap-4 sm:grid-cols-3" onSubmit={(event) => submit(event, periodMutation.mutate)}>
        <Field label="Nombre"><Input value={periodName} onChange={(e) => setPeriodName(e.target.value)} required /></Field>
        <Field label="Desde"><DatePicker value={startsOn} onChange={setStartsOn} /></Field>
        <Field label="Hasta"><DatePicker value={endsOn} onChange={setEndsOn} /></Field>
        <Button disabled={periodMutation.isPending}>Crear periodo</Button>
      </form></CardContent></Card>
      <Card><CardHeader><CardTitle>Mapeo automático</CardTitle></CardHeader><CardContent><form className="grid gap-4 sm:grid-cols-2" onSubmit={(event) => submit(event, mappingMutation.mutate)}>
        <Field label="Concepto"><Select value={category} onValueChange={setCategory}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent>{Object.entries(categories).map(([value, label]) => <SelectItem key={value} value={value}>{label}</SelectItem>)}</SelectContent></Select></Field>
        <Field label="Cuenta"><Select value={accountId} onValueChange={setAccountId} required><SelectTrigger><SelectValue placeholder={accounts.isLoading ? "Cargando cuentas…" : "Selecciona cuenta"} /></SelectTrigger><SelectContent>{postingAccounts.map((item) => <SelectItem key={item.accountId} value={item.accountId}>{item.code} — {item.name}</SelectItem>)}</SelectContent></Select></Field>
        <Field label="Alcance"><Select value={mappingScope} onValueChange={(value) => setMappingScope(value as "tenant" | "business")}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value="tenant">Toda la empresa</SelectItem><SelectItem value="business">Solo esta sede</SelectItem></SelectContent></Select></Field>
        <Field label="Vigente desde"><DatePicker value={effectiveFrom} onChange={setEffectiveFrom} /></Field>
        <Button disabled={!accountId || mappingMutation.isPending}>Guardar mapeo</Button>
      </form></CardContent></Card>
    </div>
    <Card><CardHeader><CardTitle>Mapeos vigentes</CardTitle></CardHeader><CardContent>{mappings.isLoading ? <p>Cargando…</p> : <div className="overflow-x-auto"><table className="w-full text-sm"><thead><tr className="border-b text-left"><th className="py-2">Concepto</th><th>Cuenta</th><th>Alcance</th><th>Desde</th></tr></thead><tbody>{(mappings.data ?? []).map((item) => <tr key={item.mappingId} className="border-b"><td className="py-3">{categories[item.category] ?? item.category}</td><td>{mappedAccount.get(item.accountId) ?? item.accountId}</td><td>{item.businessId ? "Sede" : "Empresa"}</td><td>{item.effectiveFrom}</td></tr>)}</tbody></table></div>}</CardContent></Card>
    <div className="grid gap-5 md:grid-cols-2"><Summary title="Centros de costo" values={(centers.data ?? []).map((item) => `${item.code} — ${item.name}${item.isDefault ? " (predeterminado)" : ""}`)} /><Summary title="Periodos" values={(periods.data ?? []).map((item) => `${item.name}: ${item.startsOn} a ${item.endsOn} — ${item.status}`)} /></div>
  </div>;
}

function submit(event: FormEvent, mutate: () => void) { event.preventDefault(); mutate(); }
function showError(error: unknown) { toast.error(error instanceof Error ? error.message : "No fue posible guardar la configuración"); }
function Field({ label, children }: { label: string; children: ReactNode }) { return <div className="space-y-2"><Label>{label}</Label>{children}</div>; }
function Summary({ title, values }: { title: string; values: string[] }) { return <Card><CardHeader><CardTitle>{title}</CardTitle></CardHeader><CardContent>{values.length ? <ul className="space-y-2 text-sm">{values.map((value) => <li key={value}>{value}</li>)}</ul> : <p className="text-sm text-muted-foreground">Sin registros.</p>}</CardContent></Card>; }
