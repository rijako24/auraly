export enum BusinessConfigurationKey {
  Personality = 0,
  EntityExtractionConfig = 1,
  SalesStrategy = 2,
  PaymentConfig = 3,
  OperatingHours = 4,
  PaymentMethods = 5,
  Integrations = 6,
  EscalationContacts = 7,
  PaymentConfirmationMessages = 8,
}

export const BusinessConfigurationKeyLabels: Record<BusinessConfigurationKey, string> = {
  [BusinessConfigurationKey.Personality]: "Personalidad",
  [BusinessConfigurationKey.EntityExtractionConfig]: "Extracción de Entidades",
  [BusinessConfigurationKey.SalesStrategy]: "Estrategia de Ventas",
  [BusinessConfigurationKey.PaymentConfig]: "Configuración de Pagos",
  [BusinessConfigurationKey.OperatingHours]: "Horarios de Operación",
  [BusinessConfigurationKey.PaymentMethods]: "Métodos de Pago",
  [BusinessConfigurationKey.Integrations]: "Integraciones",
  [BusinessConfigurationKey.EscalationContacts]: "Contactos de Escalamiento",
  [BusinessConfigurationKey.PaymentConfirmationMessages]: "Mensajes de Confirmación",
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
  [ReservationStatus.Completed]: "bg-blue-100 text-blue-800 dark:bg-blue-900 dark:text-blue-300",
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
  [PaymentTransactionStatus.Refunded]: "bg-purple-100 text-purple-800 dark:bg-purple-900 dark:text-purple-300",
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
  [ServiceTier.Deluxe]: "bg-violet-100 text-violet-800 dark:bg-violet-900 dark:text-violet-300",
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
  [LeadStatus.New]: "bg-blue-100 text-blue-800 dark:bg-blue-900 dark:text-blue-300",
  [LeadStatus.Contacted]: "bg-yellow-100 text-yellow-800 dark:bg-yellow-900 dark:text-yellow-300",
  [LeadStatus.Closed]: "bg-green-100 text-green-800 dark:bg-green-900 dark:text-green-300",
};
