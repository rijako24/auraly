import type { PurchaseEvidencePolicy } from "@/services/api/parties";

export function allowedPurchaseEvidenceTypes(policy: PurchaseEvidencePolicy | null) {
  if (policy === "SupplierElectronicInvoice") return ["SupplierElectronicInvoice", "InternalReceiptVoucher", "ForeignCommercialInvoice"];
  if (policy === "BuyerElectronicSupportDocument") return ["BuyerElectronicSupportDocument", "InternalReceiptVoucher", "ForeignCommercialInvoice"];
  if (policy === "InternalReceiptVoucher") return ["InternalReceiptVoucher", "ForeignCommercialInvoice"];
  return ["SupplierElectronicInvoice", "BuyerElectronicSupportDocument", "InternalReceiptVoucher", "ForeignCommercialInvoice"];
}
