"use client";

import { Download, Loader2, Printer, Save, Scale, X } from "lucide-react";
import { useEffect, useState } from "react";
import {
  PosEdgeClient,
  loadBrowserPrinterConfiguration,
  saveBrowserPrinterConfiguration,
  type PosPrintTemplateFormat,
  type PosPrinterConfiguration,
} from "@/services/pos/pos-edge-client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { loadPosInstaller, type PosInstaller } from "@/services/pos/pos-installer";

type PrinterConfigurationClient = Pick<PosEdgeClient,
  "printerConfiguration" | "savePrinterConfiguration" | "readScaleWeight">;

export function PosPrinterDialog({
  client,
  onClose,
}: {
  client: PrinterConfigurationClient | null;
  onClose: () => void;
}) {
  const [value, setValue] = useState<PosPrinterConfiguration | null>(null);
  const [printers, setPrinters] = useState<string[]>([]);
  const [serialPorts, setSerialPorts] = useState<string[]>([]);
  const [busy, setBusy] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [feedback, setFeedback] = useState<string | null>(null);
  const [installer, setInstaller] = useState<PosInstaller | null>(null);
  const [installerError, setInstallerError] = useState<string | null>(null);

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

  useEffect(() => {
    if (client) return;
    let active = true;
    loadPosInstaller()
      .then((value) => active && setInstaller(value))
      .catch((caught: unknown) => active && setInstallerError(
        caught instanceof Error ? caught.message : "No fue posible consultar el instalador.",
      ));
    return () => { active = false; };
  }, [client]);

  async function save() {
    if (!value) return;
    setBusy(true);
    setError(null);
    setFeedback(null);
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
              <Printer className="h-5 w-5" /> Periféricos
            </h2>
            <p className="mt-1 text-sm text-slate-600">
              {client
                ? "La impresora y la balanza se configuran para este computador."
                : "Puedes imprimir desde el navegador o instalar Auraly para usar los periféricos directamente."}
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
              <div className="rounded-2xl border border-teal-200 bg-teal-50/60 p-4"><p className="font-semibold text-slate-950">{client?"Impresión directa por flujo":"Impresión desde el navegador"}</p><p className="mt-1 text-xs text-slate-600">{client?"Cada flujo usa su formato, impresora de Windows y, si corresponde, su ancho de tirilla. No se abre la impresión del navegador.":"Sin Auraly, al emitir se abrirá el diálogo del navegador para escoger la impresora y confirmar el trabajo."}</p></div>
              <WorkflowPrinterCard title="Punto de venta" description="Facturas y comprobantes emitidos desde la caja." format={value.posOutputFormat??"Receipt"} printerName={value.posPrinterName??printerFor(value,value.posOutputFormat??"Receipt")} paperWidth={value.receiptPaperWidthMillimeters} printers={printers} onChange={(format,printerName,paperWidth)=>setValue(configureWorkflow(value,"pos",format,printerName,paperWidth))}/>
              <WorkflowPrinterCard title="Facturas desde pedidos" description="Facturas electrónicas y comprobantes de venta generados al facturar pedidos." format={value.ordersOutputFormat??"HalfLetter"} printerName={value.ordersPrinterName??printerFor(value,value.ordersOutputFormat??"HalfLetter")} paperWidth={value.ordersReceiptPaperWidthMillimeters??80} printers={printers} onChange={(format,printerName,paperWidth)=>setValue(configureWorkflow(value,"orders",format,printerName,paperWidth))}/>
              {client&&!printers.length && (
                <p className="rounded-xl bg-amber-50 p-3 text-sm text-amber-900">
                  Windows no reporto impresoras instaladas. Instala el controlador y vuelve a abrir esta configuracion.
                </p>
              )}
              {!client && <section className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
                <h3 className="font-semibold text-slate-950">Impresión directa</h3>
                <p className="mt-1 text-sm text-slate-600">Para imprimir sin abrir el diálogo del navegador, seleccionar las impresoras de este equipo y controlar corte o cajón compatibles, instala Auraly.</p>
                {installer ? <a href={installer.downloadUrl} className="mt-3 inline-flex h-10 items-center gap-2 rounded-xl bg-teal-700 px-4 text-sm font-bold text-white"><Download className="h-4 w-4" />Instalar Auraly {installer.version}</a> : installerError ? <p className="mt-3 text-sm text-amber-700">{installerError}</p> : <p className="mt-3 flex items-center gap-2 text-sm text-slate-500"><Loader2 className="h-4 w-4 animate-spin" />Consultando instalador…</p>}
              </section>}
              {client&&<ScaleConfiguration
                  value={value.scale ?? defaultScale()}
                  serialPorts={serialPorts}
                  busy={busy}
                  onChange={(scale) => setValue({ ...value, scale })}
                  onTest={async () => {
                    setBusy(true); setError(null); setFeedback(null);
                    try {
                      const result = await client.readScaleWeight();
                      setFeedback(`Balanza conectada: ${result.weight} ${result.unit} (${result.portName}).`);
                    } catch (caught) {
                      setError(caught instanceof Error ? caught.message : "No fue posible leer la balanza.");
                    } finally { setBusy(false); }
                  }}
                />}
              {!client && <section className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
                <h3 className="flex items-center gap-2 font-semibold text-slate-950"><Scale className="h-4 w-4" />Balanza</h3>
                <p className="mt-1 text-sm text-slate-600">Para conectar y leer automáticamente una balanza debes instalar Auraly. Sin la aplicación, el peso se ingresa manualmente.</p>
              </section>}
            </>
          ) : null}
          {feedback && <p className="rounded-xl bg-emerald-50 p-3 text-sm text-emerald-800">{feedback}</p>}
          {error && <p className="rounded-xl bg-red-50 p-3 text-sm text-red-700">{error}</p>}
        </div>
        <footer className="flex shrink-0 justify-end gap-2 border-t bg-white px-6 py-4">
          <button type="button" onClick={onClose} disabled={busy}
            className="h-10 rounded-xl border px-4 text-sm font-semibold">Cancelar</button>
          <button type="button" onClick={() => void save()} disabled={busy || !value || !validPeripheralConfiguration(value, Boolean(client))}
            className="flex h-10 items-center gap-2 rounded-xl bg-teal-700 px-4 text-sm font-bold text-white disabled:opacity-50">
            {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
            Guardar
          </button>
        </footer>
      </section>
    </div>
  );
}

