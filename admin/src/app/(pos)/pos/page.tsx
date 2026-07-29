"use client";

import {
  AlertTriangle,
  Barcode,
  CheckCircle2,
  Clock3,
  Loader2,
  PackageSearch,
  RotateCcw,
  Save,
  Trash2,
  Wifi,
  WifiOff,
} from "lucide-react";
import Image from "next/image";
import { FormEvent, useCallback, useEffect, useMemo, useRef, useState } from "react";

import {
  PosDraft,
  PosDocumentNumberPreview,
  PosPaymentInput,
  PosEdgeClient,
  PosEdgeError,
  readEdgeTokenFromLaunch,
} from "@/services/pos/pos-edge-client";
import { PosPaymentDialog } from "./pos-payment-dialog";


const money = new Intl.NumberFormat("es-CO", {
  style: "currency",
  currency: "COP",
  maximumFractionDigits: 0,
});

export default function PosPage() {
  const scanner = useRef<HTMLInputElement>(null);
  const [token, setToken] = useState<string | null>(null);
  const [draft, setDraft] = useState<PosDraft | null>(null);
  const [temporaries, setTemporaries] = useState<PosDraft[]>([]);
  const [scan, setScan] = useState("");
  const [busy, setBusy] = useState(false);
  const [edgeReady, setEdgeReady] = useState(false);
  const [online, setOnline] = useState(true);
  const [message, setMessage] = useState("Esperando producto");
  const [error, setError] = useState<string | null>(null);
  const [temporaryOpen, setTemporaryOpen] = useState(false);
  const [temporaryName, setTemporaryName] = useState("");
  const [paymentOpen, setPaymentOpen] = useState(false);
  const [nextNumber, setNextNumber] = useState<PosDocumentNumberPreview | null>(null);
  const [temporaryReference, setTemporaryReference] = useState("");
  const client = useMemo(() => (token ? new PosEdgeClient(token) : null), [token]);

  const focusScanner = useCallback(() => {
    window.requestAnimationFrame(() => scanner.current?.focus());
  }, []);

  const refreshTemporaries = useCallback(async () => {
    if (!client) return;
    setTemporaries(await client.temporaries());
  }, [client]);

  useEffect(() => {
    setToken(readEdgeTokenFromLaunch());
    setOnline(navigator.onLine);
    const connected = () => setOnline(true);
    const disconnected = () => setOnline(false);
    window.addEventListener("online", connected);
    window.addEventListener("offline", disconnected);
    if ("serviceWorker" in navigator) {
      void navigator.serviceWorker.register("/pos-sw.js", { scope: "/pos" });
    }
    return () => {
      window.removeEventListener("online", connected);
      window.removeEventListener("offline", disconnected);
    };
  }, []);

  useEffect(() => {
    if (!client) return;
    let active = true;
    let checking = false;
    let hydrated = false;

    const connect = async () => {
      if (checking) return;
      checking = true;
      try {
        await client.health();
        if (!hydrated) {
          const [current, pending, numbers] = await Promise.all([
            client.activeDraft(),
            client.temporaries(),
            client.nextNumbers(),
          ]);
          if (!active) return;
          setDraft(current);
          setTemporaries(pending);
          setNextNumber(numbers.document);
          hydrated = true;
          focusScanner();
        }
        if (active) setEdgeReady(true);
      } catch {
        hydrated = false;
        if (active) setEdgeReady(false);
      } finally {
        checking = false;
      }
    };

    void connect();
    const interval = window.setInterval(() => void connect(), 3_000);
    return () => {
      active = false;
      window.clearInterval(interval);
    };
  }, [client, focusScanner]);


  useEffect(() => {
    const handleShortcut = (event: KeyboardEvent) => {
      if (
        event.key === "F2" &&
        !busy &&
        Boolean(draft?.lines.length) &&
        !temporaryOpen
      ) {
        event.preventDefault();
        setPaymentOpen(true);
      }
    };
    window.addEventListener("keydown", handleShortcut);
    return () => window.removeEventListener("keydown", handleShortcut);
  }, [busy, draft?.lines.length, temporaryOpen]);
  async function capture(event: FormEvent) {
    event.preventDefault();
    const value = scan.trim();
    if (!client || !value || busy) return;
    if (!edgeReady) {
      setError("POS Edge no est\u00e1 conectado. El c\u00f3digo se conservar\u00e1 para reintentar.");
      setMessage("Esperando conexi\u00f3n con POS Edge");
      focusScanner();
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const result = await client.capture(value, draft?.customerId ?? null);
      if (result.status === "Added" && result.draft) {
        setDraft(result.draft);
        setMessage(`${result.draft.lines.at(-1)?.description ?? "Producto"} agregado`);
      }
      setScan("");
    } catch (caught) {
      showError(caught);
    } finally {
      setBusy(false);
      focusScanner();
    }
  }

  async function changeQuantity(lineId: string, quantity: number) {
    if (!client || !draft || quantity <= 0) return;
    setBusy(true);
    setError(null);
    try {
      const result = await client.changeQuantity(
        draft.draftId.value,
        lineId,
        quantity,
      );
      if (result.draft) setDraft(result.draft);
      setMessage("Cantidad actualizada");
    } catch (caught) {
      showError(caught);
    } finally {
      setBusy(false);
      focusScanner();
    }
  }

  async function removeLine(lineId: string) {
    if (!client || !draft) return;
    setBusy(true);
    try {
      setDraft(await client.removeLine(draft.draftId.value, lineId));
      setMessage("Producto retirado");
    } catch (caught) {
      showError(caught);
    } finally {
      setBusy(false);
      focusScanner();
    }
  }

  async function saveTemporary(event: FormEvent) {
    event.preventDefault();
    if (!client || !draft || !temporaryName.trim()) return;
    setBusy(true);
    try {
      await client.saveTemporary(
        draft.draftId.value,
        temporaryName,
        temporaryReference,
        "",
      );
      setDraft(await client.activeDraft());
      await refreshTemporaries();
      setTemporaryOpen(false);
      setTemporaryName("");
      setTemporaryReference("");
      setMessage("Venta temporal guardada");
    } catch (caught) {
      showError(caught);
    } finally {
      setBusy(false);
      focusScanner();
    }
  }

  async function recoverTemporary(id: string) {
    if (!client) return;
    setBusy(true);
    try {
      setDraft(await client.recoverTemporary(id));
      await refreshTemporaries();
      setMessage("Venta temporal recuperada");
    } catch (caught) {
      showError(caught);
    } finally {
      setBusy(false);
      focusScanner();
    }
  }

  function openPayment() {
    if (!draft?.lines.length || busy) return;
    setError(null);
    setPaymentOpen(true);
  }

  async function completeSale(payments: PosPaymentInput[]) {
    if (!client || !draft || busy) return;
    setBusy(true);
    setError(null);
    try {
      const result = await client.completeSale(
        draft.draftId.value,
        null,
        payments,
      );
      setDraft(result.nextDraft);
      setNextNumber(result.nextDocumentNumber);
      setPaymentOpen(false);
      setMessage(
        `${result.issuedSale.documentNumber} emitida e impresa (DIAN ${result.issuedSale.fiscalNumber}). Nueva venta lista.`,
      );
    } catch (caught) {
      showError(caught);
    } finally {
      setBusy(false);
      focusScanner();
    }
  }

  function showError(caught: unknown) {
    const status = caught instanceof PosEdgeError ? caught.status : 0;
    if (!(caught instanceof PosEdgeError)) setEdgeReady(false);
    const text =
      status === 404
        ? "Producto no encontrado en el catálogo local"
        : status === 409
          ? "La cantidad solicitada no está disponible"
          : status === 503 && caught instanceof PosEdgeError && caught.message.includes("tirilla")
            ? "La factura fue emitida, pero la tirilla no pudo imprimirse. Reintenta sin modificar la venta."
            : status === 503
            ? "La bodega exige validar inventario y no hay conexión"
            : "No fue posible comunicarse con Auraly POS Edge";
    setError(text);
    setMessage("Revisa la novedad");
  }

  if (!token) {
    return <ConnectionGate onConnect={setToken} />;
  }

  return (
    <main className="min-h-screen bg-[#eef3f3] text-slate-950">
      <header className="flex min-h-16 items-center justify-between gap-4 bg-auraly-background px-5 py-3 text-auraly-text shadow-lg">
        <div className="flex items-center gap-3">
          <Image
            src="/brand/auraly-mark.png"
            alt="Auraly"
            width={38}
            height={38}
            className="rounded-xl"
            priority
          />
          <div>
            <p className="text-base font-semibold tracking-tight">Auraly POS</p>
            <p className="text-xs text-auraly-secondary">
              Caja configurada · Bodega asignada
            </p>
          </div>
        </div>
        <div className="flex flex-wrap items-center justify-end gap-2 text-xs">
          <StatusChip
            ok={edgeReady}
            label={edgeReady ? "POS Edge listo" : "POS Edge sin conexión"}
          />
          <StatusChip
            ok={online}
            label={online ? "Internet disponible" : "Venta local"}
            network
          />
          <span className="rounded-full bg-white/10 px-3 py-1.5">
            {temporaries.length} temporales
          </span>
        </div>
      </header>

      <section className="grid min-h-[calc(100vh-4rem)] grid-cols-1 gap-4 p-4 xl:grid-cols-[minmax(0,1fr)_340px]">
        <div className="flex min-w-0 flex-col gap-4">
          <form
            onSubmit={capture}
            className="rounded-2xl border border-teal-900/10 bg-white p-3 shadow-sm"
          >
            <label
              htmlFor="pos-scanner"
              className="mb-2 flex items-center justify-between gap-3 text-sm font-medium text-slate-700"
            >
              <span className="flex items-center gap-2">
                <Barcode className="h-5 w-5 text-teal-700" />
                Escanea o escribe un código
              </span>
              <span className="text-xs font-normal text-slate-500">Enter para agregar</span>
            </label>
            <div className="flex gap-2">
              <input
                ref={scanner}
                id="pos-scanner"
                value={scan}
                onChange={(event) => setScan(event.target.value)}
                disabled={busy}
                autoComplete="off"
                inputMode="text"
                className="h-14 min-w-0 flex-1 rounded-xl border-2 border-teal-700/25 bg-slate-50 px-4 text-xl font-semibold tracking-wide outline-none transition focus:border-teal-600 focus:bg-white focus:ring-4 focus:ring-teal-600/10 disabled:opacity-50"
                placeholder="Código de barras, interno o referencia"
                aria-describedby="capture-state"
              />
              <button
                type="submit"
                disabled={!scan.trim() || busy}
                className="min-w-28 rounded-xl bg-teal-700 px-5 font-semibold text-white transition hover:bg-teal-800 focus:outline-none focus:ring-4 focus:ring-teal-600/20 disabled:cursor-not-allowed disabled:opacity-50"
              >
                {busy ? <Loader2 className="mx-auto animate-spin" /> : "Agregar"}
              </button>
            </div>
            <p
              id="capture-state"
              className={`mt-2 flex min-h-5 items-center gap-2 text-sm ${
                error ? "text-red-700" : "text-slate-500"
              }`}
              role={error ? "alert" : "status"}
            >
              {error ? <AlertTriangle className="h-4 w-4" /> : <CheckCircle2 className="h-4 w-4 text-teal-600" />}
              {error ?? message}
            </p>
          </form>

          <div className="min-h-[360px] flex-1 overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
            <div className="overflow-auto">
              <table className="w-full min-w-[820px] border-collapse text-sm">
                <thead className="sticky top-0 z-10 bg-slate-100 text-left text-xs uppercase tracking-wide text-slate-600">
                  <tr>
                    <th className="px-4 py-3">Producto</th>
                    <th className="w-28 px-3 py-3 text-right">Cantidad</th>
                    <th className="w-32 px-3 py-3 text-right">Precio</th>
                    <th className="w-28 px-3 py-3 text-right">Descuento</th>
                    <th className="w-24 px-3 py-3 text-right">IVA</th>
                    <th className="w-32 px-3 py-3 text-right">Total</th>
                    <th className="w-16 px-3 py-3" aria-label="Acciones" />
                  </tr>
                </thead>
                <tbody>
                  {draft?.lines.map((line) => (
                    <tr
                      key={line.lineId}
                      className="border-t border-slate-100 transition hover:bg-teal-50/40"
                    >
                      <td className="px-4 py-3">
                        <p className="font-semibold text-slate-900">{line.description}</p>
                        <p className="mt-0.5 text-xs text-slate-500">
                          {line.productCode} · {line.unitCode} · {priceLabel(line.priceSource)}
                        </p>
                      </td>
                      <td className="px-3 py-2 text-right">
                        <input
                          type="number"
                          min="0.001"
                          step="0.001"
                          defaultValue={line.quantity}
                          onKeyDown={(event) => {
                            if (event.key === "Enter") {
                              event.preventDefault();
                              void changeQuantity(line.lineId, event.currentTarget.valueAsNumber);
                            }
                          }}
                          onBlur={(event) => {
                            if (event.currentTarget.valueAsNumber !== line.quantity)
                              void changeQuantity(line.lineId, event.currentTarget.valueAsNumber);
                          }}
                          className="h-10 w-24 rounded-lg border border-slate-300 bg-white px-2 text-right font-semibold outline-none focus:border-teal-600 focus:ring-2 focus:ring-teal-600/15"
                          aria-label={`Cantidad de ${line.description}`}
                        />
                      </td>
                      <td className="px-3 py-3 text-right tabular-nums">{money.format(line.unitPrice)}</td>
                      <td className="px-3 py-3 text-right tabular-nums">{money.format(line.discount)}</td>
                      <td className="px-3 py-3 text-right tabular-nums">{money.format(line.tax)}</td>
                      <td className="px-3 py-3 text-right font-semibold tabular-nums">{money.format(line.total)}</td>
                      <td className="px-3 py-2 text-right">
                        <button
                          type="button"
                          onClick={() => void removeLine(line.lineId)}
                          className="inline-flex h-10 w-10 items-center justify-center rounded-lg text-slate-500 transition hover:bg-red-50 hover:text-red-700 focus:outline-none focus:ring-2 focus:ring-red-300"
                          aria-label={`Eliminar ${line.description}`}
                        >
                          <Trash2 className="h-4 w-4" />
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            {!draft?.lines.length && (
              <div className="flex min-h-[300px] flex-col items-center justify-center px-6 text-center text-slate-500">
                <PackageSearch className="mb-3 h-12 w-12 text-teal-700/45" />
                <p className="font-semibold text-slate-700">La venta está vacía</p>
                <p className="mt-1 max-w-sm text-sm">
                  Escanea el primer producto. Al agregarlo, el foco vuelve automáticamente al lector.
                </p>
              </div>
            )}
          </div>
        </div>

        <aside className="flex flex-col gap-4">
          <section className="rounded-2xl bg-auraly-background p-5 text-auraly-text shadow-lg">
            <div className="mb-5 flex items-center justify-between">
              <div>
                <p className="text-xs uppercase tracking-[0.16em] text-auraly-secondary">Venta actual</p>
                <p className="mt-1 text-sm font-medium">Consumidor final</p>
                <p className="mt-0.5 text-xs text-auraly-secondary">
                  Próxima: {nextNumber?.isAvailable ? nextNumber.fullNumber : "Serie no disponible"}
                </p>
              </div>
              <span className="rounded-lg bg-white/10 px-2 py-1 text-xs">
                {draft?.lines.length ?? 0} líneas
              </span>
            </div>
            <dl className="space-y-3 text-sm">
              <TotalRow label="Subtotal" value={draft?.untaxedAmount ?? 0} />
              <TotalRow label="Impuestos" value={draft?.taxAmount ?? 0} />
              <div className="border-t border-white/15 pt-4">
                <dt className="text-sm text-auraly-secondary">Total a pagar</dt>
                <dd className="mt-1 text-right text-4xl font-bold tracking-tight text-auraly-light">
                  {money.format(draft?.payableAmount ?? 0)}
                </dd>
              </div>
            </dl>
            <button
              type="button"
              disabled={!draft?.lines.length || busy}
              onClick={openPayment}
              className="mt-5 flex h-14 w-full items-center justify-center gap-2 rounded-xl bg-auraly-accent px-4 text-lg font-bold text-auraly-background transition hover:bg-auraly-light disabled:cursor-not-allowed disabled:opacity-40"
            >
              Cobrar
              <span className="rounded bg-black/10 px-2 py-0.5 text-xs font-semibold">F2</span>
            </button>

            <button
              type="button"
              disabled={!draft?.lines.length || busy}
              onClick={() => setTemporaryOpen(true)}
              className="mt-5 flex h-12 w-full items-center justify-center gap-2 rounded-xl border border-auraly-accent/40 bg-white/5 font-semibold transition hover:bg-white/10 disabled:cursor-not-allowed disabled:opacity-40"
            >
              <Save className="h-4 w-4" />
              Guardar temporal
            </button>
          </section>

          <section className="flex-1 rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
            <div className="mb-3 flex items-center justify-between">
              <div>
                <p className="font-semibold text-slate-900">Ventas temporales</p>
                <p className="text-xs text-slate-500">Pendientes de recuperar</p>
              </div>
              <Clock3 className="h-5 w-5 text-teal-700" />
            </div>
            <div className="space-y-2">
              {temporaries.map((temporary) => (
                <article
                  key={temporary.draftId.value}
                  className="rounded-xl border border-slate-200 p-3"
                >
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0">
                      <p className="truncate font-medium">{temporary.name}</p>
                      <p className="mt-0.5 text-xs text-slate-500">
                        {temporary.reference || "Sin referencia"} · {temporary.lines.length} líneas
                      </p>
                    </div>
                    <p className="font-semibold tabular-nums">{money.format(temporary.payableAmount)}</p>
                  </div>
                  <button
                    type="button"
                    onClick={() => void recoverTemporary(temporary.draftId.value)}
                    disabled={Boolean(draft?.lines.length) || busy}
                    className="mt-3 flex h-9 w-full items-center justify-center gap-2 rounded-lg bg-teal-50 text-sm font-semibold text-teal-800 transition hover:bg-teal-100 disabled:cursor-not-allowed disabled:opacity-45"
                  >
                    <RotateCcw className="h-4 w-4" />
                    Recuperar
                  </button>
                </article>
              ))}
              {!temporaries.length && (
                <p className="rounded-xl border border-dashed border-slate-300 p-5 text-center text-sm text-slate-500">
                  No hay ventas temporales.
                </p>
              )}
            </div>
          </section>
        </aside>
      </section>

      {paymentOpen && draft && (
        <PosPaymentDialog
          total={draft.payableAmount}
          busy={busy}
          onCancel={() => setPaymentOpen(false)}
          onConfirm={completeSale}
        />
      )}

      {temporaryOpen && (
        <div className="fixed inset-0 z-50 grid place-items-center bg-slate-950/55 p-4">
          <form
            onSubmit={saveTemporary}
            className="w-full max-w-md rounded-2xl bg-white p-5 shadow-2xl"
          >
            <h2 className="text-lg font-semibold">Guardar venta temporal</h2>
            <p className="mt-1 text-sm text-slate-500">
              No asigna consecutivo fiscal ni genera movimientos.
            </p>
            <label className="mt-5 block text-sm font-medium">
              Nombre
              <input
                autoFocus
                required
                value={temporaryName}
                onChange={(event) => setTemporaryName(event.target.value)}
                className="mt-1 h-11 w-full rounded-lg border border-slate-300 px-3 outline-none focus:border-teal-600 focus:ring-2 focus:ring-teal-600/15"
                placeholder="Ej. Cliente espera"
              />
            </label>
            <label className="mt-3 block text-sm font-medium">
              Referencia
              <input
                value={temporaryReference}
                onChange={(event) => setTemporaryReference(event.target.value)}
                className="mt-1 h-11 w-full rounded-lg border border-slate-300 px-3 outline-none focus:border-teal-600 focus:ring-2 focus:ring-teal-600/15"
                placeholder="Opcional"
              />
            </label>
            <div className="mt-5 flex justify-end gap-2">
              <button
                type="button"
                onClick={() => {
                  setTemporaryOpen(false);
                  focusScanner();
                }}
                className="h-10 rounded-lg border border-slate-300 px-4 font-medium"
              >
                Cancelar
              </button>
              <button
                type="submit"
                disabled={busy || !temporaryName.trim()}
                className="h-10 rounded-lg bg-teal-700 px-4 font-semibold text-white disabled:opacity-50"
              >
                Guardar
              </button>
            </div>
          </form>
        </div>
      )}
    </main>
  );
}

function ConnectionGate({ onConnect }: { onConnect: (token: string) => void }) {
  const [value, setValue] = useState("");
  return (
    <main className="grid min-h-screen place-items-center bg-auraly-background p-6 text-auraly-text">
      <form
        onSubmit={(event) => {
          event.preventDefault();
          const token = value.trim();
          if (!token) return;
          window.sessionStorage.setItem("auraly.pos.edge-token", token);
          onConnect(token);
        }}
        className="w-full max-w-md rounded-3xl border border-white/10 bg-auraly-surface p-7 shadow-2xl"
      >
        <Image
          src="/brand/auraly-mark.png"
          alt="Auraly"
          width={56}
          height={56}
          className="rounded-2xl"
          priority
        />
        <h1 className="mt-5 text-2xl font-semibold">Conectar Auraly POS</h1>
        <p className="mt-2 text-sm leading-6 text-auraly-secondary">
          Abre esta pantalla desde Auraly POS Edge. Para desarrollo local puedes ingresar la sesión generada por el host.
        </p>
        <label className="mt-5 block text-sm font-medium">
          Sesión local
          <input
            type="password"
            value={value}
            onChange={(event) => setValue(event.target.value)}
            className="mt-2 h-12 w-full rounded-xl border border-white/15 bg-white/5 px-3 text-white outline-none focus:border-auraly-accent"
            autoComplete="off"
          />
        </label>
        <button
          type="submit"
          disabled={!value.trim()}
          className="mt-4 h-12 w-full rounded-xl bg-auraly-accent font-semibold text-auraly-background transition hover:bg-auraly-light disabled:opacity-40"
        >
          Conectar
        </button>
      </form>
    </main>
  );
}

function StatusChip({
  ok,
  label,
  network = false,
}: {
  ok: boolean;
  label: string;
  network?: boolean;
}) {
  const Icon = network ? (ok ? Wifi : WifiOff) : ok ? CheckCircle2 : AlertTriangle;
  return (
    <span
      className={`flex items-center gap-1.5 rounded-full px-3 py-1.5 ${
        ok ? "bg-emerald-400/15 text-emerald-100" : "bg-amber-400/15 text-amber-100"
      }`}
    >
      <Icon className="h-3.5 w-3.5" />
      {label}
    </span>
  );
}

function TotalRow({ label, value }: { label: string; value: number }) {
  return (
    <div className="flex items-center justify-between gap-3">
      <dt className="text-auraly-secondary">{label}</dt>
      <dd className="font-semibold tabular-nums">{money.format(value)}</dd>
    </div>
  );
}

function priceLabel(source: string) {
  if (source === "PriceList") return "Lista de precio";
  if (source === "PriceChannel") return "Canal de precio";
  return "Precio del negocio";
}
