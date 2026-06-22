export enum ReservationStatus {
  Pending = 0,
  Confirmed = 1,
  Completed = 2,
  Cancelled = 3,
  PendingCalendar = 4,
  OnHold = 5,
}

export const ReservationStatusLabels: Record<ReservationStatus, string> = {
  [ReservationStatus.Pending]: "Pendiente",
  [ReservationStatus.Confirmed]: "Confirmada",
  [ReservationStatus.Completed]: "Completada",
  [ReservationStatus.Cancelled]: "Cancelada",
  [ReservationStatus.PendingCalendar]: "Pendiente Calendario",
  [ReservationStatus.OnHold]: "En Espera",
};

export const ReservationStatusColors: Record<ReservationStatus, string> = {
  [ReservationStatus.Pending]: "bg-yellow-100 text-yellow-800 dark:bg-yellow-900 dark:text-yellow-300",
  [ReservationStatus.Confirmed]: "bg-green-100 text-green-800 dark:bg-green-900 dark:text-green-300",
  [ReservationStatus.Completed]: "bg-teal-100 text-teal-800 dark:bg-teal-900 dark:text-teal-300",
  [ReservationStatus.Cancelled]: "bg-red-100 text-red-800 dark:bg-red-900 dark:text-red-300",
  [ReservationStatus.PendingCalendar]: "bg-orange-100 text-orange-800 dark:bg-orange-900 dark:text-orange-300",
  [ReservationStatus.OnHold]: "bg-gray-100 text-gray-800 dark:bg-gray-900 dark:text-gray-300",
};

export enum PaymentTransactionStatus {
  Created = "Created",
  Confirmed = "Confirmed",
  Failed = "Failed",
  Refunded = "Refunded",
  Expired = "Expired",
  Abandoned = "Abandoned",
  Superseded = "Superseded",
}

export const PaymentStatusLabels: Record<PaymentTransactionStatus, string> = {
  [PaymentTransactionStatus.Created]: "Creado",
  [PaymentTransactionStatus.Confirmed]: "Confirmado",
  [PaymentTransactionStatus.Failed]: "Fallido",
  [PaymentTransactionStatus.Refunded]: "Reembolsado",
  [PaymentTransactionStatus.Expired]: "Expirado",
  [PaymentTransactionStatus.Abandoned]: "Abandonado",
  [PaymentTransactionStatus.Superseded]: "Reemplazado",
};

export const PaymentStatusColors: Record<PaymentTransactionStatus, string> = {
  [PaymentTransactionStatus.Created]: "bg-yellow-100 text-yellow-800 dark:bg-yellow-900 dark:text-yellow-300",
  [PaymentTransactionStatus.Confirmed]: "bg-green-100 text-green-800 dark:bg-green-900 dark:text-green-300",
  [PaymentTransactionStatus.Failed]: "bg-red-100 text-red-800 dark:bg-red-900 dark:text-red-300",
  [PaymentTransactionStatus.Refunded]: "bg-cyan-100 text-cyan-800 dark:bg-cyan-900 dark:text-cyan-300",
  [PaymentTransactionStatus.Expired]: "bg-gray-100 text-gray-800 dark:bg-gray-900 dark:text-gray-300",
  [PaymentTransactionStatus.Abandoned]: "bg-gray-100 text-gray-800 dark:bg-gray-900 dark:text-gray-300",
  [PaymentTransactionStatus.Superseded]: "bg-slate-100 text-slate-800 dark:bg-slate-800 dark:text-slate-300",
};

export enum PaymentTransactionSource {
  Automated = "Automated",
  Manual = "Manual",
}

export const PaymentSourceLabels: Record<PaymentTransactionSource, string> = {
  [PaymentTransactionSource.Automated]: "Automático",
  [PaymentTransactionSource.Manual]: "Manual",
};

export enum OrderStatus {
  Draft = "Draft",
  PendingConfirmation = "PendingConfirmation",
  Confirmed = "Confirmed",
  SyncPending = "SyncPending",
  Synced = "Synced",
  SyncFailed = "SyncFailed",
  Cancelled = "Cancelled",
  AwaitingPayment = "AwaitingPayment",
  Expired = "Expired",
}

export const OrderStatusLabels: Record<OrderStatus, string> = {
  [OrderStatus.Draft]: "Borrador",
  [OrderStatus.PendingConfirmation]: "Pendiente",
  [OrderStatus.Confirmed]: "Confirmado",
  [OrderStatus.SyncPending]: "Por sincronizar",
  [OrderStatus.Synced]: "Sincronizado",
  [OrderStatus.SyncFailed]: "Fallo sync",
  [OrderStatus.Cancelled]: "Cancelado",
  [OrderStatus.AwaitingPayment]: "Esperando pago",
  [OrderStatus.Expired]: "Expirado",
};

