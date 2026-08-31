import { apiClient, withPagedDefaults } from "./client";
import type { PagedRequest, PagedResponse } from "@/types/api";
import type { Tenant } from "@/types/entities";

export interface ProvisionTenantRequest {
  provisioningRequestId: string;
  legalName: string; tradeName: string; nit: string; verificationDigit: string;
  countryId: string; administrativeDivisionId: string; cityId: string;
  address: string; phone: string; email: string; taxResponsibilities: string;
  businessName: string; businessAddress: string; businessPhone: string; businessEmail: string;
  timeZone: string; inventoryCostBasis: "LatestReceiptCost" | "WeightedAverageCost";
  invitationEmail: string; maximumUsers: number; maximumEnrolledDevices: number;
}

export interface TenantEnrolledDevice {
  deviceId: string; name: string; isActive: boolean; createdAt: string;
  lastSeenAt: string | null; businessId: string | null; businessName: string | null;
}
export interface TenantBranding {
  tenantId: string; displayName: string; legalName: string | null; logoUrl: string | null;
}
export interface ProvisionTenantResult {
  provisioningRequestId: string; tenantId: string; tenantKey: string; businessId: string;
  salesWarehouseId: string; ordersWarehouseId: string; defaultCustomerId: string;
  administratorUserId: string | null; status: string;
}

export interface TenantCommercialPlan {
  planId: string; code: string; name: string; monthlyPriceCop: number;
  salesTaxRate: number; annualDiscountRate: number; includedFullUsers: number; includedSellerUsers: number;
  includedPosDevices: number; includedDianDocuments: number; includedPayrollEmployees: number;
  isRecommended: boolean; isCustom: boolean; features: string[];
}
export interface TenantCommercialAddOn {
  addOnId: string; code: string; name: string; unitLabel: string; unitSize: number;
  monthlyUnitPriceCop: number; salesTaxRate: number;
}
export interface TenantCommercialCatalog { plans: TenantCommercialPlan[]; addOns: TenantCommercialAddOn[]; }
export interface TenantQuoteRequest {
  planCode: string; billingPeriod: "Monthly" | "Annual"; additionalFullUsers: number;
  sellerUsers: number; additionalPosDevices: number; dianDocumentPacks: number;
  payrollEmployeePacks: number;
}
export interface TenantQuote {
  planCode: string; planName: string; billingPeriod: "Monthly" | "Annual";
  monthlySubtotalCop: number; periods: number; grossPeriodAmountCop: number;
  discountRate: number; discountAmountCop: number; taxAmountCop: number;
  payableAmountCop: number; monthlyEquivalentCop: number; fullUserLimit: number;
  sellerUserLimit: number; posDeviceLimit: number; dianDocumentMonthlyLimit: number;
  payrollEmployeeLimit: number;
  lines?: TenantQuoteLine[];
}
export interface TenantQuoteLine {
  code: string; name: string; quantity: number; unitSize: number;
  monthlyUnitPriceCop: number; monthlyTotalCop: number; salesTaxRate: number;
}
export interface TenantProvisioningWidget {
  publicKey: string; reference: string; amountInCents: number; currency: string;
  integritySignature: string; expirationTime: string | null; redirectUrl: string | null;
}
export interface TenantProvisioningCheckout {
  draftId: string; accessToken: string; expiresAt: string; quote: TenantQuote;
  widget: TenantProvisioningWidget;
}
export interface TenantProvisioningCheckoutStatus {
  draftId: string; status: string; paymentStatus: string; tenantId: string | null;
  tenantKey: string | null; errorMessage: string | null;
}
export interface TenantProvisioningGeography { id: string; code: string; name: string; }
export interface TenantCommercialSubscription {
  subscriptionId: string; planCode: string; planName: string; billingPeriod: "Monthly" | "Annual";
  status: string; currentPeriodStart: string; currentPeriodEnd: string;
  fullUserLimit: number; sellerUserLimit: number; posDeviceLimit: number;
  dianDocumentMonthlyLimit: number; dianDocumentsUsed: number; payrollEmployeeLimit: number;
}
export interface TenantSubscriptionUsage {
  fullUsers: number; sellerUsers: number; posDevices: number; payrollEmployees: number;
}
export interface TenantRenewalOrder {
  renewalOrderId: string; revision: number; status: string; isCurrent: boolean;
  targetPeriodStart: string; targetPeriodEnd: string; dueAt: string;
  quote: TenantQuote; usage: TenantSubscriptionUsage;
}
export interface TenantSubscriptionCheckout { renewalOrderId: string; widget: TenantProvisioningWidget; }
export interface TenantSubscriptionReceiptLine {
  code: string; name: string; quantity: number; unitPrice: number; netAmount: number;
  taxRate: number; taxAmount: number; totalAmount: number;
}
export interface TenantSubscriptionReceipt {
  documentId: string; documentNumber: string; issuedAt: string; periodStart: string; periodEnd: string;
  billingPeriod: "Monthly" | "Annual"; currencyCode: string; subtotal: number; taxAmount: number;
  totalAmount: number; paymentMethod: string; paymentReference: string; cufe: string | null;
  fiscalStatus: string; lines: TenantSubscriptionReceiptLine[];
}
export interface PlatformTenantSubscription extends TenantCommercialSubscription {
  tenantId: string; tenantKey: string; tenantName: string; tenantEmail: string;
  renewalOrderId: string | null; renewalStatus: string | null;
  renewalDueAt: string | null; renewalPayableAmount: number | null;
}
export interface TenantBillingNotification {
  notificationId: string; renewalOrderId: string; eventKey: string;
  title: string; message: string; actionUrl: string;
  createdAt: string; readAt: string | null;
}
export interface FiscalCertificateExpiryAlert {
  tenantId: string; tenantName: string; validTo: string; isExpired: boolean;
}
export interface PlatformBillingPolicy {
  emailRemindersEnabled: boolean; preDueReminderDays: number;
  overdueReminderIntervalDays: number; gracePeriodDays: number;
  billingTimeZoneId: string; updatedAt: string; version: string;
}
export interface UpdatePlatformBillingPolicy extends Omit<PlatformBillingPolicy, "updatedAt"> { reason: string; }
export interface RecordTenantSubscriptionPayment {
  paymentMethodCode: "Cash" | "Transfer" | "DebitCard" | "CreditCard";
  reference: string; paidAt: string; note: string | null;
}

