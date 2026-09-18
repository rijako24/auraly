import type {
  PosIssuedSaleFilters,
  PosIssuedSaleSearchPage,
  PosSaleDocumentType,
  PosServerHistoryScope,
} from "./pos-edge-client";

export type ServerIssuedSalePage = {
  items: Array<{
    documentId: string;
    documentType: PosSaleDocumentType;
    documentNumber: string;
    fiscalNumber: string | null;
    issuedAt: string;
    total: number;
    customerIdentification: string;
    customerName: string;
    fiscalStatus: string | null;
  }>;
  hasMore: boolean;
  nextOffset: number | null;
};

export function buildServerIssuedSalesSearchRequest(
  context: PosServerHistoryScope,
  filters: PosIssuedSaleFilters,
  skip: number,
  take: number,
) {
  return {
    context,
    ...filters,
    from: filters.from || null,
    to: filters.to || null,
    skip,
    take,
  };
}

export function mapServerIssuedSalesPage(
  page: ServerIssuedSalePage,
): PosIssuedSaleSearchPage {
  return {
    items: page.items.map((sale) => ({
      ...sale,
      documentId: { value: sale.documentId },
    })),
    hasMore: page.hasMore,
    nextOffset: page.nextOffset,
  };
}
