import type { PosSaleDocumentType } from "./pos-edge-client";

export const fiscalConfigurationRequiredMessage =
  "La facturación electrónica no está configurada para esta sede. Ve a Configuración fiscal para activarla. Mientras tanto, solo puedes emitir comprobantes de venta.";

type FiscalReadiness = {
  isReadyForOnlineSales: boolean;
};

export function fiscalLaunchReadinessError(
  mode: "online" | "enroll",
  readiness: FiscalReadiness,
): string | null {
  if (mode === "enroll") return null;
  return readiness.isReadyForOnlineSales
    ? null
    : fiscalConfigurationRequiredMessage;
}

export function canIssuePosDocument(
  documentType: PosSaleDocumentType,
  fiscalReady: boolean,
): boolean {
  return documentType === "SalesReceipt" || fiscalReady;
}
