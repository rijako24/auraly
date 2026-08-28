import { apiClient } from "./client";
import { isFiscalStatusSynchronizationMessage } from "./fiscal-onboarding-events";

export type FiscalResolutionConfiguration = {
  businessId: string;
  fiscalAuthorizationId: string | null;
  authorizationNumber: string | null;
  validFrom: string | null;
  validUntil: string | null;
  prefix: string | null;
  rangeStart: number | null;
  rangeEnd: number | null;
  hasActiveAuthorization: boolean;
  isReadyForOnlineSales: boolean;
  isReadyForEnrollment: boolean;
};

export type FiscalDeviceSeriesAssignment = {
  deviceId: string; deviceName: string; deviceIsActive: boolean;
  lastSeenAt: string | null; businessId: string; businessName: string;
  seriesId: string | null; prefix: string | null; rangeStart: number | null;
  rangeEnd: number | null; isProvisioned: boolean;
};

export type FiscalDeviceSeriesWorkspace = {
  businessId: string;
  availableConsecutives: number;
  devices: FiscalDeviceSeriesAssignment[];
};

export type DianNumberingRangeOption = {
  dianNumberingRangeId: string;
  authorizationNumber: string;
  resolutionDate: string | null;
  prefix: string;
  rangeStart: number;
  rangeEnd: number;
  validFrom: string;
  validUntil: string;
  isAvailable: boolean;
  assignedBusinessId: string | null;
  assignedBusinessName: string | null;
};

export type FiscalOnboardingConfiguration = {
  businessId: string;
  businessName: string;
  legalName: string;
  supplierTaxId: string;
  supplierCheckDigit: string;
  stage: "NotConfigured" | "HabilitationReady" | "HabilitationAccepted" | "ProductionReady" | "ProductionActive";
  softwareIdentificationCode: string | null;
  testSetId: string | null;
  hasCertificate: boolean;
  certificateThumbprintSuffix: string | null;
  certificateValidFrom: string | null;
  certificateValidTo: string | null;
  habilitationAccepted: boolean;
  habilitationAcceptedAt: string | null;
  productionActive: boolean;
  assignedRange: DianNumberingRangeOption | null;
  availableRanges: DianNumberingRangeOption[];
  missingRequirements: string[];
  latestHabilitationAttempt: FiscalHabilitationAttempt | null;
  assignedSupportDocumentRange: DianNumberingRangeOption | null;
};

export type FiscalHabilitationAttempt = {
  documentId: string;
  status: string;
  isTerminalFailure: boolean;
  errorCode: string | null;
  errorMessage: string | null;
  updatedAt: string;
};

type FiscalSynchronizationNegotiation = {
  clientAccessUri: string;
  expiresAt: string;
};

export type SaveDianHabilitationConfiguration = {
  softwareIdentificationCode: string;
  softwarePin: string;
  testSetId: string;
  certificatePassword: string;
  certificate: File;
};

export const fiscalConfigurationApi = {
  getOnboarding: (businessId: string) =>
    apiClient.get<FiscalOnboardingConfiguration>(
      "/commerce/v1/fiscal/configuration/onboarding",
      { businessId },
    ),
  subscribeToOnboarding: (
    businessId: string,
    onStatusChanged: () => void,
  ) => {
    let stopped = false;
    let socket: WebSocket | null = null;
    let reconnectTimer: number | null = null;
    const connect = async () => {
      try {
        const negotiation = await apiClient.post<FiscalSynchronizationNegotiation>(
          `/commerce/v1/fiscal/configuration/onboarding/synchronization/negotiate?businessId=${encodeURIComponent(businessId)}`,
        );
        if (stopped) return;
        const current = new WebSocket(
          negotiation.clientAccessUri,
          "json.webpubsub.azure.v1",
        );
        socket = current;
        current.addEventListener("message", (event: MessageEvent<string>) => {
          if (isFiscalStatusSynchronizationMessage(event.data)) onStatusChanged();
        });
        current.addEventListener("close", () => {
          if (stopped || socket !== current) return;
          reconnectTimer = window.setTimeout(() => void connect(), 1_000);
        });
      } catch {
        socket?.close();
        socket = null;
        if (!stopped)
          reconnectTimer = window.setTimeout(() => void connect(), 2_000);
      }
    };
    void connect();
    return () => {
      stopped = true;
      if (reconnectTimer !== null) window.clearTimeout(reconnectTimer);
      socket?.close();
    };
  },
  configureHabilitation: (
    businessId: string,
    request: SaveDianHabilitationConfiguration,
  ) => {
    const body = new FormData();
    body.append("softwareIdentificationCode", request.softwareIdentificationCode);
    body.append("softwarePin", request.softwarePin);
    body.append("testSetId", request.testSetId);
    body.append("certificatePassword", request.certificatePassword);
    body.append("certificate", request.certificate);
    return apiClient.postForm<FiscalOnboardingConfiguration>(
      `/commerce/v1/fiscal/configuration/onboarding/habilitation?businessId=${encodeURIComponent(businessId)}`,
      body,
    );
  },
  synchronizeNumberingRanges: (businessId: string) =>
    apiClient.post<FiscalOnboardingConfiguration>(
      `/commerce/v1/fiscal/configuration/onboarding/numbering-ranges/synchronize?businessId=${encodeURIComponent(businessId)}`,
    ),
  activateProduction: (businessId: string, dianNumberingRangeId: string) =>
    apiClient.post<FiscalOnboardingConfiguration>(
      `/commerce/v1/fiscal/configuration/onboarding/activate-production?businessId=${encodeURIComponent(businessId)}`,
      { dianNumberingRangeId },
    ),
  activateSupportDocument: (businessId: string, dianNumberingRangeId: string) =>
    apiClient.post<FiscalOnboardingConfiguration>(
      `/commerce/v1/fiscal/configuration/onboarding/activate-support-document?businessId=${encodeURIComponent(businessId)}`,
      { dianNumberingRangeId },
    ),
  get: (businessId: string) =>
    apiClient.get<FiscalResolutionConfiguration>(
      "/commerce/v1/fiscal/configuration",
      { businessId },
    ),
  getDevices: (businessId: string) =>
    apiClient.get<FiscalDeviceSeriesWorkspace>(
      "/commerce/v1/fiscal/configuration/devices", { businessId }),
  assignDeviceSeries: (businessId: string, deviceId: string) =>
    apiClient.post<FiscalDeviceSeriesWorkspace>(
      `/commerce/v1/fiscal/configuration/devices/assign?businessId=${encodeURIComponent(businessId)}`,
      { deviceId },
    ),
};
