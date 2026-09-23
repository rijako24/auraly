# Contrato de trabajo para agentes de codigo

Este archivo aplica a todo el repositorio. Las instrucciones mas cercanas a un subdirectorio complementan estas reglas; si existe una contradiccion, se debe detener el cambio y exponerla en vez de inventar una tercera variante.

## Lectura obligatoria

Antes de modificar codigo, configuracion, esquema, infraestructura o pruebas:

1. Leer `CONTEXTO_CODEX.md` para conocer el sistema, sus fuentes de verdad y comandos vigentes.
2. Leer `docs/estandares-de-ingenieria.md` y aplicar su lista de verificacion.
3. Leer `docs/invariantes-arquitectonicas-auraly.md`. Es la politica canonica de motores, colas, catalogos, tablas y dropdowns.
4. Usar `docs/mapa-motores-flujos-y-extensiones.md` para localizar el propietario y el punto correcto de extension antes de crear codigo.
5. Consultar los documentos de arquitectura o decision que gobiernen el modulo afectado. Un diseno aprobado es la linea base; no se reemplaza ni se crea una arquitectura paralela sin una decision explicita.
6. Si el cambio toca el motor conversacional, prompts, `Agents.SettingsJson`, flows, facts, signals, operaciones, outcomes, checkout, reservas, pagos, escalaciones o canales inbound, leer completo `docs/agent-engine-manual.md`.
7. Obedecer cualquier `AGENTS.md` mas especifico dentro del arbol que se vaya a editar.

No se debe programar basandose solo en nombres de archivos, memoria del modelo o supuestos. Cuando documentacion y runtime difieran, se identifica la autoridad de ese dato, se valida en el codigo y se corrige la documentacion en el mismo cambio cuando corresponda.

## Preflight obligatorio

Antes de crear una clase, servicio, flujo, handler, endpoint, tabla, configuracion o componente:

- Revisar `git status` y preservar cambios existentes que no pertenezcan a la tarea.
- Buscar con `rg` capacidades, contratos, registros DI, call sites, tests, seeds, tablas y configuraciones equivalentes.
- Trazar el flujo de extremo a extremo y nombrar el propietario canonico de la regla.
- Determinar si se debe extender, reutilizar, consolidar o eliminar algo existente. Copiar una implementacion para avanzar mas rapido no es una opcion valida.
- Toda funcionalidad nueva debe entrar por un motor y punto de extension canonicos. Una tarea funcional no puede crear otro motor, processor, worker propietario, job table, writer o cola que replique una capacidad existente; si ningun propietario actual parece aplicable, se detiene la implementacion y se eleva la decision arquitectonica.
- Identificar efectos sobre multi-tenancy, autorizacion, datos, concurrencia, idempotencia, compatibilidad, observabilidad y rollback.
- Tratar rendimiento como atributo obligatorio desde el diseño: definir el camino critico, volumen esperado, numero de viajes de red/consultas y presupuesto de latencia o throughput antes de implementar. No se acepta N+1, trabajo por render, polling descontrolado ni procesamiento proporcional a datos ajenos a la operacion.
- Prohibir lecturas de base de datos, llamadas HTTP, lecturas de secretos u operaciones de blob por elemento. Las lecturas deben resolverse por conjunto, lote o pagina y su cantidad de viajes debe permanecer acotada al crecer el volumen. Un comando por elemento solo se admite cuando cada elemento es una transaccion independiente, el lote tiene limite explicito y se reutiliza el motor canonico; nunca habilita N+1 de lectura. Si una cardinalidad de uno es una invariante, debe estar respaldada por esquema/contrato y validarse explicitamente en el codigo.
- Evitar resincronizaciones, invalidaciones y refetches redundantes. Un comando debe devolver o permitir reutilizar el estado autoritativo producido; solo se admite una nueva lectura cuando exista una razon de consistencia documentada, con alcance acotado y prueba que verifique el numero de viajes.
- Ejecutar solamente lo pedido y los pasos indispensables para completarlo. No inferir, diseñar ni agregar procesos, flujos de recuperacion, abstracciones, estados, efectos o mejoras no solicitados. Si para continuar hace falta una decision fuera del alcance explicito, se informa y se solicita esa decision antes de implementarla. Un reintento idempotente de un comando ya terminado se limita a devolver su resultado autoritativo y no repite efectos.
- En frontend, cada recurso visible tiene un solo propietario de carga y refresco. Componentes duplicados, contadores auxiliares y vistas ocultas no pueden consultar por separado el mismo recurso. Una mutacion reutiliza su respuesta autoritativa y actualiza ese propietario; no remonta componentes ni dispara un segundo GET para reconstruir lo que el comando ya devolvio.
- El estado operativo autoritativo se actualiza por evento push o por una accion explicita del usuario. Se prohibe introducir polling periodico de red. La unica excepcion es el heartbeat propietario de un lease activo: se agenda despues de completar el intento anterior, corre solo mientras el lease existe, usa un intervalo inferior a su expiracion y se detiene ante fallo o liberacion. Toda reconexion de WebSocket/SSE usa la politica compartida de backoff, evita intentos simultaneos, tiene un maximo finito de intentos fallidos y se reactiva solamente por una señal explicita como visibilidad, conectividad o accion del usuario.
- Cada indicador de progreso representa una sola etapa y se cierra al terminar esa etapa. No se mantiene un mensaje como `Actualizando precios` mientras se ejecutan guardado, inventario, impresion o sincronizacion; las etapas posteriores deben declararse por separado y medirse de extremo a extremo.
- Definir evidencia de aceptacion antes de implementar: test, build, lint, consulta o escenario reproducible.

