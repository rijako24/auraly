import { apiClient } from "./client";

export type PartyRole = "Customer" | "Supplier";
export interface PartyWorkspaceItem {
  partyId: string; partyType: "NaturalPerson" | "Organization";
  identificationTypeCode: string | null; identification: string | null; verificationDigit: string | null;
  displayName: string; legalName: string | null; firstName: string | null; lastName: string | null;
  email: string | null; phone: string | null; roles: PartyRole[];
  primarySiteName: string | null; cityName: string | null; isActive: boolean;
  completionStatus: "Complete" | "Incomplete"; rowVersion: string;
}
export interface PartyWorkspacePage { items: PartyWorkspaceItem[]; page: number; pageSize: number; totalCount: number; totalPages: number; }
export interface GeographyItem { countryId: string; code: string; name: string; isActive: boolean; }
export interface DivisionItem { administrativeDivisionId: string; countryId: string; code: string; name: string; divisionType: string; isActive: boolean; }
export interface CityItem { cityId: string; administrativeDivisionId: string; code: string; name: string; isActive: boolean; }
export interface PartyInput { partyType: string; identificationCountryId: string; identificationTypeCode: string; identification: string; verificationDigit: string | null; displayName: string; legalName: string | null; firstName: string | null; lastName: string | null; email: string | null; phone: string | null; }
export interface PartySiteInput { code: string; name: string; countryId: string; administrativeDivisionId: string; cityId: string; addressLine: string; neighborhood: string | null; postalCode: string | null; email: string | null; phone: string | null; isPrimary: boolean; }
export interface CreateThirdPartyRequest { operationId: string; businessId: string; party: PartyInput; primarySite: PartySiteInput; pricing?: null; }

export const partiesApi = {
  page: (params: { page: number; pageSize: number; search?: string; role?: string; isActive?: boolean; isIncomplete?: boolean }) =>
    apiClient.get<PartyWorkspacePage>("/commerce/v1/parties", params),
  createCustomer: (request: CreateThirdPartyRequest) => apiClient.post("/commerce/v1/customers", request),
  createSupplier: (request: CreateThirdPartyRequest) => apiClient.post("/commerce/v1/suppliers", request),
  update: (partyId: string, request: { partyType: string; displayName: string; legalName: string | null; firstName: string | null; lastName: string | null; verificationDigit: string | null; email: string | null; phone: string | null; rowVersion: string }) =>
    apiClient.put<PartyWorkspaceItem>(`/commerce/v1/parties/${partyId}`, request),
  setStatus: (partyId: string, isActive: boolean, rowVersion: string) =>
    apiClient.post<PartyWorkspaceItem>(`/commerce/v1/parties/${partyId}/status`, { isActive, rowVersion }),
  countries: (includeInactive = false) => apiClient.get<GeographyItem[]>("/commerce/v1/masters/geography/countries", { includeInactive }),
  divisions: (countryId: string, includeInactive = false) => apiClient.get<DivisionItem[]>(`/commerce/v1/masters/geography/countries/${countryId}/divisions`, { includeInactive }),
  cities: (divisionId: string, includeInactive = false) => apiClient.get<CityItem[]>(`/commerce/v1/masters/geography/divisions/${divisionId}/cities`, { includeInactive }),
  createCountry: (body: { code: string; name: string; isActive: boolean }) => apiClient.post<GeographyItem>("/commerce/v1/masters/geography/countries", body),
  createDivision: (body: { countryId: string; code: string; name: string; divisionType: string; isActive: boolean }) => apiClient.post<DivisionItem>("/commerce/v1/masters/geography/divisions", body),
  createCity: (body: { administrativeDivisionId: string; code: string; name: string; isActive: boolean }) => apiClient.post<CityItem>("/commerce/v1/masters/geography/cities", body),
};

