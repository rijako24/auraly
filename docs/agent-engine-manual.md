# Manual Del Motor Agentic

Este documento es una guia estable para entender, cambiar y revisar el motor agentic de Talkio AI. Debe usarse antes de tocar prompts, `Agents.SettingsJson`, tools, flows, seeds, reservas, checkout, pagos, escalaciones o comportamiento conversacional.

## Proposito Del Motor

El motor agentic atiende conversaciones de WhatsApp de forma generica y multitenant. Su trabajo es convertir mensajes naturales en decisiones seguras de negocio: consultar catalogo, capturar facts, cotizar, validar disponibilidad, crear reservas, gestionar pagos, enviar secuencias, escalar a humanos y conservar memoria util.

El motor no debe depender de un negocio especifico. Cada tenant expresa su comportamiento en configuracion, catalogo, plantillas, secuencias, facts, flows, policies e integraciones.

## Flujo Runtime

1. `WhatsAppMessageProcessorService` identifica negocio, numero, conversacion y agente; la capa inbound controla idempotencia, debounce y recibos del proveedor.
2. `AgentConversationService` carga historial, `ConversationState`, facts, memoria del cliente, reloj del negocio y `Agents.SettingsJson`.
3. `AgentConfigProvider` deserializa la configuracion y `AgentConfigurationCompiler` valida flows, facts, signals, operaciones, outcomes, templates y policies antes de ejecutar el turno.
4. `DeterministicConversationPosition` resuelve el flow y la etapa actuales. `TurnPlanScopeBuilder` limita los facts, signals y acciones que pertenecen al turno.
5. Los enrichers agregan contexto estructurado autoritativo, por ejemplo catalogo consultado, ofertas recientes y selecciones de carrito pendientes.
6. `LlmTurnPlanner` no responde al cliente ni modifica estado: propone un `TurnPlan` JSON con facts, signals, decision y directiva de respuesta.
7. `DeterministicTurnCoordinator` valida y normaliza el plan, elimina replays no sustentados, protege selecciones pendientes, aplica facts y decide la etapa que puede ejecutar cada signal.
8. `DeterministicStageExecutor` ejecuta las `IAgentOperation` declaradas en la configuracion. Las operaciones consultan backend, validan invariantes y devuelven outcomes y datos estructurados.
9. Los efectos `onOutcome` actualizan facts, agregan presentaciones requeridas, disparan secuencias o gobiernan transiciones. Los fragments `Required`/`Exclusive` conservan resultados autoritativos sin depender de redaccion libre.
10. `DeterministicResponseRenderer` compone la respuesta final desde guidance, estado y presentaciones; `DeterministicTurnEffectProcessor` persiste efectos y produce los mensajes outbound.
11. `AgentConversationService` registra mensajes, estado, uso, trazas y errores. Los servicios de canal entregan WhatsApp, adjuntos, notificaciones y eventos.

El LLM interpreta lenguaje natural dentro de un contrato acotado. La validez de una mutacion, su idempotencia, inventario, precio, transicion y persistencia pertenecen al runtime determinista.

## Fuentes De Verdad

- Codigo de motor: `src/Application/MimosBabySpa.Application/Agents`.
- Configuracion viva del agente: `Agents.SettingsJson`.
- Seed principal por tenant: scripts en `database/MimosBabySpa.Database/Scripts/Seeds`.
- Catalogo de servicios/productos: tablas e integraciones, nunca texto duplicado en prompts.
- Contratos de operaciones: implementaciones en `Agents/Operations`, `OperationDescriptor` y `AgentOperationRegistry`.
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
- Las decisiones deterministas pertenecen a servicios de dominio/aplicacion. El LLM propone facts, signals y decisiones dentro del scope; el coordinador y las operaciones validan antes de cambiar estado.

## Prompts Y Guidance

Los prompts deben decir que hacer, con instrucciones afirmativas y verificables. La guidance orienta la redaccion, los datos a pedir y el objetivo; signals, actions, conditions y transitions gobiernan la ejecucion.

Premisas para prompts:

- Escribir acciones positivas: "presenta el catalogo autoritativo", "pide fecha y hora cuando falten", "confirma el resumen usando la presentacion requerida".
- Usar nombres exactos de facts y signals cuando la guidance deba ayudar al extractor; las operations se autorizan en `actions`, no en prosa.
- Mantener la voz del negocio en `persona`, `policies`, `conversationGuidance`, `templates` o `messageSequences`.
- Mantener facts, signals, actions, conditions y flow alineados: lo que se pide en guidance debe existir en el contrato compilado.
- Preferir instrucciones cortas, accionables y sin duplicar reglas de codigo.
- Evitar listas de prohibiciones como mecanismo principal de control conversacional.
- Evitar ejemplos de frases cuando puedan rigidizar el agente o filtrar comportamiento de un tenant a otro.
- Evitar repetir catalogo, precios, disponibilidad, horarios o condiciones que ya vienen de servicios/backend.
- Evitar prompt patches para corregir bugs deterministas. Si el problema es validacion, estado, invalidacion, pricing, disponibilidad o routing, corregir el motor.

