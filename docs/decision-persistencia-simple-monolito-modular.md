# Decisión: persistencia simple para el monolito modular

## Prevalencia

Este documento reemplaza las decisiones anteriores que exigían:

- un esquema SQL por módulo;
- un `DbContext` por módulo;
- migraciones independientes por módulo.

La separación por librerías .NET se conserva sin cambios.

---

## Decisión definitiva para el MVP

Auraly Commerce utilizará inicialmente:

- una sola base de datos Azure SQL;
- el esquema existente `dbo`;
- un solo `AuralyDbContext`;
- una sola línea de migraciones de Entity Framework;
- una sola tabla de historial de migraciones;
- transacciones SQL normales para las operaciones que involucren varios módulos.

Esta elección reduce complejidad operativa y acelera el MVP.

---

## Separación que sí se mantiene

Cada contexto continúa separado en proyectos:

```text
Auraly.Domain.<Modulo>
Auraly.Application.<Modulo>
Auraly.Infrastructure.<Modulo>
Auraly.Contracts.<Modulo>
```

Ejemplo:

```text
Auraly.Domain.Sales
Auraly.Application.Sales
Auraly.Infrastructure.Sales
Auraly.Contracts.Sales

Auraly.Domain.Inventory
Auraly.Application.Inventory
Auraly.Infrastructure.Inventory
Auraly.Contracts.Inventory
```

La simplificación de la persistencia no autoriza:

- referencias de Application hacia Infrastructure;
- acceso de un módulo a repositorios de otro;
- uso de entidades de dominio ajenas;
- reglas de negocio dentro del `DbContext`;
- consultas SQL dispersas por toda la aplicación;
- acceso directo a la base desde la API.

---

## Proyecto central de persistencia

La solución tendrá una librería técnica:

```text
Auraly.Infrastructure.Database
```

Responsabilidades:

- `AuralyDbContext`;
- creación y configuración global de la conexión;
- transacciones;
- interceptores;
- auditoría técnica;
- outbox;
- migraciones;
- registro de configuraciones EF aportadas por los módulos.

Esta librería es un componente de infraestructura del monolito, no un módulo de negocio.

Estructura:

```text
src/
  Infrastructure/
    Auraly.Infrastructure.Database/
      AuralyDbContext.cs
      Migrations/
      Interceptors/
      Outbox/

  Modules/
    Sales/
      Auraly.Domain.Sales/
      Auraly.Application.Sales/
      Auraly.Infrastructure.Sales/
      Auraly.Contracts.Sales/

    Inventory/
      Auraly.Domain.Inventory/
      Auraly.Application.Inventory/
      Auraly.Infrastructure.Inventory/
      Auraly.Contracts.Inventory/
```

---

## Configuración EF por módulo

Aunque exista un único `DbContext`, cada módulo conserva cerca de sí sus configuraciones:

```text
Auraly.Infrastructure.Sales/
  Persistence/
    Configurations/
      SaleConfiguration.cs
      SaleLineConfiguration.cs
      PaymentConfiguration.cs
    Repositories/
      SaleRepository.cs

Auraly.Infrastructure.Inventory/
  Persistence/
    Configurations/
      StockMovementConfiguration.cs
      WarehouseConfiguration.cs
    Repositories/
      InventoryRepository.cs
```

`AuralyDbContext` aplica las configuraciones de las librerías registradas. Las migraciones resultantes se generan y almacenan centralmente en `Auraly.Infrastructure.Database`.

De esta manera:

- cada módulo organiza su mapeo;
- existe un solo modelo EF;
- existe una sola transacción;
- existe un solo comando de migración;
- no hay que coordinar el orden de múltiples migraciones.

---

## Propiedad lógica de tablas

Aunque todas las tablas estén en `dbo`, cada tabla tiene un único módulo propietario.

Ejemplo:

| Tabla | Propietario |
|---|---|
| `Products` | Catalog |
| `ProductBarcodes` | Catalog |
| `PriceLists` | Pricing |
| `StockMovements` | Inventory |
| `GoodsReceipts` | Purchasing |
| `AccountsPayable` | Payables |
| `Sales` | Sales |
| `CashSessions` | Cash |
| `AccountsReceivable` | Receivables |
| `Returns` | Returns |
| `ElectronicDocuments` | Fiscal |
| `PosDevices` | PosSync |

La propiedad se documenta y se verifica en revisiones y pruebas de arquitectura. Que una tabla esté disponible dentro del mismo `DbContext` no significa que cualquier módulo pueda modificarla.

---

## Transacciones

La principal ventaja de esta decisión es que una venta puede guardarse en una única transacción:

```text
Factura
Líneas
Medios de pago
Movimiento de caja
Movimiento de inventario
Cuenta por cobrar, si aplica
Solicitud fiscal en outbox
```

Si una operación obligatoria falla, se revierte toda la transacción.

La comunicación externa con DIAN, Web PubSub o cualquier proveedor ocurre después del commit mediante outbox; nunca se mantiene una transacción SQL abierta mientras se espera una red externa.

---

## Reglas para no crear un monolito acoplado

1. La API no inyecta `AuralyDbContext` en endpoints.
2. Application no referencia `AuralyDbContext`.
3. Cada caso de uso accede mediante interfaces definidas por su módulo.
4. Cada repositorio pertenece a una sola Infrastructure modular.
5. Un repositorio no se reutiliza desde otro módulo.
6. Los módulos conversan mediante Contracts o servicios de Application.
7. Domain nunca conoce Entity Framework.
8. Las entidades no tienen navegaciones hacia agregados de otros dominios salvo una excepción explícitamente documentada.
9. Los reportes pueden consultar modelos combinados, pero nunca escribir fuentes operativas.
10. Las migraciones son centrales, pequeñas y revisadas antes de aplicarse.

---

## Ruta futura a microservicios

Esta decisión hace que separar físicamente la base en el futuro requiera trabajo de datos. Es un costo aceptado conscientemente para acelerar el MVP.

La mayor parte de la preparación se conserva porque:

- el dominio ya está separado;
- los casos de uso ya están separados;
- los contratos ya están versionados;
- la propiedad de tablas está documentada;
- las integraciones asíncronas usan outbox e idempotencia;
- los módulos no consumen Infrastructure ajena.

Cuando se extraiga un módulo:

1. se crea su `DbContext` independiente;
2. se copian sus configuraciones EF existentes;
3. se genera una migración inicial para sus tablas;
4. se migran los datos de las tablas que posee;
5. se sustituyen llamadas internas por HTTP o Service Bus;
6. se elimina su registro del host original.

El esfuerzo estará principalmente en migrar datos y reemplazar transacciones compartidas por una saga, no en reescribir reglas de negocio.

---

## Cuándo reconsiderar la separación física

No se separará por anticipación. Se reconsiderará cuando exista al menos uno de estos motivos:

- un módulo necesita escalar de forma independiente;
- requiere aislamiento normativo o de seguridad;
- sus migraciones bloquean el despliegue general;
- necesita otra tecnología de almacenamiento;
- tiene un equipo y ritmo de despliegue independiente;
- sus transacciones ya no necesitan atomicidad con otros módulos;
- la carga o disponibilidad justifica el costo operacional.

---

## Conclusión

> Para el MVP: una base, `dbo`, un `DbContext` y migraciones centrales. Para la mantenibilidad: Domain, Application, Infrastructure y Contracts separados por contexto.

Esta es la mejor relación entre velocidad actual y capacidad futura de evolución.
