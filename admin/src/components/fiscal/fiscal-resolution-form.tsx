"use client";

import { useEffect, useState } from "react";
import { CheckCircle2, KeyRound, Loader2 } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { buildFiscalResolutionFormState } from "@/lib/fiscal-resolution-form-state";
import type {
  FiscalResolutionConfiguration,
  SaveFiscalResolutionConfiguration,
} from "@/services/api/fiscal-configuration";

type Props = {
  value: FiscalResolutionConfiguration | null;
  saving?: boolean;
  onSave: (request: SaveFiscalResolutionConfiguration) => Promise<void>;
};

const text = {
  resolutionNumber: "N\u00famero de resoluci\u00f3n",
  issuerTaxId: "NIT del emisor",
  dianPrefix: "Prefijo DIAN",
  environment: "Ambiente",
  habilitation: "Habilitaci\u00f3n",
  production: "Producci\u00f3n",
  rangeStart: "Inicio del rango autorizado DIAN",
  rangeEnd: "Final del rango autorizado DIAN",
  firstDianConsecutive: "Primer consecutivo DIAN",
  firstDianConsecutiveHelp: "Es la primera numeraci\u00f3n fiscal que usar\u00e1 esta resoluci\u00f3n; no es el consecutivo operativo Auraly.",
  validFrom: "Vigente desde",
  validUntil: "Vigente hasta",
  technicalKeyVersion: "Versi\u00f3n de clave t\u00e9cnica",
  replaceTechnicalKey: "Reemplazar clave t\u00e9cnica",
  technicalKey: "Clave t\u00e9cnica",
  storedTechnicalKey: "Clave almacenada de forma segura",
  enterTechnicalKey: "Ingresa la clave t\u00e9cnica",
  protectedTechnicalKey: "La clave existente est\u00e1 cargada, pero nunca se devuelve ni se muestra. D\u00e9jala vac\u00eda para conservarla.",
  qrUrl: "URL oficial de consulta QR",
  online: "Facturaci\u00f3n en l\u00ednea",
  onlineHelp: "Serie emitida y numerada por el servidor.",
  enrolled: "Equipos enrolados",
  enrolledHelp: "Permite provisionar numeraci\u00f3n para POS Edge.",
  save: "Guardar resoluci\u00f3n",
} as const;

const today = new Date().toISOString().slice(0, 10);

export function FiscalResolutionForm({ value, saving = false, onSave }: Props) {
  const [form, setForm] = useState(() => buildFiscalResolutionFormState(value, today));

  useEffect(() => {
    setForm(buildFiscalResolutionFormState(value, today));
  }, [value]);

  const set = <K extends keyof SaveFiscalResolutionConfiguration>(
    key: K,
    next: SaveFiscalResolutionConfiguration[K],
  ) => setForm((current) => ({ ...current, [key]: next }));

  const numberingLocked = value?.canSetInitialConsecutive === false;

  return (
    <form
      className="space-y-5"
      onSubmit={(event) => {
        event.preventDefault();
        void onSave(form);
      }}
    >
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label={text.resolutionNumber}>
          <Input required value={form.authorizationNumber} onChange={(event) => set("authorizationNumber", event.target.value)} />
        </Field>
        <Field label={text.issuerTaxId}>
          <Input required value={form.supplierTaxId} onChange={(event) => set("supplierTaxId", event.target.value)} />
        </Field>
        <Field label={text.dianPrefix}>
          <Input required value={form.prefix} onChange={(event) => set("prefix", event.target.value.toUpperCase())} />
        </Field>
        <Field label={text.environment}>
          <Select value={String(form.environment)} onValueChange={(next) => set("environment", Number(next))}>
            <SelectTrigger><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="2">{text.habilitation}</SelectItem>
              <SelectItem value="1">{text.production}</SelectItem>
            </SelectContent>
          </Select>
        </Field>
        <Field label={text.rangeStart}>
          <Input required type="number" min={1} value={form.rangeStart} disabled={numberingLocked} onChange={(event) => set("rangeStart", Number(event.target.value))} />
        </Field>
        <Field label={text.rangeEnd}>
          <Input required type="number" min={form.rangeStart} value={form.rangeEnd} disabled={numberingLocked} onChange={(event) => set("rangeEnd", Number(event.target.value))} />
        </Field>
        <Field label={text.firstDianConsecutive}>
          <Input required type="number" min={form.rangeStart} max={form.rangeEnd} value={form.initialConsecutive} disabled={numberingLocked} onChange={(event) => set("initialConsecutive", Number(event.target.value))} />
          <p className="text-xs text-muted-foreground">{text.firstDianConsecutiveHelp}</p>
        </Field>
        <div className="hidden sm:block" />
        <Field label={text.validFrom}>
          <Input required type="date" value={form.validFrom} onChange={(event) => set("validFrom", event.target.value)} />
        </Field>
        <Field label={text.validUntil}>
          <Input required type="date" value={form.validUntil} onChange={(event) => set("validUntil", event.target.value)} />
        </Field>
        <Field label={text.technicalKeyVersion}>
          <Input required value={form.technicalKeyVersion} onChange={(event) => set("technicalKeyVersion", event.target.value)} />
        </Field>
        <Field label={value?.hasTechnicalKey ? text.replaceTechnicalKey : text.technicalKey}>
          <div className="relative">
            <KeyRound className="absolute left-3 top-3 h-4 w-4 text-muted-foreground" />
            <Input
              className="pl-9"
              type="password"
              required={!value?.hasTechnicalKey}
              value={form.technicalKey ?? ""}
              placeholder={value?.hasTechnicalKey ? text.storedTechnicalKey : text.enterTechnicalKey}
              onChange={(event) => set("technicalKey", event.target.value || null)}
            />
          </div>
          {value?.hasTechnicalKey && <p className="text-xs text-muted-foreground">{text.protectedTechnicalKey}</p>}
        </Field>
      </div>
      <Field label={text.qrUrl}>
        <Input required type="url" value={form.qrValidationUrl} onChange={(event) => set("qrValidationUrl", event.target.value)} />
      </Field>
      <div className="grid gap-3 sm:grid-cols-2">
        <Mode checked={form.prepareOnlineSeries} disabled={numberingLocked} onChange={(next) => set("prepareOnlineSeries", next)} title={text.online} description={text.onlineHelp} />
        <Mode checked={form.prepareOfflineSeries} disabled={numberingLocked} onChange={(next) => set("prepareOfflineSeries", next)} title={text.enrolled} description={text.enrolledHelp} />
      </div>
      <Button className="w-full" type="submit" disabled={saving || (!form.prepareOnlineSeries && !form.prepareOfflineSeries)}>
        {saving ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <CheckCircle2 className="mr-2 h-4 w-4" />}
        {text.save}
      </Button>
    </form>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <div className="space-y-2"><Label>{label}</Label>{children}</div>;
}

function Mode({ checked, onChange, title, description, disabled = false }: {
  checked: boolean;
  onChange: (value: boolean) => void;
  title: string;
  description: string;
  disabled?: boolean;
}) {
  return (
    <label className={`flex items-center justify-between gap-4 rounded-xl border p-4 ${disabled ? "cursor-not-allowed opacity-60" : "cursor-pointer"} ${checked ? "border-primary/40 bg-primary/5" : ""}`}>
      <span><b className="block text-sm">{title}</b><small className="text-muted-foreground">{description}</small></span>
      <Switch checked={checked} disabled={disabled} onCheckedChange={onChange} />
    </label>
  );
}
