# Lenguaje Conversacional De Flujos

Este lenguaje permite configurar agentes por etapas habladas sin escribir reglas del motor ni depender de instrucciones imperativas. La idea es que negocio describa el flujo con palabras de dominio controladas, y el motor traduzca esas palabras a herramientas mediante configuracion.

## Principio

Lo conversacional puede ser natural. Lo que cambia estado, agenda, pago, reserva, pedido, escalamiento o datos debe estar declarado como accion semantica.

El motor entiende primitivas genericas. El tenant define el negocio.

## Estructura

```json
{
  "flow": {
    "stageDetection": "automatic",
    "language": {
      "actions": {
        "resolver_servicio": {
          "name": "Resolver servicio exacto",
          "purpose": "Convertir la seleccion del cliente en un servicio canonico del catalogo.",
          "tool": "resolve_service_selection",
          "requires": ["service"],
          "produces": ["service"],
          "whenMissingData": "Si no hay coincidencia unica, pide elegir entre opciones oficiales."
        }
      }
    },
    "stages": []
  }
}
```

## Etapas

Una etapa debe leerse como una instruccion de negocio, no como codigo.

```json
{
  "id": "discovery",
  "name": "Descubrimiento",
  "goal": "Ayudar al cliente a elegir un servicio exacto del catalogo.",
  "ask": "Que servicio te interesa?",
  "collect": ["booking_intent", "service"],
  "allowedActions": ["mostrar_catalogo", "resolver_servicio", "registrar_dato"],
  "conversationGuidance": "Saludo generico: presenta categorias oficiales. Servicio puntual: presenta servicios oficiales relevantes.",
  "advanceWhenFacts": ["service"]
}
```

Campos principales:

- `id`: identificador estable de la etapa.
- `name`: nombre visible para admins y trazas.
- `goal`: objetivo de negocio de la etapa.
- `ask`: pregunta conversacional sugerida cuando falta input.
- `collect`: datos que la etapa puede recoger.
- `allowedActions`: acciones semanticas permitidas en esta etapa.
- `conversationGuidance`: guia hablada, sin mini-prompts tecnicos ni ejemplos quemados.
- `advanceWhenFacts`: facts que completan la etapa.
- `reentryOnFactChanged`: facts que obligan a recalcular una etapa ya procesada.
- `afterTool`: reglas declarativas existentes para efectos posteriores a tools.

## Acciones Semanticas

Una accion semantica conecta lenguaje de negocio con una tool tecnica. El motor no debe conocer nombres de servicios, categorias, tenants ni reglas particulares.

```json
"validar_disponibilidad": {
  "name": "Validar disponibilidad",
  "purpose": "Confirmar en agenda la fecha y hora solicitadas, o presentar horarios disponibles.",
  "tool": "check_availability",
  "requires": ["service", "desired_date"],
  "produces": ["desired_date", "desired_time", "availability_checked"],
  "whenMissingData": "Si falta fecha u hora, pregunta solo el dato necesario; si hay slots, presentalos y aplica el seleccionado."
}
```

Campos:

- `name`: nombre humano de la accion.
- `purpose`: para que sirve en lenguaje de negocio.
- `tool`: tool tecnica configurada para ejecutar la accion.
- `requires`: facts que normalmente necesita.
- `produces`: facts o resultados que puede producir.
- `whenMissingData`: comportamiento conversacional si falta informacion.
- `onSuccess`: comportamiento conversacional esperado si sale bien.
- `onProblem`: comportamiento conversacional esperado si falla.

## Acciones Globales

Las acciones globales permiten interrumpir la etapa actual sin quemar reglas en el motor.

```json
{
  "id": "manage_existing_reservation",
  "priority": 900,
  "goal": "Gestionar reservas existentes cuando el cliente quiera cambiar, cancelar o completar una reserva ya pagada.",
  "runtimeWhenAny": ["context:ManageableReservations.any"],
  "allowedActions": ["obtener_reservas_cliente", "gestionar_reserva"]
}
```

## Reglas De Configuracion

- No poner nombres de servicios o categorias en C#.
- La guia conversacional vive en `conversationGuidance`.
- No poner instrucciones imperativas tecnicas en `conversationGuidance`.
- No inventar catalogo: todo servicio, categoria o adicional debe venir de herramientas o catalogo configurado.
- No bloquear avance por datos opcionales. Solo `advanceWhenFacts` debe detener etapa.

## Separacion Correcta

- `conversationGuidance`: como hablar y que criterio conversacional seguir.
- `allowedActions`: que puede hacer la etapa.
- `language.actions`: como se traduce una accion de negocio a una tool.
- `guards`: precondiciones de seguridad por capability.
- `afterTool`: efectos declarativos despues de una tool.
- `factSchema`: datos conocidos, scope y retencion.

## Checklist Para Crear Un Flujo

1. Define los facts en `factSchema`.
2. Define las acciones semanticas en `flow.language.actions`.
3. Crea etapas con `goal`, `ask`, `collect` y `allowedActions`.
4. Usa `advanceWhenFacts` solo para datos obligatorios de avance.
5. Agrega `reentryOnFactChanged` cuando un cambio invalida una etapa previa.
6. Agrega acciones globales para interrupciones transversales.
7. Valida que cada accion tenga tool existente en `enabledTools`.
8. Prueba happy path y edge cases desde consola.
