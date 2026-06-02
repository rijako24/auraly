/** Catálogo estático de tools del motor (nombres en snake_case). */

export interface AgentToolMeta {
  name: string;
  label: string;
  group: string;
}

export const AGENT_TOOLS_CATALOG: AgentToolMeta[] = [
  { name: "set_fact", label: "Registrar dato", group: "Estado" },
  { name: "get_service_catalog", label: "Catálogo de servicios", group: "Catálogo" },
  { name: "check_availability", label: "Disponibilidad", group: "Agenda" },
  { name: "prepare_checkout", label: "Resumen de checkout", group: "Reserva" },
  { name: "create_reservation", label: "Crear reserva", group: "Reserva" },
  { name: "assign_paid_slot", label: "Asignar slot pagado", group: "Reserva" },
  { name: "verify_payment", label: "Verificar pago", group: "Pagos" },
  { name: "generate_payment_link", label: "Link de pago", group: "Pagos" },
  { name: "reschedule_reservation", label: "Reagendar", group: "Gestión" },
  { name: "suspend_reservation", label: "Cancelar / suspender", group: "Gestión" },
  { name: "escalate_to_human", label: "Escalar a humano", group: "Soporte" },
  { name: "resolve_pricing", label: "Resolver precios", group: "Catálogo" },
];

export const GUARD_REQUIRE_PREFIXES = [
  "fact:",
  "verification:",
  "state:",
  "expr:",
  "flag:",
] as const;
