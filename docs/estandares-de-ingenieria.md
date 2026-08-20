# Estandares de ingenieria

Este documento define como se disenan, implementan, revisan y verifican cambios en Auraly. Es normativa para trabajo de codigo, configuracion, datos e infraestructura. Los principios son medios para producir software correcto y mantenible; no justifican complejidad ceremonial.

Las palabras **debe**, **no debe** y **obligatorio** expresan requisitos. Una excepcion requiere una razon concreta, alcance acotado, pruebas y una condicion de retiro.

## 1. Orden de prioridades

Ante una decision tecnica, priorizar en este orden:

1. Correctitud del dominio, seguridad y proteccion de datos.
2. Invariantes y diseno aprobado del sistema.
3. Una sola fuente de verdad y ausencia de ejecucion duplicada.
4. Claridad, simplicidad y facilidad de cambio.
5. Compatibilidad, operabilidad y rendimiento medido.
6. Preferencias de estilo.

SOLID, Clean Architecture, DDD y los patrones se aplican cuando reducen acoplamiento o protegen una invariante. No se crean interfaces, capas de paso, factories, repositories, eventos o mediadores solo para poder nombrar un patron. Una interfaz con una sola implementacion es valida cuando protege una frontera externa o invierte una dependencia que el caso de uso no debe conocer; no lo es por reflejo para cada clase.

- **KISS:** escoger la solucion correcta mas simple que haga explicitas las invariantes.
- **YAGNI:** no construir extensibilidad, compatibilidad, configuracion ni generalidad para casos hipoteticos.
- **DRY:** evitar duplicar conocimiento y decisiones, no perseguir similitud textual a costa de una abstraccion incorrecta.
- **Boy Scout con alcance:** mejorar lo que se toca cuando reduce el riesgo del cambio, sin convertir la tarea en un refactor no solicitado.

## 2. Analizar antes de crear

Todo cambio comienza con descubrimiento del sistema existente:

- Buscar por concepto de dominio y tambien por nombres tecnicos, endpoints, tablas, settings, operaciones, outcomes y mensajes relacionados.
- Revisar contratos, implementaciones, call sites, registros DI, configuracion, schema/seeds y tests; no basta leer una clase aislada.
- Trazar entrada, validacion, decision, persistencia, efectos externos, salida, reintentos y errores.
- Identificar el propietario actual de cada regla y dato. Si hay varios, consolidarlos en vez de agregar otro.
- Verificar si existe una decision arquitectonica vigente. Si el nuevo requisito la contradice, hacer visible el conflicto antes de codificar una ruta paralela.
- Confirmar la causa raiz de un bug con evidencia. Corregir sintomas mediante frases, flags o condiciones especiales solo es aceptable cuando esa sea realmente la capa propietaria.
- Evaluar el radio de impacto: tenants, datos historicos, clientes API, admin, workers, integraciones, despliegue y rollback.

La implementacion debe ser el slice vertical mas pequeno que deje una capacidad completa. Un diff pequeno no es una virtud si deja contratos o fuentes de verdad inconsistentes.

## 3. Arquitectura limpia y limites

La direccion conceptual de dependencias es hacia el dominio:

- **Domain** contiene lenguaje, entidades, value objects e invariantes estables. No conoce EF, HTTP, Azure, OpenAI, UI ni detalles de despliegue.
- **Application** coordina casos de uso y politicas. Define puertos para fronteras externas o variaciones reales y no contiene detalles de transporte.
- **Infrastructure** implementa persistencia e integraciones. Traduce contratos externos, pero no decide reglas de negocio que pertenecen al dominio o aplicacion.
- **API/Functions/Console** son composition roots y adaptadores de entrada. Autentican, validan el contrato de transporte, invocan un caso de uso y mapean la respuesta; no duplican el caso de uso.
- **Admin/frontend** presenta y captura datos. No replica autorizacion, precios, estados o transiciones como fuente autoritativa.

Reglas de limites:

- No acceder directamente a base de datos o proveedores desde UI, controllers, functions o renderers para saltarse el caso de uso canonico.
- No usar service locator, estado global mutable ni dependencias estaticas ocultas.
- Inyectar reloj, generadores de IDs, clientes externos y otras fuentes no deterministas cuando afecten comportamiento comprobable.
- Mantener DTOs de transporte separados de entidades persistidas cuando sus responsabilidades o ciclos de vida difieran.
- Traducir modelos de proveedores en el borde para impedir que sus detalles contaminen el dominio.
- Evitar ciclos entre modulos y dependencias transversales no declaradas.

