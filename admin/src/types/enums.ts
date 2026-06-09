export enum BusinessConfigurationKey {
  Integrations = 0,
  PaymentConfirmationMessages = 1,
  SchedulingPolicy = 2,
}

export const BusinessConfigurationKeyLabels: Record<BusinessConfigurationKey, string> = {
  [BusinessConfigurationKey.Integrations]: "Integraciones",
  [BusinessConfigurationKey.PaymentConfirmationMessages]: "Mensajes de Confirmacion de Pago",
  [BusinessConfigurationKey.SchedulingPolicy]: "Política de Agendamiento",
};

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
  Created = 0,
  Confirmed = 1,
  Failed = 2,
  Refunded = 3,
  Expired = 4,
}

export const PaymentStatusLabels: Record<PaymentTransactionStatus, string> = {
  [PaymentTransactionStatus.Created]: "Creado",
  [PaymentTransactionStatus.Confirmed]: "Confirmado",
  [PaymentTransactionStatus.Failed]: "Fallido",
  [PaymentTransactionStatus.Refunded]: "Reembolsado",
  [PaymentTransactionStatus.Expired]: "Expirado",
};

export const PaymentStatusColors: Record<PaymentTransactionStatus, string> = {
  [PaymentTransactionStatus.Created]: "bg-yellow-100 text-yellow-800 dark:bg-yellow-900 dark:text-yellow-300",
  [PaymentTransactionStatus.Confirmed]: "bg-green-100 text-green-800 dark:bg-green-900 dark:text-green-300",
  [PaymentTransactionStatus.Failed]: "bg-red-100 text-red-800 dark:bg-red-900 dark:text-red-300",
  [PaymentTransactionStatus.Refunded]: "bg-cyan-100 text-cyan-800 dark:bg-cyan-900 dark:text-cyan-300",
  [PaymentTransactionStatus.Expired]: "bg-gray-100 text-gray-800 dark:bg-gray-900 dark:text-gray-300",
};

export enum PaymentTransactionSource {
  Automated = 0,
  Manual = 1,
}

export const PaymentSourceLabels: Record<PaymentTransactionSource, string> = {
  [PaymentTransactionSource.Automated]: "Automático",
  [PaymentTransactionSource.Manual]: "Manual",
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
