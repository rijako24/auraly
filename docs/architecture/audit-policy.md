# Política de auditoría de Auraly

## Decisión

Auraly no usa triggers de base de datos ni genera una fila de auditoría por cada request. Las invariantes se ejecutan en servicios transaccionales explícitos. La auditoría de datos se captura de forma centralizada en ApplicationDbContext.SaveChanges; los documentos del motor se evidencian mediante sus eventos canónicos y outbox.

Una prueba de arquitectura rechaza cualquier definición CREATE TRIGGER o ALTER TRIGGER dentro del proyecto de base de datos.

## Qué se audita

1. **Gobierno y seguridad:** creación, activación e inactivación de tenants y usuarios; cambios de cupos; enrolamiento o revocación de cajas; roles y permisos; recuperación de contraseña y configuración fiscal.
2. **Configuración de negocio:** cambios de maestros que alteren ventas, inventario, impuestos, precios o contabilidad.
3. **Motor transaccional:** documentos aceptados, transiciones de estado, inventario, cartera, tesorería, fiscal y asientos. La evidencia es el documento, sus eventos y outbox, sin copiar cada línea a AuditLogs.
4. **Acceso HTTP:** se observa con telemetría y correlación, pero no crea auditoría por verbo o URL. La evidencia auditable nace del cambio confirmado.

## Diseño centralizado

- Un único punto en ApplicationDbContext.SaveChanges inspecciona solo una lista permitida de agregados de gobierno y configuración.
- Captura únicamente propiedades escalares cambiadas y agrega una fila compacta por agregado al mismo DbContext y al mismo commit.
- Servicios y controladores no llaman auditoría manualmente.
- AuditLog, outbox, mensajes, líneas y tablas de alto volumen quedan excluidos para evitar ciclos, duplicidad y costo innecesario.
- Operaciones que no usan EF deben producir un evento canónico/outbox en su propia transacción.
- La entrega o analítica posterior es asíncrona e idempotente.

## Datos mínimos

Cada registro identifica fecha UTC, actor, tenant del actor, sede cuando aplique, correlación, operación, tipo e identificador del agregado y valores anteriores/nuevos permitidos. Cuando se administra otro tenant, el identificador del tenant objetivo queda dentro del cambio capturado.

Nunca se almacenan contraseñas, hashes, salts, tokens, PIN, secretos, credenciales, certificados, claves privadas, datos de tarjeta, payloads crudos ni cuerpos sensibles. Los nombres de propiedades sensibles se excluyen centralmente y los textos permitidos se limitan en tamaño.

## Atomicidad, concurrencia y motor

- Auditoría y cambio se confirman o revierten juntos.
- No hay llamadas de red, colas ni un segundo SaveChanges en la ruta crítica.
- La publicación asíncrona usa outbox; nunca se publica antes del commit.
- Los cupos de usuarios y cajas se validan con UPDLOCK/HOLDLOCK dentro de la transacción que crea o reactiva.
- Los rangos fiscales se validan en el servicio transaccional que guarda resolución y series.
- TenantKey no aparece en contratos de actualización y el agregado solo lo asigna al aprovisionarse.

## Presupuesto de rendimiento

La captura reutiliza el ChangeTracker, filtra primero por tipo y estado, serializa solo campos modificados y añade la auditoría al lote SQL existente. Se monitorean los percentiles de SaveChanges y el volumen por agregado. Una prueba de regresión debe demostrar que entidades excluidas no generan auditoría y que datos sensibles nunca aparecen.

## Retención y acceso

Los eventos fiscales y contables siguen la retención legal aplicable. La auditoría de seguridad y configuración se conserva durante el periodo de cumplimiento definido para la plataforma. AuditLogs y eventos son append-only para la aplicación ordinaria: no existen endpoints de edición o eliminación. Las exportaciones, purgas autorizadas y consultas masivas se ejecutan por lotes fuera de la ruta transaccional.

## Revisión obligatoria

Todo PR que agregue un comando debe declarar: agregado, invariante, frontera transaccional, evidencia generada, campos redactados y flujo completo que lo prueba. No se aceptan triggers, auditoría por middleware HTTP, llamadas manuales repetidas desde servicios ni movimientos contables creados directamente por botones/controladores.