## 4. DDD practico

- Usar el lenguaje ubicuo vigente del negocio en nombres de clases, metodos, estados, eventos y pruebas. No crear sinonimos tecnicos para el mismo concepto.
- Definir claramente aggregate roots y modificar sus invariantes por una ruta coherente. No permitir que distintos servicios escriban parcialmente el mismo estado sin coordinacion.
- Modelar como value object los conceptos con reglas propias —por ejemplo dinero, rangos, identificadores o periodos— cuando eso elimine estados invalidos; no envolver tipos primitivos sin aportar una invariante.
- Las entidades protegen invariantes locales; los servicios de dominio expresan reglas que no pertenecen naturalmente a una entidad; los casos de uso coordinan I/O y transacciones.
- Los eventos de dominio/integracion se usan cuando existe desacoplamiento temporal o varios consumidores reales. No sustituyen una llamada directa clara dentro de la misma transaccion.
- Respetar limites de contexto. Compartir contratos pequenos o IDs es preferible a compartir modelos internos mutables.

## 5. SOLID y diseno de codigo

- **Responsabilidad unica:** una unidad tiene una razon principal de cambio. Separar orquestacion, validacion, persistencia, integracion y presentacion cuando evolucionen por motivos distintos.
- **Abierto/cerrado:** extender por puntos de composicion existentes cuando haya variacion real; no construir jerarquias especulativas.
- **Sustitucion:** una implementacion debe respetar precondiciones, resultados, errores, idempotencia y semantica del contrato que implementa.
- **Segregacion:** contratos pequenos y cohesionados para consumidores reales; evitar interfaces omnibus y variantes booleanas que cambien por completo el comportamiento.
- **Inversion:** las politicas dependen de abstracciones en fronteras volatiles. Las abstracciones pertenecen al consumidor, no al proveedor por conveniencia.

Ademas:

- Preferir composicion sobre herencia.
- Hacer explicitos estados y resultados esperados; evitar booleanos ambiguos y strings libres para conceptos cerrados.
- Usar guard clauses para precondiciones, funciones pequenas y nombres que expresen intencion.
- Evitar parametros booleanos que seleccionen algoritmos, metodos que mezclen lectura y mutacion, y efectos secundarios escondidos.
- Comentar el **por que**, las restricciones o decisiones no obvias; el codigo debe explicar el **que**. Eliminar comentarios obsoletos junto con el cambio.
- Eliminar codigo muerto y compatibilidad temporal al completar el cutover seguro.
- No aplicar umbrales arbitrarios de lineas o numero de clases. Refactorizar cuando haya responsabilidades mezcladas, duplicacion, dificultad de prueba o alto costo de cambio.

## 6. Patrones sin sobreingenieria

Antes de introducir un patron debe poder nombrarse el problema que resuelve, la variacion existente y el costo que evita.

- Strategy para algoritmos realmente intercambiables.
- Adapter/anti-corruption layer para proveedores o modelos externos.
- Factory cuando la construccion tiene invariantes o multiples variantes, no para ocultar un constructor simple.
- Repository cuando protege el lenguaje/persistencia de un aggregate o consulta; no para envolver mecanicamente cada `DbSet`.
- Specification cuando predicados de dominio complejos se reutilizan y componen.
- State machine cuando estados y transiciones requieren validacion explicita.
- Outbox/inbox cuando se necesita consistencia durable entre persistencia y mensajeria.

Si una funcion directa es mas clara y conserva los limites, preferirla.

## 7. Una sola fuente de verdad y cero duplicacion

Cada comportamiento debe tener un propietario. Consumidores distintos reutilizan el propietario o un contrato compartido; no copian la regla.

- No duplicar reglas entre backend y frontend, motor y prompt, codigo y seed, tabla e integracion, endpoint y worker, ni produccion y tests.
- Los tests pueden expresar el mismo comportamiento como expectativa, pero deben construir datos mediante builders/fixtures y ejercer el contrato publico, no copiar el algoritmo productivo.
- Centralizar solo conceptos semanticamente iguales. Parecido visual o textual no implica misma responsabilidad.
- Cuando dos implementaciones ya existen, elegir la canonica con evidencia, migrar consumidores, agregar regresion y retirar la redundante en un cutover seguro.
- No crear helpers genericos que oculten diferencias de dominio. Una abstraccion compartida debe tener nombre y contrato mas claros que el codigo repetido.
- No dejar dos rutas activas "temporalmente" sin propietario, telemetria, fecha/condicion de retiro y estrategia de rollback.

