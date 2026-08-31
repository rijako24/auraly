import { apiClient } from "./client";

export interface FiscalDocumentQuotaItem {
  documentId: string; sourceDocumentType: string; fiscalDocumentType: string;
  auralyNumber: string; dianNumber: string; status: string; issuedAt: string;
  updatedAt: string; quotaBlockedAt: string | null; lastStatusDescription: string | null;
}
export interface FiscalDocumentQuotaPage {
  items: FiscalDocumentQuotaItem[]; page: number; pageSize: number; totalCount: number;
}

export const fiscalDocumentsApi = {
  quotaHistory: (page: number, pageSize: number, status?: string) =>
    apiClient.get<FiscalDocumentQuotaPage>("/commerce/v1/fiscal/documents", {
      page, pageSize, quotaOnly: true, status: status || undefined,
    }),
};