export const OrderStatusColors: Record<OrderStatus, string> = {
  [OrderStatus.Draft]: "bg-slate-100 text-slate-800 dark:bg-slate-800 dark:text-slate-300",
  [OrderStatus.PendingConfirmation]: "bg-yellow-100 text-yellow-800 dark:bg-yellow-900 dark:text-yellow-300",
  [OrderStatus.Confirmed]: "bg-green-100 text-green-800 dark:bg-green-900 dark:text-green-300",
  [OrderStatus.SyncPending]: "bg-cyan-100 text-cyan-800 dark:bg-cyan-900 dark:text-cyan-300",
  [OrderStatus.Synced]: "bg-teal-100 text-teal-800 dark:bg-teal-900 dark:text-teal-300",
  [OrderStatus.SyncFailed]: "bg-red-100 text-red-800 dark:bg-red-900 dark:text-red-300",
  [OrderStatus.Cancelled]: "bg-gray-100 text-gray-800 dark:bg-gray-900 dark:text-gray-300",
  [OrderStatus.AwaitingPayment]: "bg-orange-100 text-orange-800 dark:bg-orange-900 dark:text-orange-300",
  [OrderStatus.Expired]: "bg-gray-100 text-gray-800 dark:bg-gray-900 dark:text-gray-300",
};

export enum OrderSource {
  Bot = "Bot",
  Admin = "Admin",
  Api = "Api",
}

export const OrderSourceLabels: Record<OrderSource, string> = {
  [OrderSource.Bot]: "Bot",
  [OrderSource.Admin]: "Admin",
  [OrderSource.Api]: "API",
};

export enum OrderFulfillmentMode {
  Local = "Local",
  External = "External",
}

export const OrderFulfillmentModeLabels: Record<OrderFulfillmentMode, string> = {
  [OrderFulfillmentMode.Local]: "Local",
  [OrderFulfillmentMode.External]: "Externo",
};

export enum ServiceType {
  Standard = 0,
  AddOn = 1,
}

export const ServiceTypeLabels: Record<ServiceType, string> = {
  [ServiceType.Standard]: "Estándar",
  [ServiceType.AddOn]: "Add-on",
};

export const ServiceTypeColors: Record<ServiceType, string> = {
  [ServiceType.Standard]: "bg-slate-100 text-slate-800 dark:bg-slate-800 dark:text-slate-300",
  [ServiceType.AddOn]: "bg-teal-100 text-teal-800 dark:bg-teal-900 dark:text-teal-300",
};

export enum ServiceTier {
  Base = 0,
  Premium = 1,
  Deluxe = 2,
}

export const ServiceTierLabels: Record<ServiceTier, string> = {
  [ServiceTier.Base]: "Base",
  [ServiceTier.Premium]: "Premium",
  [ServiceTier.Deluxe]: "Deluxe",
};

export const ServiceTierColors: Record<ServiceTier, string> = {
  [ServiceTier.Base]: "bg-slate-100 text-slate-800 dark:bg-slate-800 dark:text-slate-300",
  [ServiceTier.Premium]: "bg-amber-100 text-amber-800 dark:bg-amber-900 dark:text-amber-300",
  [ServiceTier.Deluxe]: "bg-cyan-100 text-cyan-800 dark:bg-cyan-900 dark:text-cyan-300",
};

export enum ConversationStateEnum {
  Idle = 0,
  CollectingData = 1,
  CheckingAvailability = 2,
  ReadyToReserve = 3,
  CreatingReservation = 4,
  WaitingForPayment = 5,
  Confirmed = 6,
}

export const ConversationStateLabels: Record<ConversationStateEnum, string> = {
  [ConversationStateEnum.Idle]: "Inactiva",
  [ConversationStateEnum.CollectingData]: "Recopilando datos",
  [ConversationStateEnum.CheckingAvailability]: "Verificando disponibilidad",
  [ConversationStateEnum.ReadyToReserve]: "Lista para reservar",
  [ConversationStateEnum.CreatingReservation]: "Creando reservación",
  [ConversationStateEnum.WaitingForPayment]: "Esperando pago",
  [ConversationStateEnum.Confirmed]: "Confirmada",
};

export const ConversationStateColors: Record<ConversationStateEnum, string> = {
  [ConversationStateEnum.Idle]: "bg-gray-100 text-gray-800 dark:bg-gray-800 dark:text-gray-300",
  [ConversationStateEnum.CollectingData]: "bg-teal-100 text-teal-800 dark:bg-teal-900 dark:text-teal-300",
  [ConversationStateEnum.CheckingAvailability]: "bg-amber-100 text-amber-800 dark:bg-amber-900 dark:text-amber-300",
  [ConversationStateEnum.ReadyToReserve]: "bg-teal-100 text-teal-800 dark:bg-teal-900 dark:text-teal-300",
  [ConversationStateEnum.CreatingReservation]: "bg-cyan-100 text-cyan-800 dark:bg-cyan-900 dark:text-cyan-300",
  [ConversationStateEnum.WaitingForPayment]: "bg-orange-100 text-orange-800 dark:bg-orange-900 dark:text-orange-300",
  [ConversationStateEnum.Confirmed]: "bg-green-100 text-green-800 dark:bg-green-900 dark:text-green-300",
};

export enum ConversationLifecycleStatus {
  Active = "Active",
  Closed = "Closed",
}

