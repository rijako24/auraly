"use client";

import { Hash, Loader2, Save } from "lucide-react";
import { useEffect, useState } from "react";

import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  fiscalConfigurationApi,
  type SalesInvoiceNumberingConfiguration,
} from "@/services/api/fiscal-configuration";

export function InvoiceNumberingCard({
  businessId,
  onChanged,
}: {
  businessId: string;
  onChanged?: (value: SalesInvoiceNumberingConfiguration) => void;
}) {
  const [value, setValue] = useState<SalesInvoiceNumberingConfiguration | null>(null);
  const [initial, setInitial] = useState(1);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError(null);
    fiscalConfigurationApi.getNumbering(businessId)
      .then((result) => {
        if (!active) return;
        setValue(result);
        setInitial(result.initialConsecutive ?? 1);
      })
      .catch((caught: unknown) => {
        if (active) setError(caught instanceof Error ? caught.message : "No fue posible cargar la numeración.");
      })
      .finally(() => active && setLoading(false));
    return () => { active = false; };
  }, [businessId]);

  async function save() {
    setSaving(true);
    setError(null);
    try {
      const saved = await fiscalConfigurationApi.saveNumbering(businessId, initial);
      setValue(saved);
      setInitial(saved.initialConsecutive ?? initial);
      onChanged?.(saved);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "No fue posible guardar la numeración.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <section className="h-full rounded-2xl border bg-card p-5 shadow-sm">
      <div className="flex items-start gap-3">
        <span className="rounded-xl bg-teal-100 p-2 text-teal-800"><Hash className="h-5 w-5" /></span>
        <div className="min-w-0 flex-1">
          <h3 className="font-bold">Numeración operativa</h3>
          <p className="mt-1 text-sm text-muted-foreground">Consecutivo Auraly visible. Es independiente de la resolución y del número DIAN.</p>
        </div>
      </div>
      {loading ? (
        <p className="mt-4 flex items-center gap-2 text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin" />Cargando numeración…</p>
      ) : (
        <div className="mt-4 grid gap-3 sm:grid-cols-[1fr_auto] sm:items-end">
          <label className="space-y-1 text-sm font-medium">
            {value?.canSetInitialConsecutive ? "Primera factura" : "Siguiente factura"}
            <Input type="number" min={1} value={value?.canSetInitialConsecutive ? initial : (value?.nextConsecutive ?? initial)} disabled={!value?.canSetInitialConsecutive || saving} onChange={(event) => setInitial(Number(event.target.value))} />
          </label>
          {value?.canSetInitialConsecutive && (
            <Button type="button" onClick={() => void save()} disabled={saving || initial < 1}>
              {saving ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Save className="mr-2 h-4 w-4" />}Guardar numeración
            </Button>
          )}
        </div>
      )}
      {value?.hasIssuedInvoices && <p className="mt-3 text-xs text-muted-foreground">Ya existen facturas en esta sede; Auraly continuará el consecutivo y no permitirá reiniciarlo.</p>}
      {error && <p className="mt-3 text-sm font-medium text-destructive">{error}</p>}
    </section>
  );
}