## Planeacion, Ejecucion Y Presentacion

El contrato central de un turno es `TurnPlan`:

| Parte | Responsabilidad |
| --- | --- |
| `facts` | Afirmaciones estructuradas que pasan por schema, evidencia, validacion e invalidacion. |
| `signals` | Intenciones operativas con payload tipado, por ejemplo cambios de carrito o consulta de catalogo. |
| `decision` | Seleccion de flow/etapa cuando corresponde. |
| `response` | Directiva conversacional; no reemplaza outcomes ni presentaciones autoritativas. |

Reglas de ejecucion:

- Cada signal tiene un propietario efectivo. Una accion de etapa prevalece sobre el fallback global equivalente para evitar doble ejecucion.
- El coordinador puede corregir un plan inseguro: elimina replays, conserva mutaciones independientes, sintetiza una continuacion pendiente o difiere una finalizacion bloqueada.
- Las operaciones reciben `OperationContext`, ejecutan reglas deterministas y retornan `OperationOutcome` con `code`, datos y contexto de error.
- `onOutcome` declara respuesta y efectos. La configuracion decide que template, fact, secuencia o transicion corresponde a cada outcome.
- Las presentaciones `Required` y `Exclusive` son la fuente de verdad visible para catalogos, carritos, disponibilidad, totales y checkout. El renderer no puede sustituirlas por datos inventados.

## Carrito, Lotes Y Aclaraciones Pendientes

`commerce.apply_order_changes` procesa cada referencia del mensaje como un comando `add`, `set_quantity`, `remove` o `cancel_pending`. Las referencias resolubles se aplican una sola vez; las que necesitan decision se guardan en el fact efimero `system.pending_cart_commands`. El nombre escrito por el cliente se conserva por separado en `system.cart_item_presentation`, de modo que la salida puede mostrar `solicitud (producto resuelto)` sin alterar la identidad canonica del catalogo.

La memoria pendiente conserva, por linea:

- comando y cantidad originales;
- texto original solicitado;
- codigo del problema y candidatos autoritativos;
- si requiere resolucion;
- si ya fue aplicado, para impedir duplicados durante aclaraciones posteriores.

Un mensaje posterior puede resolver, reemplazar, cambiar cantidad o cancelar cualquier subconjunto de las lineas pendientes. El orden de la respuesta no obliga a resolver primero el primer producto. Las mutaciones nuevas e independientes tambien se procesan y el resto del lote permanece en memoria.

`commerce.pendingCart` gobierna el cierre:

| Propiedad | Responsabilidad |
| --- | --- |
| `discardOnFinalizeIssueCodes` | Codigos pendientes que el tenant permite dejar fuera en un cierre contextual, por ejemplo una respuesta afirmativa a la pregunta del bot. |
| `discardAllOnExplicitFinalization` | Si esta activo, una intencion explicita y semantica de continuar con el carrito actual descarta todos los pendientes no resueltos, sin importar su codigo. |
| `finalizeConfirmationPhrases` | Respuestas afirmativas exactas que confirman el cierre solamente cuando todos los pendientes restantes son descartables. |
| `cancellationRules` | Frases con modo `exact`, `contains`, `prefix` o `suffix` que permiten descartar el pendiente citado o el que el bot acaba de presentar de forma unica. |
| `quantityCorrectionPhrases` | Verbos que habilitan una correccion numerica cuando existe un unico pendiente de existencia insuficiente compatible. |

Las aclaraciones de descarte y cantidad se recuperan de forma determinista antes de ejecutar el plan: nunca dependen del nombre de un producto, solo del estado pendiente, la referencia del mensaje actual o de la ultima presentacion del bot y las frases configuradas. Una cantidad solo se acepta si es unica, positiva y no supera el maximo informado por inventario.

El cierre principal es semantico. El planner extrae el fact con rol `order.finalized` cuando el cliente expresa claramente que desea conservar el carrito actual y continuar, aunque use una formulacion nunca vista. `commerce.conversation.finalizationRules` es una proteccion determinista complementaria, no una lista exhaustiva de frases que el cliente deba memorizar.

Si `discardAllOnExplicitFinalization=true`, una finalizacion semantica explicita genera `cancel_pending` para todas las referencias no resueltas, conserva el fact de finalizacion y avanza hacia entrega. Esto evita que una incidencia antigua de inventario, busqueda o ambiguedad mantenga secuestrado un carrito que el cliente ya decidio cerrar. Las afirmaciones contextuales cortas, como “si”, siguen una ruta mas conservadora: solo cierran cuando los codigos restantes pertenecen a `discardOnFinalizeIssueCodes`.

