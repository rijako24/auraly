import { businessesApi } from "@/services/api/businesses";
import { apiClient } from "@/services/api/client";
import { tenantsApi } from "@/services/api/tenants";
import { printPosHtmlDocument } from "./pos-browser-print";
import type { PosEdgeClient } from "./pos-edge-client";

export type PortfolioPaymentReceipt = {
  paymentId: string;
  direction: "Receivable" | "Payable";
  documentNumber: string;
  paidAt: string;
  companyName: string;
  legalName: string | null;
  nit: string | null;
  verificationDigit: string | null;
  companyLogoSource: string | null;
  businessName: string;
  businessAddress: string | null;
  businessPhone: string | null;
  partyName: string;
  partyIdentification: string;
  responsibleName: string;
  totalAmount: number;
  allocations: Array<{ documentNumber: string; amount: number }>;
  payments: Array<{ methodName: string; amount: number; reference: string | null }>;
};

export async function printPortfolioPayment(
  receipt: PortfolioPaymentReceipt,
  businessId: string,
  printerClient: Pick<PosEdgeClient, "printPortfolioPayment"> | null,
): Promise<void> {
  if (printerClient) {
    await printerClient.printPortfolioPayment(receipt);
    return;
  }
  const [branding, business] = await Promise.all([
    tenantsApi.getBranding(),
    businessesApi.getById(businessId),
  ]);
  const complete = {
    ...receipt,
    companyName: branding.displayName || branding.legalName || receipt.companyName,
    legalName: branding.legalName,
    nit: branding.nit,
    verificationDigit: branding.verificationDigit,
    companyLogoSource: branding.logoUrl,
    businessName: business.name,
    businessAddress: business.address,
    businessPhone: business.phone,
  };
  const { html } = await apiClient.post<{ html: string }>(
    "/commerce/v1/pos/drafts/portfolio-payments/receipt/render",
    { receipt: complete, paperWidthMillimeters: 80 },
  );
  await printPosHtmlDocument(html, "El pago quedó registrado, pero no se pudo abrir la impresión.");
}
