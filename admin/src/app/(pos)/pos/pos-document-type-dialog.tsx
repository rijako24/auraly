"use client";

import { Check, FileText, Loader2, Receipt, X } from "lucide-react";
import Link from "next/link";
import { useEffect, useState } from "react";

import {
  fiscalConfigurationApi,
  type FiscalResolutionConfiguration,
} from "@/services/api/fiscal-configuration";
import type { PosClient, PosSaleDocumentType } from "@/services/pos/pos-edge-client";
import { useAuthStore } from "@/stores/auth-store";
import { usePosReferenceOptions } from "./use-pos-reference-options";

const documentVisuals: Record<PosSaleDocumentType, { icon: typeof FileText }> = {
  SalesInvoice: { icon: FileText },
  SalesReceipt: { icon: Receipt },
};


export function PosDocumentTypeDialog({
  client,
  value,
  invoiceRequired = false,
  businessId,
  edgeMode,
  edgeFiscalReady,
  busy,
  onFiscalEnrollmentRequired,
  onSelect,
  onCancel,
}: {
  client: PosClient;
  value: PosSaleDocumentType;
  invoiceRequired?: boolean;
  businessId: string;
  edgeMode: boolean;
  edgeFiscalReady: boolean;
  busy: boolean;
  onFiscalEnrollmentRequired: () => void;
  onSelect: (value: PosSaleDocumentType) => Promise<void>;
  onCancel: () => void;
}) {
  const canManageFiscal = useAuthStore(state =>
    state.user?.permissions.includes("fiscal.configuration.manage") ?? false,
  );
  const documentTypes = usePosReferenceOptions(client, "sales-document-type");
  const options = (documentTypes.data ?? []).flatMap((option) => {
    if (option.code !== "SalesInvoice" && option.code !== "SalesReceipt") return [];
    const documentType = option.code as PosSaleDocumentType;
    return [{ value: documentType, title: option.label, description: option.description ?? "", ...documentVisuals[documentType] }];
  });
  const [configuration, setConfiguration] =
    useState<FiscalResolutionConfiguration | null>(null);
  const [configuringInvoice, setConfiguringInvoice] = useState(false);
  const [checkingFiscal, setCheckingFiscal] = useState(false);
  const [fiscalError, setFiscalError] = useState<string | null>(null);

  useEffect(() => {
    const handleEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape" && !busy) {
        event.preventDefault();
        onCancel();
      }
    };

    window.addEventListener("keydown", handleEscape);
    return () => window.removeEventListener("keydown", handleEscape);
  }, [busy, onCancel]);

  async function loadFiscalState() {
    const nextConfiguration = await fiscalConfigurationApi.get(businessId);
    setConfiguration(nextConfiguration);
    return nextConfiguration;
  }

  async function selectDocument(next: PosSaleDocumentType) {
    setFiscalError(null);
    if (next === "SalesReceipt") {
      await onSelect(next);
      return;
    }
    if (!businessId) {
      setFiscalError("No se pudo identificar la sede de esta caja.");
      return;
    }
    setCheckingFiscal(true);
    try {
      if (edgeMode) {
        if (edgeFiscalReady) await onSelect(next);
        else onFiscalEnrollmentRequired();
        return;
      }
      const state = await loadFiscalState();
      if (!state.isReadyForOnlineSales) {
        if (!canManageFiscal) {
          setFiscalError("La factura electrónica no está lista en esta sede. Solicita a un usuario con permiso de configuración fiscal que complete la resolución; esta venta permanecerá guardada.");
          return;
        }
        setConfiguringInvoice(true);
        return;
      }
      await onSelect(next);
    } catch (caught) {
      setFiscalError(
        !canManageFiscal
          ? "No tienes permiso para consultar o configurar la resolución fiscal. Solicita apoyo a un administrador; esta venta permanecerá guardada."
          : caught instanceof Error
          ? caught.message
          : "No fue posible verificar la configuración fiscal.",
      );
      setConfiguringInvoice(canManageFiscal);
    } finally {
      setCheckingFiscal(false);
    }
  }

  return (
    <div
      className="fixed inset-0 z-[60] grid place-items-center bg-slate-950/65 p-4"
      onMouseDown={(event) => {
        if (event.currentTarget === event.target && !busy) onCancel();
      }}
    >
      <section
        role="dialog"
        aria-modal="true"
        aria-labelledby="pos-document-type-title"
        aria-describedby="pos-document-type-description"
        className="w-full max-w-3xl overflow-hidden rounded-3xl bg-white shadow-2xl"
      >
        <header className="flex items-start justify-between gap-4 border-b border-slate-200 px-6 py-5">
          <div>
            <p className="text-xs font-bold uppercase tracking-[0.18em] text-teal-700">
              Nueva venta
            </p>
            <h2 id="pos-document-type-title" className="mt-1 text-xl font-bold text-slate-950">
              Tipo de documento
            </h2>
            <p id="pos-document-type-description" className="mt-1 text-sm text-slate-600">
              {invoiceRequired
                ? "El cliente requiere factura electrónica. Verifica la configuración fiscal para continuar."
                : "Elige el documento que emitirá esta venta."}
            </p>
          </div>
          <button
            type="button"
            onClick={onCancel}
            disabled={busy || checkingFiscal}
            className="grid h-10 w-10 shrink-0 place-items-center rounded-xl text-slate-500 outline-none transition hover:bg-slate-100 hover:text-slate-900 focus:ring-2 focus:ring-teal-500/30 disabled:opacity-50"
            aria-label="Cerrar"
          >
            <X className="h-5 w-5" />
          </button>
        </header>

        <div className="grid gap-3 p-6 sm:grid-cols-2">
          {options.filter(option => !invoiceRequired || option.value === "SalesInvoice").map((option) => {
            const selected = option.value === value;
            const Icon = option.icon;
            return (
              <button
                key={option.value}
                autoFocus={selected}
                type="button"
                disabled={busy || checkingFiscal}
                onClick={() => void selectDocument(option.value)}
                className={`relative min-h-40 rounded-2xl border-2 p-5 text-left outline-none transition focus:ring-4 focus:ring-teal-500/20 disabled:cursor-wait disabled:opacity-60 ${
                  selected
                    ? "border-teal-500 bg-teal-50 shadow-sm"
                    : "border-slate-200 bg-white hover:border-teal-300 hover:bg-slate-50"
                }`}
              >
                <span
                  className={`grid h-11 w-11 place-items-center rounded-xl ${
                    selected ? "bg-teal-600 text-white" : "bg-slate-100 text-slate-700"
                  }`}
                >
                  {(busy || checkingFiscal) && option.value === "SalesInvoice" ? (
                    <Loader2 className="h-5 w-5 animate-spin" />
                  ) : (
                    <Icon className="h-5 w-5" />
                  )}
                </span>
                <span className="mt-4 block font-bold text-slate-950">{option.title}</span>
                <span className="mt-1 block text-sm leading-5 text-slate-600">
                  {option.description}
                </span>
                {selected && (
                  <span className="absolute right-4 top-4 grid h-7 w-7 place-items-center rounded-full bg-teal-600 text-white">
                    <Check className="h-4 w-4" />
                  </span>
                )}
              </button>
            );
          })}
        </div>
        {documentTypes.isLoading && (
          <p className="mx-6 mb-6 text-sm text-slate-500">Cargando tipos de documento...</p>
        )}
        {documentTypes.isError && (
          <p role="alert" className="mx-6 mb-6 rounded-xl bg-red-50 p-3 text-sm text-red-700">
            No fue posible cargar los tipos de documento. Cierra e intenta nuevamente.
          </p>
        )}
        {fiscalError && !configuringInvoice && (
          <p className="mx-6 mb-6 rounded-xl bg-red-50 p-3 text-sm text-red-700">
            {fiscalError}
          </p>
        )}
        {configuringInvoice && (
          <div className="max-h-[55vh] space-y-4 overflow-y-auto border-t border-slate-200 bg-slate-50 p-6">
            <div>
              <h3 className="font-bold text-slate-950">Configurar factura electrónica</h3>
              <p className="mt-1 text-sm text-slate-600">
                El POS no puede cargar certificados, cambiar el emisor ni asignar resoluciones.
                Un administrador debe completar la activación DIAN para esta sede desde Configuración fiscal.
              </p>
            </div>
            {configuration?.authorizationNumber && <p className="rounded-xl bg-white p-3 text-sm text-slate-700">Resolución detectada: {configuration.authorizationNumber}. Aún no cumple todos los requisitos para este modo de venta.</p>}
            {fiscalError && (
              <p className="rounded-xl bg-red-50 p-3 text-sm text-red-700">{fiscalError}</p>
            )}
            <Link href="/dashboard/settings/fiscal" className="flex h-12 w-full items-center justify-center rounded-xl border border-teal-600 font-bold text-teal-700">
              Abrir activación fiscal
            </Link>
            <button
              type="button"
              disabled={busy || checkingFiscal}
              onClick={() => void selectDocument("SalesInvoice")}
              className="h-12 w-full rounded-xl bg-teal-600 font-bold text-white disabled:opacity-50"
            >
              {checkingFiscal
                ? "Verificando..."
                : edgeMode && !edgeFiscalReady
                  ? "Preparar serie fiscal en este equipo"
                  : "Volver a verificar"}
            </button>
          </div>
        )}
      </section>
    </div>
  );
}