Cuando una operacion deja un carrito con productos y todos sus pendientes contextuales son descartables, devuelve `can_finalize_with_pending=true`. La plantilla puede informar que esas referencias quedaran fuera y preguntar “¿Eso sería todo o deseas agregar algo más?”. Una respuesta incluida en `finalizeConfirmationPhrases` cierra unicamente en ese contexto; la misma palabra no finaliza globalmente ni confirma accidentalmente una opcion ambigua.

Una aclaracion posterior que aplica cambios presenta solamente los productos agregados, corregidos o retirados y pregunta si desea algo mas; no repite todo el inventario de pendientes historicos. Cada retiro se marca explicitamente en la presentacion para que una mutacion valida nunca quede silenciosa. La clasificacion completa se conserva para la respuesta inicial del lote. Si el cliente pide ver el carrito, la accion global de solo lectura ejecuta `commerce.get_order_draft` y lo presenta cuantas veces se solicite, sin convertir la consulta en una mutacion.

Las sustituciones usan dos memorias autoritativas: la ultima oferta de catalogo y `system.cart_item_presentation`. El planner comunica la referencia rechazada mediante el campo tipado `replacement_reference`; las frases configuradas solo son respaldo determinista. La busqueda de alternativas no elimina anticipadamente la linea rechazada, incluso si el planner propuso tambien `remove`: ese retiro se difiere. Si el cliente elige de forma inequivoca otra opcion ofrecida con cantidad, el motor elimina la linea resuelta anterior y agrega la nueva; no reutiliza `set_quantity` sobre el producto rechazado. Si la eleccion sigue siendo ambigua, la referencia anterior no se modifica y el motor vuelve a resolver el catalogo.

Una seleccion afirmativa de una opcion ofrecida no autoriza inventar cantidad. Expresiones como “si, agregame” conservan la seleccion conversacional pero el guard determinista elimina cualquier mutacion cuya cantidad no aparezca explicitamente; despues se pregunta la cantidad. Numeros impresos en el empaque (`550 gr`, `x 7 und`) no cuentan por si solos como cantidad solicitada.

Esta proteccion no confia en la cantidad propuesta por el modelo: durante un seguimiento de catalogo sin cantidad escrita elimina `add` y `set_quantity`, aunque coincidan con la cantidad de la linea rechazada. Conserva retiros independientes del mismo mensaje y reconoce cantidades realmente solicitadas al inicio, junto a un verbo, con unidad o al final de cada elemento de una lista; los numeros propios del empaque siguen excluidos.

Los nombres de productos y las formas concretas de hablar no estan quemados en el motor. El tenant configura reglas de respaldo y politicas; la intencion semantica, el estado vigente, las ofertas autoritativas y las identidades del carrito gobiernan la decision.

## Contrato Vigente De SettingsJson

`AgentConfigProvider` deserializa con `UnmappedMemberHandling.Disallow`: una propiedad raiz desconocida invalida toda la configuracion. La fuente de verdad para nombres y tipos es la clase privada `AgentSettings` del provider, seguida por las clases de configuracion y `AgentConfigurationCompiler`. Este manual explica el contrato actual, pero cuando codigo y documento difieran manda el codigo.

### Propiedades raiz soportadas

| Propiedad | Responsabilidad | Default relevante |
| --- | --- | --- |
| `persona` | Identidad, tono y forma de hablar. No autoriza acciones. | Vacio |
| `policies` | Politicas narrativas del tenant que no sustituyen validaciones. | Vacio |
| `flows` | Procesos primarios y secundarios. Debe existir exactamente uno primario. | Requerido |
| `globalActions` | Capacidades semanticas transversales disponibles desde cualquier etapa. | `[]` |
| `factSchema` | Contrato de facts, roles, fuentes, scopes, opciones e invalidacion. | `[]` |
| `templates` | Textos estructurados y overrides por `templateId`. | `{}` |
| `conversationOpening` | Politica del primer turno: `enabled`, `guidance`, `allowQuestions`. | Desactivada |
| `failureResponses` | Respuestas genericas de infraestructura, hoy `llmUnavailable`. | Mensaje generico |
| `conversationFollowUp` | Retoma contextual de una espera declarada por una respuesta. | Desactivada |
| `model` | Deployment/model de Azure OpenAI. | `gpt-4.1-mini` |
| `temperature` | Variabilidad del planner/renderer. En transacciones conviene `0.1`–`0.2`. | `0.2` |
| `historyWindowSize` | Mensajes entregados al renderer/contexto general. | `20` |
| `extractorHistoryWindowSize` | Mensajes inmediatamente anteriores entregados al extractor semantico. | `2` |
| `messageSequences` | Secuencias outbound nombradas. | `{}` |
| `webhooks` | Mapeo de callbacks externos, actualmente Wompi. | Vacio |
| `notifications` | Eventos internos hacia destinatarios configurados. | `{}` |
| `escalations` | Handoff humano y escalacion externa. | Vacio |
| `reservationAutomations` | Confirmaciones y recordatorios programados. | Vacio |
| `interactiveActions` | Boton `scope:outcome:sourceId` a operacion determinista. | `{}` |
| `reservationManagement` | Politica de cambios sobre reservas existentes. | Vacio |
| `checkout` | Moneda, modos, metodos de pago, shipping y bindings. | COP/modos vacios |
| `commerce` | Proveedor y protecciones conversacionales de productos/carrito. | Desactivado/Local |
| `operatingHours` | Admision determinista fuera de horario. | Desactivado |

