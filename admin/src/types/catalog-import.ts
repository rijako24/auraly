export interface CatalogImportServiceLine {
  serviceName: string;
  description?: string | null;
  durationMinutes: number;
  price: number;
  categoryName: string;
  serviceType: string;
  tier: string;
  selected: boolean;
}

export interface CatalogImportDraft {
  sourceFileName?: string | null;
  extractedTextPreview?: string | null;
  services: CatalogImportServiceLine[];
}

export interface CatalogImportResult {
  categoriesCreated: number;
  servicesCreated: number;
  servicesSkipped: number;
  warnings: string[];
}