## 8. Hardcoding y configuracion

No todo literal es hardcoding. Es hardcoding cuando un valor que cambia por tenant, ambiente, operacion o proveedor queda incrustado en una capa que no lo posee.

| Tipo de dato o regla | Propietario esperado |
| --- | --- |
| Secretos, tokens, connection strings, claves | Variables seguras, Key Vault o proveedor de configuracion; nunca repositorio, logs o respuestas |
| URLs, timeouts, limites y parametros de ambiente | Options tipadas/configuracion con validacion al inicio y defaults tecnicos justificados |
| Reglas, textos y variaciones administrables por tenant/business | Tabla o configuracion tipada canonica; el motor conversacional se rige por su manual |
| Catalogo, precios, inventario, disponibilidad y horarios vivos | Backend, tabla o integracion autoritativa consultada en runtime |
| Invariantes universales y estables del dominio | Codigo de dominio, tipos/constantes con nombre y pruebas |
| Codigos cerrados de protocolo o proveedor | Adapter de infraestructura, enum/constante con nombre y traduccion en el borde |
| Datos de pruebas | Builders, fixtures y fakes aislados de produccion |

Reglas adicionales:

- No usar numeros magicos, GUIDs, telefonos, nombres de negocio, frases de clientes, productos, precios, ciudades o fechas especiales en logica generica.
- Un default debe ser seguro, visible y tener un propietario. Si su uso puede cobrar, reservar, publicar o perder datos, fallar cerrado es preferible a adivinar.
- No esconder configuracion en prompts, comentarios, nombres de archivos o variables estaticas mutables.
- Al agregar una propiedad de configuracion, completar contrato tipado, validacion, carga, documentacion, admin/seed cuando aplique y pruebas de configuracion valida e invalida.
- En Auraly, todo selector de datos de negocio se rige por la politica de tablas y dropdowns de `docs/invariantes-arquitectonicas-auraly.md`; no se crean arrays de opciones quemadas en frontend o backend.

## 9. Componentes de IA

Las reglas concretas del motor conversacional viven exclusivamente en `docs/agent-engine-manual.md`. Este apartado contiene solo criterios transversales para cualquier componente probabilistico:

- Tratar la salida de un modelo como entrada no confiable: contrato estructurado, scope, autorizacion, normalizacion y validacion antes de todo efecto.
- Una salida generada no es autoridad sobre identidad, permisos, dinero, inventario, disponibilidad, persistencia ni transiciones de estado.
- Corregir invariantes en su propietario determinista; no usar instrucciones de prompt como sustituto de validacion de dominio.
- Consultar fuentes autoritativas para datos vivos en vez de copiarlos al contexto o prompt.
- Proteger tools e integraciones contra prompt injection, argumentos fuera de scope, replay y escalacion de privilegios.
- Registrar decisiones tecnicas con correlacion suficiente para diagnostico, sin almacenar prompts, PII o secretos innecesarios.

## 10. Datos, EF y consistencia

- Toda lectura/escritura debe aplicar el alcance de tenant/business que corresponda y verificar propiedad en servidor. Nunca confiar solo en IDs recibidos del cliente.
- Definir el limite transaccional en el caso de uso. No hacer `SaveChanges` parciales que dejen una operacion de negocio a medias salvo que el workflow durable lo modele explicitamente.
- Usar idempotencia para webhooks, comandos reintentables, pagos, reservas, mensajes y efectos externos. Una clave de idempotencia tiene scope y retencion definidos.
- Proteger escrituras concurrentes con constraints, tokens/versiones, aislamiento o operaciones atomicas segun el conflicto real. No usar check-then-act sin proteccion.
- Guardar instantes en UTC y convertir con `TimeProvider` y el reloj/zona del negocio. No usar `DateTime.Now` ni la zona del servidor como autoridad.
- Usar `decimal` y moneda/escala explicitas para dinero; definir redondeo en un solo lugar.
- Crear constraints e indices que refuercen invariantes e idempotencia. Validacion de aplicacion sin constraint no basta ante concurrencia.
- En lecturas EF, proyectar lo necesario, evitar N+1, paginar colecciones no acotadas y usar tracking solo cuando se vaya a modificar la entidad.
- Propagar `CancellationToken` en I/O asincrono y evitar sync-over-async.
- Todo cambio de esquema incluye compatibilidad de despliegue, backfill si aplica, actualizacion del proyecto SQL/seeds y plan de rollback o roll-forward.
- No editar ni regenerar datos productivos como efecto colateral de startup sin una estrategia explicita.

