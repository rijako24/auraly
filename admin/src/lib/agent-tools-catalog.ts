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
  { name: "prepare_order_checkout", label: "Checkout de pedido", group: "Pedidos" },
  { name: "create_reservation", label: "Crear reserva", group: "Reserva" },
  { name: "assign_paid_slot", label: "Asignar slot pagado", group: "Reserva" },
  { name: "verify_payment", label: "Verificar pago", group: "Pagos" },
  { name: "suspend_reservation", label: "Cancelar / suspender", group: "Gestión" },
  { name: "escalate_to_human", label: "Escalar a humano", group: "Soporte" },
  { name: "resolve_pricing", label: "Resolver precios", group: "Catálogo" },
  { name: "search_products", label: "Buscar productos", group: "Pedidos" },
  { name: "add_order_item", label: "Agregar producto", group: "Pedidos" },
  { name: "remove_order_item", label: "Quitar producto", group: "Pedidos" },
  { name: "get_order_draft", label: "Ver pedido", group: "Pedidos" },
  { name: "create_order", label: "Crear pedido", group: "Pedidos" },
  { name: "resolve_external_escalation", label: "Resolver escalamiento externo", group: "Externos" },
  { name: "accept_external_escalation", label: "Aceptar escalamiento externo", group: "Externos" },
  { name: "decline_external_escalation", label: "Rechazar escalamiento externo", group: "Externos" },
];

export const GUARD_REQUIRE_PREFIXES = [
  "fact:",
  "verification:",
  "state:",
  "expr:",
  "flag:",
] as const;