function WorkflowPrinterCard({title,description,format,printerName,paperWidth,printers,onChange}:{title:string;description:string;format:PosPrintTemplateFormat;printerName:string|null;paperWidth:58|80;printers:string[];onChange:(format:PosPrintTemplateFormat,printerName:string|null,paperWidth:58|80)=>void}){
  return <section className="space-y-4 rounded-2xl border border-slate-200 bg-slate-50 p-4"><div><h3 className="font-semibold text-slate-950">{title}</h3><p className="text-xs text-slate-600">{description}</p></div><div className="grid gap-4 sm:grid-cols-2"><Field label="Formato"><Select value={format} onValueChange={next=>onChange(next as PosPrintTemplateFormat,printerName,paperWidth)}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent><SelectItem value="Receipt">Tirilla</SelectItem><SelectItem value="HalfLetter">Media carta</SelectItem><SelectItem value="HalfLegal">Media oficio</SelectItem><SelectItem value="Letter">Carta</SelectItem></SelectContent></Select></Field>{printers.length>0&&<Field label="Impresora del sistema"><Select value={printerName??undefined} onValueChange={next=>onChange(format,next,paperWidth)}><SelectTrigger><SelectValue placeholder="Selecciona una impresora"/></SelectTrigger><SelectContent>{printers.map(printer=><SelectItem key={printer} value={printer}>{printer}</SelectItem>)}</SelectContent></Select></Field>}{format==="Receipt"&&<Field label="Ancho de tirilla"><Select value={String(paperWidth)} onValueChange={next=>onChange(format,printerName,Number(next) as 58|80)}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent><SelectItem value="80">80 mm</SelectItem><SelectItem value="58">58 mm</SelectItem></SelectContent></Select></Field>}</div></section>;
}

function printerFor(value:PosPrinterConfiguration,format:PosPrintTemplateFormat){
  return value.templateRoutes?.find(route=>route.format===format)?.printerName??(format==="Receipt"?value.receiptPrinterName:value.letterPrinterName);
}

function configureWorkflow(value:PosPrinterConfiguration,workflow:"pos"|"orders",format:PosPrintTemplateFormat,printerName:string|null,paperWidth:58|80):PosPrinterConfiguration{
  const routes=(value.templateRoutes??[]).filter(route=>route.format!==format);
  const templateRoutes=[...routes,...(["SalesInvoice","SalesReceipt"] as const).map(documentType=>({documentType,format,printerName}))];
  return {...value,receiptMode:"WindowsRaw",orderMode:"WindowsPrint",templateRoutes,
    ...(workflow==="pos"?{posOutputFormat:format,posPrinterName:printerName,receiptPaperWidthMillimeters:paperWidth,receiptPrinterName:printerName}:{ordersOutputFormat:format,ordersPrinterName:printerName,ordersReceiptPaperWidthMillimeters:paperWidth}),
    ...(format!=="Receipt"?{letterPrinterName:printerName}:{})};
}