## 11. APIs, eventos y trabajos distribuidos

- Validar forma, limites y semantica del input en el borde; validar invariantes nuevamente en el propietario del dominio.
- Autorizar por recurso y accion, no solo por presencia de autenticacion.
- Mantener DTOs y errores estables. Un cambio incompatible requiere versionado o cutover coordinado.
- No filtrar excepciones internas, SQL, stack traces, tokens o PII al cliente.
- En llamadas externas definir timeout, cancelacion, politica de retry solo para fallas transitorias, backoff con jitter y limite. No reintentar efectos no idempotentes a ciegas.
- Un consumidor debe tolerar entrega al menos una vez, duplicados y reordenamiento segun su contrato.
- Preservar orden por conversacion mediante la sesion/clave canonica; no crear consumidores paralelos que rompan esa garantia.
- Usar dead-letter y observabilidad para fallas no recuperables. No completar mensajes que no se procesaron correctamente.
- Separar adaptacion de canal de decision de negocio: HTTP, WhatsApp, demo u otro canal convergen en el mismo caso de uso/motor.

## 12. Seguridad y privacidad

- Aplicar minimo privilegio, deny-by-default y separacion entre autenticacion, autorizacion y pertenencia al tenant.
- Validar y normalizar entradas; parametrizar SQL; codificar salidas en su contexto; restringir uploads por tipo, tamano y contenido.
- Nunca registrar secretos, access tokens, payment data, documentos, conversaciones completas o PII si no es imprescindible. Redactar identificadores sensibles.
- No retornar diferencias que permitan enumerar usuarios, negocios o recursos.
- Verificar firmas, timestamps y replay protection de webhooks cuando el proveedor lo permita.
- Mantener dependencias al minimo y revisar impacto de seguridad/licencia antes de agregar una.
- Fallar cerrado en autorizacion, pagos y mutaciones irreversibles. Un fallback de disponibilidad no puede convertirse en permiso.

## 13. Errores, observabilidad y rendimiento

- No usar `catch` vacios, excepciones para flujo normal ni mensajes de error vagos. Capturar solo cuando se pueda agregar contexto, compensar o traducir.
- Conservar la excepcion original y registrar con logging estructurado, correlation/trace ID, tenant/business seguro, operacion y outcome.
- Distinguir errores esperados de dominio, input invalido, conflicto, no autorizado, transitorio y fallo interno.
- Exponer metricas para throughput, latencia, errores, reintentos, dead letters e idempotencia en caminos criticos.
- Medir antes de optimizar. Evitar caches sin politica de invalidez, scope de tenant, limite de memoria y comportamiento ante stale data.
- No resolver latencia introduciendo ejecucion duplicada o consistencia eventual no modelada.
- Los logs ayudan a diagnosticar pero no sustituyen tests ni estado durable.

## 14. Frontend y contratos de UI

- Reutilizar componentes y convenciones existentes antes de crear variantes visuales o de estado.
- Mantener accesibilidad: HTML semantico, labels, teclado, foco, contraste y estados de carga/error/vacio.
- Evitar duplicar server state en stores globales. Usar la herramienta de estado segun su propietario y ciclo de vida.
- Validar para UX en cliente, pero conservar validacion y autorizacion autoritativas en servidor.
- No codificar permisos, estados, catalogos o reglas de negocio solo en la UI.
- Mantener limites server/client de Next.js y consultar la documentacion instalada de la version del repositorio antes de usar APIs que puedan haber cambiado.
- Cubrir flujos criticos y estados de error, no solo el happy path.

## 15. Estrategia de pruebas

Toda correccion incluye una prueba que falle antes y pase despues en la capa mas baja que demuestre el comportamiento real.

