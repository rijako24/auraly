"use client";

import {
  AlertTriangle,
  Banknote,
  Barcode,
  CheckCircle2,
  Clock3,
  ClipboardList,
  Loader2,
  RotateCcw,
  Save,
  Search,
  Trash2,
  Wifi,
  WifiOff,
} from "lucide-react";
import Image from "next/image";
import { FormEvent, useCallback, useEffect, useMemo, useRef, useState } from "react";

import {
  PosCatalogProduct,
  PosDraft,
  PosDocumentNumberPreview,
  PosPaymentInput,
  PosEdgeClient,
  PosEdgeError,
  readEdgeTokenFromLaunch,
} from "@/services/pos/pos-edge-client";
import { PosConfirmDialog } from "./pos-confirm-dialog";
import { PosPaymentDialog } from "./pos-payment-dialog";
import type { PosPaymentSettlement } from "./pos-payment-settlement";
import { PosProductSearchDialog } from "./pos-product-search-dialog";


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
  const [serverConnected, setServerConnected] = useState(false);
  const [workstation, setWorkstation] = useState({
    registerCode: "—",
    userDisplayName: "—",
  });
  const [message, setMessage] = useState("Esperando producto");
  const [error, setError] = useState<string | null>(null);
  const [temporaryOpen, setTemporaryOpen] = useState(false);
  const [temporaryName, setTemporaryName] = useState("");
  const [paymentOpen, setPaymentOpen] = useState(false);
  const [productSearchOpen, setProductSearchOpen] = useState(false);
  const [sidePanel, setSidePanel] = useState<"temporaries" | "orders">("temporaries");
  const [selectedLineId, setSelectedLineId] = useState<string | null>(null);
  const [confirmation, setConfirmation] = useState<
    | { kind: "line"; lineId: string; productName: string }
    | { kind: "sale" }
    | null
  >(null);
  const [lastSettlement, setLastSettlement] = useState<{
    documentNumber: string;
    received: number;
    change: number;
  } | null>(null);
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
    if ("serviceWorker" in navigator) {
      void navigator.serviceWorker.register("/pos-sw.js", { scope: "/pos" });
    }
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
        const health = await client.health();
        if (active) setServerConnected(health.serverConnected);
        if (active) {
          setWorkstation({
            registerCode: health.registerCode,
            userDisplayName: health.userDisplayName,
          });
        }
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
        if (active) {
          setEdgeReady(false);
          setServerConnected(false);
        }
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
        event.key === "F1" &&
        !busy &&
        Boolean(draft?.lines.length) &&
        !temporaryOpen &&
        !productSearchOpen &&
        !paymentOpen &&
        !confirmation
      ) {
        event.preventDefault();
        setPaymentOpen(true);
      } else if (
        event.key === "F2" &&
        !busy &&
        !temporaryOpen &&
        !paymentOpen &&
        !confirmation
      ) {
        event.preventDefault();
        setProductSearchOpen(true);
      } else if (
        event.key === "F4" &&
        !busy &&
        Boolean(draft?.lines.length) &&
        !temporaryOpen &&
        !paymentOpen &&
        !productSearchOpen &&
        !confirmation
      ) {
        event.preventDefault();
        setTemporaryOpen(true);
      } else if (
        event.key === "F5" &&
        !busy &&
        selectedLineId &&
        !temporaryOpen &&
        !paymentOpen &&
        !productSearchOpen &&
        !confirmation
      ) {
        event.preventDefault();
        requestRemoveLine(selectedLineId);
      } else if (
        event.key === "F6" &&
        !busy &&
        !temporaryOpen &&
        !paymentOpen &&
        !productSearchOpen &&
        !confirmation
      ) {
        event.preventDefault();
        requestCancelSale();
      }
    };
    window.addEventListener("keydown", handleShortcut);
    return () => window.removeEventListener("keydown", handleShortcut);
  }, [
    busy,
    confirmation,
    draft?.lines.length,
    paymentOpen,
    productSearchOpen,
    selectedLineId,
    temporaryOpen,
  ]);

  async function capture(event: FormEvent) {
    event.preventDefault();
    await captureValue(scan.trim());
  }

  async function captureValue(value: string): Promise<boolean> {
    if (!client || !value || busy) return false;
    if (!edgeReady) {
      setError("POS Edge no est\u00e1 conectado. El c\u00f3digo se conservar\u00e1 para reintentar.");
      setMessage("Esperando conexi\u00f3n con POS Edge");
      focusScanner();
      return false;
    }
    setBusy(true);
    setError(null);
    try {
      const startsNewSale = !draft?.lines.length;
      const result = await client.capture(value, draft?.customerId ?? null);
      if (result.status === "Added" && result.draft) {
        setDraft(result.draft);
        setSelectedLineId(result.draft.lines.at(-1)?.lineId ?? null);
        setMessage(`${result.draft.lines.at(-1)?.description ?? "Producto"} agregado`);
        if (startsNewSale) setLastSettlement(null);
        setScan("");
        return true;
      }
      return false;
    } catch (caught) {
      showError(caught);
      return false;
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
      const updated = await client.removeLine(draft.draftId.value, lineId);
      setDraft(updated);
      setSelectedLineId(updated.lines.at(-1)?.lineId ?? null);
      setMessage("Producto retirado");
    } catch (caught) {
      showError(caught);
    } finally {
      setBusy(false);
      focusScanner();
    }
  }

  function requestRemoveLine(lineId: string) {
    const line = draft?.lines.find((candidate) => candidate.lineId === lineId);
    if (!line || busy) return;
    setConfirmation({ kind: "line", lineId, productName: line.description });
  }

  function requestCancelSale() {
    if (!draft?.lines.length || busy) return;
    setConfirmation({ kind: "sale" });
  }

  async function cancelSale() {
    if (!client || !draft?.lines.length || busy) return;
    setBusy(true);
    setError(null);
    try {
      const next = await client.cancelDraft(draft.draftId.value);
      setDraft(next);
      setSelectedLineId(null);
      setScan("");
      setMessage("Venta reiniciada. Nueva venta lista.");
    } catch (caught) {
      showError(caught);
    } finally {
      setBusy(false);
      focusScanner();
    }
  }

  async function confirmDestructiveAction() {
    if (!confirmation) return;
    if (confirmation.kind === "line") {
      await removeLine(confirmation.lineId);
    } else {
      await cancelSale();
    }
    setConfirmation(null);
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

  async function completeSale(
    payments: PosPaymentInput[],
    settlement: PosPaymentSettlement,
  ) {
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
      setLastSettlement({
        documentNumber: result.issuedSale.documentNumber,
        received: settlement.received,
        change: settlement.change,
      });
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

  const searchProducts = useCallback(
    (term: string, skip: number) =>
      client?.searchProducts(term, skip, 50) ??
      Promise.resolve({
        items: [],
        hasMore: false,
        nextOffset: null,
      }),
    [client],
  );

  async function selectSearchProduct(product: PosCatalogProduct) {
    const added = await captureValue(product.productCode);
    if (added) setProductSearchOpen(false);
    return added;
  }

  function openOrders() {
    if (!serverConnected) {
      setError(null);
      setMessage("Los pedidos se consultan en línea. Auraly Server no está disponible.");
      focusScanner();
      return;
    }
    window.location.assign("/dashboard/orders");
  }

  function showError(caught: unknown) {
    const status = caught instanceof PosEdgeError ? caught.status : 0;
    if (!(caught instanceof PosEdgeError)) setEdgeReady(false);
    const text =
      status === 409 &&
      caught instanceof PosEdgeError &&
      caught.message.includes("pendiente de imprimir")
        ? caught.message
        : status === 404
        ? "Producto no encontrado en el catálogo local"
        : status === 409
          ? "La cantidad solicitada no está disponible"
          : status === 503 && caught instanceof PosEdgeError && caught.message.includes("tirilla")
            ? "La factura fue emitida, pero la tirilla no pudo imprimirse. Reintenta sin modificar la venta."
            : status === 503
            ? "La bodega exige validar inventario y no hay conexión"
            : "No fue posible acceder a los servicios locales de la caja";
    setError(text);
    setMessage("Revisa la novedad");
  }

  if (!token) {
    return <ConnectionGate onConnect={setToken} />;
  }

  return (
    <main className="min-h-screen bg-[#eef3f3] text-slate-950">
      <header className="flex min-h-16 items-center justify-between gap-4 bg-auraly-background px-5 py-3 text-auraly-text shadow-lg">
        <div>
          <p className="text-base font-semibold tracking-tight">Caja {workstation.registerCode}</p>
          <p className="text-xs text-auraly-secondary">
            Cajero: {workstation.userDisplayName}
          </p>
        </div>
        <div className="flex flex-wrap items-center justify-end gap-2 text-xs">
          <StatusChip
            ok={serverConnected}
            label={serverConnected ? "Conectado con Auraly" : "Modo sin conexión"}
            network
          />
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
              <button
                type="button"
                onClick={() => setProductSearchOpen(true)}
                disabled={busy || !edgeReady}
                className="flex min-w-28 items-center justify-center gap-2 rounded-xl border border-teal-700/25 bg-white px-4 font-semibold text-teal-800 transition hover:bg-teal-50 focus:outline-none focus:ring-4 focus:ring-teal-600/15 disabled:opacity-45"
              >
                <Search className="h-4 w-4" />
                Buscar <span className="rounded bg-teal-50 px-1.5 py-0.5 text-xs">F2</span>
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
              <table className="w-full min-w-[680px] border-collapse text-sm">
                <thead className="sticky top-0 z-10 bg-slate-100 text-left text-xs font-semibold tracking-wide text-slate-600">
                  <tr>
                    <th className="px-4 py-3">Producto</th>
                    <th className="w-28 px-3 py-3 text-right">Cantidad</th>
                    <th className="w-36 px-3 py-3 text-right">Precio unitario</th>
                    <th className="w-36 px-3 py-3 text-right">Total</th>
                    <th className="w-16 px-3 py-3" aria-label="Acciones" />
                  </tr>
                </thead>
                <tbody>
                  {draft?.lines.map((line) => (
                    <tr
                      key={line.lineId}
                      onClick={() => setSelectedLineId(line.lineId)}
                      className={`border-t border-slate-100 transition ${
                        selectedLineId === line.lineId
                          ? "bg-teal-50 ring-2 ring-inset ring-teal-600/25"
                          : "hover:bg-teal-50/40"
                      }`}
                    >
                      <td className="px-4 py-3.5">
                        <p className="text-[15px] font-bold leading-snug text-slate-950">
                          {line.description}
                        </p>
                        <p className="mt-1 text-xs font-medium text-slate-500">
                          {line.productCode} · {line.unitCode} · {priceLabel(line.priceSource)}
                        </p>
                        <div className="mt-2 flex flex-wrap items-center gap-1.5 text-[11px] font-semibold tabular-nums">
                          {line.discount > 0 && (
                            <span className="rounded-full border border-amber-200 bg-amber-50 px-2 py-0.5 text-amber-800">
                              Descuento −{money.format(line.discount)}
                            </span>
                          )}
                          <span
                            className="rounded-full border border-sky-200 bg-sky-50 px-2 py-0.5 text-sky-800"
                            title={`Impuesto ${line.taxCode}`}
                          >
                            IVA {line.taxRate}% · {money.format(line.tax)}
                          </span>
                        </div>
                      </td>
                      <td className="px-3 py-2 text-right">
                        <input
                          key={`${line.lineId}-${line.quantity}`}
                          type="number"
                          min="0.001"
                          step="0.001"
                          defaultValue={line.quantity}
                          onFocus={() => setSelectedLineId(line.lineId)}
                          onKeyDown={(event) => {
                            if (event.key === "Enter") {
                              event.preventDefault();
                              event.currentTarget.blur();
                            }
                          }}
                          onBlur={(event) => {
                            const quantity = event.currentTarget.valueAsNumber;
                            if (!Number.isFinite(quantity) || quantity <= 0) {
                              event.currentTarget.value = String(line.quantity);
                              focusScanner();
                            } else if (quantity !== line.quantity) {
                              void changeQuantity(line.lineId, quantity);
                            } else {
                              focusScanner();
                            }
                          }}
                          className="h-10 w-24 rounded-lg border border-slate-300 bg-white px-2 text-right font-semibold outline-none focus:border-teal-600 focus:ring-2 focus:ring-teal-600/15"
                          aria-label={`Cantidad de ${line.description}`}
                        />
                      </td>
                      <td className="px-3 py-3 text-right font-medium tabular-nums text-slate-700">
                        {money.format(line.unitPrice)}
                      </td>
                      <td className="px-3 py-3 text-right text-base font-bold tabular-nums text-slate-950">
                        {money.format(line.total)}
                      </td>
                      <td className="px-3 py-2 text-right">
                        <button
                          type="button"
                          onClick={() => requestRemoveLine(line.lineId)}
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
              <div className="relative flex min-h-[300px] flex-col items-center justify-center overflow-hidden px-6 text-center">
                <div className="absolute inset-0 bg-[radial-gradient(circle_at_center,rgba(13,148,136,0.10),transparent_52%)]" />
                <div className="relative grid h-20 w-20 place-items-center rounded-full border border-teal-200 bg-teal-50 shadow-[0_0_0_12px_rgba(20,184,166,0.05)]">
                  <Barcode className="h-9 w-9 text-teal-700" />
                </div>
                <p className="relative mt-6 text-xl font-bold tracking-tight text-slate-900">Caja lista para vender</p>
                <p className="relative mt-1 max-w-md text-sm text-slate-500">
                  Escanea el primer producto o abre la búsqueda. El lector queda preparado para continuar sin usar el mouse.
                </p>
                <div className="relative mt-5 flex flex-wrap items-center justify-center gap-2 text-xs font-semibold">
                  <span className="rounded-full border border-teal-200 bg-white px-3 py-1.5 text-teal-800">Lector activo</span>
                  <span className="rounded-full border border-slate-200 bg-white px-3 py-1.5 text-slate-700">Buscar producto · F2</span>
                  <span className="rounded-full border border-slate-200 bg-white px-3 py-1.5 text-slate-700">
                    Próxima {nextNumber?.isAvailable ? nextNumber.fullNumber : "por calcular"}
                  </span>
                </div>
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
              <span className="rounded bg-black/10 px-2 py-0.5 text-xs font-semibold">F1</span>
            </button>

            <button
              type="button"
              disabled={!draft?.lines.length || busy}
              onClick={() => setTemporaryOpen(true)}
              className="mt-5 flex h-12 w-full items-center justify-center gap-2 rounded-xl border border-auraly-accent/40 bg-white/5 font-semibold transition hover:bg-white/10 disabled:cursor-not-allowed disabled:opacity-40"
            >
              <Save className="h-4 w-4" />
              Guardar temporal
              <span className="rounded bg-white/10 px-2 py-0.5 text-xs font-semibold">F4</span>
            </button>

            <div className="mt-2 grid grid-cols-2 gap-2">
              <button
                type="button"
                disabled={!selectedLineId || busy}
                onClick={() => selectedLineId && requestRemoveLine(selectedLineId)}
                className="flex h-11 items-center justify-center gap-2 rounded-xl border border-white/15 bg-white/5 text-sm font-semibold transition hover:bg-white/10 disabled:opacity-40"
              >
                <Trash2 className="h-4 w-4" />
                Producto
                <span className="rounded bg-white/10 px-1.5 py-0.5 text-xs">F5</span>
              </button>
              <button
                type="button"
                disabled={!draft?.lines.length || busy}
                onClick={requestCancelSale}
                className="flex h-11 items-center justify-center gap-2 rounded-xl border border-white/15 bg-white/5 text-sm font-semibold transition hover:bg-white/10 disabled:opacity-40"
              >
                Reiniciar
                <span className="rounded bg-white/10 px-1.5 py-0.5 text-xs">F6</span>
              </button>
            </div>
          </section>

          {lastSettlement ? (
            <section
              className="relative flex min-h-56 flex-1 overflow-hidden rounded-2xl border border-emerald-300 bg-emerald-50 p-5 shadow-sm"
              role="status"
              aria-live="assertive"
            >
              <div className="absolute -right-12 -top-12 h-40 w-40 rounded-full bg-emerald-200/45" />
              <div className="relative flex w-full flex-col justify-between">
                <div className="flex items-center gap-3">
                  <span className="grid h-12 w-12 place-items-center rounded-2xl bg-emerald-700 text-white shadow-sm">
                    <Banknote className="h-6 w-6" />
                  </span>
                  <div>
                    <p className="text-xs font-bold uppercase tracking-[0.16em] text-emerald-700">Entregar al cliente</p>
                    <p className="text-xs text-emerald-800">Venta {lastSettlement.documentNumber} completada</p>
                  </div>
                </div>
                <div className="py-5 text-center">
                  <p className="text-sm font-medium text-emerald-800">Cambio</p>
                  <p className="mt-1 text-5xl font-black tracking-tight tabular-nums text-emerald-950">
                    {money.format(lastSettlement.change)}
                  </p>
                  <p className="mt-3 text-sm text-emerald-800">
                    Recibido: <span className="font-bold tabular-nums">{money.format(lastSettlement.received)}</span>
                  </p>
                </div>
                <p className="text-center text-xs text-emerald-700">
                  Este aviso se ocultará al agregar el primer producto de la siguiente venta.
                </p>
              </div>
            </section>
          ) : (
          <section className="flex-1 rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
            <div
              role="tablist"
              aria-label="Documentos pendientes"
              className="mb-4 grid grid-cols-2 gap-1 rounded-xl bg-slate-100 p-1"
            >
              <button
                type="button"
                role="tab"
                aria-selected={sidePanel === "temporaries"}
                onClick={() => setSidePanel("temporaries")}
                className={`flex min-h-11 items-center justify-center gap-2 rounded-lg px-3 text-sm font-semibold transition ${
                  sidePanel === "temporaries"
                    ? "bg-white text-teal-800 shadow-sm"
                    : "text-slate-600 hover:text-slate-900"
                }`}
              >
                <Clock3 className="h-4 w-4" />
                Temporales
                <span className="rounded-full bg-teal-50 px-2 py-0.5 text-xs text-teal-800">
                  {temporaries.length}
                </span>
              </button>
              <button
                type="button"
                role="tab"
                aria-selected={sidePanel === "orders"}
                onClick={() => setSidePanel("orders")}
                className={`flex min-h-11 items-center justify-center gap-2 rounded-lg px-3 text-sm font-semibold transition ${
                  sidePanel === "orders"
                    ? "bg-white text-teal-800 shadow-sm"
                    : "text-slate-600 hover:text-slate-900"
                }`}
              >
                <ClipboardList className="h-4 w-4" />
                Pedidos
              </button>
            </div>

            {sidePanel === "temporaries" ? (
              <div>
                <div className="mb-3">
                  <p className="font-semibold text-slate-900">Ventas temporales</p>
                  <p className="text-xs text-slate-500">Pendientes de recuperar</p>
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
              </div>
            ) : (
              <div className="grid min-h-48 place-items-center rounded-xl border border-dashed border-slate-300 p-5 text-center">
                <div>
                  <span className="mx-auto grid h-12 w-12 place-items-center rounded-xl bg-teal-50 text-teal-700">
                    <ClipboardList className="h-6 w-6" />
                  </span>
                  <p className="mt-3 font-semibold text-slate-900">Pedidos</p>
                  <p className="mt-1 text-sm leading-5 text-slate-500">
                    {serverConnected
                      ? "Consulta los pedidos disponibles para recuperar o facturar."
                      : "Los pedidos se consultan en línea y Auraly Server no está disponible."}
                  </p>
                  {serverConnected && (
                    <button
                      type="button"
                      onClick={openOrders}
                      className="mt-4 h-10 rounded-lg bg-teal-700 px-4 text-sm font-semibold text-white transition hover:bg-teal-800"
                    >
                      Abrir pedidos
                    </button>
                  )}
                </div>
              </div>
            )}
          </section>
          )}
        </aside>
      </section>

      {productSearchOpen && client && (
        <PosProductSearchDialog
          busy={busy}
          onSearch={searchProducts}
          onSelect={selectSearchProduct}
          onCancel={() => {
            setProductSearchOpen(false);
            focusScanner();
          }}
        />
      )}

      {paymentOpen && draft && (
        <PosPaymentDialog
          total={draft.payableAmount}
          busy={busy}
          onCancel={() => {
            setPaymentOpen(false);
            focusScanner();
          }}
          onConfirm={completeSale}
        />
      )}

      {confirmation && (
        <PosConfirmDialog
          title={
            confirmation.kind === "line"
              ? "¿Eliminar este producto?"
              : "¿Reiniciar toda la venta?"
          }
          description={
            confirmation.kind === "line"
              ? `${confirmation.productName} se retirará de la venta actual.`
              : "Se eliminarán todos los productos capturados y se abrirá una venta limpia."
          }
          confirmLabel={confirmation.kind === "line" ? "Sí, eliminar" : "Sí, reiniciar"}
          busy={busy}
          onConfirm={confirmDestructiveAction}
          onCancel={() => {
            setConfirmation(null);
            focusScanner();
          }}
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
          Abre esta pantalla desde la aplicación instalada. Para desarrollo local puedes ingresar la sesión local generada por el host.
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
