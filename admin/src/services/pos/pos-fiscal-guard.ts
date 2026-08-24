import type { PosSaleDocumentType } from "./pos-edge-client";

export const fiscalConfigurationRequiredMessage =
  "La facturación electrónica no está configurada para esta sede. Ve a Configuración fiscal para activarla. Mientras tanto, solo puedes emitir comprobantes de venta.";

export function canIssuePosDocument(
  documentType: PosSaleDocumentType,
  fiscalReady: boolean,
): boolean {
  return documentType === "SalesReceipt" || fiscalReady;
}
