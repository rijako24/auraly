# Decisión: monolito modular por librerías .NET

## Decisión definitiva

Auraly Commerce se desplegará inicialmente dentro de la misma API, pero el código no se organizará como capas globales compartidas.

Cada contexto de negocio tendrá sus propias librerías:

```text
Auraly.Domain.<Modulo>
Auraly.Application.<Modulo>
Auraly.Infrastructure.<Modulo>
Auraly.Contracts.<Modulo>
```

Ejemplo:

```text
Auraly.Domain.Catalog
Auraly.Application.Catalog
Auraly.Infrastructure.Catalog
Auraly.Contracts.Catalog

Auraly.Domain.Inventory
Auraly.Application.Inventory
Auraly.Infrastructure.Inventory
Auraly.Contracts.Inventory

Auraly.Domain.Sales
Auraly.Application.Sales
Auraly.Infrastructure.Sales
Auraly.Contracts.Sales
```

No se crearán únicamente:

```text
Auraly.Domain
Auraly.Application
Auraly.Infrastructure
```

como grandes librerías generales, porque con el tiempo mezclarían todos los dominios y dificultarían la extracción.

---

## Estructura de la solución

```text
src/
  Hosts/
    Auraly.Api/
    Auraly.Functions/
    Auraly.Workers/

  Modules/
    Catalog/
      Auraly.Domain.Catalog/
      Auraly.Application.Catalog/
      Auraly.Infrastructure.Catalog/
      Auraly.Contracts.Catalog/

    Pricing/
      Auraly.Domain.Pricing/
      Auraly.Application.Pricing/
      Auraly.Infrastructure.Pricing/
      Auraly.Contracts.Pricing/

    Inventory/
      Auraly.Domain.Inventory/
      Auraly.Application.Inventory/
      Auraly.Infrastructure.Inventory/
      Auraly.Contracts.Inventory/

    Purchasing/
    Payables/
    Sales/
    Cash/
    Receivables/
    Returns/
    Fiscal/
    PosSync/
    Reporting/

  Shared/
    Auraly.SharedKernel/
    Auraly.BuildingBlocks/

tests/
  Architecture/
    Auraly.Tests.Architecture/
  Modules/
    Catalog/
    Inventory/
    Sales/
```

Se crea un grupo de librerías por contexto de negocio, no por pantalla, caso de uso o entidad.

---

## Responsabilidad de cada librería

### `Auraly.Domain.<Modulo>`

Contiene:

- entidades;
- agregados;
- value objects;
- reglas e invariantes;
- servicios de dominio;
- eventos de dominio;
- especificaciones estrictamente propias del dominio.

No referencia:

- Entity Framework;
- Azure;
- HTTP;
- Functions;
- la API;
- Infrastructure;
- dominios internos de otros módulos.

### `Auraly.Application.<Modulo>`

Contiene:

- casos de uso;
- comandos y queries internos;
- handlers;
- validadores;
- DTO internos;
- interfaces o puertos requeridos por los casos de uso;
- autorización de aplicación;
- coordinación de transacciones;
- mapeos entre contratos y dominio.

Referencia:

- su propio Domain;
- sus propios Contracts;
- Contracts públicos de otros módulos cuando sea necesario.

No referencia Infrastructure.

### `Auraly.Infrastructure.<Modulo>`

Contiene:

- `DbContext`;
- configuraciones Entity Framework;
- repositorios;
- migraciones;
- clientes externos;
- Azure Storage, Service Bus o Web PubSub;
- implementaciones de los puertos definidos por Application;
- registro de dependencias del módulo.

Referencia su Application y Domain, pero nunca Infrastructure de otro módulo.

### `Auraly.Contracts.<Modulo>`

Contiene únicamente la superficie pública del módulo:

- solicitudes públicas;
- respuestas;
- eventos de integración versionados;
- identificadores y DTO serializables;
- interfaces públicas estrictamente necesarias.

No expone:

- entidades EF;
- agregados;
- repositorios;
- tipos internos del dominio;
- detalles de Azure o SQL.

Estos contratos deben poder transportarse mañana mediante HTTP o Azure Service Bus sin cambiar el dominio.

---

## Dependencias permitidas

```text
                    +--------------------+
                    | Auraly.Api         |
                    | Composition Root   |
                    +----------+---------+
                               |
                   registra cada módulo
                               |
          +--------------------+--------------------+
          |                    |                    |
          v                    v                    v
 Infrastructure.Catalog  Infrastructure.Sales  Infrastructure.Inventory
          |                    |                    |
          v                    v                    v
 Application.Catalog     Application.Sales     Application.Inventory
          |                    |                    |
          v                    v                    v
 Domain.Catalog          Domain.Sales          Domain.Inventory
```

Comunicación entre módulos:

```text
Application.Sales
        |
        v
Contracts.Inventory
```

Nunca:

```text
Application.Sales
        |
        v
Infrastructure.Inventory
```

Tampoco:

```text
Domain.Sales
        |
        v
Domain.Inventory
```

---

## La API como host

`Auraly.Api` será el composition root y contendrá solamente:

