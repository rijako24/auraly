import { apiClient } from "./client";
import { isFiscalStatusSynchronizationMessage } from "./fiscal-onboarding-events";
import {
  realtimeReconnectDelay,
  wasRealtimeConnectionStable,
} from "@/lib/realtime-reconnect-policy";

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
  nextConsecutive: number | null;
  remainingConsecutives: number | null;
  expirationWarningDays: number;
  remainingNumberWarningThreshold: number;
  warningMessages: string[] | null;
  hasDianDocumentQuota: boolean;
};

export type FiscalOnlineSeriesAssignment = {
  seriesId: string; fiscalAuthorizationId: string; authorizationNumber: string;
  prefix: string; rangeStart: number; rangeEnd: number; nextConsecutive: number;
  remainingConsecutives: number; validFrom: string; validUntil: string;
};

export type FiscalDeviceSeriesAssignment = {
  deviceId: string; deviceName: string; deviceIsActive: boolean;
  lastSeenAt: string | null; businessId: string; businessName: string;
  seriesId: string | null; fiscalAuthorizationId: string | null;
  authorizationNumber: string | null; prefix: string | null; rangeStart: number | null;
  rangeEnd: number | null; isProvisioned: boolean;
};

export type FiscalAssignableResolution = {
  dianNumberingRangeId: string; authorizationNumber: string; prefix: string;
  rangeStart: number; rangeEnd: number; validFrom: string; validUntil: string;
};

export type FiscalDeviceSeriesWorkspace = {
  businessId: string;
  availableConsecutives: number;
  availableResolutions: FiscalAssignableResolution[];
  devices: FiscalDeviceSeriesAssignment[];
  onlineAssignment: FiscalOnlineSeriesAssignment | null;
  expirationWarningDays: number;
  remainingNumberWarningThreshold: number;
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

export type SupportDocumentNumberingConfiguration = {
  authorizationNumber: string;
  resolutionDate: string | null;
  prefix: string;
  rangeStart: number;
  rangeEnd: number;
  validFrom: string;
  validUntil: string;
};

export type SaveSupportDocumentSoftwareConfiguration = {
  softwareIdentificationCode: string;
  softwarePin: string;
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
  assignedSupportDocumentRange: SupportDocumentNumberingConfiguration | null;
  supportDocumentSoftwareIdentificationCode: string | null;
  hasSupportDocumentSoftwarePin: boolean;
  availableSupportDocumentRanges: DianNumberingRangeOption[];
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
    let connecting = false;
    let socket: WebSocket | null = null;
    let reconnectTimer: number | null = null;
    let failedAttempts = 0;
    let openedAt: number | null = null;
    const scheduleReconnect = () => {
      if (reconnectTimer !== null) return;
      const delay = realtimeReconnectDelay(failedAttempts);
      failedAttempts += 1;
      if (stopped || delay === null) return;
      reconnectTimer = window.setTimeout(() => {
        reconnectTimer = null;
        void connect();
      }, delay);
    };
    const connect = async () => {
      if (stopped || connecting || socket) return;
      connecting = true;
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
        current.addEventListener("open", () => {
          if (!stopped && socket === current) openedAt = Date.now();
        });
        current.addEventListener("message", (event: MessageEvent<string>) => {
          if (isFiscalStatusSynchronizationMessage(event.data)) onStatusChanged();
        });
        current.addEventListener("close", () => {
          if (stopped || socket !== current) return;
          socket = null;
          if (wasRealtimeConnectionStable(openedAt, Date.now())) failedAttempts = 0;
          openedAt = null;
          scheduleReconnect();
        });
      } catch {
        socket?.close();
        socket = null;
        if (!stopped) scheduleReconnect();
      } finally {
        connecting = false;
      }
    };
    const restartConnection = () => {
      if (document.visibilityState !== "visible" || socket) return;
      failedAttempts = 0;
      void connect();
    };
    window.addEventListener("online", restartConnection);
    document.addEventListener("visibilitychange", restartConnection);
    void connect();
    return () => {
      stopped = true;
      window.removeEventListener("online", restartConnection);
      document.removeEventListener("visibilitychange", restartConnection);
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
  assignOnlineResolution: (businessId: string, dianNumberingRangeId: string) =>
    apiClient.post<FiscalOnboardingConfiguration>(
      `/commerce/v1/fiscal/configuration/onboarding/assign-online-resolution?businessId=${encodeURIComponent(businessId)}`,
      { dianNumberingRangeId },
    ),
  activateProduction: (businessId: string) =>
    apiClient.post<FiscalOnboardingConfiguration>(
      `/commerce/v1/fiscal/configuration/onboarding/activate-production?businessId=${encodeURIComponent(businessId)}`,
    ),
  configureSupportDocumentSoftware: (
    businessId: string,
    request: SaveSupportDocumentSoftwareConfiguration,
  ) =>
    apiClient.post<FiscalOnboardingConfiguration>(
      `/commerce/v1/fiscal/configuration/onboarding/support-document/software?businessId=${encodeURIComponent(businessId)}`,
      request,
    ),
  synchronizeSupportDocumentNumberingRanges: (businessId: string) =>
    apiClient.post<FiscalOnboardingConfiguration>(
      `/commerce/v1/fiscal/configuration/onboarding/support-document/numbering-ranges/synchronize?businessId=${encodeURIComponent(businessId)}`,
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
  assignDeviceSeries: (businessId: string, deviceId: string, dianNumberingRangeId: string) =>
    apiClient.post<FiscalDeviceSeriesWorkspace>(
      `/commerce/v1/fiscal/configuration/devices/assign?businessId=${encodeURIComponent(businessId)}`,
      { deviceId, dianNumberingRangeId },
    ),
  unassignDeviceSeries: (businessId: string, deviceId: string) =>
    apiClient.post<FiscalDeviceSeriesWorkspace>(
      `/commerce/v1/fiscal/configuration/devices/unassign?businessId=${encodeURIComponent(businessId)}`,
      { deviceId },
    ),
  saveResolutionAlerts: (
    businessId: string,
    expirationWarningDays: number,
    remainingNumberWarningThreshold: number,
  ) => apiClient.put<FiscalDeviceSeriesWorkspace>(
    `/commerce/v1/fiscal/configuration/resolutions/alerts?businessId=${encodeURIComponent(businessId)}`,
    { expirationWarningDays, remainingNumberWarningThreshold },
  ),
};