export const ConversationLifecycleStatusLabels: Record<ConversationLifecycleStatus, string> = {
  [ConversationLifecycleStatus.Active]: "Activa",
  [ConversationLifecycleStatus.Closed]: "Cerrada",
};

export const ConversationLifecycleStatusColors: Record<ConversationLifecycleStatus, string> = {
  [ConversationLifecycleStatus.Active]: "bg-teal-100 text-teal-800 dark:bg-teal-900 dark:text-teal-300",
  [ConversationLifecycleStatus.Closed]: "bg-gray-100 text-gray-800 dark:bg-gray-800 dark:text-gray-300",
};

export function getConversationStageLabel(stageName?: string | null): string {
  return stageName?.trim() || "Sin etapa";
}

function getNormalizedConversationStage(stageName?: string | null): string {
  return (stageName ?? "")
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .toLowerCase()
    .trim();
}

export function getConversationStageColor(): string {
  return "border text-white";
}

export function getConversationStageStyle(stageName?: string | null): Record<string, string> {
  const normalized = getNormalizedConversationStage(stageName);
  const color = getConversationStageAccentColor(normalized);

  return {
    backgroundColor: `${color}26`,
    borderColor: color,
    boxShadow: `0 0 0 1px ${color}33`,
    color: "#ffffff",
  };
}

function getConversationStageAccentColor(normalized: string): string {
  if (!normalized) {
    return "#64748b";
  }

  if (
    normalized.includes("reserva creada") ||
    normalized.includes("confirmada") ||
    normalized.includes("cierre") ||
    normalized.includes("finalization")
  ) {
    return "#22c55e";
  }

  if (normalized.includes("pago") || normalized.includes("checkout")) {
    return "#10b981";
  }

  if (
    normalized.includes("agenda") ||
    normalized.includes("disponibilidad") ||
    normalized.includes("scheduling")
  ) {
    return "#f59e0b";
  }

  if (
    normalized.includes("datos") ||
    normalized.includes("cliente") ||
    normalized.includes("envio") ||
    normalized.includes("customer") ||
    normalized.includes("shipping")
  ) {
    return "#f97316";
  }

  if (normalized.includes("complement")) {
    return "#d946ef";
  }

  if (
    normalized.includes("servicio") ||
    normalized.includes("producto") ||
    normalized.includes("selection")
  ) {
    return "#06b6d4";
  }

  if (normalized.includes("descubr") || normalized.includes("discovery")) {
    return "#38bdf8";
  }

  return "#94a3b8";
}

export enum SystemConfigurationKey {
  ToneAndStyle = 1,
  HumanEscalationErrorThreshold = 2,
}

export const SystemConfigurationKeyLabels: Record<SystemConfigurationKey, string> = {
  [SystemConfigurationKey.ToneAndStyle]: "Tono y Estilo",
  [SystemConfigurationKey.HumanEscalationErrorThreshold]: "Umbral de Escalamiento",
};

export enum LeadStatus {
  New = "New",
  Contacted = "Contacted",
  Closed = "Closed",
}

export const LeadStatusLabels: Record<LeadStatus, string> = {
  [LeadStatus.New]: "Nuevo",
  [LeadStatus.Contacted]: "Contactado",
  [LeadStatus.Closed]: "Cerrado",
};

export const LeadStatusColors: Record<LeadStatus, string> = {
  [LeadStatus.New]: "bg-teal-100 text-teal-800 dark:bg-teal-900 dark:text-teal-300",
  [LeadStatus.Contacted]: "bg-yellow-100 text-yellow-800 dark:bg-yellow-900 dark:text-yellow-300",
  [LeadStatus.Closed]: "bg-green-100 text-green-800 dark:bg-green-900 dark:text-green-300",
};

export enum PromotionItemType {
  Any = 0,
  Product = 1,
  Service = 2,
  ProductCategory = 3,
  ServiceCategory = 4,
  AnyProduct = 5,
  AnyService = 6,
}

export const PromotionItemTypeLabels: Record<PromotionItemType, string> = {
  [PromotionItemType.Any]: "Cualquier item",
  [PromotionItemType.Product]: "Producto",
  [PromotionItemType.Service]: "Servicio",
  [PromotionItemType.ProductCategory]: "Categoria producto",
  [PromotionItemType.ServiceCategory]: "Categoria servicio",
  [PromotionItemType.AnyProduct]: "Cualquier producto",
  [PromotionItemType.AnyService]: "Cualquier servicio",
};

export enum PromotionBenefitType {
  PercentageDiscount = 0,
  AmountDiscount = 1,
  FixedUnitPrice = 2,
  FreeItem = 3,
}

export const PromotionBenefitTypeLabels: Record<PromotionBenefitType, string> = {
  [PromotionBenefitType.PercentageDiscount]: "Porcentaje",
  [PromotionBenefitType.AmountDiscount]: "Valor fijo",
  [PromotionBenefitType.FixedUnitPrice]: "Precio fijo",
  [PromotionBenefitType.FreeItem]: "Gratis",
};