No estan soportadas actualmente como raices `maxToolIterations`, `consecutiveErrorEscalationThreshold`, `enabledTools`, `defaultFlow`, `flow` ni `guards`. Tampoco se soportan las estructuras legacy `allowedActions`, `entryActions`, `afterTool`, `skipWhen`, `autoSetOnSkip`, `variants`, `onSuccess` u `onProblem` dentro de stages. Si una necesidad exige una propiedad nueva, primero debe agregarse al contrato C#, al compilador, al admin y a pruebas; no debe inventarse en un seed.

## De Un Flujo Hablado A Configuracion

El dueño del negocio puede explicar el proceso en lenguaje natural. La configuracion no debe copiar literalmente esa narracion; debe convertirla en un contrato intermedio:

1. Objetivos de negocio y definicion de exito.
2. Proceso primario y procesos secundarios realmente distintos.
3. Checkpoints durables, no cada mensaje del bot.
4. Facts requeridos, opcionales, adelantables y derivados.
5. Sistema autoritativo para identidad, catalogo, disponibilidad, precio, pago y persistencia.
6. Intenciones semanticas que disparan comportamiento.
7. Acciones de lectura y efectos externos.
8. Outcomes recuperables, terminales y rutas de escalacion.
9. Confirmaciones necesarias antes de efectos irreversibles.
10. Mensajes exactos, checkout, horarios, integraciones y excepciones.

Solo se pregunta al negocio cuando falta una decision que cambia dinero, fulfillment, privacidad, autorizacion, confirmacion, sistema de verdad o escalacion. Tono, etiquetas y formato seguro pueden resolverse con una suposicion declarada.

La traduccion recomendada es:

| Concepto hablado | Propiedad |
| --- | --- |
| “El bot se llama..., habla...” | `persona` |
| “Nunca ofrecemos..., nuestra politica...” | `policies`, si no es una regla ejecutable |
| “Primero..., luego...” | `flows[].stages` |
| “En cualquier momento puede...” | `globalActions` |
| “Necesitamos nombre, fecha...” | `factSchema` + stage `collect`/condiciones |
| “Cuando diga que quiere cambiar...” | Signal semantica + action `on_signal` |
| “Consulta el ERP y...” | `actions[].operation` + `onOutcome` |
| “Si responde X, guarda/limpia/muestra...” | Efectos del outcome |
| “Avanza cuando...” | `transitions[].condition` |
| “Este resumen debe salir asi” | `templates` + `presentation.add` |
| “Se cobra/envia/entrega de esta forma” | `checkout` |
| “Fuera de horario...” | `operatingHours` |
| “Si falla o pide humano...” | `escalations` |

## Flows

Un flow representa un proceso multi-turn completo.

| Propiedad | Responsabilidad |
| --- | --- |
| `id` | Identificador estable y unico. |
| `type` | `primary` o `secondary`. Debe existir exactamente un primario. |
| `routingGuidance` | Descripcion natural positiva usada por el router generico. |
| `ttlSeconds` | Continuidad opcional de un flow secundario. |
| `stages` | Checkpoints ordenados del proceso. |

El primario es el centro de gravedad. Un secundario solo se justifica para otra gestion multi-turn, por ejemplo modificar una reserva existente. El router debe volver al primario cuando no haya evidencia clara del secundario.

## Stages

Un stage es un checkpoint durable con objetivo, facts, signals, acciones y transiciones.

| Propiedad | Responsabilidad |
| --- | --- |
| `id` | Identificador estable dentro del flow. |
| `name` | Nombre para administracion y trazas. |
| `goal` | Objetivo de negocio de la etapa. |
| `collect` | Facts que pueden aceptarse si el cliente los entrega, incluso adelantados. |
| `advanceWhenFacts` | Compatibilidad declarativa: facts cuya presencia habilita avance; no decide por si sola que preguntar. |
| `signals` | Intenciones/payloads semanticos que la etapa puede recibir. |
| `actions` | Operaciones registradas y condiciones exactas de ejecucion. |
| `transitions` | Movimientos deterministas a otro stage. |
| `response` | Directiva de respuesta por defecto de la etapa. |
| `conversationGuidance` | Guia de redaccion; no autoriza operaciones ni muta estado. |
| `reentryOnFactChanged` | Facts cuyo cambio devuelve el cursor a este checkpoint para recalcular. |

