# Manual Del Motor Agentic

Este documento es una guia estable para entender, cambiar y revisar el motor agentic de Talkio AI. Debe usarse antes de tocar prompts, `Agents.SettingsJson`, tools, flows, seeds, reservas, checkout, pagos, escalaciones o comportamiento conversacional.

## Proposito Del Motor

El motor agentic atiende conversaciones de WhatsApp de forma generica y multitenant. Su trabajo es convertir mensajes naturales en decisiones seguras de negocio: consultar catalogo, capturar facts, cotizar, validar disponibilidad, crear reservas, gestionar pagos, enviar secuencias, escalar a humanos y conservar memoria util.

El motor no debe depender de un negocio especifico. Cada tenant expresa su comportamiento en configuracion, catalogo, plantillas, secuencias, facts, flows, policies e integraciones.

## Flujo Runtime

1. `WhatsAppMessageProcessorService` recibe el mensaje, resuelve negocio, conversacion y agente activo.
2. `AgentConversationService` carga historial, estado, facts, memoria y configuracion del agente.
3. `AgentConfigProvider` parsea `Agents.SettingsJson`, normaliza flows y valida referencias.
4. `AgentPromptComposer` arma el prompt con persona, politicas, flows, etapa activa, facts, tools, catalogo/contexto y guardrails.
5. `AgentToolRegistry` filtra las tools habilitadas por `enabledTools`, etapa, accion global y guards.
6. `AzureOpenAIChatClient` ejecuta el turno con function calling.
7. Las tools hacen cambios deterministas de negocio y devuelven datos estructurados.
8. El motor persiste mensajes, facts, checkpoints, verificaciones, efectos y mensajes outbound.
9. Los servicios outbound envian WhatsApp, adjuntos, notificaciones o eventos segun corresponda.

## Fuentes De Verdad

- Codigo de motor: `src/Application/MimosBabySpa.Application/Agents`.
- Configuracion viva del agente: `Agents.SettingsJson`.
- Seed principal por tenant: scripts en `database/MimosBabySpa.Database/Scripts/Seeds`.
- Catalogo de servicios/productos: tablas e integraciones, nunca texto duplicado en prompts.
- Contratos de tools: implementaciones en `Agents/Tools/Impl` y metadata del registry.
- Admin declarativo: `admin/src/types/agent-settings.ts`.
- Documentacion relacionada: `docs/agent-conversational-flow-language.md`, `docs/agent-engine-technical-debt.md`, `docs/diseno-mensajes-post-reserva.md`.

## Principios Obligatorios De Cambio

- Todo cambio del motor debe ser generico, multitenant, medible y comprobado.
- Antes de agregar codigo, buscar si ya existe una capacidad parecida y preferir unificar o centralizar.
- Evitar parches puntuales. Una correccion debe resolver la causa, no solo el caso observado.
- Evitar redundancia entre prompt, seed, codigo, templates y tests. Una regla debe vivir en el lugar con mas autoridad.
- Evitar quemar nombres de negocios, telefonos, frases de un tenant, servicios, precios, horarios, ciudades, adjuntos o outcomes en codigo del motor.
- Si el comportamiento varia por tenant, debe moverse a `SettingsJson`, catalogo, integracion, seed o tabla de configuracion.
- Si una tool cambia estado, agenda, pago, reserva, pedido, escalacion o datos persistidos, debe tener contrato explicito, validacion y prueba.
- Si un dato puede quedar obsoleto por cambio de servicio, fecha, hora, add-ons, metodo de pago o customer facts, debe invalidarse o recalcularse.
- Si una decision depende de disponibilidad, precio, catalogo, pago o reserva, debe consultar backend/tool; no debe confiar en texto generado.
- Todo cambio relevante debe tener una forma de medicion: test, escenario de consola, log verificable, assert de estado o validacion contra SQL.
- Las decisiones deterministas pertenecen a servicios de dominio/aplicacion. El LLM solo decide conversacion y selecciona tools dentro del contrato permitido.

## Prompts Y Guidance

Los prompts deben decir que hacer, con instrucciones afirmativas y verificables. La guidance debe orientar el comportamiento esperado, los datos a pedir, la tool exacta a usar y el criterio de avance.

Premisas para prompts:

- Escribir acciones positivas: "usa `get_service_catalog` para presentar servicios oficiales", "pide fecha y hora cuando falten", "confirma el resumen usando datos de la tool".
- Usar nombres exactos de tools cuando el LLM deba llamarlas.
- Mantener la voz del negocio en `persona`, `policies`, `conversationGuidance`, `templates` o `messageSequences`.
- Mantener facts, tools, guards y flow alineados: lo que se pide en prompt debe existir como tool/fact/guard.
- Preferir instrucciones cortas, accionables y sin duplicar reglas de codigo.
- Evitar listas de prohibiciones como mecanismo principal de control conversacional.
- Evitar ejemplos de frases cuando puedan rigidizar el agente o filtrar comportamiento de un tenant a otro.
- Evitar repetir catalogo, precios, disponibilidad, horarios o condiciones que ya vienen de servicios/backend.
- Evitar prompt patches para corregir bugs deterministas. Si el problema es validacion, estado, invalidacion, pricing, disponibilidad o routing, corregir el motor.

## SettingsJson: Propiedades Raiz

| Propiedad | Responsabilidad |
| --- | --- |
| `model` | Deployment/model usado por el agente. Default actual: `gpt-4.1-mini`. |
| `temperature` | Creatividad del modelo. Mantener baja/moderada en flujos transaccionales. |
| `maxToolIterations` | Limite anti-loop de llamadas a tools por turno. |
| `historyWindowSize` | Cantidad de mensajes historicos enviados al LLM. |
| `consecutiveErrorEscalationThreshold` | Errores consecutivos antes de auto-escalar. |
| `persona` | Identidad, tono y rol del agente. |
| `policies` | Politicas operativas del negocio/tenant. |
| `enabledTools` | Set total de tools disponibles para el agente. |
| `defaultFlow` | Flow primario preferido cuando existen `flows`. |
| `flow` | Definicion legacy de un unico flow; se normaliza como flow primario. |
| `flows` | Lista de procesos conversacionales primarios y secundarios. |
| `globalActions` | Acciones transversales puntuales disponibles fuera de la etapa activa. |
| `factSchema` | Schema de datos que el agente puede conocer, persistir, hidratar o invalidar. |
| `guards` | Precondiciones declarativas por capability. |
| `templates` | Plantillas reutilizables requeridas por tools o respuestas. |
| `messageSequences` | Catalogo nombrado de mensajes outbound y adjuntos. |
| `webhooks` | Mapeo de outcomes externos hacia acciones/secuencias. |
| `notifications` | Notificaciones internas por evento del motor. |
| `reservationAutomations` | Automatizaciones de confirmacion, recordatorio u otras acciones de reserva. |
| `reservationManagement` | Politica generica para cambios sobre reservas existentes. |
| `checkout` | Modos de checkout, moneda, metodos de pago, shipping y bindings. |
| `commerce` | Configuracion de comercio/productos y proveedor. |
| `operatingHours` | Reglas de atencion por horarios y grupos bloqueados. |
| `escalations` | Configuracion unificada de escalacion humana o externa. |

## Flows

Un flow representa un proceso conversacional. El flow primario es el centro de gravedad; los secundarios se usan para procesos multi-turn distintos, como gestionar una reserva existente.

| Propiedad | Responsabilidad |
| --- | --- |
| `id` | Identificador estable del flow. |
| `type` | `primary` o `secondary`. |
| `routingGuidance` | Criterio natural para que el router elija el flow. |
| `ttlSeconds` | Ventana de continuidad para flows secundarios. |
| `stageDetection` | Estrategia de deteccion de etapa; hoy se usa principalmente `automatic`. |
| `stages` | Etapas ordenadas del proceso. |

Reglas:

- El router debe ser conservador con flows secundarios.
- Si no hay senal clara para un flow secundario, volver al primario.
- El routing debe ser generico y declarativo, sin keywords de un tenant quemadas en codigo.

## Stages

Una etapa declara un objetivo de negocio y las tools exactas que se pueden usar en ese momento.

