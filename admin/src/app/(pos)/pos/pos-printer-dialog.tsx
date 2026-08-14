"use client";

import { Loader2, Printer, Save, X } from "lucide-react";
import { useEffect, useState } from "react";
import {
  PosEdgeClient,
  type PosPrinterConfiguration,
} from "@/services/pos/pos-edge-client";

export function PosPrinterDialog({
  client,
  onClose,
}: {
  client: PosEdgeClient;
  onClose: () => void;
}) {
  const [value, setValue] = useState<PosPrinterConfiguration | null>(null);
  const [printers, setPrinters] = useState<string[]>([]);
  const [busy, setBusy] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    client.printerConfiguration()
      .then((view) => {
        if (!active) return;
        setValue(view.configuration);
        setPrinters(view.installedPrinters);
      })
      .catch((caught) => {
        if (active)
          setError(caught instanceof Error
            ? caught.message
            : "No fue posible consultar las impresoras.");
      })
      .finally(() => active && setBusy(false));
    return () => { active = false; };
  }, [client]);

  async function save() {
    if (!value) return;
    setBusy(true);
    setError(null);
    try {
      const view = await client.savePrinterConfiguration(value);
      setValue(view.configuration);
      setPrinters(view.installedPrinters);
      onClose();
    } catch (caught) {
      setError(caught instanceof Error
        ? caught.message
        : "No fue posible guardar las impresoras.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="fixed inset-0 z-[70] grid place-items-center bg-slate-950/65 p-4">
      <section className="w-full max-w-xl overflow-hidden rounded-3xl bg-white shadow-2xl">
        <header className="flex items-start justify-between border-b px-6 py-5">
          <div>
            <p className="text-xs font-bold uppercase tracking-[.16em] text-teal-700">
              Equipo local
            </p>
            <h2 className="mt-1 flex items-center gap-2 text-xl font-bold">
              <Printer className="h-5 w-5" /> Impresoras
            </h2>
            <p className="mt-1 text-sm text-slate-600">
              Se guardan en este computador, no en el tenant ni en el instalador.
            </p>
          </div>
          <button type="button" onClick={onClose} disabled={busy}
            className="grid h-10 w-10 place-items-center rounded-xl hover:bg-slate-100"
            aria-label="Cerrar"><X className="h-5 w-5" /></button>
        </header>
        <div className="space-y-5 p-6">
          {busy && !value ? (
            <div className="flex min-h-36 items-center justify-center gap-2 text-sm text-slate-600">
              <Loader2 className="h-5 w-5 animate-spin" /> Consultando Windows...
            </div>
          ) : value ? (
            <>
              <Field label="Salida de tirilla">
                <select value={value.receiptMode}
                  onChange={(event) => setValue({
                    ...value,
                    receiptMode: event.target.value as PosPrinterConfiguration["receiptMode"],
                  })}
                  className="h-11 w-full rounded-xl border border-slate-300 bg-white px-3">
                  <option value="BrowserPreview">Vista previa en pantalla</option>
                  <option value="WindowsRaw">Impresora termica de Windows</option>
                  <option value="File">Archivo ESC/POS</option>
                </select>
              </Field>
              {value.receiptMode === "WindowsRaw" && (
                <PrinterSelect label="Impresora para tirilla"
                  value={value.receiptPrinterName}
                  printers={printers}
                  onChange={(receiptPrinterName) => setValue({
                    ...value, receiptPrinterName,
                  })} />
              )}
              <Field label="Ancho de tirilla">
                <select value={value.receiptPaperWidthMillimeters}
                  onChange={(event) => setValue({
                    ...value,
                    receiptPaperWidthMillimeters:
                      Number(event.target.value) as 58 | 80,
                  })}
                  className="h-11 w-full rounded-xl border border-slate-300 bg-white px-3">
                  <option value={80}>80 mm</option>
                  <option value={58}>58 mm</option>
                </select>
              </Field>
              <PrinterSelect label="Impresora para carta"
                value={value.letterPrinterName}
                printers={printers}
                optional
                onChange={(letterPrinterName) => setValue({
                  ...value, letterPrinterName,
                })} />
              {!printers.length && (
                <p className="rounded-xl bg-amber-50 p-3 text-sm text-amber-900">
                  Windows no reporto impresoras instaladas. Instala el controlador y vuelve a abrir esta configuracion.
                </p>
              )}
            </>
          ) : null}
          {error && <p className="rounded-xl bg-red-50 p-3 text-sm text-red-700">{error}</p>}
        </div>
        <footer className="flex justify-end gap-2 border-t px-6 py-4">
          <button type="button" onClick={onClose} disabled={busy}
            className="h-10 rounded-xl border px-4 text-sm font-semibold">Cancelar</button>
          <button type="button" onClick={() => void save()} disabled={busy || !value}
            className="flex h-10 items-center gap-2 rounded-xl bg-teal-700 px-4 text-sm font-bold text-white disabled:opacity-50">
            {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
            Guardar
          </button>
        </footer>
      </section>
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <label className="block space-y-2 text-sm font-semibold">
    <span>{label}</span>{children}
  </label>;
}

function PrinterSelect({
  label, value, printers, optional = false, onChange,
}: {
  label: string;
  value: string | null;
  printers: string[];
  optional?: boolean;
  onChange: (value: string | null) => void;
}) {
  return <Field label={label}>
    <select value={value ?? ""} onChange={(event) => onChange(event.target.value || null)}
      className="h-11 w-full rounded-xl border border-slate-300 bg-white px-3">
      <option value="">{optional ? "No configurada" : "Selecciona una impresora"}</option>
      {printers.map((printer) => <option key={printer} value={printer}>{printer}</option>)}
    </select>
  </Field>;
}