## Forma de trabajar

- Todo despliegue, incluido DEV, debe ejecutarse desde un commit ya integrado en
  `origin/main`. No se publica desde ramas de trabajo, commits sueltos o un
  checkout con cambios sin confirmar. El release de producción puede usar un
  tag inmutable, siempre que su commit pertenezca a `origin/main`.
- Trabajar siempre sobre el único checkout operativo, ya sea en la rama `main`
  o en una rama creada a partir de `main`. No crear `git worktree`, clones
  anidados ni copias paralelas del repositorio. Una tarea que requiera
  aislamiento puede usar commits pequeños y reversibles en su rama; si el
  checkout contiene cambios ajenos, primero se identifican y preservan sin
  abrir otro árbol de trabajo.
- Realizar el cambio minimo coherente que resuelva la causa raiz y deje el sistema consistente de punta a punta.
- No mezclar refactors, renombrados o formateos ajenos a la tarea.
- No ocultar errores con fallbacks silenciosos, datos inventados, `catch` vacios o defaults inseguros.
- No introducir dependencias, patrones, capas o abstracciones sin una necesidad demostrable.
- Mantener contratos compatibles salvo que el cambio de ruptura este aprobado y tenga migracion/cutover.
- Actualizar codigo, contratos, DI, persistencia, seeds, admin, pruebas y documentacion cuando sean partes reales del mismo slice.
- Si una regla exige una excepcion, documentar motivo, alcance, riesgo, mitigacion y condicion de retiro. Una excepcion no se convierte en precedente implicito.

## Criterio de terminado

Un cambio de implementacion no esta terminado hasta que:

- Cumple `docs/estandares-de-ingenieria.md` y las invariantes del modulo.
- Tiene pruebas proporcionales al riesgo, incluida una regresion para el bug corregido.
- Compila y pasa los checks relevantes de backend/frontend; si alguno no se pudo ejecutar, se reporta expresamente.
- Cumple el presupuesto de rendimiento acordado y aporta una medicion reproducible del camino critico, sin carga de diagnostico concurrente que adultere el resultado.
- No deja rutas duplicadas, codigo muerto, configuracion huerfana ni secretos/datos sensibles.
- Conserva aislamiento por tenant, idempotencia, autorizacion y observabilidad donde aplican.
- La entrega resume archivos cambiados, decisiones, evidencia ejecutada y riesgos pendientes reales.

## Auditoria posterior obligatoria

Despues de cada implementacion y antes de entregarla, el agente debe auditar el diff completo contra este archivo, `docs/estandares-de-ingenieria.md`, las invariantes y los documentos propietarios del modulo. Esta revision posterior no se sustituye por haber hecho preflight ni por ejecutar tests.

Esta auditoria es una revision fuerte tipo PR de **todo** cambio realizado, no una
lectura superficial ni un resumen de lo implementado. Debe volver a cuestionar
el diseno y revisar archivo por archivo en busca de malas practicas, riesgos,
codigo repetido, reglas duplicadas, propietarios alternos y rutas diferentes
para el mismo proceso. Tambien debe comprobar que no se agregaron procesos,
estados, reintentos, recuperaciones, abstracciones o efectos que el usuario no
pidio. Un hallazgo se corrige y se vuelven a ejecutar los checks afectados; si
no puede corregirse sin una decision fuera del alcance, se entrega como bloqueo
con evidencia concreta. Ningun cambio se considera terminado solamente porque
compile o porque sus pruebas pasen.

La auditoria debe comprobar y dejar en la entrega evidencia explicita de que:

- la funcionalidad reutilizo el motor, flujo, writer, tabla, catalogo y punto de extension canonicos, sin crear una ruta paralela;
- cada regla y escritura conserva un unico propietario y no quedo duplicada entre capas, prompts, seeds, UI o pruebas;
- se cumplieron multi-tenancy, autorizacion, idempotencia, concurrencia, observabilidad, compatibilidad y rollback donde aplican;
- el codigo cumple las buenas practicas de diseno y seguridad proporcionales al cambio, sin hardcoding, fallbacks silenciosos, codigo muerto ni abstracciones innecesarias;
- las pruebas y checks ejecutados demuestran el criterio de aceptacion, incluida la regresion del comportamiento modificado;
- se audito el numero de consultas y viajes de red del camino modificado: no hay I/O por elemento, N+1, fetch-all, resincronizaciones/refetches redundantes ni polling no acotado; cuando aplique, una prueba o medicion verifica cantidad de viajes, paginacion y presupuesto de latencia;
- se verifico que cada recurso frontend tenga un solo propietario de carga, que no queden vistas ocultas consultando, que las reconexiones usen el backoff acotado compartido y que los estados de progreso coincidan con la etapa realmente activa;
- la documentacion canonica quedo alineada y cualquier contradiccion encontrada se corrigio en el mismo cambio o se reporto como bloqueo real.

Si la auditoria posterior encuentra un incumplimiento, incluida una mala practica preexistente en el camino tocado que el cambio mantenga, replique o agrave, la implementacion no esta terminada: se corrige en el mismo cambio o se reporta como bloqueo real, y se repiten los checks afectados antes de entregar.