export const tenantsApi = {
  list: (params?: Partial<PagedRequest>) => apiClient.get<PagedResponse<Tenant>>("/tenants", withPagedDefaults(params)),
  fiscalCertificateExpiryAlerts: () =>
    apiClient.get<FiscalCertificateExpiryAlert[]>("/tenants/fiscal-certificate-expiry-alerts"),
  getById: (id: string) => apiClient.get<Tenant>(`/tenants/${id}`),
  getBranding: () => apiClient.get<TenantBranding>("/tenants/branding"),
  create: (tenant: ProvisionTenantRequest, quote: TenantQuoteRequest) =>
    apiClient.post<ProvisionTenantResult>("/tenants", { tenant, quote }),
  update: (id: string, data: Partial<Tenant>) => apiClient.put<Tenant>(`/tenants/${id}`, data),
  uploadLogo: (id: string, file: File) => {
    const body = new FormData();
    body.append("file", file);
    return apiClient.postForm<Tenant>(`/tenants/${id}/logo`, body);
  },
  deactivate: (id: string) => apiClient.delete(`/tenants/${id}`),
  activate: (id: string) => apiClient.post(`/tenants/${id}/activate`, {}),
  listDevices: (id: string) => apiClient.get<TenantEnrolledDevice[]>(`/tenants/${id}/devices`),
  deactivateDevice: (id: string, deviceId: string) => apiClient.delete(`/tenants/${id}/devices/${deviceId}`),
  billingPolicy: () => apiClient.get<PlatformBillingPolicy>("/tenants/billing-policy"),
  updateBillingPolicy: (data: UpdatePlatformBillingPolicy) =>
    apiClient.put<PlatformBillingPolicy>("/tenants/billing-policy", data),
  subscription: (id: string) =>
    apiClient.get<TenantCommercialSubscription | null>(`/tenants/${id}/subscription`),
  subscriptions: (params?: Partial<PagedRequest> & { status?: string }) => {
    const value = withPagedDefaults(params);
    const query = new URLSearchParams({ page: String(value.page), pageSize: String(value.pageSize) });
    if (value.search) query.set("search", String(value.search));
    if (params?.status) query.set("status", params.status);
    return apiClient.get<PagedResponse<PlatformTenantSubscription>>(`/tenants/subscriptions?${query}`);
  },
  renewalOrder: (id: string) =>
    apiClient.get<TenantRenewalOrder | null>(`/tenants/${id}/subscription/renewal-order`),
  recordSubscriptionPayment: (id: string, renewalOrderId: string, data: RecordTenantSubscriptionPayment) =>
    apiClient.post<TenantSubscriptionReceipt>(`/tenants/${id}/subscription/renewal-orders/${renewalOrderId}/record-payment`, data),
};