Una etapa no equivale a una pregunta. Datos relacionados que pertenecen al mismo checkpoint deben pedirse juntos cuando el negocio lo permita.

## Signals

Una signal representa intencion operativa o input estructurado del mensaje actual.

| Propiedad | Responsabilidad |
| --- | --- |
| `type` | Identificador semantico. |
| `description` | Criterio positivo para que el extractor la emita. |
| `valueSchema` | JSON Schema estricto del payload. |
| `ambiguityRules` | Reglas para detectar valores incompatibles, por ejemplo varios destinos. |

Los schemas de objeto requieren `additionalProperties:false` y todas sus propiedades en `required`; un valor opcional se modela con tipo nullable. La misma signal no puede declarar schemas distintos entre stages. Una signal no reemplaza un fact cuando solo se necesita capturar un dato; se usa cuando el mensaje autoriza o describe una operacion.

## Actions Y Operaciones

Una action une una signal/condicion con una `IAgentOperation` registrada.

| Propiedad | Responsabilidad |
| --- | --- |
| `id` | Identificador de la action. |
| `operation` | Id exacto del `OperationDescriptor`. |
| `trigger` | `on_enter`, `when_ready`, `on_signal`, `on_fact_changed` o `manual`. |
| `signal` | Signal requerida cuando el trigger es `on_signal`. |
| `condition` | Predicado determinista adicional. |
| `arguments` | Bindings declarativos a inputs de la operacion. |
| `execution` | Idempotencia, timeout y reintentos. |
| `onOutcome` | Handlers por codigo declarado por la operacion. |

La configuracion no inventa tools. Debe consultar el `OperationDescriptor`: id, input schema, argumentos requeridos, outcomes y templates requeridos. El compilador rechaza operaciones desconocidas, argumentos obligatorios sin binding, outcomes ajenos y templates faltantes.

### Triggers

- `on_enter`: trabajo determinista al activar un stage.
- `when_ready`: accion cuando sus facts/condiciones ya estan listos.
- `on_signal`: comando semantico explicito del usuario.
- `on_fact_changed`: recalculo cuando cambia una dependencia.
- `manual`: accion segura que el planner puede seleccionar dentro del scope.

### Execution e idempotencia

| Valor | Uso |
| --- | --- |
| `input_version` | Default para lecturas/calculos dependientes de inputs actuales. |
| `once_per_request` | Efecto externo que debe ocurrir una sola vez por solicitud. |
| `none` | Comando repetible donde cada turno es una orden nueva; normalmente `maxAttempts=1`. |

`timeoutSeconds` debe ser positivo, `maxAttempts` al menos uno y no se permiten reintentos con `idempotency=none`.

## Conditions

Una `condition` puede combinar:

- `all`, `any`, `not`;
- `factPresent`, `factMissing`, `factChanged`;
- `signalPresent`;
- `verificationActive`, `verificationMissing`;
- `factEquals` con `key` y `value`.

Las condiciones son la capa de autorizacion declarativa. Toda referencia a fact o signal debe existir. La guidance puede explicar el comportamiento, pero no reemplaza una condition.

## Outcomes, Effects Y Responses

Cada operacion retorna un `OperationOutcome` con codigo y datos estructurados. `onOutcome.<code>` selecciona efectos y respuesta.

Efectos soportados:

| `type` | Responsabilidad |
| --- | --- |
| `fact.set` | Fija un fact. |
| `facts.set_from_outcome` | Mapea datos estructurados del outcome a facts. |
| `facts.clear` | Invalida facts, confirmaciones o resultados stale. |
| `presentation.add` | Agrega fragmento autoritativo desde template/datos. |
| `sequence.enqueue` | Encola una secuencia outbound. |
| `event.emit` | Emite evento interno. |
| `escalation.human` | Entrega la gestion a una persona. |
| `request.complete` | Cierra la solicitud vigente. |

`response` y las respuestas de outcomes aceptan `mode`, `guidance`, `template`, `sendMessageSequence`, `suppressText` y `awaitCustomerReply`. `guidance` solo gobierna redaccion. `template` y `sendMessageSequence` deben existir. `awaitCustomerReply` es semantica de maquina: declara que la respuesta entregada deja una espera del cliente; no contiene el texto futuro ni programa nada por si sola.