function ScaleConfiguration({ value, serialPorts, busy, onChange, onTest }: {
  value: NonNullable<PosPrinterConfiguration["scale"]>;
  serialPorts: string[];
  busy: boolean;
  onChange: (value: NonNullable<PosPrinterConfiguration["scale"]>) => void;
  onTest: () => Promise<void>;
}) {
  return <div className="space-y-4 rounded-2xl border border-slate-200 bg-slate-50 p-4">
    <label className="flex items-center justify-between gap-3 font-semibold text-slate-900"><span>Balanza conectada</span><Switch checked={value.enabled} onCheckedChange={enabled=>onChange({...value,enabled})}/></label>
    {value.enabled && <>
      <Field label="Puerto de la balanza">
        <Select value={value.portName||undefined} onValueChange={portName=>onChange({...value,portName})}><SelectTrigger><SelectValue placeholder="Selecciona un puerto"/></SelectTrigger><SelectContent>{serialPorts.map(port=><SelectItem key={port} value={port}>{port}</SelectItem>)}</SelectContent></Select>
      </Field>
      {!serialPorts.length && <p className="rounded-xl border border-amber-200 bg-amber-50 p-3 text-sm text-amber-900">Windows no reporta puertos COM. Conecta la balanza o instala su controlador y vuelve a abrir Periféricos.</p>}
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="Velocidad"><Input type="number" min={1} value={value.baudRate} onChange={(event) => onChange({ ...value, baudRate: Number(event.target.value) })}/></Field>
        <Field label="Bits de datos"><Select value={String(value.dataBits)} onValueChange={dataBits=>onChange({...value,dataBits:Number(dataBits)})}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent>{[5,6,7,8].map(bits=><SelectItem key={bits} value={String(bits)}>{bits}</SelectItem>)}</SelectContent></Select></Field>
        <Field label="Paridad"><Select value={value.parity} onValueChange={parity=>onChange({...value,parity:parity as typeof value.parity})}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent><SelectItem value="None">Ninguna</SelectItem><SelectItem value="Odd">Impar</SelectItem><SelectItem value="Even">Par</SelectItem><SelectItem value="Mark">Marca</SelectItem><SelectItem value="Space">Espacio</SelectItem></SelectContent></Select></Field>
        <Field label="Bits de parada"><Select value={value.stopBits} onValueChange={stopBits=>onChange({...value,stopBits:stopBits as typeof value.stopBits})}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent><SelectItem value="One">1</SelectItem><SelectItem value="OnePointFive">1,5</SelectItem><SelectItem value="Two">2</SelectItem></SelectContent></Select></Field>
        <Field label="Tiempo de espera (ms)"><Input type="number" min={200} max={10000} value={value.timeoutMilliseconds} onChange={(event) => onChange({ ...value, timeoutMilliseconds: Number(event.target.value) })}/></Field>
      </div>
      <label className="flex items-center justify-between gap-3 text-sm font-semibold"><span>La balanza requiere comando de lectura</span><Switch checked={value.sendsRequest} onCheckedChange={sendsRequest=>onChange({...value,sendsRequest})}/></label>
      {value.sendsRequest&&<Field label="Comando de lectura"><Input value={value.requestText} onChange={event=>onChange({...value,requestText:event.target.value})} placeholder="Ejemplo: P\\r\\n"/></Field>}
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="Posición inicial"><Input type="number" min={0} value={value.startIndex} onChange={event=>onChange({...value,startIndex:Number(event.target.value)})}/></Field>
        <Field label="Longitud (0 = automática)"><Input type="number" min={0} value={value.length} onChange={event=>onChange({...value,length:Number(event.target.value)})}/></Field>
      </div>
      <div className="grid gap-3 sm:grid-cols-2">
        <label className="flex items-center justify-between gap-3 rounded-xl border bg-white p-3 text-sm font-semibold"><span>Lectura invertida</span><Switch checked={value.reverse} onCheckedChange={reverse=>onChange({...value,reverse})}/></label>
        <label className="flex items-center justify-between gap-3 rounded-xl border bg-white p-3 text-sm font-semibold"><span>Dividir el valor por 1.000</span><Switch checked={value.divideBy1000} onCheckedChange={divideBy1000=>onChange({...value,divideBy1000})}/></label>
      </div>
      <Button type="button" variant="outline" disabled={busy || !value.portName} onClick={() => void onTest()}>Probar balanza</Button>
    </>}
  </div>;
}

function defaultScale(): NonNullable<PosPrinterConfiguration["scale"]> {
  return { enabled: false, portName: "", baudRate: 9600, dataBits: 8, parity: "None", stopBits: "One", sendsRequest: false, requestText: "", startIndex: 0, length: 0, reverse: false, divideBy1000: false, timeoutMilliseconds: 2000 };
}

export function validPeripheralConfiguration(value: PosPrinterConfiguration, direct: boolean) {
  if (!direct) return true;
  if (!value.posPrinterName || !value.ordersPrinterName) return false;
  if (!value.scale?.enabled) return true;
  return Boolean(value.scale.portName) && value.scale.baudRate > 0 &&
    value.scale.dataBits >= 5 && value.scale.dataBits <= 8 &&
    value.scale.timeoutMilliseconds >= 200 && value.scale.timeoutMilliseconds <= 10000;
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <label className="block space-y-2 text-sm font-semibold">
    <span>{label}</span>{children}
  </label>;
}