- configuración del host;
- autenticación y middleware;
- registro de módulos;
- mapeo de endpoints;
- OpenAPI;
- observabilidad;
- manejo transversal de errores.

Ejemplo:

```csharp
builder.Services
    .AddCatalogModule(configuration)
    .AddPricingModule(configuration)
    .AddInventoryModule(configuration)
    .AddSalesModule(configuration)
    .AddFiscalModule(configuration)
    .AddPosSyncModule(configuration);

app.MapCatalogEndpoints();
app.MapPricingEndpoints();
app.MapInventoryEndpoints();
app.MapSalesEndpoints();
app.MapFiscalEndpoints();
app.MapPosSyncEndpoints();
```

La API no contiene handlers, repositorios ni reglas de facturación.

---

## Separación de base de datos

Separar librerías sin separar propiedad de datos no prepara realmente para microservicios.

Aunque inicialmente exista una sola base Azure SQL, cada módulo tendrá:

- su propio esquema SQL;
- su propio `DbContext`;
- sus propias migraciones;
- propiedad exclusiva de sus tablas.

```text
catalog.*
pricing.*
inventory.*
purchasing.*
payables.*
sales.*
cash.*
receivables.*
returns.*
fiscal.*
possync.*
reporting.*
```

Reglas:

1. Un `DbContext` no mapea tablas de otro módulo.
2. No existen navegaciones EF entre módulos.
3. Un módulo no escribe en tablas ajenas.
4. Los comandos no resuelven reglas mediante joins entre esquemas.
5. Reporting usa proyecciones reconstruibles y nunca modifica fuentes.
6. Las referencias externas se guardan por identificador.

Durante el MVP podrán existir transacciones coordinadas en la misma base, pero siempre mediante interfaces de Application. No se permitirá manipulación directa de tablas de otro contexto.

---

## Comunicación preparada para extracción

Dentro del monolito, una llamada puede ejecutarse en proceso, pero debe cruzar el mismo contrato que usaría fuera del proceso.

Ejemplo de venta:

```text
Sales confirma venta
    |
    +--> Contracts.Inventory: comprometer movimiento
    +--> Contracts.Cash: registrar medios de pago
    +--> Contracts.Receivables: generar cartera si aplica
    +--> Contracts.Fiscal: solicitar documento electrónico
```

Los efectos asíncronos utilizan:

- outbox transaccional;
- inbox por consumidor;
- idempotencia;
- eventos versionados;
- `EventId`;
- `TenantId`;
- `CorrelationId`;
- `CausationId`;
- fecha UTC.

Cuando un módulo se extraiga, el adaptador en proceso se reemplaza por HTTP o Service Bus. Los casos de uso y el dominio no deben cambiar.

---

## Contextos del MVP

Las librerías se crean cuando el contexto entra en construcción, no todas vacías desde el primer día.

Orden recomendado:

1. Catalog.
2. Pricing.
3. Cash.
4. Sales.
5. PosSync.
6. Inventory.
7. Purchasing.
8. Payables.
9. Receivables.
10. Returns.
11. Fiscal.
12. Reporting.

Organization y Parties pueden reutilizar la base actual de Auraly si respetan las mismas reglas de aislamiento.

---

## Pruebas de arquitectura obligatorias

CI debe fallar cuando:

- Domain referencia Infrastructure;
- Application referencia Infrastructure;
- un módulo referencia Infrastructure de otro;
- un Domain referencia otro Domain;
- una entidad EF aparece en Contracts;
- un `DbContext` mapea tablas fuera de su esquema;
- la API contiene implementaciones de casos de uso.

Estas restricciones se implementarán con `NetArchTest` o `ArchUnitNET` y pruebas adicionales sobre los modelos de EF.

---

## SharedKernel

`Auraly.SharedKernel` será deliberadamente pequeño.

Puede contener:

- `Money`;
- identificadores base;
- `Result`;
- abstracción de eventos;
- fechas o unidades universales.

No puede contener:

- Producto;
- Cliente;
- Factura;
- Bodega;
- Precio;
- Caja;
- repositorios;
- un `DbContext` compartido.

Si un concepto pertenece a un negocio concreto, pertenece a su módulo.

---

## Extracción futura de un módulo

Un módulo estará listo para migrar a microservicio cuando:

1. sea propietario de su esquema y migraciones;
2. no use entidades ni infraestructura de otros;
3. publique contratos versionados;
4. use outbox e inbox;
5. tenga pruebas unitarias e integración propias;
6. pueda registrarse mediante un único método;
7. tenga observabilidad y configuración propias.

La extracción consistirá en:

1. mover los proyectos del módulo a un nuevo host;
2. migrar su esquema a una base independiente;
3. reemplazar el adaptador en proceso por Service Bus o HTTP;
4. enrutar sus endpoints mediante gateway;
5. desplegarlo de forma separada.

La intención es que no sea necesario renombrar entidades, reescribir reglas ni desenredar consultas cruzadas.

---

## Regla central

> Compartir inicialmente proceso y base de datos es aceptable. Compartir modelos internos, tablas, `DbContext`, repositorios o infraestructura entre módulos no lo es.