Las presentaciones `Required` obligan a incluir el fragmento. `Exclusive` evita que el renderer mezcle otra reconstruccion libre. Totales, catalogos, disponibilidad, carritos, checkout y clasificaciones deben preferir presentaciones autoritativas.

## Transitions

Cada transition declara:

| Propiedad | Responsabilidad |
| --- | --- |
| `id` | Identificador estable. |
| `priority` | Orden cuando varias condiciones pueden cumplirse. |
| `condition` | Predicado determinista. |
| `to` | Stage destino existente. |
| `effects` | Efectos aplicados al mover el cursor. |

Las transiciones deben modelar checkpoints, no frases. Condiciones superpuestas necesitan prioridades claras. Los facts derivados se invalidan mediante `dependsOn`; `reentryOnFactChanged` reposiciona el cursor cuando corresponde recalcular.

## Global Actions

Una accion global tiene `id`, `priority`, `goal`, `conversationGuidance`, una unica `signal`, `actions` y `response`. Sus actions solo admiten trigger `on_signal`.

Se usa para una capacidad corta y transversal: ver el carrito, mutarlo explicitamente desde cualquier etapa, pedir catalogo o escalar. No se usa para otro proceso multi-turn. Si el stage activo y una global action poseen la misma signal, el propietario del stage prevalece para impedir doble ejecucion.

## Fact Schema

Los facts son el contrato de datos del agente.

| Propiedad | Responsabilidad |
| --- | --- |
| `key` | Clave tecnica unica. |
| `role` | Significado universal como `customer.name`, `booking.date` o `shipping.address`. |
| `label` | Nombre legible para extractor/admin. |
| `type` | `string`, `number`, `date`, `time`, `phone`, `email`, etc. |
| `extractionGuidance` | Normalizacion semantica especifica del tenant. |
| `options` | Conjunto canonico pequeno con `value`, `label` y `selector`. |
| `required` | Obligatoriedad general del dato. |
| `source` | `user`, `channel` o `system`. |
| `customerReadable` | Permite divulgarlo mediante operaciones de lectura configuradas. |
| `showInCollectedInfo` | Lo incluye en informacion de reserva/calendario. |
| `defaultValue` | Valor hidratable; debe pertenecer a `options` si existen. |
| `scope` | `customer`, `request` o `ephemeral`. |
| `retentionDays` | Vigencia temporal. |
| `expireOnBusinessDayChange` | Expira al cambiar el dia operativo. |
| `dependsOn` | Fuentes cuyo cambio invalida este fact. |
| `valueSource` | Servicio/catalogo que canonicaliza el valor. |

Reglas:

- `customer` solo para datos estables reutilizables.
- `request` para la orden/reserva actual.
- `ephemeral` para cotizaciones, verificaciones, candidatos y estados recalculables.
- Catalogos cambiantes no se modelan con `options`; se consultan por operacion.
- Facts derivados declaran todas sus dependencias.
- Cambiar carrito, servicio, fecha, hora, add-ons, fulfillment o pago debe limpiar confirmaciones y calculos dependientes.

## Templates Y Presentaciones

`templates` es un diccionario `templateId -> texto`. Una operacion puede exigir templates mediante su descriptor y el compilador verifica su presencia.

Usar templates para:

- carritos y reservas;
- precios, impuestos, descuentos, envio, deposito y totales;
- opciones de disponibilidad;
- checkout e instrucciones de pago;
- resultados estructurados o texto legal/aprobado.

Los valores deben venir de facts u outcomes autoritativos. No se queman montos, inventario ni identidades que pertenecen a servicios. Si el template muestra el nombre escrito por el cliente y el producto resuelto, ambos vienen de memoria/presentacion estructurada, no de una sustitucion textual del LLM.

## Message Sequences

`messageSequences.<id>.messages` contiene pasos `text` o `whatsapp_template`.

Un paso soporta `body`, `attachmentId`, `buttons`, `templateName`, `language`, `headerParameters` y `bodyParameters`.

Validaciones actuales:

- una secuencia tiene al menos un mensaje;
- `text` requiere body o adjunto;
- `whatsapp_template` requiere `templateName`;
- maximo tres botones;
- id de boton no vacio y maximo 256 caracteres;
- titulo no vacio y maximo 20;
- botones en texto requieren body.

La secuencia gobierna entrega outbound exacta. Una operacion crea el estado; la secuencia, webhook, notification o processor decide el mensaje. Evitar que el LLM y una secuencia confirmen lo mismo dos veces.

## Webhooks, Notifications, Interactive Actions Y Automations