- **Unitarias:** invariantes, value objects, decisiones y transformaciones puras.
- **Aplicacion/contrato:** casos de uso, autorizacion, idempotencia, configuracion y adapters con dependencias controladas.
- **Integracion:** EF/SQL, serializacion, DI, colas, proveedores simulados y limites donde un mock esconderia el riesgo.
- **End-to-end o escenarios:** solo para journeys criticos y contratos entre superficies.
- **Arquitectura:** agregar checks automatizados cuando una frontera importante se viole repetidamente.

Las pruebas deben ser deterministas, independientes de hora/red/orden global, legibles como comportamiento y sin delays arbitrarios. Verificar resultados, estado durable y efectos; no acoplarse a llamadas internas salvo que el contrato sea precisamente una colaboracion como idempotencia o no duplicacion.

Checks minimos segun alcance:

```powershell
dotnet build Auraly.Commerce.sln
dotnet test src/Tests/Auraly.Platform.Tests/Auraly.Platform.Tests.csproj
```

Para admin:

```powershell
npm --prefix admin run lint
npm --prefix admin run test:pos
npm --prefix admin run build
```

Ejecutar primero pruebas enfocadas y luego ampliar proporcionalmente al riesgo. Nunca afirmar que un check paso si no se ejecuto; reportar bloqueo, comando y causa.

## 16. Automatizacion de calidad y supply chain

- Los checks locales y CI deben ser reproducibles y usar las versiones/lock files del repositorio. No actualizar dependencias o locks incidentalmente.
- Ejecutar formatters, linters, analyzers, type checks, builds y tests que correspondan al stack modificado.
- No suprimir warnings, reglas de analyzer, errores de TypeScript o lint con exclusiones amplias. Una supresion local requiere motivo y evidencia de seguridad.
- Incorporar al CI escaneo de secretos, dependencias vulnerables y licencias cuando la plataforma lo permita; un hallazgo critico no se ignora con un baseline nuevo sin remediacion aprobada.
- Usar cobertura para localizar caminos sin evidencia, no como una cifra que incentive tests vacios. Riesgo, invariantes y fallas historicas determinan la profundidad.
- No versionar outputs de build, logs, dumps, temporales ni artefactos generados salvo que sean una fuente intencional del producto.
- Los scripts de automatizacion deben ser idempotentes, fallar con salida accionable y evitar mutaciones destructivas implicitas.
- Si una frontera o regla se viola repetidamente, convertirla en una prueba de arquitectura, constraint, analyzer o check de CI en vez de confiar permanentemente en una lista manual.

## 17. Documentacion, decisiones y deuda

- Actualizar documentacion estable cuando cambia un contrato, source of truth, flujo operativo o comando.
- Registrar una decision/ADR cuando cambien limites, persistencia, topologia, seguridad, consistencia o contratos publicos; no para detalles locales reversibles.
- No crear bitacoras de trabajo o archivos `*_COMPLETADO.md`. La evidencia temporal pertenece al issue/PR; la regla vigente pertenece al documento canonico.
- Marcar deuda con causa, riesgo, propietario y condicion de resolucion; `TODO` sin contexto no es gestion de deuda.
- No preservar comportamiento incorrecto por compatibilidad accidental. Hacer explicito si la compatibilidad es contractual y planear la migracion.
- Si codigo y documento difieren, determinar cual representa la decision vigente, corregir la divergencia y no crear una tercera interpretacion.

## 18. Definition of Done

Antes de entregar, verificar:

- [ ] Se entendio el flujo existente y se reutilizo el punto de extension canonico.
- [ ] La regla tiene un solo propietario y no quedaron rutas o motores duplicados.
- [ ] Se respetan arquitectura, DDD/SOLID de forma proporcional y limites de capas.
- [ ] No hay hardcoding de tenant, ambiente, secretos, catalogo ni datos vivos.
- [ ] Multi-tenancy, autorizacion, idempotencia, concurrencia y tiempo fueron evaluados.
- [ ] Errores, logging, metricas y cancelacion son adecuados al riesgo.
- [ ] Se actualizaron todos los contratos/persistencia/configuracion/UI realmente afectados.
- [ ] Hay regresiones y pruebas de fallas relevantes, no solo happy path.
- [ ] Build, tests y lint relevantes se ejecutaron o sus bloqueos se reportaron con precision.
- [ ] No se suprimieron gates ni se alteraron dependencias/lock files sin formar parte del alcance.
- [ ] No se mezclaron cambios ajenos ni se sobrescribio trabajo existente.
- [ ] Documentacion y decisiones canonicas quedaron sincronizadas.
