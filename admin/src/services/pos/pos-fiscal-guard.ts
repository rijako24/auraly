import type { PosSaleDocumentType } from "./pos-edge-client";

export const fiscalConfigurationRequiredMessage =
  "La facturación electrónica no está configurada para esta sede. Ve a Configuración fiscal para activarla. Mientras tanto, solo puedes emitir comprobantes de venta.";
export const dianQuotaExhaustedMessage =
  "No hay cupo de documentos DIAN. La venta no se cambiará automáticamente: selecciona comprobante de venta o compra un paquete antes de emitir una factura electrónica.";

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
): string | null {
  if (documentType === "SalesReceipt") return null;
  if (!fiscalReady) return fiscalConfigurationRequiredMessage;
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
