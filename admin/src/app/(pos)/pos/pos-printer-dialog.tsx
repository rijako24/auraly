"use client";

import { Loader2, Printer, Save, X } from "lucide-react";
import { useEffect, useState } from "react";
import {
  PosEdgeClient,
  loadBrowserPrinterConfiguration,
  saveBrowserPrinterConfiguration,
  type PosPrinterConfiguration,
} from "@/services/pos/pos-edge-client";

export function PosPrinterDialog({
  client,
  onClose,
}: {
  client: PosEdgeClient | null;
  onClose: () => void;
}) {
  const [value, setValue] = useState<PosPrinterConfiguration | null>(null);
  const [printers, setPrinters] = useState<string[]>([]);
  const [serialPorts, setSerialPorts] = useState<string[]>([]);
  const [busy, setBusy] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    const operation = client
      ? client.printerConfiguration()
      : Promise.resolve({
          configuration: loadBrowserPrinterConfiguration(),
          installedPrinters: [] as string[],
          serialPorts: [] as string[],
        });
    operation
      .then((view) => {
        if (!active) return;
        setValue(view.configuration);
        setPrinters(view.installedPrinters);
        setSerialPorts(view.serialPorts ?? []);
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
      if (client) {
        const view = await client.savePrinterConfiguration(value);
        setValue(view.configuration);
        setPrinters(view.installedPrinters);
        setSerialPorts(view.serialPorts ?? []);
      } else {
        setValue(saveBrowserPrinterConfiguration(value));
      }
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
      <section className="flex max-h-[calc(100dvh-2rem)] w-full max-w-xl flex-col overflow-hidden rounded-3xl bg-white shadow-2xl">
        <header className="flex shrink-0 items-start justify-between border-b px-6 py-5">
          <div>
            <p className="text-xs font-bold uppercase tracking-[.16em] text-teal-700">
              Equipo local
            </p>
            <h2 className="mt-1 flex items-center gap-2 text-xl font-bold">
              <Printer className="h-5 w-5" /> Impresión
            </h2>
            <p className="mt-1 text-sm text-slate-600">
              {client
                ? "Se guardan en este computador, no en el tenant ni en el instalador."
                : "El navegador guarda los formatos; la impresora se elige en su ventana de impresión."}
            </p>
          </div>
          <button type="button" onClick={onClose} disabled={busy}
            className="grid h-10 w-10 place-items-center rounded-xl hover:bg-slate-100"
            aria-label="Cerrar"><X className="h-5 w-5" /></button>
        </header>
        <div className="min-h-0 flex-1 space-y-5 overflow-y-auto p-6">
          {busy && !value ? (
            <div className="flex min-h-36 items-center justify-center gap-2 text-sm text-slate-600">
              <Loader2 className="h-5 w-5 animate-spin" /> Consultando Windows...
            </div>
          ) : value ? (
            <>
              <div className="rounded-2xl border border-teal-200 bg-teal-50/60 p-4">
                <p className="font-semibold text-slate-950">Formato por flujo de facturación</p>
                <p className="mb-4 mt-1 text-xs text-slate-600">
                  Solo aplica a facturas y comprobantes. Los informes continúan en carta.
                </p>
                <div className="grid gap-4 sm:grid-cols-2">
                  <FormatSelect label="Punto de venta"
                    value={value.posOutputFormat ?? "Receipt"}
                    onChange={(posOutputFormat) => setValue({ ...value, posOutputFormat })} />
                  <FormatSelect label="Pedidos"
                    value={value.ordersOutputFormat ?? "HalfLetter"}
                    onChange={(ordersOutputFormat) => setValue({ ...value, ordersOutputFormat })} />
                </div>
              </div>
              <Field label="Salida de tirilla">
                <select value={value.receiptMode}
                  onChange={(event) => setValue({
                    ...value,
                    receiptMode: event.target.value as PosPrinterConfiguration["receiptMode"],
                  })}
                  className="h-11 w-full rounded-xl border border-slate-300 bg-white px-3">
                  {!client && <option value="BrowserPreview">Vista previa en pantalla</option>}
                  {client && <option value="WindowsRaw">Impresora de Windows · impresión directa</option>}
                  {client && <option value="File">Archivo ESC/POS</option>}
                </select>
              </Field>
              {value.receiptMode === "WindowsRaw" && (
                <div className="space-y-4 rounded-2xl border border-slate-200 bg-slate-50 p-4">
                  <p className="font-semibold text-slate-900">Plantillas de tirilla</p>
                  <TemplatePrinterSelect value={value} printers={printers}
                    documentType="SalesInvoice" format="Receipt"
                    label="Factura electrónica · tirilla" onChange={setValue} />
                  <TemplatePrinterSelect value={value} printers={printers}
                    documentType="SalesReceipt" format="Receipt"
                    label="Comprobante de venta · tirilla" onChange={setValue} />
                  <p className="text-xs text-slate-500">La venta se envía directamente a la impresora de su plantilla. Microsoft XPS abre el diálogo para guardar.</p>
                </div>
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
              <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
                <p className="font-semibold text-slate-900">Media carta</p>
                <p className="mb-4 mt-1 text-xs text-slate-600">
                  El documento sale dos veces en una hoja carta, listo para corte central. Aplica a la facturación desde POS o Pedidos.
                </p>
                <div className="space-y-4">
                  <Field label="Salida media carta">
                    <select value={value.orderMode ?? "BrowserPreview"}
                      onChange={(event) => setValue({
                        ...value,
                        orderMode: event.target.value as PosPrinterConfiguration["orderMode"],
                      })}
                      className="h-11 w-full rounded-xl border border-slate-300 bg-white px-3">
                      {!client && <option value="BrowserPreview">Vista previa e impresora del sistema</option>}
                      {client && <option value="WindowsPrint">Enviar a una impresora de Windows</option>}
                    </select>
                  </Field>
                  {client && <>
                    <TemplatePrinterSelect value={value} printers={printers}
                      documentType="SalesInvoice" format="HalfLetter"
                      label="Factura electrónica · media carta"
                      optional={(value.orderMode ?? "BrowserPreview") !== "WindowsPrint"}
                      onChange={setValue} />
                    <TemplatePrinterSelect value={value} printers={printers}
                      documentType="SalesReceipt" format="HalfLetter"
                      label="Comprobante de venta · media carta"
                      optional={(value.orderMode ?? "BrowserPreview") !== "WindowsPrint"}
                      onChange={setValue} />
                  </>}
                </div>
              </div>
              {client && !printers.length && (
                <p className="rounded-xl bg-amber-50 p-3 text-sm text-amber-900">
                  Windows no reporto impresoras instaladas. Instala el controlador y vuelve a abrir esta configuracion.
                </p>
              )}
              {client && (
                <ScaleConfiguration
                  value={value.scale ?? defaultScale()}
                  serialPorts={serialPorts}
                  busy={busy}
                  onChange={(scale) => setValue({ ...value, scale })}
                  onTest={async () => {
                    setBusy(true); setError(null);
                    try {
                      const result = await client.readScaleWeight();
                      setError(`Balanza conectada: ${result.weight} ${result.unit} (${result.portName}).`);
                    } catch (caught) {
                      setError(caught instanceof Error ? caught.message : "No fue posible leer la balanza.");
                    } finally { setBusy(false); }
                  }}
                />
              )}
            </>
          ) : null}
          {error && <p className="rounded-xl bg-red-50 p-3 text-sm text-red-700">{error}</p>}
        </div>
        <footer className="flex shrink-0 justify-end gap-2 border-t bg-white px-6 py-4">
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

function ScaleConfiguration({ value, serialPorts, busy, onChange, onTest }: {
  value: NonNullable<PosPrinterConfiguration["scale"]>;
  serialPorts: string[];
  busy: boolean;
  onChange: (value: NonNullable<PosPrinterConfiguration["scale"]>) => void;
  onTest: () => Promise<void>;
}) {
  return <div className="space-y-4 rounded-2xl border border-slate-200 bg-slate-50 p-4">
    <label className="flex items-center gap-2 font-semibold text-slate-900">
      <input type="checkbox" checked={value.enabled} onChange={(event) => onChange({ ...value, enabled: event.target.checked })} />
      Balanza conectada
    </label>
    {value.enabled && <>
      <Field label="Puerto de la balanza">
        <select value={value.portName} onChange={(event) => onChange({ ...value, portName: event.target.value })} className="h-11 w-full rounded-xl border border-slate-300 bg-white px-3">
          <option value="">Selecciona un puerto</option>
          {serialPorts.map((port) => <option key={port} value={port}>{port}</option>)}
        </select>
      </Field>
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="Velocidad"><input type="number" min={1} value={value.baudRate} onChange={(event) => onChange({ ...value, baudRate: Number(event.target.value) })} className="h-11 w-full rounded-xl border border-slate-300 bg-white px-3" /></Field>
        <Field label="Tiempo de espera (ms)"><input type="number" min={200} max={10000} value={value.timeoutMilliseconds} onChange={(event) => onChange({ ...value, timeoutMilliseconds: Number(event.target.value) })} className="h-11 w-full rounded-xl border border-slate-300 bg-white px-3" /></Field>
      </div>
      <button type="button" disabled={busy || !value.portName} onClick={() => void onTest()} className="h-10 rounded-xl border px-4 text-sm font-semibold disabled:opacity-50">Probar balanza</button>
    </>}
  </div>;
}

function defaultScale(): NonNullable<PosPrinterConfiguration["scale"]> {
  return { enabled: false, portName: "", baudRate: 9600, dataBits: 8, parity: "None", stopBits: "One", sendsRequest: false, requestText: "", startIndex: 0, length: 0, reverse: false, divideBy1000: false, timeoutMilliseconds: 2000 };
}

function FormatSelect({
  label, value, onChange,
}: {
  label: string;
  value: "Receipt" | "HalfLetter";
  onChange: (value: "Receipt" | "HalfLetter") => void;
}) {
  return <Field label={label}>
    <select value={value}
      onChange={(event) => onChange(event.target.value as "Receipt" | "HalfLetter")}
      className="h-11 w-full rounded-xl border border-slate-300 bg-white px-3">
      <option value="Receipt">Tirilla</option>
      <option value="HalfLetter">Media carta</option>
    </select>
  </Field>;
}

function TemplatePrinterSelect({
  value, printers, documentType, format, label, optional = false, onChange,
}: {
  value: PosPrinterConfiguration;
  printers: string[];
  documentType: "SalesInvoice" | "SalesReceipt";
  format: "Receipt" | "HalfLetter";
  label: string;
  optional?: boolean;
  onChange: (value: PosPrinterConfiguration) => void;
}) {
  const route = value.templateRoutes?.find((item) =>
    item.documentType === documentType && item.format === format);
  const fallback = format === "Receipt"
    ? value.receiptPrinterName
    : value.letterPrinterName;
  return <PrinterSelect label={label} value={route?.printerName ?? fallback}
    printers={printers} optional={optional}
    onChange={(printerName) => {
      const routes = (value.templateRoutes ?? []).filter((item) =>
        !(item.documentType === documentType && item.format === format));
      onChange({
        ...value,
        templateRoutes: [...routes, { documentType, format, printerName }],
      });
    }} />;
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
