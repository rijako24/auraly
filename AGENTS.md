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
- Identificar efectos sobre multi-tenancy, autorizacion, datos, concurrencia, idempotencia, compatibilidad, observabilidad y rollback.
- Definir evidencia de aceptacion antes de implementar: test, build, lint, consulta o escenario reproducible.

## Forma de trabajar

- Trabajar siempre sobre el único checkout operativo de la rama `main`. No crear
  `git worktree`, clones anidados ni copias paralelas del repositorio. Una tarea
  que requiera aislamiento usa commits pequeños y reversibles en `main`; si
  `main` contiene cambios ajenos, primero se identifican y consolidan sin abrir
  otro árbol de trabajo.
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
- No deja rutas duplicadas, codigo muerto, configuracion huerfana ni secretos/datos sensibles.
- Conserva aislamiento por tenant, idempotencia, autorizacion y observabilidad donde aplican.
- La entrega resume archivos cambiados, decisiones, evidencia ejecutada y riesgos pendientes reales.
