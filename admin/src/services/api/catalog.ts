import { apiClient } from "./client";
import type {
  CatalogImportDraft,
  CatalogImportResult,
  CatalogImportServiceLine,
} from "@/types/catalog-import";

export const catalogApi = {
  extractFromFile: async (businessId: string, file: File): Promise<CatalogImportDraft> => {
    const formData = new FormData();
    formData.append("file", file);

    const res = await fetch(`/api/businesses/${businessId}/catalog/extract`, {
      method: "POST",
      credentials: "include",
      headers: { "X-Business-Id": businessId },
      body: formData,
    });

    if (!res.ok) {
      const err = await res.json().catch(() => ({ message: res.statusText }));
      throw new Error(err.message || "Error al extraer catálogo");
    }

    return res.json();
  },

  confirmImport: (businessId: string, services: CatalogImportServiceLine[]) =>
    apiClient.post<CatalogImportResult>(`/businesses/${businessId}/catalog/import`, {
      services,
      skipExistingByName: true,
    }),
};