| Propiedad | Responsabilidad |
| --- | --- |
| `id` | Identificador estable de la etapa. |
| `name` | Nombre corto para admins/trazas. |
| `goal` | Objetivo narrativo de negocio. |
| `collect` | Facts que la etapa puede recoger. |
| `allowedActions` | Tools exactas permitidas en la etapa. |
| `entryActions` | Tools que el motor puede ejecutar al entrar si se cumplen condiciones. |
| `conversationGuidance` | Instruccion hablada de que hacer en la etapa. |
| `onSuccess` | Guia de respuesta ante resultado exitoso. |
| `onProblem` | Guia de respuesta ante problema recuperable. |
| `advanceWhenFacts` | Facts requeridos para avanzar. |
| `reentryOnFactChanged` | Facts que fuerzan recalculo o repeticion de acciones dependientes. |
| `skipWhen` | Expresion de facts que permite saltar la etapa. |
| `autoSetOnSkip` | Facts que se fijan automaticamente al saltar. |
| `afterTool` | Reglas declarativas tras ejecutar una tool. |
| `variants` | Variantes por contexto de engagement. |

## Entry Actions

`entryActions` son mini-flujos deterministas al entrar a una etapa o accion global.

| Propiedad | Responsabilidad |
| --- | --- |
| `tool` | Nombre exacto de la tool a ejecutar. |
| `arguments` | Argumentos fijos/declarativos para la tool. |
| `when.requiredFacts` | Facts que deben existir. |
| `when.missingFacts` | Facts que deben faltar. |
| `when.missingVerifications` | Verificaciones que deben faltar. |
| `when.messageMatches[].anyOf` | Senales textuales declarativas para activar la accion. |

## After Tool

`afterTool` permite convertir resultados estructurados de tools en efectos declarativos sin quemar dominio en el motor.

| Propiedad | Responsabilidad |
| --- | --- |
| `tool` | Tool cuyo resultado se evalua. |
| `when.path` | Ruta dentro del resultado estructurado. |
| `when.equals` | Valor esperado para activar la regla. |
| `when.notEquals` | Valor que no debe coincidir para activar la regla. |
| `setFact.key` | Fact unico a persistir. |
| `setFact.value` | Valor del fact unico. |
| `setFacts` | Varios facts a persistir. |
| `sendMessageSequence` | Secuencia outbound a encolar. |
| `sendOncePerConversation` | Evita duplicar la secuencia en la conversacion. |

## Global Actions

Las acciones globales son capacidades transversales puntuales. Deben usarse para cosas como escalacion clara, consulta puntual de catalogo o accion que no depende de una etapa concreta.

| Propiedad | Responsabilidad |
| --- | --- |
| `id` | Identificador estable. |
| `priority` | Orden relativo si aplica. |
| `goal` | Objetivo de la accion. |
| `conversationGuidance` | Guia positiva de uso. |
| `allowedActions` | Tools exactas habilitadas. |
| `entryActions` | Acciones deterministas condicionales. |

## Fact Schema

Los facts son el contrato de datos del agente. Permiten persistencia, hidratacion, invalidacion y binding con tools sin depender de nombres arbitrarios.

| Propiedad | Responsabilidad |
| --- | --- |
| `key` | Clave tecnica del fact. |
| `role` | Rol semantico universal, por ejemplo `customer.name` o `booking.service`. |
| `label` | Nombre legible para el LLM/admin. |
| `type` | Tipo esperado: `string`, `number`, `date`, `time`, `phone`, `email`, etc. |
| `required` | Indica si el dato es obligatorio para el flujo. |
| `source` | Origen: `user`, `channel` o `system`. |
| `showInCollectedInfo` | Si se muestra en informacion recolectada de reserva/calendario. |
| `defaultValue` | Valor por defecto hidratable. |
| `scope` | `customer`, `request` o `ephemeral`. |
| `retentionDays` | Ventana de retencion. |
| `expireOnBusinessDayChange` | Expira al cambiar el dia del negocio. |
| `dependsOn` | Facts que invalidan este fact cuando cambian. |
| `valueSource` | Autoridad para validar/canonicalizar valores. |
| `aliases` | Alias de claves para facts no respaldados por catalogo. |

Reglas:

- Facts de catalogo deben resolverse por tool o servicio autoritativo.
- Facts derivados o verificaciones deben depender de los facts fuente.
- Facts customer-scoped deben ser estables y no depender de datos de una solicitud puntual.

## Guards

`guards` define precondiciones por capability, normalmente bajo claves `capability:<id>`.

