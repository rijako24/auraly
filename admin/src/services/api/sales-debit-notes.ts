import { apiClient, withPagedDefaults } from "./client";

export interface SalesDebitNoteListItem {
  debitNoteId: string;
  originalDocumentId: string;
  documentNumber: string;
  originalDocumentNumber: string;
  customerName: string;
  customerIdentification: string;
  issuedAt: string;
  conceptCode: "1" | "2" | "3" | "4";
  reasonDescription: string;
  totalAmount: number;
  status: string;
  fiscalStatus: string;
  cude: string | null;
}

export interface SalesDebitNoteDetail {
  header: SalesDebitNoteListItem;
  dueAt: string;
  untaxedAmount: number;
  taxAmount: number;
  notes: string | null;
  lines: Array<{
    lineNumber: number;
    description: string;
    quantity: number;
    unitPrice: number;
    taxCode: string;
    taxRate: number;
    untaxedAmount: number;
    taxAmount: number;
    lineTotal: number;
  }>;
}

export interface SalesDebitNotePage {
  items: SalesDebitNoteListItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface ConfirmSalesDebitNoteRequest {
  debitNoteId: string;
  businessId: string;
  originalDocumentId: string;
  issuedAt: string;
  dueAt: string;
  conceptCode: "1" | "2" | "3" | "4";
  reasonDescription: string;
  notes: string | null;
  lines: Array<{
    description: string;
    quantity: number;
    unitPrice: number;
    taxCode: string;
    taxRate: number;
  }>;
}

export const salesDebitNotesApi = {
  list: (params: { page?: number; pageSize?: number; search?: string; from?: string; to?: string }) =>
    apiClient.get<SalesDebitNotePage>("/commerce/v1/sales-debit-notes", withPagedDefaults(params)),
  get: (id: string) => apiClient.get<SalesDebitNoteDetail>(`/commerce/v1/sales-debit-notes/${id}`),
  confirm: (request: ConfirmSalesDebitNoteRequest) =>
    apiClient.postIdempotent<{ debitNoteId: string; documentNumber: string; status: string }>(
      "/commerce/v1/sales-debit-notes/confirm",
      request,
      `sales-debit-note-${request.debitNoteId}`,
    ),
};
