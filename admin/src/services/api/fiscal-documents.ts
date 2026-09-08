import { apiClient } from "./client";

export interface FiscalDocumentQuotaItem {
  documentId: string; sourceDocumentType: string; fiscalDocumentType: string;
  auralyNumber: string; dianNumber: string; status: string; issuedAt: string;
  updatedAt: string; quotaBlockedAt: string | null; lastStatusDescription: string | null;
}
export interface FiscalDocumentQuotaPage {
  items: FiscalDocumentQuotaItem[]; page: number; pageSize: number; totalCount: number;
}
export interface FiscalDocumentView extends FiscalDocumentQuotaItem {
  businessId:string;uniqueCodeType:string;uniqueCode:string|null;deviceId:string|null;
  attemptCount:number;trackId:string|null;lastStatusCode:string|null;
}

export const fiscalDocumentsApi = {
  get: (documentId:string) => apiClient.get<FiscalDocumentView>(`/commerce/v1/fiscal/documents/${documentId}`),
  quotaHistory: (page: number, pageSize: number, status?: string) =>
    apiClient.get<FiscalDocumentQuotaPage>("/commerce/v1/fiscal/documents", {
      page, pageSize, quotaOnly: true, status: status || undefined,
    }),
};