- `webhooks.wompi.<outcome>.sendMessageSequence` mapea outcomes Wompi soportados.
- `notifications.<event>` requiere `enabled`, `recipients` y `sendMessageSequence`.
- Un recipient puede ser telefono, valor de contexto o selector `inbound:<tipo-o-clave>`.
- `interactiveActions.<scope>.<outcome>` declara `operation`, `arguments` y secuencia opcional.
- El payload interactivo es `scope:outcome:sourceId`; `{source_id}` enlaza el recurso inmutable sin depender del orden de mensajes.
- `reservationAutomations.confirmation` y `.reminder` admiten `enabled`, `trigger`, action por outcome y secuencia por defecto.
- Triggers de automatizacion pueden ser relativos (`hoursBefore`) o por dias/hora segun la definicion.

Toda operacion interactiva o automatizada tambien se valida contra el registry y sus argumentos requeridos.

## Checkout

`checkout` centraliza moneda y modos `reservation`, `enrollment` u `order`.

| Propiedad | Responsabilidad |
| --- | --- |
| `currency` | Moneda del tenant. |
| `modes.<mode>.paymentMethods` | Metodos disponibles. |
| `modes.<mode>.shipping` | `enabled`, ciudad local, costo local y nacional. |
| `requiredFactRoles` | Overrides avanzados de roles obligatorios. |
| `systemFactBindings` | Overrides hacia datos de sistema. |
| `templateFactBindings` | Overrides hacia templates. |

Los defaults del motor ya enlazan roles comunes para reserva, matricula y pedido; un seed normal debe preferirlos y solo sobrescribirlos cuando el negocio use otra semantica.

Cada metodo de pago admite:

- `label` y `aliases`;
- `payment.percentage` si existe cobro;
- `template`;
- `confirmationOutcome` cuando hay pago o aprobacion;
- `manualConfirmationRequired`;
- `manualExpirationMinutes`.

Si existe un solo metodo puede seleccionarse por defecto. Con varios, el cliente debe escoger uno valido. Un porcentaje debe estar entre 1 y 100. Efectivo o datafono al recibir normalmente no configuran porcentaje, pero ambos presentan el resumen autoritativo y requieren la misma confirmacion verbal si asi lo define el flow. Una transferencia con aprobacion manual no crea/confirmar como pagada hasta recibir el outcome autoritativo.

Precio, promociones, total, deposito y shipping salen de servicios. Cualquier dependencia modificada invalida el checkout anterior.

## Commerce

| Propiedad | Responsabilidad |
| --- | --- |
| `enabled` | Activa comercio. |
| `provider` | `Local`, `Siigo`, `CustomHttp`, `Mantis` u otro enum soportado. |
| `offerMemoryMaxSnapshots` | Ofertas recientes recordadas. Default `8`. |
| `offerMemoryMaxProducts` | Productos maximos recordados. Default `100`. |
| `conversation.*` | Protecciones de lenguaje contextual. |
| `pendingCart.*` | Politica de pendientes, descarte y correcciones. |
| `matching.*` | Umbrales genericos de similitud. |

### Protecciones conversacionales

- `contextualConfirmationPhrases`: confirma una opcion presentada; no cierra globalmente.
- `finalizationRules`: respaldo determinista de cierre; la signal/fact semantica es primaria.
- `cartReviewRules`: protege solicitudes de solo lectura.
- `productReplacementRules`: indica rechazo/reemplazo de una referencia ofrecida.
- `candidateSelectionPhrases`: ordinales/demostrativos.
- `clauseSeparators`: divide aclaraciones con varias clausulas.
- `additionalRequestPhrases`: distingue incremento de cantidad total.
- `quantityWords`: cantidades escritas.
- `pendingCart.discardOnFinalizeIssueCodes`: incidencias descartables en cierre contextual.
- `discardAllOnExplicitFinalization`: permite descartar pendientes ante cierre semantico explicito.
- `finalizeConfirmationPhrases`, `cancellationRules`, `quantityCorrectionPhrases`: respaldos acotados.
- `matching.exactNameDominanceMinimumMatches`, `candidateMentionSimilarity`, `pendingReferenceSimilarity`, `candidateSelectionSimilarity`: umbrales medidos entre 0 y 1.

No convertir estas listas en el entendimiento principal. Sirven para lenguaje corto/peligroso alrededor de un estado ya conocido. Ajustar umbrales solo con conversaciones etiquetadas y regresion.

### Catalogo, alias e historial por cliente

El catalogo activo es la fuente de verdad. Los alias son datos, no codigo:

- Alias de negocio exacto y univoco: puede `AutoResolve`.
- Alias por cliente con una unica referencia historica: `AutoResolve`.
- Alias por cliente con varias referencias: todas quedan `SuggestOnly`.
- Evidencia web de equivalencia exacta puede respaldar un alias de negocio.
- Similitud de categoria/uso nunca basta para auto-resolver.

“Agrega salchicha” prioriza productos habituales y pide elegir si hay varios. “¿Que salchichas tienes?” consulta todo el catalogo activo, ordenando habituales primero; el historial nunca oculta productos no comprados. Productos inactivos no se ofrecen aunque exista alias.

