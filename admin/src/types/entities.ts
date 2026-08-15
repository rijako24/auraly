import {
  ConversationStateEnum,
  ConversationLifecycleStatus,
  LeadStatus,
  OrderFulfillmentMode,
  OrderSource,
  OrderStatus,
  PaymentTransactionSource,
  PaymentTransactionStatus,
  ReservationStatus,
  ServiceTier,
  ServiceType,
  PromotionBenefitType,
  PromotionItemType,
  SystemConfigurationKey,
} from "./enums";
import type { AgentBotType } from "./agent-bot-type";

export interface Tenant {
  tenantId: string;
  name: string;
  email: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string | null;
  maximumUsers: number;
  maximumEnrolledDevices: number;
  businessCount: number;
  activeUserCount: number;
  activeEnrolledDeviceCount: number;
  businesses?: Business[];
}

export interface Business {
  businessId: string;
  tenantId: string;
  name: string;
  description: string;
  address: string;
  phone: string;
  email: string;
  website: string;
  logoUrl: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string | null;
  tenant?: Tenant;
}


export interface BusinessResource {
  businessResourceId: string;
  businessId: string;
  resourceName: string;
  quantity: number;
  createdAt: string;
  updatedAt: string | null;
}

export interface BusinessWhatsAppNumber {
  businessWhatsAppNumberId: string;
  businessId: string;
  phoneNumber: string;
  whatsAppPhoneNumberId: string;
  whatsAppAccessToken: string;
  isActive: boolean;
  createdAt: string;
}

export interface BusinessAttachment {
  businessAttachmentId: string;
  businessId: string;
  blobPath: string;
  mediaType: string;
  filename: string | null;
  description: string | null;
  isActive: boolean;
  createdAt: string;
}


export interface PromotionCondition {
  promotionConditionId?: string | null;
  itemType: PromotionItemType;
  productId?: string | null;
  serviceId?: string | null;
  categoryName?: string | null;
  minQuantity: number;
  minSubtotal?: number | null;
}

export interface PromotionBenefit {
  promotionBenefitId?: string | null;
  benefitType: PromotionBenefitType;
  targetItemType: PromotionItemType;
  productId?: string | null;
  serviceId?: string | null;
  categoryName?: string | null;
  discountPercentage?: number | null;
  discountAmount?: number | null;
  fixedUnitPrice?: number | null;
  appliesToQuantity?: number | null;
}

export interface Promotion {
  promotionId: string;
  businessId: string;
  name: string;
  description?: string | null;
  isActive: boolean;
  startsAtUtc?: string | null;
  endsAtUtc?: string | null;
  priority: number;
  isCombinable: boolean;
  couponCode?: string | null;
  conditions: PromotionCondition[];
  benefits: PromotionBenefit[];
  createdAt: string;
  updatedAt?: string | null;
}
export interface Service {
  serviceId: string;
  businessId: string;
  serviceName: string;
  description: string;
  keywords?: string | null;
  durationMinutes: number;
  price: number;
  includeInCheckoutTotal: boolean;
  isActive: boolean;
  categoryId: string;
  categoryName?: string;
  tier: ServiceTier;
  serviceType: ServiceType;
  fulfillmentKind?: number;
  fixedScheduleLabel?: string | null;
  createdAt: string;
  updatedAt: string | null;
  category?: ServiceCategory;
  business?: Business;
}

export interface ServiceCategory {
  serviceCategoryId: string;
  businessId: string;
  name: string;
  displayOrder: number;
  isActive: boolean;
  createdAt: string;
  updatedAt: string | null;
}

export interface ServiceAddOnRule {
  serviceAddOnRuleId: string;
  businessId: string;
  addOnServiceId: string;
  compatibleServiceId: string | null;
  displayOrder: number;
  addOnService?: Service;
  compatibleService?: Service;
}

export interface ServiceBundleItem {
  serviceBundleItemId: string;
  bundleServiceId: string;
  includedServiceId: string;
  displayOrder: number;
  bundleService?: Service;
  includedService?: Service;
}

