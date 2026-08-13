import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { buildFiscalResolutionFormState } from "./fiscal-resolution-form-state";
import type { FiscalResolutionConfiguration } from "@/services/api/fiscal-configuration";

const configured: FiscalResolutionConfiguration = {
  businessId: "11111111-1111-1111-1111-111111111111",
  fiscalAuthorizationId: "22222222-2222-2222-2222-222222222222",
  authorizationNumber: "18764000001",
  supplierTaxId: "900123456",
  environment: 2,
  qrValidationUrl: "https://example.test/qr?key=",
  technicalKeyVersion: "v3",
  validFrom: "2026-01-02",
  validUntil: "2027-01-02",
  prefix: "SETT",
  rangeStart: 500,
  rangeEnd: 900,
  initialConsecutive: 510,
  nextConsecutive: 511,
  canSetInitialConsecutive: false,
  hasActiveAuthorization: true,
  hasOnlineSeries: true,
  hasOfflineSeriesAvailable: false,
  hasTechnicalKey: true,
  isReadyForOnlineSales: true,
  isReadyForEnrollment: false,
};

describe("fiscal resolution form state", () => {
  it("hydrates every editable field from the saved resolution", () => {
    assert.deepEqual(buildFiscalResolutionFormState(configured, "2026-08-11"), {
      authorizationNumber: "18764000001",
      supplierTaxId: "900123456",
      environment: 2,
      qrValidationUrl: "https://example.test/qr?key=",
      technicalKeyVersion: "v3",
      technicalKey: null,
      validFrom: "2026-01-02",
      validUntil: "2027-01-02",
      prefix: "SETT",
      rangeStart: 500,
      rangeEnd: 900,
      initialConsecutive: 510,
      prepareOnlineSeries: true,
      prepareOfflineSeries: true,
    });
  });

  it("clears the previous business values when the selected business has no resolution", () => {
    const empty = buildFiscalResolutionFormState(null, "2026-08-11");
    assert.equal(empty.authorizationNumber, "");
    assert.equal(empty.supplierTaxId, "");
    assert.equal(empty.prefix, "");
  });
});