| Propiedad | Responsabilidad |
| --- | --- |
| `requires` | Facts o verificaciones requeridas antes de permitir una capability. |

Los guards protegen acciones sensibles como reservar, cotizar, cobrar, cambiar reservas o confirmar estados.

## Templates

`templates` contiene textos reutilizables que las tools pueden requerir por `templateId`. Deben centralizar formatos de resumen, disponibilidad, checkout y respuestas estructuradas.

Reglas:

- Si una tool declara template requerido, el seed debe proveerlo.
- Una plantilla debe usar datos de tool/facts, no informacion quemada.
- Si varias tools usan el mismo formato, centralizar en un template comun.

## Message Sequences

`messageSequences` es un catalogo de mensajes outbound nombrados. Sirve para textos, adjuntos, botones o plantillas WhatsApp.

| Propiedad | Responsabilidad |
| --- | --- |
| `messages` | Lista ordenada de pasos outbound. |
| `type` | Tipo de paso: texto, attachment, template u otro soportado. |
| `body` | Cuerpo del mensaje. |
| `attachmentId` | Adjunto del negocio. |
| `buttons` | Botones interactivos. |
| `templateName` | Template oficial de WhatsApp. |
| `language` | Idioma del template. |
| `headerParameters` | Parametros de header. |
| `bodyParameters` | Parametros de body. |

Reglas:

- La confirmacion debe vivir en un solo lugar por tenant: respuesta del LLM o secuencia, segun la guidance.
- Las tools de reserva deben crear datos/efectos; el envio se gobierna por sequence, webhook, notification o processor.

## Webhooks, Notifications Y Automations

| Propiedad | Responsabilidad |
| --- | --- |
| `webhooks.wompi.<outcome>.sendMessageSequence` | Secuencia a enviar cuando Wompi produce ese outcome. |
| `notifications.<event>.enabled` | Activa notificacion interna. |
| `notifications.<event>.recipients` | Destinatarios configurados. |
| `notifications.<event>.sendMessageSequence` | Secuencia que se envia a destinatarios. |
| `reservationAutomations.confirmation` | Automatizacion asociada a confirmaciones. |
| `reservationAutomations.reminder` | Automatizacion asociada a recordatorios. |
| `reservationAutomations.*.trigger` | Momento/condicion de disparo. |
| `reservationAutomations.*.actions` | Acciones declarativas por outcome/caso. |
| `reservationAutomations.*.sendMessageSequence` | Secuencia por defecto. |

## Checkout

`checkout` centraliza cotizacion, moneda, modos, pago y shipping.

| Propiedad | Responsabilidad |
| --- | --- |
| `currency` | Moneda del tenant. |
| `categoryModes` | Mapeo legacy categoria -> modo. |
| `modes` | Modos modernos por tipo de venta/reserva/pedido. |
| `modes.<mode>.paymentMethods` | Metodos de pago disponibles. |
| `modes.<mode>.shipping` | Reglas de envio si aplica. |
| `modes.<mode>.requiredFactRoles` | Roles de facts obligatorios. |
| `modes.<mode>.systemFactBindings` | Bindings desde facts de sistema. |
| `modes.<mode>.templateFactBindings` | Bindings hacia templates. |
| `paymentMethods.<id>.label` | Nombre visible del metodo. |
| `paymentMethods.<id>.aliases` | Alias conversacionales. |
| `paymentMethods.<id>.payment` | Tipo/porcentaje de pago. |
| `paymentMethods.<id>.template` | Template asociado. |
| `paymentMethods.<id>.confirmationOutcome` | Outcome esperado de confirmacion. |

Reglas:

- Precio, promociones, totales, deposito y shipping deben salir de servicios centralizados.
- Si cambia algun dependency fact, una cotizacion previa queda sospechosa y debe recalcularse.
- Un pago no debe confirmar una reserva antes de cumplir el flujo configurado.

## Commerce

| Propiedad | Responsabilidad |
| --- | --- |
| `enabled` | Activa capacidades de comercio/productos. |
| `provider` | Proveedor: `Local`, `Siigo`, `CustomHttp`, `Mantis` u otro soportado. |

La integracion externa debe quedar atras de interfaces/servicios. La conversacion debe usar tools genericas de comercio, no llamadas directas ni comportamiento quemado.

## Operating Hours