export interface ServiceResourceUsage {
  serviceResourceUsageId: string;
  serviceId: string;
  businessResourceId: string;
  quantity: number;
  businessResource?: BusinessResource;
}

export interface Employee {
  employeeId: string;
  businessId: string;
  partyId?: string | null;
  name: string;
  isActive: boolean;
  serviceIds?: string[];
  createdAt: string;
  updatedAt: string | null;
  services?: Service[];
}

export interface WorkingHour {
  workingHourId?: string | null;
  dayOfWeek: number;
  openTime: string;
  closeTime: string;
  isActive: boolean;
}

export interface EmployeeWorkingHours {
  employeeId: string;
  usesBusinessFallback: boolean;
  workingHours: WorkingHour[];
}

export interface BusinessAvailabilityBlock {
  businessAvailabilityBlockId: string;
  businessId: string;
  employeeId: string | null;
  employeeName?: string | null;
  date: string;
  startTime: string | null;
  endTime: string | null;
  reason: string;
  source: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string | null;
}

export type BusinessAvailabilityBlockPayload = Pick<
  BusinessAvailabilityBlock,
  "employeeId" | "date" | "startTime" | "endTime" | "reason" | "isActive"
>;

export interface EmployeeScheduleException {
  employeeScheduleExceptionId: string;
  businessId: string;
  employeeId: string;
  date: string;
  openTime: string | null;
  closeTime: string | null;
  isClosed: boolean;
  reason: string | null;
  createdAt: string;
  updatedAt: string | null;
}

export type EmployeeScheduleExceptionPayload = Pick<
  EmployeeScheduleException,
  "date" | "openTime" | "closeTime" | "isClosed" | "reason"
>;

export interface GoogleCalendarIntegration {
  isEnabled: boolean;
  calendarId: string;
  timeZone: string;
  scopes: string | null;
  hasClientId: boolean;
  hasClientSecret: boolean;
  hasRefreshToken: boolean;
  lastError: string | null;
  lastSyncAt: string | null;
}

export interface WompiIntegration {
  isEnabled: boolean;
  mode: "test" | "production";
  sandboxBaseUrl: string;
  productionBaseUrl: string;
  requestTimeoutSeconds: number;
  checkoutBaseUrl: string;
  hasPrivateKey: boolean;
  hasPublicKey: boolean;
  hasEventsSecret: boolean;
  hasIntegritySecret: boolean;
  lastError: string | null;
  lastSyncAt: string | null;
}

export interface MantisIntegration {
  isConfigured: boolean;
  isEnabled: boolean;
  baseUrl: string;
  requestTimeoutSeconds: number;
  currency: string;
  hasAuthorizationToken: boolean;
  lastError: string | null;
  lastSyncAt: string | null;
}

export interface XionIntegration {
  isConfigured: boolean;
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
  lastError: string | null;
  lastSyncAt: string | null;
}

export interface IntegrationSettings {
  googleCalendar: GoogleCalendarIntegration;
  wompi: WompiIntegration;
  mantis: MantisIntegration;
  xion: XionIntegration;
}
export interface EmployeeService {
  employeeServiceId: string;
  employeeId: string;
  serviceId: string;
  createdAt: string;
}

export interface Reservation {
  reservationId: string;
  businessId: string;
  serviceId: string | null;
  serviceName?: string;
  employeeId: string | null;
  employeeName?: string;
  reservationDateTime: string | null;
  durationMinutes: number | null;
  status: ReservationStatus;
  conversationId: string | null;
  createdAt: string;
  updatedAt: string | null;
  service?: Service;
  employee?: Employee;
  addOns?: ReservationAddOn[];
}

export interface ReservationAddOn {
  reservationAddOnId: string;
  reservationId: string;
  addOnServiceId: string;
  priceSnapshot: number;
  addOnService?: Service;
}

export type ConversationOwner = "Bot" | "Human";

