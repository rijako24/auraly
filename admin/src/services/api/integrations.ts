import { apiClient } from "./client";
import type { IntegrationSettings } from "@/types/entities";

export interface UpdateGoogleCalendarIntegration {
  isEnabled: boolean;
  calendarId: string;
  timeZone: string;
  scopes?: string | null;
  clientId?: string | null;
  clientSecret?: string | null;
  refreshToken?: string | null;
}

export interface UpdateWompiIntegration {
  isEnabled: boolean;
  mode: "test" | "production";
  sandboxBaseUrl: string;
  productionBaseUrl: string;
  requestTimeoutSeconds: number;
  checkoutBaseUrl: string;
  privateKey?: string | null;
  publicKey?: string | null;
  eventsSecret?: string | null;
  integritySecret?: string | null;
}

export interface UpdateMantisIntegration {
  isEnabled: boolean;
  baseUrl: string;
  requestTimeoutSeconds: number;
  currency: string;
  authorizationToken?: string | null;
}

export interface UpdateXionIntegration {
  isEnabled: boolean;
  baseUrl: string;
  requestTimeoutSeconds: number;
  currency: string;
  sucursalId: number;
  vendedorId: number;
  equipoId: number;
  bodegaId: number;
  empresaId: number;
  centroDeCostoId: number;
  usuarioId: number;
  rutaId: number;
  validateStockOnCreate: boolean;
  orderHistoryDays: number;
}
export type OperationalMode = "test" | "production";

export interface UpdateOperationalMode {
  mode: OperationalMode;
}

export interface ProductIdentityRefreshResult {
  productsFound: number;
  productsChanged: number;
  completedAtUtc: string;
}

export interface MantisChannelWarehouse {
  businessWhatsAppNumberId: string;
  phoneNumber: string;
  whatsAppPhoneNumberId: string;
  warehouseCode: string | null;
  warehouseName: string | null;
  isActive: boolean;
}

export const integrationsApi = {
  getSettings: (businessId: string) =>
    apiClient.get<IntegrationSettings>(`/businesses/${businessId}/integrations`),
  updateGoogleCalendar: (
    businessId: string,
    data: UpdateGoogleCalendarIntegration
  ) =>
    apiClient.put<IntegrationSettings>(
      `/businesses/${businessId}/integrations/google-calendar`,
      data
    ),
  updateWompi: (businessId: string, data: UpdateWompiIntegration) =>
    apiClient.put<IntegrationSettings>(
      `/businesses/${businessId}/integrations/wompi`,
      data
    ),
  updateMantis: (businessId: string, data: UpdateMantisIntegration) =>
    apiClient.put<IntegrationSettings>(
      `/businesses/${businessId}/integrations/commerce/mantis`,
      data
    ),
  updateXion: (businessId: string, data: UpdateXionIntegration) =>
    apiClient.put<IntegrationSettings>(
      `/businesses/${businessId}/integrations/commerce/xion`,
      data
    ),
  updateOperationalMode: (businessId: string, data: UpdateOperationalMode) =>
    apiClient.put<IntegrationSettings>(
      `/businesses/${businessId}/integrations/operational-mode`,
      data
    ),
  refreshMantisProduct: (businessId: string, query: string) =>
    apiClient.post<ProductIdentityRefreshResult>(
      "/businesses/" + businessId + "/products/catalog/refresh-product",
      { query }
    ),
  getMantisWarehouses: (businessId: string) =>
    apiClient.get<MantisChannelWarehouse[]>(
      `/businesses/${businessId}/integrations/commerce/mantis/warehouses`
    ),
  updateMantisWarehouses: (
    businessId: string,
    channels: MantisChannelWarehouse[]
  ) =>
    apiClient.put<MantisChannelWarehouse[]>(
      `/businesses/${businessId}/integrations/commerce/mantis/warehouses`,
      { channels }
    ),
};