export const tenantCommercialApi = {
  catalog: () => apiClient.get<TenantCommercialCatalog>("/tenant-commercial/catalog"),
  quote: (data: TenantQuoteRequest) => apiClient.post<TenantQuote>("/tenant-commercial/quote", data),
  startCheckout: (tenant: ProvisionTenantRequest, quote: TenantQuoteRequest, redirectUrl?: string) =>
    apiClient.post<TenantProvisioningCheckout>("/tenant-commercial/provisioning-checkouts", { tenant, quote, redirectUrl }),
  confirmCheckout: async (draftId: string, accessToken: string, transactionId: string) => {
    const response = await fetch(`/api/tenant-commercial/provisioning-checkouts/${draftId}/confirm`, {
      method: "POST", credentials: "include",
      headers: { "Content-Type": "application/json", "X-Provisioning-Token": accessToken },
      body: JSON.stringify({ transactionId }),
    });
    const body = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(body.detail || body.message || "No fue posible confirmar el pago.");
    return body as TenantProvisioningCheckoutStatus;
  },
  checkoutStatus: async (draftId: string, accessToken: string) => {
    const response = await fetch(`/api/tenant-commercial/provisioning-checkouts/${draftId}`, {
      credentials: "include", headers: { "X-Provisioning-Token": accessToken },
    });
    const body = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(body.detail || body.message || "No fue posible consultar el pago.");
    return body as TenantProvisioningCheckoutStatus;
  },
  countries: () => apiClient.get<TenantProvisioningGeography[]>("/tenant-commercial/geography/countries"),
  divisions: (countryId: string) => apiClient.get<TenantProvisioningGeography[]>(`/tenant-commercial/geography/countries/${countryId}/divisions`),
  cities: (divisionId: string) => apiClient.get<TenantProvisioningGeography[]>(`/tenant-commercial/geography/divisions/${divisionId}/cities`),
  subscription: () => apiClient.get<TenantCommercialSubscription | null>("/tenant-commercial/subscription"),
  renewalOrder: () => apiClient.get<TenantRenewalOrder | null>("/tenant-commercial/subscription/renewal-order"),
  reviseRenewalOrder: (data: TenantQuoteRequest) =>
    apiClient.put<TenantRenewalOrder>("/tenant-commercial/subscription/renewal-order", data),
  startRenewalCheckout: (redirectUrl?: string) =>
    apiClient.post<TenantSubscriptionCheckout>("/tenant-commercial/subscription/renewal-order/checkout", { redirectUrl }),
  confirmRenewalCheckout: (renewalOrderId: string, transactionId: string) =>
    apiClient.post<TenantSubscriptionReceipt>(`/tenant-commercial/subscription/renewal-orders/${renewalOrderId}/confirm`, { transactionId }),
  renewalReceipt: (renewalOrderId: string) =>
    apiClient.get<TenantSubscriptionReceipt>(`/tenant-commercial/subscription/renewal-orders/${renewalOrderId}/receipt`),
  billingNotifications: (take = 30) =>
    apiClient.get<TenantBillingNotification[]>(`/tenant-commercial/subscription/notifications?take=${take}`),
  markBillingNotificationRead: (notificationId: string) =>
    apiClient.post<void>(`/tenant-commercial/subscription/notifications/${notificationId}/read`, {}),
};
