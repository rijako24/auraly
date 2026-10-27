import type { PosSaleDocumentType } from "./pos-edge-client";

export const fiscalConfigurationRequiredMessage =
  "La facturación electrónica no está configurada para esta sede. Ve a Configuración fiscal para activarla. Mientras tanto, solo puedes emitir comprobantes de venta.";
export const localFiscalSeriesRequiredMessage =
  "La facturación electrónica local no está lista en esta caja. Revisa su configuración fiscal y asigna o renueva una resolución DIAN para este equipo. Las facturas desde pedidos usan la resolución online de la sede.";
export const dianQuotaExhaustedMessage =
  "No tienes documentos DIAN disponibles. Puedes seguir registrando comprobantes de venta y entradas de mercancía. Renueva o amplía tu cupo para emitir documentos electrónicos. Puedes volver y elegir un comprobante no electrónico.";

type FiscalReadiness = {
  isReadyForOnlineSales: boolean;
  hasDianDocumentQuota?: boolean;
};

export function fiscalLaunchReadinessError(
  mode: "online" | "enroll",
  readiness: FiscalReadiness,
): string | null {
  if (mode === "enroll") return null;
  return posDocumentReadinessError(
    "SalesInvoice",
    readiness.isReadyForOnlineSales,
    readiness.hasDianDocumentQuota !== false,
  );
}

export function posDocumentReadinessError(
  documentType: PosSaleDocumentType,
  fiscalReady: boolean,
  dianQuotaAvailable = true,
  mode: "online" | "edge" = "online",
): string | null {
  if (documentType === "SalesReceipt") return null;
  if (!fiscalReady) return mode === "edge"
    ? localFiscalSeriesRequiredMessage
    : fiscalConfigurationRequiredMessage;
  return dianQuotaAvailable ? null : dianQuotaExhaustedMessage;
}

export function canIssuePosDocument(
  documentType: PosSaleDocumentType,
  fiscalReady: boolean,
  dianQuotaAvailable = true,
): boolean {
  return posDocumentReadinessError(
    documentType,
    fiscalReady,
    dianQuotaAvailable,
  ) === null;
}
