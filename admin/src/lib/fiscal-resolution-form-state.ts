import type {
  FiscalResolutionConfiguration,
  SaveFiscalResolutionConfiguration,
} from "@/services/api/fiscal-configuration";

export function buildFiscalResolutionFormState(
  value: FiscalResolutionConfiguration | null,
  today: string,
): SaveFiscalResolutionConfiguration {
  if (!value?.hasActiveAuthorization) {
    return {
      authorizationNumber: "",
      supplierTaxId: "",
      environment: 2,
      qrValidationUrl: "https://catalogo-vpfe-hab.dian.gov.co/document/searchqr?documentkey=",
      technicalKeyVersion: "1",
      technicalKey: null,
      validFrom: today,
      validUntil: today,
      prefix: "",
      rangeStart: 1,
      rangeEnd: 1_000_000,
      initialConsecutive: 1,
      prepareOnlineSeries: true,
      prepareOfflineSeries: true,
    };
  }

  return {
    authorizationNumber: value.authorizationNumber ?? "",
    supplierTaxId: value.supplierTaxId ?? "",
    environment: value.environment,
    qrValidationUrl: value.qrValidationUrl ?? "",
    technicalKeyVersion: value.technicalKeyVersion ?? "1",
    technicalKey: null,
    validFrom: value.validFrom?.slice(0, 10) ?? today,
    validUntil: value.validUntil?.slice(0, 10) ?? today,
    prefix: value.prefix ?? "",
    rangeStart: value.rangeStart ?? 1,
    rangeEnd: value.rangeEnd ?? 1_000_000,
    initialConsecutive: value.initialConsecutive ?? value.rangeStart ?? 1,
    prepareOnlineSeries: value.hasOnlineSeries,
    prepareOfflineSeries: value.hasOfflineSeriesAvailable,
  };
}
