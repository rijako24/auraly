import {
  BusinessConfigurationKey,
  LeadStatus,
  PaymentTransactionSource,
  PaymentTransactionStatus,
  ReservationStatus,
  ServiceTier,
  ServiceType,
  SystemConfigurationKey,
} from "./enums";

export interface Tenant {
  tenantId: string;
  name: string;
  email: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string | null;
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

export interface BusinessConfiguration {
  businessConfigurationId: string;
  businessId: string;
  key: BusinessConfigurationKey;
  value: string;
  description: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string | null;
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

export interface Service {
  serviceId: string;
  businessId: string;
  serviceName: string;
  description: string;
  durationMinutes: number;
  price: number;
  isActive: boolean;
  categoryId: string;
  tier: ServiceTier;
  serviceType: ServiceType;
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
  name: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string | null;
  services?: Service[];
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
  serviceId: string;
  employeeId: string;
  reservationDateTime: string;
  durationMinutes: number;
  status: ReservationStatus;
  calendarEventId: string | null;
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

export interface Conversation {
  conversationId: string;
  businessId: string;
  userNumber: string;
  lastMessage: string | null;
  timestamp: string;
  customerName: string | null;
  messages?: Message[];
  business?: Business;
}

export interface Message {
  messageId: string;
  conversationId: string;
  sender: "User" | "Bot";
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
  business?: Business;
}

export interface PaymentTransaction {
  paymentTransactionId: string;
  businessId: string;
  conversationId: string;
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

export interface AppUser {
  userId: string;
  tenantId: string;
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

/** Admin — agent list row */
export interface Agent {
  agentId: string;
  businessId: string;
  name: string;
  description: string | null;
  agentTypeName: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string | null;
}

export interface AgentPromptSection {
  agentPromptSectionId: string;
  key: string;
  title: string;
  content: string;
  injectionPoint: string;
  displayOrder: number;
}

export interface KnowledgeSourceAdmin {
  knowledgeSourceId: string;
  name: string;
  type: string;
  content: string;
  autoInject: boolean;
  displayOrder: number;
  createdAt: string;
}

export interface AgentDetail extends Agent {
  agentTypeId: string;
  settingsJson: string | null;
  promptSections: AgentPromptSection[];
  knowledgeSources: KnowledgeSourceAdmin[];
}

export interface AgentType {
  agentTypeId: string;
  name: string;
  description: string | null;
}

export interface FlowDefinitionAdmin {
  flowDefinitionId: string;
  agentId: string;
  name: string;
  description: string | null;
  definitionJson: string;
  version: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string | null;
}

export interface FlowPort {
  id: string;
  label: string;
}

export interface FlowNodeCatalogEntry {
  id: string;
  name: string;
  type: number;
  icon: string;
  inputs: FlowPort[];
  outputs: FlowPort[];
  configSchemaJson: string;
  category?: string | null;
  color?: string | null;
}

export interface FlowSubNodeConfig {
  id: string;
  label: string;
  slot: number;
  config: Record<string, unknown>;
}

export interface FlowSubNodeSet {
  extract?: FlowSubNodeConfig;
  actions?: FlowSubNodeConfig[];
  knowledge?: FlowSubNodeConfig[];
  event?: FlowSubNodeConfig;
  classifier?: FlowSubNodeConfig;
}

export interface FlowDocumentNode {
  id: string;
  type: number;
  label: string;
  config: Record<string, unknown>;
  subNodes?: FlowSubNodeSet;
  handlesIntent?: string;
}

export interface FlowDocumentEdge {
  id: string;
  sourceNodeId: string;
  targetNodeId: string;
  portId?: string | null;
}

export interface FlowRoutingIntent {
  key: string;
  description: string;
  examples?: string[];
  degradedRegex?: string;
}

export interface FlowDocument {
  variables: unknown[];
  intentionSchema: unknown[];
  routingIntents?: FlowRoutingIntent[];
  sessionConfig: Record<string, unknown>;
  engineSettings: Record<string, unknown>;
  nodes: FlowDocumentNode[];
  edges: FlowDocumentEdge[];
  extractionInstructions?: string;
}

export interface AgentChatRequest {
  message: string;
  resetSession?: boolean;
}

export interface AgentChatResponse {
  success: boolean;
  botResponse: string;
  errorMessage: string | null;
  isEscalated: boolean;
  isFlowComplete: boolean;
  currentNodeId: string | null;
  variables: Record<string, string | null>;
}
