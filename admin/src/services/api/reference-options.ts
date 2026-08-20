import { apiClient } from "./client";

export interface ReferenceOption {
  id: string;
  code: string;
  label: string;
  description: string | null;
  sortOrder: number;
}

export const referenceOptionsApi = {
  list: (catalogCode: string) =>
    apiClient.get<ReferenceOption[]>(`/commerce/v1/reference-options/${encodeURIComponent(catalogCode)}`),
};
