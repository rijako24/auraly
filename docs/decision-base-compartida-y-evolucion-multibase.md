# Decisión: base compartida inicial y evolución controlada a multibase

Fecha: 2026-08-27

## Estado y decisión

Auraly saldrá a producción con una única base Azure SQL compartida para los
tres clientes iniciales. La aplicación, los workers, las colas y los demás
componentes de Azure continúan compartidos. No se implementa todavía un router
de bases por cliente ni se crean ambientes completos por cliente.

La fuente de conexión SQL de cada proceso es única y se resuelve en su raíz de
composición. EF Core y los factories de los módulos consumen esa misma fuente;
un repositorio, worker o módulo no puede leer su propia cadena desde
configuración. Esta es la costura que podrá reemplazarse por una política de
ruteo cuando exista un cliente que cumpla los criterios de separación.

## Frontera de datos

`Tenant` sigue siendo la empresa y frontera SaaS; `Business` sigue siendo la
sede. `Businesses(TenantId, BusinessId)` es la relación canónica de propiedad.
Los datos operativos reciben `BusinessId` y derivan el tenant mediante
`Businesses`; no se propagará ni duplicará `TenantId` en contratos, mensajes o
tablas que ya tienen una relación inequívoca con `BusinessId`.

Antes de ejecutar una operación, el servidor valida que el `BusinessId`
seleccionado pertenece al tenant autenticado y que el usuario tiene membresía.
Las claves foráneas de las raíces de dominio conservan la propiedad aun si una
validación de aplicación falla. Las pruebas de regresión deben cubrir tanto el
rechazo de una combinación tenant/sede cruzada como esas relaciones de esquema.

## Observabilidad para decidir con datos

Cada petición autenticada agrega `TenantId` y `BusinessId` al scope de logs y a
la actividad distribuida, y registra método, ruta, estado y duración. En
Application Insights se deben vigilar por tenant:

- tasa y percentiles de latencia;
- errores y throttling;
- consumo de DTU, CPU, sesiones, almacenamiento y crecimiento;
- volumen y atraso de trabajos por `BusinessId`, agregado al tenant mediante
  `Businesses`;
- incidentes o ventanas operativas incompatibles entre clientes.

No se ejecuta una prueba de carga antes del primer release. Se establece una
línea base con uso real y se revisa inicialmente cada semana.

## Criterios para separar una base

Una base dedicada se evalúa, no se activa automáticamente, cuando se presenta
al menos una de estas condiciones sostenidas:

1. obligación contractual, regulatoria, de residencia, cifrado o restauración
   independiente;
2. necesidad de backup, retención, RPO/RTO o ventana de mantenimiento propia;
3. un tenant domina de forma sostenida el consumo y degrada a los demás aun
   después de optimizar consultas e índices y escalar razonablemente la base;
4. crecimiento o concurrencia que vuelve más económico o seguro aislarlo;
5. riesgo operativo o comercial que justifica blast radius independiente.

El tamaño del cliente o el número de usuarios, por sí solos, no activan la
separación.

## Diseño futuro permitido

Si se aprueba separar un tenant, se conservarán el mismo esquema, DACPAC,
aplicación y motores. Una tabla de control fuera de las bases operativas podrá
mapear `TenantId` a un identificador opaco de conexión; los secretos vivirán en
Key Vault. El contexto autenticado se resolverá primero y luego elegirá la
conexión. `BusinessId` seguirá validándose contra `Businesses` dentro de la base
seleccionada.

La migración exigirá inventario de todas las tablas dependientes, corte de
escrituras, copia consistente, validación de conteos y hashes, actualización
atómica del mapa, smoke tests y un rollback probado. No se crearán engines,
colas, contratos ni binarios por tenant.

## Capacidad inicial de PROD

DEV permanece en Azure SQL Basic. PROD comienza en Standard S1, 20 DTU, con
precio público aproximado de USD 29,44/mes en West US 2 al momento de esta
decisión, por debajo del límite de USD 50/mes para la base. Se prefiere S1 sobre
serverless porque cajas, workers y procesos programados reducen la oportunidad
de auto-pausa y hacen menos predecible el costo. El cambio de SKU no altera
datos ni contratos; puede volver a Basic solo mientras tamaño y carga cumplan
sus límites, o bajar a S0 si ya excede la capacidad de Basic.

## Consecuencias

Se minimiza costo y complejidad para el lanzamiento, sin cerrar la evolución a
una base dedicada. El aislamiento inicial depende de validaciones y claves de
propiedad, por lo que sus pruebas y métricas son obligatorias. La activación de
PROD sigue el pipeline canónico: el mismo release inmutable validado en DEV,
aprobación del environment `prod`, `what-if`, despliegue y smoke tests; no se
publica directamente desde un árbol de trabajo con cambios locales.
