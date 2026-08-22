import { apiClient } from "./client";

export type PartyRole = "Customer" | "Supplier" | "Seller" | "Carrier" | "Employee" | "User";
export type CommercialPartyRole = Exclude<PartyRole, "Employee" | "User">;
export interface PartyWorkspaceItem {
  partyId: string; partyType: "NaturalPerson" | "Organization";
  identificationTypeCode: string | null; identification: string | null; verificationDigit: string | null;
  displayName: string; legalName: string | null; firstName: string | null; lastName: string | null;
  email: string | null; phone: string | null; roles: PartyRole[];
  primarySiteName: string | null; cityName: string | null; isActive: boolean;
  completionStatus: "Complete" | "Incomplete"; rowVersion: string;
}
export interface PartyWorkspacePage { items: PartyWorkspaceItem[]; page: number; pageSize: number; totalCount: number; totalPages: number; }
export interface CustomerMapAssignment { routeId:string; routeName:string; sellerId:string; sellerName:string; }
export interface CustomerMapSite { customerId:string; partyId:string; customerName:string; identification:string|null; partySiteId:string; siteName:string; addressLine:string; neighborhood:string|null; cityName:string; phone:string|null; googleMapsUrl:string|null; latitude:number|null; longitude:number|null; assignments:CustomerMapAssignment[]; }
export interface PartySiteDetail {
  partySiteId: string; code: string; name: string; countryId: string;
  administrativeDivisionId: string; cityId: string; addressLine: string;
  neighborhood: string | null; postalCode: string | null;
  email: string | null; phone: string | null; isPrimary: boolean; isActive: boolean;
  googleMapsUrl: string | null; googlePlaceId: string | null; latitude: number | null; longitude: number | null; rowVersion: string;
}
export interface CustomerRoleDetail { customerId: string; priceListId: string | null; priceChannelId: string | null; requiresElectronicInvoice: boolean; isActive: boolean; }
export interface SupplierRoleDetail { supplierId: string; isActive: boolean; }
export interface SupplierAcceptance { supplierId: string; partyId: string; idempotentReplay: boolean; }
export interface CustomerAcceptance { customerId: string; partyId: string; }
export interface SellerRoleDetail { sellerId: string; code: string; defaultCommissionPercent: number | null; commissionBasis: string; commissionTrigger: string; isActive: boolean; }
export interface CarrierRoleDetail { carrierId: string; code: string; transportationMode: string; isActive: boolean; }
export interface EmployeeRoleDetail { employeeId: string; isActive: boolean; }
export interface UserRoleDetail { userId: string; username: string; email: string; isActive: boolean; }
export interface PartyWorkspaceDetail {
  partyId: string; partyType: "NaturalPerson" | "Organization";
  identificationCountryId: string | null; identificationTypeCode: string | null; identification: string | null;
  verificationDigit: string | null; displayName: string; legalName: string | null;
  firstName: string | null; lastName: string | null; email: string | null; phone: string | null;
  roles: PartyRole[]; primarySite: PartySiteDetail | null; sites: PartySiteDetail[] | null;
  customer: CustomerRoleDetail | null; supplier: SupplierRoleDetail | null;
  seller: SellerRoleDetail | null; carrier: CarrierRoleDetail | null;
  employee: EmployeeRoleDetail | null; user: UserRoleDetail | null; rowVersion: string;
}
export interface PartyIdentityLookupResult { exists: boolean; hasRequestedRole: boolean; party: PartyWorkspaceDetail | null; }
export interface PartyIdentityAcceptance { partyId: string; existingIdentity: boolean; }
export interface GeographyItem { countryId: string; code: string; name: string; isActive: boolean; }
export interface DivisionItem { administrativeDivisionId: string; countryId: string; code: string; name: string; divisionType: string; isActive: boolean; }
export interface CityItem { cityId: string; administrativeDivisionId: string; code: string; name: string; isActive: boolean; }
export interface GeographyHierarchyItem { id: string; parentId: string | null; level: "Country" | "Division" | "City"; code: string; name: string; isActive: boolean; }
export interface PartyInput { partyType: string; identificationCountryId: string; identificationTypeCode: string; identification: string; verificationDigit: string | null; displayName: string; legalName: string | null; firstName: string | null; lastName: string | null; email: string | null; phone: string | null; }
export interface PartySiteInput { code: string; name: string; countryId: string; administrativeDivisionId: string; cityId: string; addressLine: string; neighborhood: string | null; postalCode: string | null; email: string | null; phone: string | null; isPrimary: boolean; googleMapsUrl: string | null; googlePlaceId: string | null; latitude: number | null; longitude: number | null; }
export interface CreateThirdPartyRequest { operationId: string; businessId: string; party: PartyInput; primarySite: PartySiteInput; pricing?: { priceListId: string | null; priceChannelId: string | null } | null; requiresElectronicInvoice?: boolean; code?: string; defaultCommissionPercent?: number | null; commissionBasis?: string; commissionTrigger?: string; transportationMode?: string; }
export interface CustomerPricingOption { id: string; code: string; name: string; }
export interface CustomerPricingOptions { priceLists: CustomerPricingOption[]; priceChannels: CustomerPricingOption[]; }
export interface SellerUserAccess { userId:string; partyId:string; username:string; email:string; isActive:boolean; roleName:string; businessId:string; }
export interface CreateSellerUserAccess { username:string; email:string; password:string; firstName:string; lastName:string; phoneNumber:string|null; }

