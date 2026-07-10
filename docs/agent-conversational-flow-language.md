# Lenguaje Conversacional De Flujos

Este lenguaje configura agentes por etapas usando nombres exactos de tools. No hay aliases semanticos intermedios: el mismo nombre que aparece en `allowedActions` es el nombre que el LLM puede llamar por function calling.

## Principio

Lo conversacional puede ser natural. Lo que cambia estado, agenda, pago, reserva, pedido, escalamiento o datos debe pasar por una tool habilitada y visible para el turno.

El tenant define flows, etapas y politicas. Las tools definen el contrato tecnico ejecutable: nombre, descripcion y schema de parametros.

## Estructura Recomendada

```json
{
  "enabledTools": ["get_service_catalog", "resolve_service_selection", "set_fact"],
  "defaultFlow": "booking",
  "flows": [
    {
      "id": "booking",
      "type": "primary",
      "routingGuidance": "Usa este flow para reservas nuevas y consultas de catalogo.",
      "stageDetection": "automatic",
      "stages": [
        {
          "id": "discovery",
          "name": "Descubrimiento",
          "goal": "Ayudar al cliente a elegir un servicio exacto del catalogo.",
          "collect": ["booking_intent", "service"],
          "allowedActions": ["get_service_catalog", "resolve_service_selection", "set_fact"],
          "conversationGuidance": "Saludo generico: usa get_service_catalog y presenta categorias oficiales. Servicio puntual: usa get_service_catalog para presentar servicios oficiales relevantes. Seleccion clara: usa resolve_service_selection para resolver el servicio exacto.",
          "advanceWhenFacts": ["service"]
        }
      ]
    },
    {
      "id": "reservation_management",
      "type": "secondary",
      "ttlSeconds": 900,
      "routingGuidance": "Usa este flow solo cuando el cliente quiera gestionar una reserva existente.",
      "stageDetection": "automatic",
      "stages": [
        {
          "id": "reservation_management",
          "name": "Gestion de reserva existente",
          "allowedActions": ["get_customer_reservations", "manage_reservation", "escalate_to_human"],
          "entryActions": [{ "tool": "get_customer_reservations", "arguments": {} }],
          "conversationGuidance": "Usa manage_reservation para cambios de reservas existentes; la tool valida disponibilidad o escala segun politica."
        }
      ]
    }
  ]
}
```

`flow` legacy sigue siendo soportado como compatibilidad: si `flows` no existe, el motor lo trata como el default flow `booking`.

## Etapas

Una etapa debe leerse como una instruccion de negocio, pero sus acciones ejecutables usan nombres exactos de tools.

Campos principales:

- `id`: identificador estable de la etapa.
- `name`: nombre visible para admins y trazas.
- `goal`: objetivo de negocio de la etapa.
- `collect`: datos que la etapa puede recoger.
- `allowedActions`: nombres exactos de tools habilitadas que pueden usarse en esa etapa.
- `conversationGuidance`: guia hablada; cuando haga falta una tool, menciona el nombre exacto de la tool.
- `advanceWhenFacts`: facts que completan la etapa.
- `reentryOnFactChanged`: facts que obligan a recalcular una etapa ya procesada.
- `entryActions`: acciones declarativas que el motor puede ejecutar al entrar en una etapa cuando se cumplen condiciones de estado.
- `afterTool`: reglas declarativas para efectos posteriores a tools.

## Flows

- `defaultFlow`: flow primario al que vuelve el motor cuando no hay razon clara para otro flow.
- `flows[].type = primary`: proceso principal del agente, normalmente reservas nuevas o ventas nuevas.
- `flows[].type = secondary`: proceso multi-turn puntual y distinto del principal, por ejemplo gestionar una reserva existente.
- `flows[].routingGuidance`: criterio conversacional para que el router elija el flow correcto.
- `flows[].ttlSeconds`: ventana de continuidad para un flow secundario activo.

El router es conservador: solo activa un flow secundario con confianza alta. Si no hay confianza suficiente, usa el default flow. Si hay un flow secundario fresco, puede continuarlo cuando el mensaje encaja con facts pendientes o cuando el clasificador lo reconoce como continuidad con el historial reciente.

## Acciones Globales

Las acciones globales son para capacidades transversales y puntuales, no para procesos multi-turn largos. Ejemplos sanos: consultar catalogo sin perder la etapa activa o escalar a humano con una frase clara del cliente.

```json
{
  "id": "human_escalation",
  "priority": 1000,
  "goal": "Escalar a una persona cuando el cliente lo pida claramente.",
  "allowedActions": ["escalate_to_human"],
  "conversationGuidance": "Escala con resumen breve de la necesidad del cliente."
}
```

## Separacion Correcta

- `conversationGuidance`: como hablar y que criterio conversacional seguir; si exige una tool, usa su nombre exacto.
- `allowedActions`: que tools puede usar la etapa o accion global.
- `enabledTools`: set total de tools habilitadas para ese agente.
- `guards`: precondiciones de seguridad por capability.
- `entryActions`: mini-flujos deterministas de entrada. Sus condiciones soportan `requiredFacts`, `missingFacts`, `missingVerifications` y `messageMatches`.
- `defaultFlow` y `flows`: enrutamiento conversacional entre procesos.
- `afterTool`: efectos declarativos despues de una tool.
- `factSchema`: datos conocidos, scope y retencion.

## Checklist Para Crear Un Flujo

1. Define los facts en `factSchema`.
2. Habilita las tools necesarias en `enabledTools`.
3. Crea etapas con `goal`, `conversationGuidance`, `collect` y `allowedActions` usando nombres exactos de tools.
4. Usa `advanceWhenFacts` solo para datos obligatorios de avance.
5. Agrega `reentryOnFactChanged` cuando un cambio invalida una etapa previa.
6. Usa `entryActions` para ejecutar tools obligatorias cuando el estado lo permite.
7. Declara flows secundarios solo para procesos multi-turn claramente distintos del default flow.
8. Agrega acciones globales solo para acciones transversales y puntuales.
9. Valida que cada entrada de `allowedActions` exista en `enabledTools`.
10. Prueba happy path y edge cases desde consola contra base de datos y configuracion real.