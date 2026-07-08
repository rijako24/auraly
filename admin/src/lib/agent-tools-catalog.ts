/** Catalogo estatico de tools del motor (nombres en snake_case). */

export interface AgentToolMeta {
  name: string;
  label: string;
  group: string;
}

export const AGENT_TOOLS_CATALOG: AgentToolMeta[] = [
  { name: "set_fact", label: "Registrar dato", group: "Estado" },
  { name: "reset_flow_context", label: "Reiniciar contexto", group: "Estado" },
  { name: "send_message_sequence", label: "Enviar secuencia", group: "Mensajes" },

  { name: "get_service_catalog", label: "Catalogo de servicios", group: "Catalogo" },
  { name: "get_compatible_add_ons", label: "Complementos compatibles", group: "Catalogo" },
  { name: "get_service_fulfillment", label: "Fulfillment del servicio", group: "Catalogo" },
  { name: "resolve_pricing", label: "Resolver precios", group: "Catalogo" },

  { name: "check_availability", label: "Disponibilidad", group: "Agenda" },
  { name: "get_customer_reservations", label: "Reservas del cliente", group: "Agenda" },
  { name: "confirm_reservation_attendance", label: "Confirmar asistencia", group: "Agenda" },
  { name: "manage_reservation", label: "Gestionar reserva", group: "Agenda" },

  { name: "prepare_checkout", label: "Resumen de checkout", group: "Reserva" },
  { name: "create_reservation", label: "Crear reserva", group: "Reserva" },
  { name: "verify_payment", label: "Verificar pago", group: "Pagos" },

  { name: "search_products", label: "Buscar productos", group: "Pedidos" },
  { name: "add_order_item", label: "Agregar producto", group: "Pedidos" },
  { name: "remove_order_item", label: "Quitar producto", group: "Pedidos" },
  { name: "update_order_item_quantity", label: "Actualizar cantidad", group: "Pedidos" },
  { name: "get_order_draft", label: "Ver pedido", group: "Pedidos" },
  { name: "prepare_order_checkout", label: "Checkout de pedido", group: "Pedidos" },
  { name: "create_order", label: "Crear pedido", group: "Pedidos" },

  { name: "start_external_interaction", label: "Iniciar interaccion externa", group: "Externos" },
  { name: "resolve_external_interaction", label: "Resolver interaccion externa", group: "Externos" },
  { name: "complete_external_interaction", label: "Completar interaccion externa", group: "Externos" },

  { name: "operations_get_reservations", label: "Operaciones: agenda", group: "Operaciones" },
  { name: "operations_block_availability", label: "Operaciones: bloquear disponibilidad", group: "Operaciones" },
  { name: "operations_request_reschedule", label: "Operaciones: pedir reagendamiento", group: "Operaciones" },
  { name: "operations_get_business_metrics", label: "Operaciones: metricas", group: "Operaciones" },
  { name: "operations_get_customer_history", label: "Operaciones: historial cliente", group: "Operaciones" },

  { name: "escalate_to_human", label: "Escalar a humano", group: "Soporte" },
];

export const GUARD_REQUIRE_PREFIXES = [
  "fact:",
  "verification:",
  "state:",
  "expr:",
  "flag:",
] as const;