export const partiesApi = {
  page: (params: { page: number; pageSize: number; search?: string; role?: string; isActive?: boolean; isIncomplete?: boolean }) =>
    apiClient.get<PartyWorkspacePage>("/commerce/v1/parties", params),
  createIdentity: (request: CreateThirdPartyRequest & { targetRole: "Employee" | "User" }) => apiClient.post<PartyIdentityAcceptance>("/commerce/v1/parties/identity", request),
  customerMap: () => apiClient.get<CustomerMapSite[]>("/commerce/v1/parties/customer-map"),
  createCustomer: (request: CreateThirdPartyRequest) => apiClient.post<CustomerAcceptance>("/commerce/v1/customers", request),
  createSupplier: (request: CreateThirdPartyRequest) => apiClient.post<SupplierAcceptance>("/commerce/v1/suppliers", request),
  createSeller: (request: CreateThirdPartyRequest) => apiClient.post("/commerce/v1/sellers", request),
  createCarrier: (request: CreateThirdPartyRequest) => apiClient.post("/commerce/v1/carriers", request),
  addSite: (customerId: string, request: { operationId: string; site: PartySiteInput }) => apiClient.post<PartySiteDetail>(`/commerce/v1/customers/${customerId}/sites`, request),
  updateSite: (customerId:string,siteId:string,request:{site:PartySiteInput;rowVersion:string}) => apiClient.put<PartySiteDetail>(`/commerce/v1/customers/${customerId}/sites/${siteId}`,request),
  identity: (params: { countryId: string; identificationTypeCode: string; identification: string; requestedRole: PartyRole }) =>
    apiClient.get<PartyIdentityLookupResult>("/commerce/v1/parties/identity", params),
  detail: (partyId: string) =>
    apiClient.get<PartyWorkspaceDetail>("/commerce/v1/parties/" + partyId),
  pricingOptions: () => apiClient.get<CustomerPricingOptions>("/commerce/v1/parties/customer-pricing-options"),
  sellerAccess: (partyId:string) => apiClient.get<SellerUserAccess|null>(`/commerce/v1/parties/${partyId}/seller-access`),
  createSellerAccess: (partyId:string,request:CreateSellerUserAccess) => apiClient.post<SellerUserAccess>(`/commerce/v1/parties/${partyId}/seller-access`,request),
  update: (partyId: string, request: { partyType: string; displayName: string; legalName: string | null; firstName: string | null; lastName: string | null; verificationDigit: string | null; email: string | null; phone: string | null; rowVersion: string }) =>
    apiClient.put<PartyWorkspaceItem>(`/commerce/v1/parties/${partyId}`, request),
  setStatus: (partyId: string, isActive: boolean, rowVersion: string) =>
    apiClient.post<PartyWorkspaceItem>(`/commerce/v1/parties/${partyId}/status`, { isActive, rowVersion }),
  saveCustomerBilling: (partyId:string, requiresElectronicInvoice:boolean) =>
    apiClient.put<CustomerRoleDetail>(`/commerce/v1/parties/${partyId}/customer-billing`, { requiresElectronicInvoice }),
  countries: (includeInactive = false) => apiClient.get<GeographyItem[]>("/commerce/v1/masters/geography/countries", { includeInactive }),
  geographyHierarchy: (includeInactive = false) => apiClient.get<GeographyHierarchyItem[]>("/commerce/v1/masters/geography/hierarchy", { includeInactive }),
  divisions: (countryId: string, includeInactive = false) => apiClient.get<DivisionItem[]>(`/commerce/v1/masters/geography/countries/${countryId}/divisions`, { includeInactive }),
  cities: (divisionId: string, includeInactive = false) => apiClient.get<CityItem[]>(`/commerce/v1/masters/geography/divisions/${divisionId}/cities`, { includeInactive }),
  createCountry: (body: { code: string; name: string; isActive: boolean }) => apiClient.post<GeographyItem>("/commerce/v1/masters/geography/countries", body),
  createDivision: (body: { countryId: string; code: string; name: string; divisionType: string; isActive: boolean }) => apiClient.post<DivisionItem>("/commerce/v1/masters/geography/divisions", body),
  createCity: (body: { administrativeDivisionId: string; code: string; name: string; isActive: boolean }) => apiClient.post<CityItem>("/commerce/v1/masters/geography/cities", body),
  updateCountry: (id: string, body: { code: string; name: string; isActive: boolean }) => apiClient.put<GeographyItem>(`/commerce/v1/masters/geography/countries/${id}`, body),
  updateDivision: (id: string, body: { countryId: string; code: string; name: string; divisionType: string; isActive: boolean }) => apiClient.put<DivisionItem>(`/commerce/v1/masters/geography/divisions/${id}`, body),
  updateCity: (id: string, body: { administrativeDivisionId: string; code: string; name: string; isActive: boolean }) => apiClient.put<CityItem>(`/commerce/v1/masters/geography/cities/${id}`, body),
};