| Propiedad | Responsabilidad |
| --- | --- |
| `enabled` | Activa control de horario operativo. |
| `gatedGroups` | Grupos/capacidades afectados por la restriccion. |

Debe proteger acciones que requieren operacion humana o disponibilidad real sin bloquear informacion segura como catalogo basico cuando el negocio lo permita.

## Escalations

| Propiedad | Responsabilidad |
| --- | --- |
| `escalations.human.contacts` | Contactos humanos para handoff. |
| `escalations.external.enabled` | Activa escalaciones externas. |
| `escalations.external.events` | Eventos configurados por nombre. |
| `events.<event>.enabled` | Activa evento externo. |
| `events.<event>.contactType` | Tipo de contacto destino. |
| `events.<event>.attemptTimeoutMinutes` | Tiempo de espera por intento. |
| `events.<event>.attemptCodePrefix` | Prefijo de codigo operativo. |
| `events.<event>.sendMessageSequence` | Secuencia enviada al contacto externo. |
| `events.<event>.contacts` | Contactos priorizados. |

La escalacion debe conservar resumen, razon y estado suficiente para que una persona continue sin repetir la conversacion.

## Reservation Management

| Propiedad | Responsabilidad |
| --- | --- |
| `automaticChangeFields` | Campos de una reserva que pueden cambiarse automaticamente. |
| `escalateChangeFields` | Campos que requieren humano. |
| `escalationReasonCode` | Codigo de razon para handoff. |
| `manageableReservationGuidance` | Guia positiva para cambios gestionables. |

Cambios de reserva deben validar propiedad, estado, disponibilidad, politica y vigencia. Cambios de servicio u otros campos sensibles deben tratarse como politica configurable, no como excepcion quemada.

## DDD, Clean Architecture Y Estilo De Codigo

- Domain contiene entidades, invariantes, enums e interfaces de repositorios.
- Application orquesta casos de uso, motor, servicios de aplicacion, DTOs, policies y contratos.
- Infrastructure implementa EF Core, repositorios, integraciones externas y servicios tecnicos.
- API/Functions solo adaptan entrada/salida, DI, autenticacion y transporte.
- Admin consume contratos y edita configuracion; no debe redefinir reglas de negocio criticas.
- Repositorios y servicios deben expresar intencion del dominio, no detalles de UI o prompt.
- Las interfaces deben existir cuando separan dominio/aplicacion de infraestructura real, no por ceremonia.
- Extraer abstracciones solo cuando reduzcan complejidad real, eliminen duplicacion significativa o coincidan con patrones existentes.
- Mantener metodos grandes bajo observacion; extraer servicios estrechos cuando el comportamiento ya este cubierto por pruebas.
- Preferir nombres de comportamiento sobre nombres de incidente.

## Checklist Antes De Cambiar El Motor

1. Leer `CONTEXTO_CODEX.md` y este manual.
2. Buscar implementaciones existentes con `rg`: tool, fact, flow, service, template, repository, tests y seeds.
3. Identificar la fuente de verdad correcta: codigo, config, catalogo, template, sequence, integration o test.
4. Definir si el cambio aplica a todos los tenants o solo a una configuracion.
5. Confirmar que no se queman datos de tenant en codigo generico.
6. Confirmar que no se duplica una regla existente.
7. Confirmar que cambios de estado pasan por tool/servicio determinista.
8. Confirmar que facts dependientes se invalidan o recalculan.
9. Actualizar seeds/config/admin types si el contrato cambia.
10. Agregar o ajustar pruebas proporcionales al riesgo.
11. Ejecutar el escenario minimo comprobable.
12. Revisar logs/warnings de `AgentConfigProvider` para referencias rotas.

## Checklist De Release

- No hay nombres de tenant quemados en motor.
- No hay tools en `allowedActions`, `entryActions`, `afterTool` o global actions fuera de `enabledTools`.
- No hay templates requeridos ausentes.
- No hay secuencias referenciadas que no existan.
- No hay reuse silencioso de checkout, disponibilidad o verificacion stale.
- No hay catalogo completo inyectado cuando corresponde busqueda filtrada.
- No hay prompts usados para tapar bugs deterministas.
- Tests/integration/console cubren happy path y edge cases del cambio.
- El comportamiento queda documentado en el lugar existente, no en una bitacora temporal.