export interface Conversation {
  conversationId: string;
  businessId: string;
  agentId: string | null;
  userNumber: string;
  lastMessage: string | null;
  lastIntent?: string | null;
  timestamp: string;
  customerName: string | null;
  customerEmail?: string | null;
  currentStageName?: string | null;
  babyAge?: number | null;
  recommendedPlan?: string | null;
  state?: ConversationStateEnum;
  status?: ConversationLifecycleStatus;
  owner?: ConversationOwner;
  botEnabled?: boolean;
  openedAt?: string;
  lastActivityAt?: string;
  closedAt?: string | null;
  closeReason?: string | null;
  messages?: Message[];
  business?: Business;
}

export interface Message {
  messageId: string;
  conversationId: string;
  sender: "User" | "Bot" | "user" | "bot";
  messageText: string;
  timestamp: string;
}

export interface ConversationContext {
  conversationContextId: string;
  conversationId: string;
  field: string;
  value: string;
  createdAt: string;
  updatedAt: string | null;
}

export interface Lead {
  leadId: string;
  businessId: string;
  userNumber: string;
  status: LeadStatus;
  timestamp: string;
  customerName: string | null;
  notes: string | null;
  conversationId?: string | null;
  conversationStatus?: ConversationLifecycleStatus | null;
  currentStageName?: string | null;
  lastActivityAt?: string | null;
  qualificationBand?: string | null;
  qualificationLabel?: string | null;
  qualificationPriority?: number | null;
  qualificationFlowId?: string | null;
  qualificationStageId?: string | null;
  qualificationUpdatedAt?: string | null;
  convertedAt?: string | null;
  business?: Business;
}

export interface WhatsAppTemplate {
  id: string;
  name: string;
  status: string;
  category: "Marketing" | "Utility" | string;
  language: string;
  headerParameterCount: number;
  bodyParameterCount: number;
}


export type CampaignStatus =
  | "Draft"
  | "Scheduled"
  | "Queued"
  | "Processing"
  | "Completed"
  | "CompletedWithErrors"
  | "Cancelled"
  | "Failed"
  | "QueueFailed";

export type CampaignRecipientStatus = "Pending" | "Sending" | "Sent" | "Failed" | "Skipped";

export interface Campaign {
  campaignId: string;
  businessId: string;
  createdByUserId: string;
  name: string;
  status: CampaignStatus;
  sourceType: "Segment" | "Import";
  filtersJson: string | null;
  templateName: string;
  languageCode: string;
  templateCategory: "Marketing" | "Utility";
  parameterMappingJson: string | null;
  scheduledAtUtc: string | null;
  recipientCount: number;
  sentCount: number;
  failedCount: number;
  createdAt: string;
  updatedAt: string | null;
  recipients?: CampaignRecipient[] | null;
}

export interface CampaignRecipient {
  campaignRecipientId: string;
  campaignId: string;
  businessId: string;
  phoneNormalized: string;
  customerName: string | null;
  sourceLeadId: string | null;
  sourceReservationId: string | null;
  status: CampaignRecipientStatus;
  whatsAppMessageId: string | null;
  error: string | null;
  variablesJson: string | null;
  attemptCount: number;
  createdAt: string;
  lastAttemptAtUtc: string | null;
  sentAt: string | null;
}

export interface CreateCampaignRequest {
  businessId: string;
  name: string;
  sourceType: "Segment" | "Import";
  audience: CampaignAudienceRequest;
  templateName: string;
  languageCode: string;
  templateCategory: "Marketing" | "Utility";
  bodyParameterKeys: string[];
  scheduledAtUtc?: string | null;
}

export interface CampaignAudienceRequest {
  segmentKey?: string | null;
  inactiveDays?: number | null;
  importedRecipients?: ImportedCampaignRecipientRequest[] | null;
}

export interface ImportedCampaignRecipientRequest {
  phone: string;
  customerName?: string | null;
  variables?: Record<string, string> | null;
}


export interface PaymentTransaction {
  paymentTransactionId: string;
  businessId: string;
  conversationId: string;
  reservationId: string | null;
  paymentReferenceId: string;
  providerTransactionId: string | null;
  amountInCents: number;
  currency: string;
  status: PaymentTransactionStatus;
  source: PaymentTransactionSource;
  createdAt: string;
  confirmedAt: string | null;
  webhookPayloadJson: string | null;
  business?: Business;
  conversation?: Conversation;
}

