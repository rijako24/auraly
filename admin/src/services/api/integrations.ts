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

export type OperationalMode = "test" | "production";

export interface UpdateOperationalMode {
  mode: OperationalMode;
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
  updateOperationalMode: (businessId: string, data: UpdateOperationalMode) =>
    apiClient.put<IntegrationSettings>(
      `/businesses/${businessId}/integrations/operational-mode`,
      data
    ),
};
