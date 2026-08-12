import { apiClient } from "./client";

export type FiscalResolutionConfiguration = {
  businessId: string;
  fiscalAuthorizationId: string | null;
  authorizationNumber: string | null;
  supplierTaxId: string | null;
  environment: number;
  qrValidationUrl: string | null;
  technicalKeyVersion: string | null;
  validFrom: string | null;
  validUntil: string | null;
  prefix: string | null;
  rangeStart: number | null;
  rangeEnd: number | null;
  initialConsecutive: number | null;
  nextConsecutive: number | null;
  canSetInitialConsecutive: boolean;
  hasActiveAuthorization: boolean;
  hasOnlineSeries: boolean;
  hasOfflineSeriesAvailable: boolean;
  hasTechnicalKey: boolean;
  isReadyForOnlineSales: boolean;
  isReadyForEnrollment: boolean;
};

export type SalesInvoiceNumberingConfiguration = {
  businessId: string;
  initialConsecutive: number | null;
  nextConsecutive: number | null;
  canSetInitialConsecutive: boolean;
  hasIssuedInvoices: boolean;
};
export type SaveFiscalResolutionConfiguration = {
  authorizationNumber: string;
  supplierTaxId: string;
  environment: number;
  qrValidationUrl: string;
  technicalKeyVersion: string;
  technicalKey: string | null;
  validFrom: string;
  validUntil: string;
  prefix: string;
  rangeStart: number;
  rangeEnd: number;
  initialConsecutive: number;
  prepareOnlineSeries: boolean;
  prepareOfflineSeries: boolean;
};

export type FiscalIssuerConnectionConfiguration = {
  businessId: string;
  fiscalIssuerConfigurationId: string | null;
  version: number | null;
  supplierTaxId: string | null;
  supplierCheckDigit: string | null;
  legalName: string | null;
  tradeName: string | null;
  taxLevelCode: string | null;
  taxSchemeId: string | null;
  taxSchemeName: string | null;
  identificationTypeCode: string | null;
  addressLine: string | null;
  cityCode: string | null;
  cityName: string | null;
  departmentCode: string | null;
  departmentName: string | null;
  postalZone: string | null;
  softwareIdentificationCode: string | null;
  softwarePinSecretReference: string | null;
  environment: number | null;
  testSetId: string | null;
  certificateProvider: string | null;
  certificateKeyReference: string | null;
  certificateThumbprint: string | null;
  dianEndpoint: string | null;
  technicalAnnexVersion: string | null;
  generatorVersion: string | null;
  validFrom: string | null;
  validTo: string | null;
  isConfigured: boolean;
  isReadyForHabilitation: boolean;
  missingRequirements: string[];
};

export type SaveFiscalIssuerConnectionConfiguration = {
  supplierTaxId: string;
  supplierCheckDigit: string;
  legalName: string;
  tradeName: string | null;
  taxLevelCode: string;
  taxSchemeId: string;
  taxSchemeName: string;
  identificationTypeCode: string;
  addressLine: string;
  cityCode: string;
  cityName: string;
  departmentCode: string;
  departmentName: string;
  postalZone: string | null;
  softwareIdentificationCode: string;
  softwarePinSecretReference: string;
  environment: number;
  testSetId: string | null;
  certificateProvider: string;
  certificateKeyReference: string;
  certificateThumbprint: string;
  dianEndpoint: string;
  technicalAnnexVersion: string;
  generatorVersion: string;
  validFrom: string;
  validTo: string | null;
};

export const fiscalConfigurationApi = {
  getIssuerConnection: (businessId: string) =>
    apiClient.get<FiscalIssuerConnectionConfiguration>(
      "/commerce/v1/fiscal/configuration/issuer",
      { businessId },
    ),
  saveIssuerConnection: (
    businessId: string,
    request: SaveFiscalIssuerConnectionConfiguration,
  ) =>
    apiClient.put<FiscalIssuerConnectionConfiguration>(
      `/commerce/v1/fiscal/configuration/issuer?businessId=${encodeURIComponent(businessId)}`,
      request,
    ),
  getNumbering: (businessId: string) =>
    apiClient.get<SalesInvoiceNumberingConfiguration>(
      "/commerce/v1/fiscal/numbering",
      { businessId },
    ),
  saveNumbering: (businessId: string, initialConsecutive: number) =>
    apiClient.put<SalesInvoiceNumberingConfiguration>(
      `/commerce/v1/fiscal/numbering?businessId=${encodeURIComponent(businessId)}`,
      { initialConsecutive },
    ),
  get: (businessId: string) =>
    apiClient.get<FiscalResolutionConfiguration>(
      "/commerce/v1/fiscal/configuration",
      { businessId },
    ),
  save: (businessId: string, request: SaveFiscalResolutionConfiguration) =>
    apiClient.put<FiscalResolutionConfiguration>(
      `/commerce/v1/fiscal/configuration?businessId=${encodeURIComponent(businessId)}`,
      request,
    ),
};
