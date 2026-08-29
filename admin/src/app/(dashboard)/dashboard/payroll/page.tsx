"use client";
import { useCallback, useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { Calculator, CheckCircle2, ExternalLink, FilePlus2, Loader2, Pencil, Settings2, Trash2, Users } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { DatePicker } from "@/components/ui/date-picker";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { ReportViewer } from "@/components/reports/report-viewer";
import { PartyRoleSelect } from "@/components/parties/party-role-select";
import { PayrollEmploymentSelect } from "@/components/payroll/payroll-employment-select";
import { accountingApi, type AccountingAccount, type AccountingCategoryDefinition, type AccountingMapping } from "@/services/api/accounting";
import { payrollApi, type PayrollCatalogOption, type PayrollConcept, type PayrollDeductionAgreement, type PayrollEmployment, type PayrollOptions, type PayrollReportDefinition, type PayrollRuleSet, type PayrollRun, type PayrollRunSummary, type SaveConcept, type SaveDeductionAgreement, type SaveEmployment, type SavePayrollNovelty, type SaveRuleSet } from "@/services/api/payroll";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });
type Section = "runs" | "employments" | "novelties" | "payments" | "electronic" | "reports" | "configuration";
export default function PayrollPage() {
    const businessId = useBusinessContextStore(state => state.selectedBusinessId), permissions = useAuthStore(state => new Set(state.user?.permissions ?? []));
    const [options, setOptions] = useState<PayrollOptions | null>(null), [runs, setRuns] = useState<PayrollRunSummary[]>([]), [selected, setSelected] = useState<PayrollRun | null>(null), [loading, setLoading] = useState(false), [section, setSection] = useState<Section>("runs");
    const [creatingRun, setCreatingRun] = useState(false), [creatingEmployment, setCreatingEmployment] = useState(false), [editingEmployment, setEditingEmployment] = useState<PayrollEmployment | null>(null), [employmentRevision, setEmploymentRevision] = useState(0), [preselectedPartyId, setPreselectedPartyId] = useState<string | null>(null), [creatingConcept, setCreatingConcept] = useState(false), [editingConcept, setEditingConcept] = useState<PayrollConcept | null>(null), [creatingRules, setCreatingRules] = useState(false), [editingRules, setEditingRules] = useState<PayrollRuleSet | null>(null), [creatingElectronic, setCreatingElectronic] = useState(false), [creatingAgreement, setCreatingAgreement] = useState(false), [editingAgreement, setEditingAgreement] = useState<PayrollDeductionAgreement | null>(null), [creatingNovelty, setCreatingNovelty] = useState(false), [creatingPayment, setCreatingPayment] = useState(false);
    const selectedId = selected?.payrollRunId;
    const load = useCallback(async () => { if (!businessId)
        return; setLoading(true); try {
        const [nextOptions, nextRuns] = await Promise.all([payrollApi.options(), payrollApi.runs()]);
        setOptions(nextOptions);
        setRuns(nextRuns);
        if (selectedId)
            setSelected(await payrollApi.run(selectedId));
    }
    catch (error) {
        toast.error(message(error, "No fue posible cargar la nómina."));
    }
    finally {
        setLoading(false);
    } }, [businessId, selectedId]);
    useEffect(() => { void load(); }, [load]);
    useEffect(() => { const query = new URLSearchParams(window.location.search), requested = query.get("section") as Section | null, partyId = query.get("partyId"); if (requested && ["runs", "employments", "novelties", "payments", "electronic", "reports", "configuration"].includes(requested))
        setSection(requested); if (partyId) {
        setPreselectedPartyId(partyId);
        setSection("employments");
        setCreatingEmployment(true);
    } }, []);
    if (!businessId)
        return <Card><CardContent className="p-8 text-center text-muted-foreground">Selecciona una sede para trabajar con nómina.</CardContent></Card>;
    return <div className="space-y-6">
    <header className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between"><div><p className="text-sm font-semibold text-emerald-600">Laboral, contable y fiscal</p><h1 className="text-3xl font-bold tracking-tight">Nómina</h1><p className="mt-1 max-w-3xl text-muted-foreground">Configura contratos y reglas versionadas, liquida con trazabilidad y aprueba una fuente contable inmutable.</p></div><div className="flex flex-wrap gap-2">{permissions.has("payroll.manage") && <Button variant="outline" onClick={() => setCreatingEmployment(true)}><Users className="mr-2 h-4 w-4"/>Nuevo contrato</Button>}{permissions.has("payroll.fiscal") && <Button variant="outline" onClick={() => setCreatingElectronic(true)}><FilePlus2 className="mr-2 h-4 w-4"/>Consolidar mes DIAN</Button>}{permissions.has("payroll.calculate") && <Button onClick={() => setCreatingRun(true)}><FilePlus2 className="mr-2 h-4 w-4"/>Nueva liquidación</Button>}</div></header>
    <div className="flex flex-wrap gap-2">{(["runs", "employments", "novelties", "payments", "electronic", "reports", "configuration"] as Section[]).map(value => <Button key={value} variant={section === value ? "default" : "outline"} onClick={() => setSection(value)}>{({ runs: "Liquidaciones", employments: "Trabajadores", novelties: "Novedades y descuentos", payments: "Pagos", electronic: "Electrónica DIAN", reports: "Reportes", configuration: "Configuración" } as Record<Section, string>)[value]}</Button>)}</div>
    {loading && <p className="flex items-center gap-2 text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin"/>Actualizando nómina…</p>}
    {section === "runs" && (
      <RunsSection runs={runs} selected={selected} canCalculate={permissions.has("payroll.calculate")} canApprove={permissions.has("payroll.approve")} onSelect={async (id) => setSelected(await payrollApi.run(id))} onChanged={load}/>
    )}
    {section === "employments" && (
      <EmploymentsSection revision={employmentRevision} canManage={permissions.has("payroll.manage")} onEdit={setEditingEmployment}/>
    )}
    {section === "novelties" && (
      <NoveltiesSection options={options} canManage={permissions.has("payroll.manage")} onAgreement={() => setCreatingAgreement(true)} onEditAgreement={setEditingAgreement} onChanged={load} onNovelty={() => setCreatingNovelty(true)}/>
    )}
    {section === "payments" && (
      <PaymentsSection options={options} canPay={permissions.has("payroll.pay")} onCreate={() => setCreatingPayment(true)}/>
    )}
    {section === "electronic" && (
      <ElectronicSection options={options} canGenerate={permissions.has("payroll.fiscal")} onGenerate={() => setCreatingElectronic(true)}/>
    )}
    {section === "reports" && (
      <PayrollReports/>
    )}
    {section === "configuration" && (
      <ConfigurationSection options={options} canConfigure={permissions.has("payroll.configure")} canConfigureAccounting={permissions.has("accounting.configure")} onConcept={() => setCreatingConcept(true)} onEditConcept={setEditingConcept} onRules={() => setCreatingRules(true)} onEditRules={setEditingRules} onChanged={load}/>
    )}
    <Dialog open={creatingRun} onOpenChange={setCreatingRun}><DialogContent><DialogHeader><DialogTitle>Nueva liquidación</DialogTitle><DialogDescription>El cálculo usa exclusivamente el conjunto de reglas aprobado para estas fechas.</DialogDescription></DialogHeader>{options && <RunForm businessId={businessId} options={options} runs={runs} onSaved={async () => { setCreatingRun(false); await load(); }}/>}</DialogContent></Dialog>
    <Dialog open={creatingEmployment || editingEmployment !== null} onOpenChange={open => { if (!open) {
        setCreatingEmployment(false);
        setEditingEmployment(null);
        setPreselectedPartyId(null);
    } }}><DialogContent className="max-h-[92dvh] max-w-3xl overflow-y-auto"><DialogHeader><DialogTitle>{editingEmployment ? "Editar relación laboral" : "Nueva relación laboral"}</DialogTitle><DialogDescription>La persona proviene del maestro de terceros; salario y contrato viven solo en Nómina.</DialogDescription></DialogHeader>{options && <EmploymentForm businessId={businessId} options={options} initial={editingEmployment} partyId={preselectedPartyId} onSaved={async () => { setCreatingEmployment(false); setEditingEmployment(null); setPreselectedPartyId(null); setEmploymentRevision(value => value + 1); await load(); }}/>}</DialogContent></Dialog>
    <Dialog open={creatingConcept || editingConcept !== null} onOpenChange={open => { if (!open) { setCreatingConcept(false); setEditingConcept(null); } }}><DialogContent className="max-h-[92dvh] max-w-3xl overflow-y-auto"><DialogHeader><DialogTitle>{editingConcept ? "Editar concepto" : "Nuevo concepto"}</DialogTitle><DialogDescription>Naturaleza, tratamiento, DIAN y cuenta se eligen desde catálogos persistidos.</DialogDescription></DialogHeader>{options && <ConceptForm options={options} initial={editingConcept} onSaved={async () => { setCreatingConcept(false); setEditingConcept(null); await load(); }}/>}</DialogContent></Dialog>
    <Dialog open={creatingRules || editingRules !== null} onOpenChange={open => { if (!open) { setCreatingRules(false); setEditingRules(null); } }}><DialogContent className="max-h-[92dvh] max-w-4xl overflow-y-auto"><DialogHeader><DialogTitle>{editingRules ? "Editar reglas en borrador" : "Nuevo conjunto de reglas"}</DialogTitle><DialogDescription>Los parámetros y unidades se cargan desde tabla. Verifica la fuente normativa antes de aprobar.</DialogDescription></DialogHeader>{options && <RuleSetForm options={options} initial={editingRules} onSaved={async () => { setCreatingRules(false); setEditingRules(null); await load(); }}/>}</DialogContent></Dialog>
    <Dialog open={creatingElectronic} onOpenChange={setCreatingElectronic}><DialogContent><DialogHeader><DialogTitle>Consolidar nómina electrónica</DialogTitle><DialogDescription>Reserva la serie, crea el XML por trabajador y lo envía al motor fiscal para validar, firmar y transmitir a la DIAN.</DialogDescription></DialogHeader><ElectronicPeriodForm businessId={businessId} onSaved={async () => { setCreatingElectronic(false); await load(); }}/></DialogContent></Dialog>
    <Dialog open={creatingAgreement || editingAgreement !== null} onOpenChange={open => { if (!open) { setCreatingAgreement(false); setEditingAgreement(null); } }}><DialogContent className="max-h-[92dvh] max-w-3xl overflow-y-auto"><DialogHeader><DialogTitle>{editingAgreement ? "Editar descuento autorizado" : "Nuevo descuento autorizado"}</DialogTitle><DialogDescription>Registra autoridad, evidencia, prioridad, vigencia y límites antes de descontar.</DialogDescription></DialogHeader>{options && <DeductionAgreementForm options={options} initial={editingAgreement} onSaved={async () => { setCreatingAgreement(false); setEditingAgreement(null); await load(); }}/>}</DialogContent></Dialog>
    <Dialog open={creatingNovelty} onOpenChange={setCreatingNovelty}><DialogContent className="max-h-[92dvh] max-w-3xl overflow-y-auto"><DialogHeader><DialogTitle>Nueva novedad</DialogTitle><DialogDescription>Los tipos y conceptos provienen de catálogos persistidos.</DialogDescription></DialogHeader>{options && <NoveltyForm options={options} onSaved={async () => { setCreatingNovelty(false); await load(); }}/>}</DialogContent></Dialog>
    <Dialog open={creatingPayment} onOpenChange={setCreatingPayment}><DialogContent><DialogHeader><DialogTitle>Confirmar lote de pago</DialogTitle><DialogDescription>Paga el neto de una liquidación aprobada y envía el comprobante al motor contable.</DialogDescription></DialogHeader>{options && <PaymentForm options={options} runs={runs} onSaved={async () => { setCreatingPayment(false); await load(); }}/>}</DialogContent></Dialog>
  </div>;
}
function RunsSection({ runs, selected, canCalculate, canApprove, onSelect, onChanged }: {
    runs: PayrollRunSummary[];
    selected: PayrollRun | null;
    canCalculate: boolean;
    canApprove: boolean;
    onSelect: (id: string) => Promise<void>;
    onChanged: () => Promise<void>;
}) {
    const [busy, setBusy] = useState<string | null>(null);
    async function calculate() { if (!selected)
        return; setBusy("calculate"); try {
        await payrollApi.calculateRun(selected.payrollRunId);
        toast.success("Liquidación calculada con snapshot de reglas.");
        await onSelect(selected.payrollRunId);
        await onChanged();
    }
    catch (error) {
        toast.error(message(error, "No fue posible calcular."));
    }
    finally {
        setBusy(null);
    } }
    async function approve() { if (!selected)
        return; setBusy("approve"); try {
        await payrollApi.approveRun(selected.payrollRunId, selected.rowVersion, `payroll-approve-${selected.payrollRunId}`);
        toast.success("Nómina aprobada y enviada al motor contable.");
        await onSelect(selected.payrollRunId);
        await onChanged();
    }
    catch (error) {
        toast.error(message(error, "No fue posible aprobar."));
    }
    finally {
        setBusy(null);
    } }
    return <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_minmax(420px,1.2fr)]"><Card><CardHeader><CardTitle>Liquidaciones</CardTitle><CardDescription>Los estados aprobados son inmutables.</CardDescription></CardHeader><CardContent className="space-y-2">{runs.map(run => <button type="button" key={run.payrollRunId} onClick={() => void onSelect(run.payrollRunId)} className={`w-full rounded-2xl border p-4 text-left transition-colors ${selected?.payrollRunId === run.payrollRunId ? "border-primary bg-primary/5" : "hover:bg-muted/40"}`}><span className="flex items-center justify-between gap-3"><b>{run.periodStart} — {run.periodEnd}</b><small>{run.status}</small></span><span className="mt-2 grid grid-cols-3 text-sm text-muted-foreground"><span>{run.employeeCount} trabajadores</span><span className="text-right">Deducciones {money.format(run.totalDeductions)}</span><strong className="text-right text-foreground">Neto {money.format(run.netPayable)}</strong></span></button>)}{runs.length === 0 && <Empty text="Todavía no hay liquidaciones."/>}</CardContent></Card><Card><CardHeader><CardTitle>Detalle</CardTitle><CardDescription>{selected ? `${selected.periodStart} — ${selected.periodEnd}` : "Selecciona una liquidación"}</CardDescription></CardHeader><CardContent>{!selected ? <Empty text="El detalle mostrará trabajadores, conceptos y totales."/> : <div className="space-y-4"><div className="grid gap-3 sm:grid-cols-3"><Metric label="Devengado" value={selected.totalEarnings}/><Metric label="Deducciones" value={selected.totalDeductions}/><Metric label="Neto" value={selected.netPayable}/></div><div className="flex flex-wrap gap-2">{canCalculate && ["Draft", "Calculated"].includes(selected.status) && <Button variant="outline" disabled={busy !== null} onClick={() => void calculate()}><Calculator className="mr-2 h-4 w-4"/>{busy === "calculate" ? "Calculando…" : "Calcular de nuevo"}</Button>}{canApprove && selected.status === "Calculated" && <Button disabled={busy !== null} onClick={() => void approve()}><CheckCircle2 className="mr-2 h-4 w-4"/>{busy === "approve" ? "Aprobando…" : "Aprobar y contabilizar"}</Button>}</div>{selected.employees.map(employee => <details key={employee.payrollRunEmployeeId} className="rounded-2xl border p-4"><summary className="cursor-pointer font-semibold">{employee.employeeName}<span className="float-right">{money.format(employee.netPayable)}</span></summary><div className="mt-3 overflow-x-auto"><table className="w-full min-w-[620px] text-sm"><thead><tr className="text-left text-muted-foreground"><th>Concepto</th><th>Naturaleza</th><th className="text-right">Base</th><th className="text-right">Valor</th></tr></thead><tbody>{employee.lines.map(line => <tr key={line.lineNumber} className="border-t"><td className="py-2">{line.conceptCode} · {line.conceptName}</td><td>{line.natureCode}</td><td className="text-right">{line.baseAmount === null ? "—" : money.format(line.baseAmount)}</td><td className="text-right font-medium">{money.format(line.amount)}</td></tr>)}</tbody></table></div></details>)}</div>}</CardContent></Card></div>;
}
function EmploymentsSection({ revision, canManage, onEdit }: {
    revision: number;
    canManage: boolean;
    onEdit: (value: PayrollEmployment) => void;
}) {
    const [page, setPage] = useState(1);
    const query = useQuery({ queryKey: ["payroll-employments", page, revision], queryFn: () => payrollApi.employments({ page, pageSize: 25 }) });
    const items = query.data?.items ?? [];
    async function deactivate(item: PayrollEmployment) { if (!window.confirm(`¿Desactivar el contrato ${item.contractNumber}?`)) return; try { await payrollApi.saveEmployment({ ...item, isActive: false }); toast.success("Contrato desactivado."); await query.refetch(); } catch (error) { toast.error(message(error, "No fue posible desactivar el contrato.")); } }
    return <Card><CardHeader><CardTitle>Relaciones laborales</CardTitle><CardDescription>El tercero conserva la identidad; aquí viven contrato, salario, banco y parámetros laborales.</CardDescription></CardHeader><CardContent className="space-y-3"><div className="overflow-x-auto rounded-xl border"><table className="w-full min-w-[820px] text-sm"><thead className="bg-muted/50"><tr><th className="p-3 text-left">Trabajador</th><th className="text-left">Contrato</th><th className="text-left">Vigencia</th><th className="text-left">Cuenta de pago</th><th className="text-right">Salario</th><th className="pr-3 text-right">Acciones</th></tr></thead><tbody>{items.map(item => <tr key={item.employmentId} className="border-t"><td className="p-3 font-medium">{item.employeeName}</td><td>{item.contractNumber}</td><td>{item.startDate} — {item.endDate ?? "vigente"}</td><td>{item.bankAccountNumber ? `•••• ${item.bankAccountNumber.slice(-4)}` : "Sin configurar"}</td><td className="text-right">{money.format(item.monthlySalary)}</td><td className="pr-3 text-right"><span className="inline-flex">{canManage && <Button size="sm" variant="ghost" onClick={() => onEdit(item)}><Pencil className="mr-2 h-4 w-4"/>Editar</Button>}{canManage && item.isActive && <Button size="sm" variant="ghost" className="text-destructive" onClick={() => void deactivate(item)}><Trash2 className="mr-2 h-4 w-4"/>Desactivar</Button>}</span></td></tr>)}</tbody></table>{query.isLoading && <p className="p-4 text-sm text-muted-foreground">Cargando trabajadores…</p>}{query.isError && <p className="p-4 text-sm text-destructive">No fue posible cargar los contratos.</p>}{!query.isLoading && !items.length && <Empty text="Crea el primer contrato laboral."/>}</div><div className="flex items-center justify-between text-sm"><span className="text-muted-foreground">{query.data?.totalCount ?? 0} contratos</span><span className="flex gap-2"><Button size="sm" variant="outline" disabled={page <= 1 || query.isFetching} onClick={() => setPage(value => value - 1)}>Anterior</Button><Button size="sm" variant="outline" disabled={!query.data || page * query.data.pageSize >= query.data.totalCount || query.isFetching} onClick={() => setPage(value => value + 1)}>Siguiente</Button></span></div></CardContent></Card>;
}
function NoveltiesSection({ options, canManage, onAgreement, onEditAgreement, onChanged, onNovelty }: {
    options: PayrollOptions | null;
    canManage: boolean;
    onAgreement: () => void;
    onEditAgreement: (value: PayrollDeductionAgreement) => void;
    onChanged: () => Promise<void>;
    onNovelty: () => void;
}) { async function deactivate(item: PayrollDeductionAgreement) { try { await payrollApi.saveDeductionAgreement({ ...item, beneficiaryPartyId: null, isActive: false }); toast.success("Descuento desactivado."); await onChanged(); } catch (error) { toast.error(message(error, "No fue posible desactivar el descuento.")); } } function evidence(url: string) { try { const parsed = new URL(url); if (!['http:', 'https:'].includes(parsed.protocol)) throw new Error(); window.open(parsed.toString(), '_blank', 'noopener,noreferrer'); } catch { toast.error("La evidencia no tiene una URL válida."); } } return <div className="grid gap-5 xl:grid-cols-2"><Card><CardHeader><span className="flex items-center justify-between gap-3"><span><CardTitle>Descuentos autorizados</CardTitle><CardDescription>Acuerdos, libranzas, embargos y otros conceptos con soporte.</CardDescription></span>{canManage && <Button size="sm" onClick={onAgreement}>Agregar</Button>}</span></CardHeader><CardContent className="space-y-2">{options?.deductionAgreements.map(item => <div key={item.deductionAgreementId} className="rounded-xl border p-3 text-sm"><span className="flex justify-between gap-3"><b>{item.employeeName}</b><span>{item.isActive ? "Activo" : "Inactivo"}</span></span><p>{item.conceptName} · {item.authorityName}</p><p className="text-muted-foreground">Ref. {item.referenceNumber} · descontado {money.format(item.deductedToDate)}{item.authorizedTotal !== null ? ` de ${money.format(item.authorizedTotal)}` : ""}</p><div className="mt-2 flex flex-wrap gap-2"><Button size="sm" variant="outline" onClick={() => evidence(item.evidenceUrl)}><ExternalLink className="mr-2 h-4 w-4"/>Ver evidencia</Button>{canManage && <Button size="sm" variant="ghost" onClick={() => onEditAgreement(item)}><Pencil className="mr-2 h-4 w-4"/>Editar</Button>}{canManage && item.isActive && <Button size="sm" variant="ghost" className="text-destructive" onClick={() => void deactivate(item)}><Trash2 className="mr-2 h-4 w-4"/>Desactivar</Button>}</div></div>)}{!options?.deductionAgreements.length && <Empty text="No hay descuentos autorizados."/>}</CardContent></Card><Card><CardHeader><span className="flex items-center justify-between gap-3"><span><CardTitle>Novedades</CardTitle><CardDescription>Ingresos, ausencias, deducciones y ajustes aplicables a una liquidación.</CardDescription></span>{canManage && <Button size="sm" onClick={onNovelty}>Agregar</Button>}</span></CardHeader><CardContent className="space-y-2">{options?.novelties.map(item => <div key={item.noveltyId} className="rounded-xl border p-3 text-sm"><span className="flex justify-between gap-3"><b>{item.employeeName}</b><span>{item.status}</span></span><p>{item.noveltyTypeName} · {item.conceptName}</p><p className="text-muted-foreground">{item.startDate} — {item.endDate} · {money.format(item.totalAmount)}</p>{item.notes && <p>{item.notes}</p>}</div>)}{!options?.novelties.length && <Empty text="No hay novedades registradas."/>}</CardContent></Card></div>; }
function PaymentsSection({ options, canPay, onCreate }: {
    options: PayrollOptions | null;
    canPay: boolean;
    onCreate: () => void;
}) { return <Card><CardHeader><span className="flex items-center justify-between gap-3"><span><CardTitle>Pagos de nómina</CardTitle><CardDescription>Cada confirmación salda el neto por trabajador y genera su comprobante contable.</CardDescription></span>{canPay && <Button onClick={onCreate}>Confirmar pago</Button>}</span></CardHeader><CardContent><div className="overflow-x-auto rounded-xl border"><table className="w-full min-w-[680px] text-sm"><thead className="bg-muted/50"><tr><th className="p-3 text-left">Fecha</th><th className="text-left">Referencia</th><th className="text-left">Medio</th><th className="text-right">Trabajadores</th><th className="text-right">Total</th><th className="pr-3 text-right">Estado</th></tr></thead><tbody>{options?.paymentBatches.map(item => <tr className="border-t" key={item.paymentBatchId}><td className="p-3">{item.paymentDate}</td><td>{item.referenceNumber}</td><td>{item.paymentMethodName}</td><td className="text-right">{item.employeeCount}</td><td className="text-right font-medium">{money.format(item.totalAmount)}</td><td className="pr-3 text-right">{item.status}</td></tr>)}</tbody></table>{!options?.paymentBatches.length && <Empty text="No hay pagos confirmados."/>}</div></CardContent></Card>; }
function ElectronicSection({ options, canGenerate, onGenerate }: {
    options: PayrollOptions | null;
    canGenerate: boolean;
    onGenerate: () => void;
}) { return <Card><CardHeader><span className="flex items-center justify-between gap-3"><span><CardTitle>Documentos electrónicos DIAN</CardTitle><CardDescription>Seguimiento mensual por trabajador sobre el motor fiscal compartido.</CardDescription></span>{canGenerate && <Button onClick={onGenerate}>Consolidar mes</Button>}</span></CardHeader><CardContent className="space-y-3">{options?.electronicPeriods.map(period => <details key={period.electronicPeriodId} className="rounded-xl border p-4"><summary className="cursor-pointer font-semibold">{period.year}-{String(period.month).padStart(2, "0")} · {period.status}<span className="float-right">{period.documents.length} documentos</span></summary><div className="mt-3 space-y-2">{period.documents.map(document => <div key={document.electronicPayrollDocumentId} className="flex flex-wrap justify-between gap-2 rounded-lg bg-muted/30 p-3 text-sm"><span>{document.employeeName} · {document.documentKind}</span><span>{document.status}</span></div>)}</div></details>)}{!options?.electronicPeriods.length && <Empty text="No se han consolidado períodos electrónicos."/>}</CardContent></Card>; }
function PayrollReports() { const today = new Date().toISOString().slice(0, 10), [from, setFrom] = useState(`${today.slice(0, 4)}-01-01`), [to, setTo] = useState(today), [partyId, setPartyId] = useState("_all"), [definitions, setDefinitions] = useState<PayrollReportDefinition[]>([]), [reportCode, setReportCode] = useState(""), [selected, setSelected] = useState<PayrollReportDefinition | null>(null), [rows, setRows] = useState<Array<Record<string, string | number | null>>>([]), [loading, setLoading] = useState(false); useEffect(() => { payrollApi.reportDefinitions().then(items => { setDefinitions(items); setReportCode(items[0]?.code ?? ""); }).catch(error => toast.error(message(error, "No fue posible cargar el catálogo de reportes."))); }, []); async function open(definition: PayrollReportDefinition) { setSelected(definition); setLoading(true); try {
    const result = await payrollApi.report(definition.code, from, to, partyId === "_all" ? undefined : partyId);
    setRows(result.rows);
}
catch (error) {
    toast.error(message(error, "No fue posible generar el reporte."));
    setSelected(null);
}
finally {
    setLoading(false);
} } if (selected && !loading) {
    const columns = selected.columns.map(column => ({ key: column.key, label: column.label, align: column.align, format: (value: unknown) => column.format === "currency" ? money.format(Number(value ?? 0)) : column.format === "number" ? Number(value ?? 0).toLocaleString("es-CO") : column.format === "datetime" && value ? new Date(String(value)).toLocaleString("es-CO") : String(value ?? "") }));
    return <ReportViewer onClose={() => setSelected(null)} title={selected.name} description={`${selected.description} · ${from} a ${to}`} rows={rows} columns={columns} fileName={`nomina-${selected.code}-${from}-${to}`}/>;
} ; const selectedDefinition = definitions.find(item => item.code === reportCode); return <Card><CardHeader><CardTitle>Reportes de nómina</CardTitle><CardDescription>Selecciona un reporte del catálogo nativo de Reporting y aplica filtros comunes.</CardDescription></CardHeader><CardContent className="grid gap-4 md:grid-cols-2 xl:grid-cols-4"><Field label="Reporte"><Select value={reportCode} onValueChange={setReportCode}><SelectTrigger><SelectValue placeholder="Selecciona un reporte" /></SelectTrigger><SelectContent>{definitions.map(definition => <SelectItem key={definition.code} value={definition.code}>{definition.name}</SelectItem>)}</SelectContent></Select></Field><Field label="Desde"><DatePicker value={from} onChange={setFrom}/></Field><Field label="Hasta"><DatePicker value={to} onChange={setTo}/></Field><Field label="Trabajador"><PartyRoleSelect role="Employee" includePartyId value={partyId} leadingOptions={[{value:"_all",label:"Todos los trabajadores"}]} placeholder="Buscar trabajador" onChange={setPartyId}/></Field><div className="rounded-xl border bg-muted/20 p-3 text-sm md:col-span-2 xl:col-span-3"><b>{selectedDefinition?.name ?? "Catálogo de reportes"}</b><p className="text-muted-foreground">{selectedDefinition?.description ?? "No hay reportes activos."}</p></div><Button className="self-end" disabled={loading || !selectedDefinition || !from || !to || to < from} onClick={() => selectedDefinition && void open(selectedDefinition)}>{loading && <Loader2 className="mr-2 h-4 w-4 animate-spin"/>}Generar reporte</Button></CardContent></Card>; }
function ConfigurationSection({ options, canConfigure, canConfigureAccounting, onConcept, onEditConcept, onRules, onEditRules, onChanged }: {
    options: PayrollOptions | null;
    canConfigure: boolean;
    canConfigureAccounting: boolean;
    onConcept: () => void;
    onEditConcept: (value: PayrollConcept) => void;
    onRules: () => void;
    onEditRules: (value: PayrollRuleSet) => void;
    onChanged: () => Promise<void>;
}) {
    async function approve(id: string, version: string) { try {
        await payrollApi.approveRuleSet(id, version);
        toast.success("Conjunto de reglas aprobado.");
        await onChanged();
    }
    catch (error) {
        toast.error(message(error, "No fue posible aprobar las reglas."));
    } }
    async function retire(item: PayrollRuleSet) { if (!window.confirm(`¿Retirar las reglas ${item.code}?`)) return; try { await payrollApi.retireRuleSet(item.ruleSetId, item.rowVersion); toast.success("Conjunto de reglas retirado."); await onChanged(); } catch (error) { toast.error(message(error, "No fue posible retirar las reglas.")); } }
    async function deactivateConcept(item: PayrollConcept) { if (!options) return; const option = (catalogCode: string, code: string | null) => code ? catalog(options, catalogCode).find(value => value.code === code)?.optionId ?? null : null; try { await payrollApi.saveConcept({ conceptId: item.conceptId, code: item.code, name: item.name, natureOptionId: option("payroll-concept-nature", item.natureCode) ?? "", calculationMethodOptionId: option("payroll-calculation-method", item.calculationMethodCode) ?? "", treatmentOptionId: option("payroll-concept-treatment", item.treatmentCode) ?? "", dianConceptOptionId: option("payroll-dian-concept", item.dianConceptCode), accountingCategoryOptionId: option("payroll-accounting-category", item.accountingCategoryCode) ?? "", systemRoleOptionId: option("payroll-system-concept-role", item.systemRoleCode), isSalaryBase: item.isSalaryBase, isSocialSecurityBase: item.isSocialSecurityBase, isBenefitsBase: item.isBenefitsBase, isTaxWithholdingBase: item.isTaxWithholdingBase, requiresDeductionAgreement: item.requiresDeductionAgreement, effectiveFrom: item.effectiveFrom, effectiveTo: item.effectiveTo, isActive: false, rowVersion: item.rowVersion }); toast.success("Concepto desactivado."); await onChanged(); } catch (error) { toast.error(message(error, "No fue posible desactivar el concepto.")); } }
    async function saveSettings(field: "electronicPayrollEnabled" | "isEmployerExemptFromHealthSenaIcbf", value: boolean) { try {
        await payrollApi.saveSettings({ isEmployerExemptFromHealthSenaIcbf: field === "isEmployerExemptFromHealthSenaIcbf" ? value : options?.settings?.isEmployerExemptFromHealthSenaIcbf ?? false, electronicPayrollEnabled: field === "electronicPayrollEnabled" ? value : options?.settings?.electronicPayrollEnabled ?? false, rowVersion: options?.settings?.rowVersion ?? null });
        toast.success("Configuración de nómina actualizada.");
        await onChanged();
    }
    catch (error) {
        toast.error(message(error, "No fue posible actualizar la configuración."));
    } }
    return <div className="grid gap-5 lg:grid-cols-2">
    <Card><CardHeader><span className="flex items-center justify-between gap-3"><span><CardTitle>Conceptos</CardTitle><CardDescription>Roles técnicos, DIAN y categorías contables.</CardDescription></span>{canConfigure && <Button size="sm" onClick={onConcept}><Settings2 className="mr-2 h-4 w-4"/>Agregar</Button>}</span></CardHeader><CardContent className="space-y-2">{options?.concepts.map(item => <div key={item.conceptId} className="rounded-xl border p-3 text-sm"><b>{item.code} · {item.name}</b><span className="block text-muted-foreground">{item.natureCode} · {item.accountingCategoryCode}{item.systemRoleCode ? ` · ${item.systemRoleCode}` : ""} · {item.isActive ? "Activo" : "Inactivo"}</span>{canConfigure && <div className="mt-2 flex gap-2"><Button size="sm" variant="ghost" onClick={() => onEditConcept(item)}><Pencil className="mr-2 h-4 w-4"/>Editar</Button>{item.isActive && <Button size="sm" variant="ghost" className="text-destructive" onClick={() => void deactivateConcept(item)}><Trash2 className="mr-2 h-4 w-4"/>Desactivar</Button>}</div>}</div>)}{!options?.concepts.length && <Empty text="Configura los conceptos requeridos por el calculador."/>}</CardContent></Card>
    <Card><CardHeader><span className="flex items-center justify-between gap-3"><span><CardTitle>Reglas versionadas</CardTitle><CardDescription>Tarifas y topes con vigencia y fuente.</CardDescription></span>{canConfigure && <Button size="sm" onClick={onRules}>Agregar</Button>}</span></CardHeader><CardContent className="space-y-2">{options?.ruleSets.map(item => <div key={item.ruleSetId} className="rounded-xl border p-3 text-sm"><span className="flex justify-between gap-3"><b>{item.code} · {item.name}</b><span>{item.status}</span></span><p className="text-muted-foreground">Desde {item.effectiveFrom} · {item.parameters.length} parámetros</p>{canConfigure && item.status !== "Retired" && <div className="mt-2 flex flex-wrap gap-2">{item.status === "Draft" && <Button size="sm" variant="ghost" onClick={() => onEditRules(item)}><Pencil className="mr-2 h-4 w-4"/>Editar</Button>}{item.status === "Draft" && <Button size="sm" variant="outline" onClick={() => void approve(item.ruleSetId, item.rowVersion)}>Aprobar</Button>}<Button size="sm" variant="ghost" className="text-destructive" onClick={() => void retire(item)}><Trash2 className="mr-2 h-4 w-4"/>Retirar</Button></div>}</div>)}{!options?.ruleSets.length && <Empty text="Crea un conjunto de reglas antes de liquidar."/>}</CardContent></Card>
    <Card className="lg:col-span-2"><CardHeader><CardTitle>Parámetros del empleador</CardTitle><CardDescription>Activa únicamente condiciones verificadas para la entidad legal.</CardDescription></CardHeader><CardContent className="grid gap-3 sm:grid-cols-2"><label className="flex items-center gap-3 rounded-xl border p-4 text-sm"><Checkbox disabled={!canConfigure} checked={options?.settings?.electronicPayrollEnabled ?? false} onCheckedChange={checked => void saveSettings("electronicPayrollEnabled", checked === true)}/><span><b className="block">Nómina electrónica</b><span className="text-muted-foreground">Habilita generación, firma y envío mensual.</span></span></label><label className="flex items-center gap-3 rounded-xl border p-4 text-sm"><Checkbox disabled={!canConfigure} checked={options?.settings?.isEmployerExemptFromHealthSenaIcbf ?? false} onCheckedChange={checked => void saveSettings("isEmployerExemptFromHealthSenaIcbf", checked === true)}/><span><b className="block">Exoneración verificada</b><span className="text-muted-foreground">Salud patronal, SENA e ICBF según condición legal.</span></span></label></CardContent></Card>
    {options && (
      <PayrollAccountingMappings options={options} canConfigure={canConfigureAccounting}/>
    )}
  </div>;
}
function RunForm({ businessId, options, runs, onSaved }: {
    businessId: string;
    options: PayrollOptions;
    runs: PayrollRunSummary[];
    onSaved: () => Promise<void>;
}) { const today = new Date().toISOString().slice(0, 10), [ruleSetId, setRuleSetId] = useState(""), [frequency, setFrequency] = useState(""), [kind, setKind] = useState<"Regular" | "Adjustment">("Regular"), [originalId, setOriginalId] = useState(""), [start, setStart] = useState(today.slice(0, 8) + "01"), [end, setEnd] = useState(today), [payment, setPayment] = useState(today), [busy, setBusy] = useState(false); async function save() { setBusy(true); try {
    await payrollApi.createRun({ payrollRunId: crypto.randomUUID(), businessId, ruleSetId, payFrequencyOptionId: frequency, runKind: kind, originalPayrollRunId: kind === "Adjustment" ? originalId : null, periodStart: start, periodEnd: end, paymentDate: payment });
    toast.success(kind === "Adjustment" ? "Ajuste creado en borrador." : "Liquidación creada en borrador.");
    await onSaved();
}
catch (error) {
    toast.error(message(error, "No fue posible crear la liquidación."));
}
finally {
    setBusy(false);
} } return <div className="grid gap-4 sm:grid-cols-2"><Field label="Tipo"><Select value={kind} onValueChange={value => { setKind(value as "Regular" | "Adjustment"); if (value === "Regular")
    setOriginalId(""); }}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value="Regular">Liquidación regular</SelectItem><SelectItem value="Adjustment">Ajuste por diferencias</SelectItem></SelectContent></Select></Field>{kind === "Adjustment" && <Field label="Liquidación original"><Select value={originalId} onValueChange={value => { setOriginalId(value); const original = runs.find(run => run.payrollRunId === value); if (original) {
    setStart(original.periodStart);
    setEnd(original.periodEnd);
    setPayment(original.paymentDate);
} }}><SelectTrigger><SelectValue placeholder="Selecciona"/></SelectTrigger><SelectContent>{runs.filter(run => run.status === "Approved" && run.runKind === "Regular").map(run => <SelectItem key={run.payrollRunId} value={run.payrollRunId}>{run.periodStart} — {run.periodEnd}</SelectItem>)}</SelectContent></Select></Field>}<CatalogField label="Periodicidad" options={catalog(options, "payroll-pay-frequency")} value={frequency} onChange={setFrequency}/><Field label="Reglas aprobadas"><Select value={ruleSetId} onValueChange={setRuleSetId}><SelectTrigger><SelectValue placeholder="Selecciona"/></SelectTrigger><SelectContent>{options.ruleSets.filter(item => item.status === "Approved").map(item => <SelectItem key={item.ruleSetId} value={item.ruleSetId}>{item.code} · {item.name}</SelectItem>)}</SelectContent></Select></Field><Field label="Inicio"><DatePicker value={start} onChange={setStart}/></Field><Field label="Fin"><DatePicker value={end} onChange={setEnd}/></Field><Field label="Fecha de pago"><DatePicker value={payment} onChange={setPayment}/></Field>{kind === "Adjustment" && <p className="self-end rounded-xl border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900">El ajuste liquida únicamente novedades nuevas como diferencias; no repite el salario ya aprobado.</p>}<DialogFooter className="sm:col-span-2"><Button disabled={busy || !frequency || !ruleSetId || !start || !end || !payment || (kind === "Adjustment" && !originalId)} onClick={() => void save()}>{busy && <Loader2 className="mr-2 h-4 w-4 animate-spin"/>}Crear borrador</Button></DialogFooter></div>; }
function EmploymentForm({ businessId, options, initial, partyId, onSaved }: {
    businessId: string;
    options: PayrollOptions;
    initial: PayrollEmployment | null;
    partyId: string | null;
    onSaved: () => Promise<void>;
}) { const today = new Date().toISOString().slice(0, 10), [busy, setBusy] = useState(false), [form, setForm] = useState<SaveEmployment>(initial ? { ...initial, rowVersion: initial.rowVersion } : { employmentId: crypto.randomUUID(), partyId: partyId ?? "", businessId, employeeId: null, contractTypeOptionId: "", salaryTypeOptionId: "", payFrequencyOptionId: "", riskClassOptionId: "", workerTypeOptionId: "", workerSubtypeOptionId: null, paymentMethodOptionId: "", contractNumber: "", startDate: today, endDate: null, monthlySalary: 0, integralSalaryPercentage: null, bankAccountReference: null, bankOptionId: null, bankAccountTypeOptionId: null, bankAccountNumber: null, isActive: true, rowVersion: null }); const set = <K extends keyof SaveEmployment>(key: K, value: SaveEmployment[K]) => setForm(current => ({ ...current, [key]: value })); const bankTransfer = catalog(options, "payroll-payment-method").find(item => item.optionId === form.paymentMethodOptionId)?.code === "BankTransfer"; async function save(event: React.FormEvent) { event.preventDefault(); setBusy(true); try {
    await payrollApi.saveEmployment(form);
    toast.success("Relación laboral guardada.");
    await onSaved();
}
catch (error) {
    toast.error(message(error, "No fue posible guardar el contrato."));
}
finally {
    setBusy(false);
} } return <form className="grid gap-4 sm:grid-cols-2" onSubmit={save}><Field label="Empleado"><PartyRoleSelect role="Employee" includePartyId disabled={Boolean(initial)} value={form.partyId} placeholder="Buscar empleado por nombre o identificación" selectedOption={initial?{value:initial.partyId,label:initial.employeeName,description:initial.contractNumber}:null} onChange={(value,party)=>setForm(current=>({...current,partyId:value,employeeId:party?.employeeId??null}))}/></Field><Field label="Número de contrato"><Input required value={form.contractNumber} onChange={event => set("contractNumber", event.target.value)}/></Field><CatalogField label="Tipo de contrato" options={catalog(options, "payroll-contract-type")} value={form.contractTypeOptionId} onChange={value => set("contractTypeOptionId", value)}/><CatalogField label="Tipo de salario" options={catalog(options, "payroll-salary-type")} value={form.salaryTypeOptionId} onChange={value => set("salaryTypeOptionId", value)}/><CatalogField label="Periodicidad" options={catalog(options, "payroll-pay-frequency")} value={form.payFrequencyOptionId} onChange={value => set("payFrequencyOptionId", value)}/><CatalogField label="Clase de riesgo" options={catalog(options, "payroll-risk-class")} value={form.riskClassOptionId} onChange={value => set("riskClassOptionId", value)}/><CatalogField label="Tipo de trabajador" options={catalog(options, "payroll-worker-type")} value={form.workerTypeOptionId} onChange={value => set("workerTypeOptionId", value)}/><CatalogField label="Subtipo" options={catalog(options, "payroll-worker-subtype")} value={form.workerSubtypeOptionId ?? ""} onChange={value => set("workerSubtypeOptionId", value || null)}/><CatalogField label="Medio de pago" options={catalog(options, "payroll-payment-method")} value={form.paymentMethodOptionId} onChange={value => set("paymentMethodOptionId", value)}/><Field label="Salario mensual"><Input required min={1} type="number" value={form.monthlySalary || ""} onChange={event => set("monthlySalary", event.currentTarget.valueAsNumber || 0)}/></Field><Field label="Porcentaje de salario integral"><Input min={0} step="any" type="number" value={form.integralSalaryPercentage ?? ""} onChange={event => set("integralSalaryPercentage", event.target.value ? event.currentTarget.valueAsNumber : null)}/></Field><Field label="Inicio"><DatePicker value={form.startDate} onChange={value => set("startDate", value)}/></Field><Field label="Fin (opcional)"><DatePicker value={form.endDate ?? ""} onChange={value => set("endDate", value || null)}/></Field>{bankTransfer && <><CatalogField label="Banco" options={catalog(options, "payroll-bank")} value={form.bankOptionId ?? ""} onChange={value => set("bankOptionId", value || null)}/><CatalogField label="Tipo de cuenta" options={catalog(options, "payroll-bank-account-type")} value={form.bankAccountTypeOptionId ?? ""} onChange={value => set("bankAccountTypeOptionId", value || null)}/><Field label="Número de cuenta"><Input required maxLength={64} inputMode="numeric" autoComplete="off" value={form.bankAccountNumber ?? ""} onChange={event => set("bankAccountNumber", event.target.value.replace(/\s/g, "") || null)}/></Field></>}<label className="flex items-center gap-2 self-end rounded-xl border p-3 text-sm"><Checkbox checked={form.isActive} onCheckedChange={checked => set("isActive", checked === true)}/>Relación activa</label><DialogFooter className="sm:col-span-2"><Button type="submit" disabled={busy || !form.employeeId || (bankTransfer && (!form.bankOptionId || !form.bankAccountTypeOptionId || !form.bankAccountNumber)) || Object.entries(form).some(([key, value]) => ["partyId", "contractTypeOptionId", "salaryTypeOptionId", "payFrequencyOptionId", "riskClassOptionId", "workerTypeOptionId", "paymentMethodOptionId"].includes(key) && !value)}>Guardar contrato</Button></DialogFooter></form>; }
function ConceptForm({ options, initial, onSaved }: {
    options: PayrollOptions;
    initial: PayrollConcept | null;
    onSaved: () => Promise<void>;
}) { const today = new Date().toISOString().slice(0, 10), option = (catalogCode: string, code: string | null) => code ? catalog(options, catalogCode).find(item => item.code === code)?.optionId ?? "" : "", [busy, setBusy] = useState(false), [form, setForm] = useState<SaveConcept>(initial ? { conceptId: initial.conceptId, code: initial.code, name: initial.name, natureOptionId: option("payroll-concept-nature", initial.natureCode), calculationMethodOptionId: option("payroll-calculation-method", initial.calculationMethodCode), treatmentOptionId: option("payroll-concept-treatment", initial.treatmentCode), dianConceptOptionId: option("payroll-dian-concept", initial.dianConceptCode) || null, accountingCategoryOptionId: option("payroll-accounting-category", initial.accountingCategoryCode), systemRoleOptionId: option("payroll-system-concept-role", initial.systemRoleCode) || null, isSalaryBase: initial.isSalaryBase, isSocialSecurityBase: initial.isSocialSecurityBase, isBenefitsBase: initial.isBenefitsBase, isTaxWithholdingBase: initial.isTaxWithholdingBase, requiresDeductionAgreement: initial.requiresDeductionAgreement, effectiveFrom: initial.effectiveFrom, effectiveTo: initial.effectiveTo, isActive: initial.isActive, rowVersion: initial.rowVersion } : { conceptId: crypto.randomUUID(), code: "", name: "", natureOptionId: "", calculationMethodOptionId: "", treatmentOptionId: "", dianConceptOptionId: null, accountingCategoryOptionId: "", systemRoleOptionId: null, isSalaryBase: false, isSocialSecurityBase: false, isBenefitsBase: false, isTaxWithholdingBase: false, requiresDeductionAgreement: false, effectiveFrom: today, effectiveTo: null, isActive: true, rowVersion: null }); const set = <K extends keyof SaveConcept>(key: K, value: SaveConcept[K]) => setForm(current => ({ ...current, [key]: value })); async function save() { setBusy(true); try {
    await payrollApi.saveConcept(form);
    toast.success("Concepto guardado.");
    await onSaved();
}
catch (error) {
    toast.error(message(error, "No fue posible guardar el concepto."));
}
finally {
    setBusy(false);
} } return <div className="grid gap-4 sm:grid-cols-2"><Field label="Código"><Input value={form.code} onChange={event => set("code", event.target.value.toUpperCase())}/></Field><Field label="Nombre"><Input value={form.name} onChange={event => set("name", event.target.value)}/></Field><CatalogField label="Naturaleza" options={catalog(options, "payroll-concept-nature")} value={form.natureOptionId} onChange={value => set("natureOptionId", value)}/><CatalogField label="Método" options={catalog(options, "payroll-calculation-method")} value={form.calculationMethodOptionId} onChange={value => set("calculationMethodOptionId", value)}/><CatalogField label="Tratamiento" options={catalog(options, "payroll-concept-treatment")} value={form.treatmentOptionId} onChange={value => set("treatmentOptionId", value)}/><CatalogField label="Categoría contable" options={catalog(options, "payroll-accounting-category")} value={form.accountingCategoryOptionId} onChange={value => set("accountingCategoryOptionId", value)}/><CatalogField label="Concepto DIAN" options={catalog(options, "payroll-dian-concept")} value={form.dianConceptOptionId ?? ""} onChange={value => set("dianConceptOptionId", value)}/><CatalogField label="Rol del calculador" options={catalog(options, "payroll-system-concept-role")} value={form.systemRoleOptionId ?? ""} onChange={value => set("systemRoleOptionId", value)}/><div className="sm:col-span-2 grid gap-2 sm:grid-cols-2">{([['isSalaryBase', 'Base salarial'], ['isSocialSecurityBase', 'Base de seguridad social'], ['isBenefitsBase', 'Base de prestaciones'], ['isTaxWithholdingBase', 'Base de retención'], ['requiresDeductionAgreement', 'Exige acuerdo de deducción']] as Array<[
    keyof SaveConcept,
    string
]>).map(([key, label]) => <label key={key} className="flex items-center gap-2 rounded-xl border p-3 text-sm"><Checkbox checked={Boolean(form[key])} onCheckedChange={checked => set(key, (checked === true) as never)}/>{label}</label>)}</div><DialogFooter className="sm:col-span-2"><Button disabled={busy || !form.code || !form.name || !form.natureOptionId || !form.calculationMethodOptionId || !form.treatmentOptionId || !form.accountingCategoryOptionId} onClick={() => void save()}>Guardar concepto</Button></DialogFooter></div>; }
function RuleSetForm({ options, initial, onSaved }: {
    options: PayrollOptions;
    initial: PayrollRuleSet | null;
    onSaved: () => Promise<void>;
}) { const today = new Date().toISOString().slice(0, 10), definitions = catalog(options, "payroll-rule-parameter"), [busy, setBusy] = useState(false), [code, setCode] = useState(initial?.code ?? ""), [name, setName] = useState(initial?.name ?? ""), [source, setSource] = useState(initial?.sourceReference ?? ""), [effectiveFrom, setEffectiveFrom] = useState(initial?.effectiveFrom ?? today), [values, setValues] = useState<Record<string, string>>(Object.fromEntries((initial?.parameters ?? []).map(item => [item.code, String(item.numericValue)]))); async function save() { const request: SaveRuleSet = { ruleSetId: initial?.ruleSetId ?? crypto.randomUUID(), countryCode: initial?.countryCode ?? "CO", code, name, effectiveFrom, effectiveTo: initial?.effectiveTo ?? null, sourceReference: source, parameters: definitions.map(item => ({ code: item.code, numericValue: Number(values[item.code]), unitCode: item.metadataCode ?? "Value", description: item.description })), rowVersion: initial?.rowVersion ?? null }; setBusy(true); try {
    await payrollApi.saveRuleSet(request);
    toast.success("Reglas guardadas en borrador; revísalas y apruébalas.");
    await onSaved();
}
catch (error) {
    toast.error(message(error, "No fue posible guardar las reglas."));
}
finally {
    setBusy(false);
} } return <div className="grid gap-4 sm:grid-cols-2"><Field label="Código"><Input value={code} onChange={event => setCode(event.target.value.toUpperCase())}/></Field><Field label="Nombre"><Input value={name} onChange={event => setName(event.target.value)}/></Field><Field label="Vigente desde"><DatePicker value={effectiveFrom} onChange={setEffectiveFrom}/></Field><Field label="Fuente normativa"><Input value={source} onChange={event => setSource(event.target.value)} placeholder="Resolución, decreto o URL verificada"/></Field><div className="sm:col-span-2 grid gap-4 sm:grid-cols-2">{definitions.map(item => <Field key={item.optionId} label={`${item.label} · ${item.metadataCode ?? "Valor"}`}><Input type="number" step="any" value={values[item.code] ?? ""} onChange={event => setValues(current => ({ ...current, [item.code]: event.target.value }))}/></Field>)}</div><DialogFooter className="sm:col-span-2"><Button disabled={busy || !code || !name || !source || definitions.some(item => values[item.code] === undefined || values[item.code] === "")} onClick={() => void save()}>Guardar borrador</Button></DialogFooter></div>; }
function DeductionAgreementForm({ options, initial, onSaved }: {
    options: PayrollOptions;
    initial: PayrollDeductionAgreement | null;
    onSaved: () => Promise<void>;
}) { const today = new Date().toISOString().slice(0, 10), [busy, setBusy] = useState(false), [form, setForm] = useState<SaveDeductionAgreement>(initial ? { deductionAgreementId: initial.deductionAgreementId, employmentId: initial.employmentId, conceptId: initial.conceptId, authorityOptionId: initial.authorityOptionId, beneficiaryPartyId: initial.beneficiaryPartyId, referenceNumber: initial.referenceNumber, evidenceUrl: initial.evidenceUrl, effectiveFrom: initial.effectiveFrom, effectiveTo: initial.effectiveTo, authorizedTotal: initial.authorizedTotal, installmentAmount: initial.installmentAmount, priority: initial.priority, mustProtectMinimumNetPay: initial.mustProtectMinimumNetPay, isActive: initial.isActive, rowVersion: initial.rowVersion } : { deductionAgreementId: crypto.randomUUID(), employmentId: "", conceptId: "", authorityOptionId: "", beneficiaryPartyId: null, referenceNumber: "", evidenceUrl: "", effectiveFrom: today, effectiveTo: null, authorizedTotal: null, installmentAmount: null, priority: 100, mustProtectMinimumNetPay: true, isActive: true, rowVersion: null }); const set = <K extends keyof SaveDeductionAgreement>(key: K, value: SaveDeductionAgreement[K]) => setForm(current => ({ ...current, [key]: value })); async function save() { setBusy(true); try {
    await payrollApi.saveDeductionAgreement(form);
    toast.success("Descuento autorizado guardado.");
    await onSaved();
}
catch (error) {
    toast.error(message(error, "No fue posible guardar el descuento."));
}
finally {
    setBusy(false);
} } return <div className="grid gap-4 sm:grid-cols-2"><Field label="Trabajador"><PayrollEmploymentSelect value={form.employmentId} onChange={value=>set("employmentId",value)}/></Field><Field label="Concepto"><Select value={form.conceptId} onValueChange={value => set("conceptId", value)}><SelectTrigger><SelectValue placeholder="Selecciona"/></SelectTrigger><SelectContent>{options.concepts.filter(item => item.isActive && item.requiresDeductionAgreement).map(item => <SelectItem key={item.conceptId} value={item.conceptId}>{item.code} · {item.name}</SelectItem>)}</SelectContent></Select></Field><CatalogField label="Autoridad" options={catalog(options, "payroll-deduction-authority")} value={form.authorityOptionId} onChange={value => set("authorityOptionId", value)}/><Field label="Referencia"><Input maxLength={100} value={form.referenceNumber} onChange={event => set("referenceNumber", event.target.value)}/></Field><Field label="Evidencia (URL)"><Input type="url" maxLength={1000} value={form.evidenceUrl} onChange={event => set("evidenceUrl", event.target.value)}/></Field><Field label="Vigente desde"><DatePicker value={form.effectiveFrom} onChange={value => set("effectiveFrom", value)}/></Field><Field label="Vigente hasta"><DatePicker value={form.effectiveTo ?? ""} onChange={value => set("effectiveTo", value || null)}/></Field><Field label="Total autorizado"><Input type="number" min={0.01} step="any" value={form.authorizedTotal ?? ""} onChange={event => set("authorizedTotal", event.target.value ? event.currentTarget.valueAsNumber : null)}/></Field><Field label="Cuota"><Input type="number" min={0.01} step="any" value={form.installmentAmount ?? ""} onChange={event => set("installmentAmount", event.target.value ? event.currentTarget.valueAsNumber : null)}/></Field><Field label="Prioridad"><Input type="number" min={1} max={999} value={form.priority} onChange={event => set("priority", event.currentTarget.valueAsNumber || 1)}/></Field><label className="flex items-center gap-2 self-end rounded-xl border p-3 text-sm"><Checkbox checked={form.mustProtectMinimumNetPay} onCheckedChange={checked => set("mustProtectMinimumNetPay", checked === true)}/>Proteger mínimo neto</label><DialogFooter className="sm:col-span-2"><Button disabled={busy || !form.employmentId || !form.conceptId || !form.authorityOptionId || !form.referenceNumber || !form.evidenceUrl} onClick={() => void save()}>Guardar autorización</Button></DialogFooter></div>; }
function NoveltyForm({ options, onSaved }: {
    options: PayrollOptions;
    onSaved: () => Promise<void>;
}) { const today = new Date().toISOString().slice(0, 10), [busy, setBusy] = useState(false), [form, setForm] = useState<SavePayrollNovelty>({ noveltyId: crypto.randomUUID(), employmentId: "", conceptId: "", noveltyTypeOptionId: "", reasonId: null, deductionAgreementId: null, startDate: today, endDate: today, quantity: 1, unitAmount: null, totalAmount: 0, notes: null, evidenceUrl: null }); const set = <K extends keyof SavePayrollNovelty>(key: K, value: SavePayrollNovelty[K]) => setForm(current => ({ ...current, [key]: value })); const agreements = options.deductionAgreements.filter(item => item.employmentId === form.employmentId && item.conceptId === form.conceptId && item.isActive), selectedConcept = options.concepts.find(item => item.conceptId === form.conceptId), method = selectedConcept?.calculationMethodCode, needsRate = method === "QuantityByRate" || method === "PercentageOfBase"; async function save() { setBusy(true); try {
    await payrollApi.saveNovelty(form);
    toast.success("Novedad registrada y aprobada.");
    await onSaved();
}
catch (error) {
    toast.error(message(error, "No fue posible guardar la novedad."));
}
finally {
    setBusy(false);
} } return <div className="grid gap-4 sm:grid-cols-2"><Field label="Trabajador"><PayrollEmploymentSelect value={form.employmentId} onChange={value=>set("employmentId",value)}/></Field><Field label="Concepto"><Select value={form.conceptId} onValueChange={value => set("conceptId", value)}><SelectTrigger><SelectValue placeholder="Selecciona"/></SelectTrigger><SelectContent>{options.concepts.filter(item => item.isActive).map(item => <SelectItem key={item.conceptId} value={item.conceptId}>{item.code} · {item.name}</SelectItem>)}</SelectContent></Select></Field><CatalogField label="Tipo de novedad" options={catalog(options, "payroll-novelty-type")} value={form.noveltyTypeOptionId} onChange={value => set("noveltyTypeOptionId", value)}/><Field label="Acuerdo de descuento (si aplica)"><Select value={form.deductionAgreementId ?? "_none"} onValueChange={value => set("deductionAgreementId", value === "_none" ? null : value)}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value="_none">No aplica</SelectItem>{agreements.map(item => <SelectItem key={item.deductionAgreementId} value={item.deductionAgreementId}>{item.referenceNumber}</SelectItem>)}</SelectContent></Select></Field><Field label="Inicio"><DatePicker value={form.startDate} onChange={value => set("startDate", value)}/></Field><Field label="Fin"><DatePicker value={form.endDate} onChange={value => set("endDate", value)}/></Field><Field label="Cantidad"><Input type="number" min={0.000001} step="any" value={form.quantity} onChange={event => set("quantity", event.currentTarget.valueAsNumber || 0)}/></Field><Field label={method === "PercentageOfBase" ? "Tarifa decimal" : "Valor unitario"}><Input type="number" min={0} step="any" value={form.unitAmount ?? ""} onChange={event => set("unitAmount", event.target.value ? event.currentTarget.valueAsNumber : null)}/></Field><Field label={needsRate ? "Total calculado por el motor" : "Valor total"}><Input type="number" min={0} step="any" disabled={needsRate} value={form.totalAmount} onChange={event => set("totalAmount", event.currentTarget.valueAsNumber || 0)}/></Field><Field label="Evidencia (URL)"><Input type="url" value={form.evidenceUrl ?? ""} onChange={event => set("evidenceUrl", event.target.value || null)}/></Field><label className="space-y-2 sm:col-span-2"><Label>Notas</Label><Input maxLength={500} value={form.notes ?? ""} onChange={event => set("notes", event.target.value || null)}/></label><DialogFooter className="sm:col-span-2"><Button disabled={busy || !form.employmentId || !form.conceptId || !form.noveltyTypeOptionId || form.quantity <= 0 || (needsRate && form.unitAmount === null)} onClick={() => void save()}>Registrar novedad</Button></DialogFooter></div>; }
function PaymentForm({ options, runs, onSaved }: {
    options: PayrollOptions;
    runs: PayrollRunSummary[];
    onSaved: () => Promise<void>;
}) { const today = new Date().toISOString().slice(0, 10), [runId, setRunId] = useState(""), [methodId, setMethodId] = useState(""), [date, setDate] = useState(today), [reference, setReference] = useState(""), [busy, setBusy] = useState(false), paidRunIds = new Set(options.paymentBatches.filter(item => item.status === "Confirmed").map(item => item.payrollRunId)); async function save() { setBusy(true); try {
    await payrollApi.createPayment({ paymentBatchId: crypto.randomUUID(), payrollRunId: runId, paymentMethodOptionId: methodId, paymentDate: date, referenceNumber: reference });
    toast.success("Pago confirmado y enviado a contabilidad.");
    await onSaved();
}
catch (error) {
    toast.error(message(error, "No fue posible confirmar el pago."));
}
finally {
    setBusy(false);
} } return <div className="grid gap-4"><Field label="Liquidación aprobada"><Select value={runId} onValueChange={setRunId}><SelectTrigger><SelectValue placeholder="Selecciona"/></SelectTrigger><SelectContent>{runs.filter(run => run.status === "Approved" && !paidRunIds.has(run.payrollRunId)).map(run => <SelectItem key={run.payrollRunId} value={run.payrollRunId}>{run.periodStart} — {run.periodEnd} · {money.format(run.netPayable)}</SelectItem>)}</SelectContent></Select></Field><CatalogField label="Medio de pago" options={catalog(options, "payroll-payment-method")} value={methodId} onChange={setMethodId}/><Field label="Fecha de pago"><DatePicker value={date} onChange={setDate}/></Field><Field label="Referencia bancaria o de caja"><Input maxLength={100} value={reference} onChange={event => setReference(event.target.value)}/></Field><DialogFooter><Button disabled={busy || !runId || !methodId || !date || !reference} onClick={() => void save()}>Confirmar pago completo</Button></DialogFooter></div>; }
function PayrollAccountingMappings({ options, canConfigure }: {
    options: PayrollOptions;
    canConfigure: boolean;
}) { const tenantId = useAuthStore(state => state.user?.tenantId ?? ""), businessId = useBusinessContextStore(state => state.selectedBusinessId), [accounts, setAccounts] = useState<AccountingAccount[]>([]), [definitions, setDefinitions] = useState<AccountingCategoryDefinition[]>([]), [mappings, setMappings] = useState<AccountingMapping[]>([]), [busy, setBusy] = useState<string | null>(null), [error, setError] = useState(""); const loadAccounting = useCallback(async () => { try {
    const [nextAccounts, nextDefinitions, nextMappings] = await Promise.all([accountingApi.accounts(), accountingApi.categoryDefinitions(), accountingApi.mappings()]);
    setAccounts(nextAccounts);
    setDefinitions(nextDefinitions);
    setMappings(nextMappings);
    setError("");
}
catch (cause) {
    setError(message(cause, "No fue posible consultar la configuración contable."));
} }, []); useEffect(() => { void loadAccounting(); }, [loadAccounting]); const categories = catalog(options, "payroll-accounting-category").map(option => ({ option, definition: definitions.find(item => item.category === option.code) })); async function save(category: string, accountId: string) { if (!tenantId || !businessId)
    return; setBusy(category); try {
    await accountingApi.setMapping({ tenantId, businessId, category, accountId, effectiveFrom: "2000-01-01", effectiveTo: null });
    toast.success("Cuenta de nómina actualizada.");
    await loadAccounting();
}
catch (cause) {
    toast.error(message(cause, "No fue posible guardar la cuenta."));
}
finally {
    setBusy(null);
} } return <Card className="lg:col-span-2"><CardHeader><CardTitle>Integración contable de nómina</CardTitle><CardDescription>Las categorías nacen del catálogo de nómina y las cuentas del plan contable. La liquidación aprobada y el pago usan estas asignaciones.</CardDescription></CardHeader><CardContent className="space-y-3">{error && <p className="rounded-xl border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900">{error} <Link className="underline" href="/dashboard/accounting">Abrir Contabilidad</Link></p>}{categories.map(({ option, definition }) => { const mapping = mappings.find(item => item.category === option.code && item.businessId === businessId) ?? mappings.find(item => item.category === option.code && item.businessId === null); return <div key={option.optionId} className="grid gap-2 rounded-xl border p-3 md:grid-cols-[minmax(220px,1fr)_minmax(260px,1.4fr)] md:items-center"><div><b className="text-sm">{option.label}</b><p className="text-xs text-muted-foreground">{option.code}</p></div><Select disabled={!canConfigure || busy === option.code} value={mapping?.accountId ?? ""} onValueChange={value => void save(option.code, value)}><SelectTrigger><SelectValue placeholder="Selecciona una cuenta"/></SelectTrigger><SelectContent>{accounts.filter(account => account.isActive && account.allowsPosting && (!definition || account.accountType === definition.accountType)).map(account => <SelectItem key={account.accountId} value={account.accountId}>{account.code} · {account.name}</SelectItem>)}</SelectContent></Select></div>; })}{!categories.length && <Empty text="No hay categorías contables de nómina activas."/>}</CardContent></Card>; }
function ElectronicPeriodForm({ businessId, onSaved }: {
    businessId: string;
    onSaved: () => Promise<void>;
}) { const [period, setPeriod] = useState(new Date().toISOString().slice(0, 7)), [busy, setBusy] = useState(false); async function save() { const [year, month] = period.split("-").map(Number); setBusy(true); try {
    const result = await payrollApi.generateElectronicPeriod({ electronicPeriodId: crypto.randomUUID(), businessId, year, month });
    toast.success(`${result.documents.length} documentos enviados a procesamiento fiscal.`);
    await onSaved();
}
catch (error) {
    toast.error(message(error, "No fue posible generar la nómina electrónica."));
}
finally {
    setBusy(false);
} } return <div className="grid gap-4"><Field label="Mes a reportar"><DatePicker value={period ? `${period}-01` : ""} onChange={value => setPeriod(value.slice(0, 7))}/></Field><DialogFooter><Button disabled={busy || !period} onClick={() => void save()}>{busy && <Loader2 className="mr-2 h-4 w-4 animate-spin"/>}Generar y enviar</Button></DialogFooter></div>; }
function CatalogField({ label, options, value, onChange }: {
    label: string;
    options: PayrollCatalogOption[];
    value: string;
    onChange: (value: string) => void;
}) { return <Field label={label}><Select value={value} onValueChange={onChange}><SelectTrigger><SelectValue placeholder="Selecciona"/></SelectTrigger><SelectContent>{options.filter(item => item.isActive).map(item => <SelectItem key={item.optionId} value={item.optionId}>{item.label}</SelectItem>)}</SelectContent></Select></Field>; }
function catalog(options: PayrollOptions, code: string) { return options.catalogs[code] ?? []; }
function Field({ label, children }: {
    label: string;
    children: React.ReactNode;
}) { return <label className="space-y-2"><Label>{label}</Label>{children}</label>; }
function Metric({ label, value }: {
    label: string;
    value: number;
}) { return <div className="rounded-xl border bg-muted/20 p-3"><p className="text-xs text-muted-foreground">{label}</p><b>{money.format(value)}</b></div>; }
function Empty({ text }: {
    text: string;
}) { return <p className="p-6 text-center text-sm text-muted-foreground">{text}</p>; }
function message(error: unknown, fallback: string) { return error && typeof error === "object" && "message" in error ? String(error.message) : fallback; }