Las claves de cliente deben ser estables por proveedor/cuenta/cliente; el telefono es fallback legacy, no identidad primaria. El entrenamiento se hace con historial real, dry-run, deteccion de conflictos, importacion idempotente y auditoria.

La integracion externa permanece detras de interfaces genericas. El seed configura provider/endpoints y lenguaje del tenant sin introducir productos o clientes en codigo del motor.

## Operating Hours

`operatingHours.enforce` (alias compatible `enabled`) activa admision fuera de horario. `outsideHours` admite:

- `guidance`: redaccion del renderer;
- `template`: presentacion exclusiva;
- `sendMessageSequence`: secuencia directa.

Si se activa, debe existir al menos una respuesta y toda referencia debe ser valida. La politica bloquea operaciones; no debe usarse una guidance para reautorizarlas.

## Conversation Follow-up

`conversationFollowUp` configura una unica retoma por espera:

- `enabled`;
- `delayMinutes`, entre 1 minuto y 30 dias;
- `guidance`, usada por el renderer existente para redactar desde el contexto vigente;
- `fallbackSequence`, opcional y validada contra `messageSequences`;
- `respectOperatingHours`, que difiere el envio a la siguiente apertura cuando `operatingHours.enforce` esta activo.

Una espera nace solo despues de entregar con exito una respuesta con `awaitCustomerReply: true`. Se persiste en `ConversationState` junto con una fecha indexada; no existe una tabla de jobs paralela. Al vencer, el proceso temporizado comprueba que siguen iguales el mensaje fuente, owner, solicitud, flow y stage, y que no existe ningun inbound posterior, incluso si aun esta en debounce.

La retoma no ejecuta planner ni operaciones: el renderer escribe un solo mensaje breve a partir de la etapa, facts e historial actuales. La fecha se elimina antes del envio mediante versionado optimista, por lo que esa espera no puede producir una segunda retoma. Si el cliente responde, el inbound elimina la espera antes del turno; la respuesta nueva del bot puede declarar otra espera independiente.

## Escalations

`escalations.human.contacts` configura handoff humano.

`escalations.external` contiene `enabled` y eventos. Cada evento habilitado puede declarar:

- `attemptTimeoutMinutes` positivo;
- `attemptCodePrefix`;
- `sendMessageSequence`;
- `contactType` o contactos explicitos;
- `pickupAddress`;
- `outcomeEvents` hacia notifications;
- contactos priorizados con `businessInboundContactId`.

Una escalacion conserva resumen, razon y estado para que la persona continue sin repetir la conversacion. El compilador valida secuencias, contactos, notifications y tiempos.

## Reservation Management

| Propiedad | Responsabilidad |
| --- | --- |
| `automaticChangeFields` | Campos modificables automaticamente. |
| `escalateChangeFields` | Campos que requieren humano. |
| `escalationReasonCode` | Razon estable del handoff. |
| `manageableReservationGuidance` | Guia positiva para cambios autorizados. |

Una modificacion valida propiedad, estado, disponibilidad, vigencia y politica. Cambiar servicio u otro campo sensible es una decision de configuracion, no una excepcion quemada.

## Compilacion, Cache Y Activacion

`AgentConfigProvider`:

1. Lee `Agents.SettingsJson`.
2. Deserializa sin admitir propiedades desconocidas.
3. Exige al menos un flow.
4. Aplica defaults.
5. Compila.
6. Rechaza y registra diagnostics si hay errores.
7. Cachea la configuracion valida diez minutos.

Cuando se actualiza un agente debe invalidarse el cache correspondiente. El compilador valida, entre otros:

- ids unicos y exactamente un primary flow;
- facts y opciones canonicas;
- signal schemas consistentes/estrictos;
- triggers, condiciones, transiciones e idempotencia;
- operaciones, argumentos, outcomes y templates;
- efectos, sequences, notifications, botones;
- commerce y umbrales;
- horarios, automatizaciones, acciones interactivas y escalaciones.

JSON valido no significa configuracion valida. Una configuracion termina solo cuando deserializa, compila y supera pruebas de comportamiento.

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
- No hay `actions[].operation` desconocidas, argumentos requeridos sin binding ni outcomes fuera del `OperationDescriptor`.
- No hay templates requeridos ausentes.
- No hay secuencias referenciadas que no existan.
- No hay reuse silencioso de checkout, disponibilidad o verificacion stale.
- No hay catalogo completo inyectado cuando corresponde busqueda filtrada.
- No hay prompts usados para tapar bugs deterministas.
- Tests/integration/console cubren happy path y edge cases del cambio.
- El comportamiento queda documentado en el lugar existente, no en una bitacora temporal.