export interface Order {
  orderId: string;
  businessId: string;
  agentId: string | null;
  conversationId: string | null;
  integrationConnectionId: string | null;
  paymentTransactionId: string | null;
  source: OrderSource;
  fulfillmentMode: OrderFulfillmentMode;
  status: OrderStatus;
  customerName: string | null;
  customerEmail: string | null;
  customerPhone: string | null;
  customerDocument: string | null;
  deliveryAddress: string | null;
  notes: string | null;
  currency: string;
  subtotal: number;
  discountTotal: number;
  taxTotal: number;
  total: number;
  customerConfirmed: boolean;
  externalOrderId: string | null;
  externalDocumentNumber: string | null;
  externalStatus: string | null;
  customAttributesJson: string | null;
  createdAt: string;
  updatedAt: string | null;
  items: OrderItem[];
}

export interface OrderItem {
  orderItemId: string;
  orderId: string;
  businessId: string;
  productId: string | null;
  integrationConnectionId: string | null;
  externalProductId: string | null;
  sku: string | null;
  productName: string;
  description: string | null;
  quantity: number;
  unitPrice: number;
  discountAmount: number;
  taxAmount: number;
  lineTotal: number;
  createdAt: string;
  updatedAt: string | null;
}

export interface OrderSummary {
  totalOrders: number;
  totalAmount: number;
  draftCount: number;
  awaitingPaymentCount: number;
  confirmedCount: number;
  syncedCount: number;
  cancelledCount: number;
}

export interface AppUser {
  userId: string;
  tenantId: string;
  partyId?: string | null;
  username: string;
  email: string;
  firstName: string;
  lastName: string;
  phoneNumber: string | null;
  avatarUrl: string | null;
  isActive: boolean;
  emailConfirmed: boolean;
  lastLoginAt: string | null;
  createdAt: string;
  updatedAt: string | null;
  fullName?: string;
  roles?: UserRole[];
}

export interface AppRole {
  roleId: string;
  tenantId: string | null;
  name: string;
  description: string | null;
  isSystemRole: boolean;
  isActive: boolean;
  createdAt: string;
  updatedAt: string | null;
  permissions?: Permission[];
  userCount?: number;
  permissionCount?: number;
}

export interface Permission {
  permissionId: string;
  module: string;
  action: string;
  resource: string;
  description: string | null;
  createdAt: string;
}

export interface UserRole {
  userRoleId: string;
  userId: string;
  roleId: string;
  businessId: string | null;
  assignedAt: string;
  role?: AppRole;
}

export interface RolePermission {
  rolePermissionId: string;
  roleId: string;
  permissionId: string;
  assignedAt: string;
  permission?: Permission;
}

export interface AuditLog {
  auditLogId: string;
  userId: string | null;
  tenantId: string | null;
  businessId: string | null;
  action: string;
  entityType: string;
  entityId: string | null;
  oldValues: string | null;
  newValues: string | null;
  ipAddress: string | null;
  userAgent: string | null;
  correlationId: string | null;
  timestamp: string;
  user?: AppUser;
}

export interface SystemConfiguration {
  systemConfigurationId: SystemConfigurationKey;
  value: string;
  description: string | null;
  createdAt: string;
  updatedAt: string | null;
  isActive: boolean;
}

export interface BusinessInboundContact {
  businessInboundContactId: string;
  businessId: string;
  type: string;
  key: string;
  name: string;
  role: string;
  phoneNumber: string;
  phoneNormalized: string;
  inboundAgentId: string;
  inboundAgentName: string;
  employeeId: string | null;
  capabilitiesJson: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string | null;
}

export interface Agent {
  agentId: string;
  businessId: string;
  agentTypeId: string;
  agentTypeName: string;
  botType: AgentBotType;
    kind: string;
name: string;
  phoneNumber: string | null;
  description: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string | null;
  settingsJson: string | null;
  settings: Record<string, unknown> | null;
}
