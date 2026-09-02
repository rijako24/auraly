# Decisión definitiva: maestro de personas, roles, sedes y cuentas de usuario

**Estado:** Aceptada
**Fecha:** 29 de julio de 2026
**Ámbito:** Auraly Commerce, SaaS y On-Premise

## 1. Decisión

Auraly tendrá un único maestro canónico `Party` para representar una persona natural o una organización.

Cliente, proveedor, empleado, vendedor, transportador, conductor y usuario no serán copias de la persona ni clases persistidas mediante herencia. Serán relaciones especializadas que referencian la misma `Party`.

```text
Party
 ├── Customer
 ├── Supplier
 ├── Employee
 ├── Seller
 ├── Carrier
 ├── Driver
 └── UserAccount (opcional)
```

La fila especializada es la fuente de verdad del rol. No se crearán booleanos `IsCustomer`, `IsSupplier`, `IsEmployee` o `IsUser` en `Parties`.

Una misma Party puede combinar roles, pero un rol no concede otro automáticamente: un cliente puede ser proveedor; un empleado puede ser vendedor y usuario; un proveedor no obtiene acceso; un usuario puede no ser empleado.

## 2. Conocimiento absorbido de Xion

Se conserva el principio útil de Xion: `Persona` concentra información común y cada capacidad posee tabla específica.

No se copian IDs numéricos, flags `EsCliente`/`EsProveedor`, contraseñas, teléfonos repetidos, dirección única, identificación usada para distinguir sedes, personas duplicadas para representar establecimientos ni un grafo Entity Framework compartido por todos los módulos.

Auraly usa composición y contratos públicos, no herencia persistente.

## 3. Propiedad y aislamiento

`Parties` lleva `TenantId` porque una identidad puede relacionarse con varios `Business` del mismo tenant. Aquí no es redundante: Party no pertenece a un solo negocio. Nunca se comparte una Party entre tenants.

Las tablas hijas no repiten TenantId cuando se deriva por PartyId, excepto si una restricción SQL, partición o integridad demostrada lo necesita.

Los roles comerciales llevan `BusinessId`: Customers, Suppliers, Employees, Sellers y, cuando aplica, Carriers y Drivers. Su unicidad mínima es `PartyId + BusinessId` por rol.

## 4. Modelo SQL canónico

Todos los IDs internos son UUIDv7 generados por la aplicación.

### Parties

Campos mínimos:

- PartyId, TenantId y PartyKind (`NaturalPerson` u `Organization`);
- DisplayName;
- nombres/apellidos o razón social/nombre comercial/representante;
- país y tipo de identificación;
- identificación original y normalizada;
- dígito de verificación separado;
- datos personales opcionales solo con caso de uso;
- estado, auditoría y RowVersion.

La identificación primaria queda en Parties para imponer:

```text
UNIQUE (TenantId, IdentificationCountryCode, IdentificationTypeCode, NormalizedIdentification)
```

La normalización depende del tipo. Para NIT colombiano se eliminan solo separadores de presentación y el dígito va separado. Esa regla no se aplica ciegamente a pasaportes o documentos extranjeros.

### PartyIdentifications

Colección opcional para identificaciones adicionales. No duplica la primaria. Conserva país, tipo, original, normalizada, vigencia, estado y auditoría.

### PartyContacts

Colección con PartyContactId, PartyId, PartySiteId opcional, tipo, propósito, valor original/normalizado, principal, estado y auditoría. No existirán Phone1 a Phone4.

### PartyAddresses

Colección con PartyAddressId, PartyId, PartySiteId opcional, propósito, dirección, geografía, código postal, coordenadas opcionales, principal, estado y auditoría.

### PartyTaxResponsibilities

Responsabilidades fiscales vigentes. El catálogo canónico de códigos se aprovisiona en
`reference.Options` bajo `tax-responsibility`; el perfil tributario del tercero conserva
la selección vigente en `CounterpartyWithholdingProfiles.Responsibilities`. Las reglas
de retención no se asignan manualmente al tercero: el motor las resuelve por dirección,
concepto, jurisdicción, base y responsabilidades requeridas. Los documentos conservan
snapshot y no dependen del valor actual.

El catálogo se consulta y administra desde Contabilidad → Retenciones →
Responsabilidades tributarias. La creación exige `catalog.update` y se realiza en el
maestro, no dentro del formulario de una regla. Una regla exige que el tercero tenga
todas las responsabilidades seleccionadas; sin selección, no filtra por esta dimensión.

### PartySites

Representa sedes o establecimientos:

- PartySiteId y PartyId;
- código corto, nombre y nombre comercial opcional;
- dirección y contactos;
- IsPrimary, IsBillingSite, IsDeliverySite;
- estado y auditoría.

El código es único por Party y solo puede existir una sede principal activa. Una sede no crea otra identidad ni repite el documento. Si tiene identificación legal distinta, es otra Party.

Ejemplo POS:

```text
900123456-7 · Comercial ABC · NORTE · Bucaramanga
900123456-7 · Comercial ABC · CENTRO · Floridablanca
```

### Customers

CustomerId, PartyId, BusinessId, código opcional, alta, estado, vendedor predeterminado, condiciones comerciales mínimas, auditoría y RowVersion. Restricción `UNIQUE (PartyId, BusinessId)`.

CxC pertenece a Receivables y referencia CustomerId.

### CustomerPricingSettings

CustomerId, BusinessId solo si hace falta para integridad SQL, PriceListId o PriceChannelId (nunca ambos), vigencia y auditoría.

Sin lista ni canal se usa el precio base del producto para el Business. La ausencia de precio especial nunca bloquea. La configuración pertenece al cliente, no a la sede.

### Suppliers

SupplierId, PartyId, BusinessId, código/EAN, crédito, reglas de recepción, tipos de suministro confirmados, contacto, estado y auditoría. Restricción `UNIQUE (PartyId, BusinessId)`.

Productos, costos y CxP referencian SupplierId.

### Employees

EmployeeId, PartyId, BusinessId, código, cargo, ingreso/retiro, sede, estado y auditoría. No contiene credenciales y puede existir sin usuario.

### Sellers

SellerId, PartyId, BusinessId, código, alta, estado, sedes permitidas y auditoría. Puede ser empleado o contratista, con o sin usuario. Ventas y pedidos referencian SellerId.

### Carriers y Drivers

Carrier es la persona u organización responsable del transporte. Driver es la persona natural que conduce. Se separan porque una transportadora puede tener varios conductores. El MVP no implementa TMS.

### AppUsers

Una cuenta de usuario es identidad de autenticación, no rol comercial. Conserva UserId, TenantId, PartyId opcional, usuario/correo de acceso normalizados, proveedor de identidad o hash seguro, estado, bloqueo, expiración y auditoría.

PartyId puede ser nulo para cuentas técnicas. Ser cliente, proveedor, empleado o vendedor no concede acceso. Permisos se asignan a UserId. No se migran contraseñas. Correo de acceso y correo fiscal pueden diferir.

## 5. Composición, no herencia

No se usará table-per-hierarchy, table-per-type, un grafo EF común ni una tabla PartyRoles que duplique las tablas especializadas.

```text
Customers.PartyId -> Parties.PartyId
Suppliers.PartyId -> Parties.PartyId
Employees.PartyId -> Parties.PartyId
Sellers.PartyId   -> Parties.PartyId
AppUsers.PartyId  -> Parties.PartyId
```

Una vista puede proyectar roles efectivos, pero la fila especializada es la única fuente de verdad.

## 6. Propiedad modular

- Parties: identidad, normalización, identificaciones, contactos, direcciones, sedes y fiscalidad.
- Sales: Customers, condiciones comerciales, selección de sede y snapshot del adquirente.
- Purchasing: Suppliers, condiciones, producto-proveedor y costos.
- Workforce/Organization: Employees, Sellers, Carriers, Drivers y asignaciones.
- Authorization: AppUsers, credenciales, sesiones, perfiles, permisos y alcances.

Una base compartida no autoriza consultas directas a tablas internas de otro módulo.

## 7. Creación administrativa y POS

La web normaliza y busca primero. Si Party existe, muestra roles y sedes y agrega solo el rol necesario. Agregar sede nunca crea otra Party ni concede usuario.

La ficha tiene pestañas de información general, identificaciones, contactos, sedes, fiscal y roles. Cada pestaña consume el contrato de su módulo dueño.

El POS:

1. busca identificación normalizada;
2. devuelve Party, Customer del negocio y sedes;
3. si Party existe sin Customer, agrega el rol con permiso;
4. si no existe, crea `Party + Customer + PartySite principal` atómicamente;
5. exige selección si hay varias sedes;
6. devuelve PartyId, CustomerId y PartySiteId;
7. regresa al campo de captura.

Web y POS usan los mismos comandos de aplicación; cambian autenticación, permisos y riqueza del contrato.

## 8. Creación offline

POS Edge persiste en SQLite un cliente pendiente con UUIDv7, identificación original/normalizada, datos fiscales, sede, idempotency key y estado.

Al reconectar, el servidor crea o reutiliza Party, Customer y sede, devuelve IDs canónicos y no modifica la factura ni su snapshot. Colisiones concurrentes se resuelven reconsultando la restricción única. Un cliente nuevo offline usa precio base.

## 9. Snapshot documental

Factura, pedido, compra, devolución, entrada y cartera conservan IDs, identificación, nombre/razón social, nombre comercial, sede, dirección, ubicación, contacto requerido, responsabilidades fiscales y condición comercial pertinente.

Cambiar Party, sede, rol o usuario no cambia documentos históricos.

## 10. Evolución de Auraly actual

No habrá maestros paralelos:

- CommerceCustomers se migra a Parties + Customers; puede conservarse CustomerId como ID del rol.
- CustomerBusinessPricing se convierte en CustomerPricingSettings.
- CommerceSellers se migra a Parties + Sellers, preservando SellerId cuando sea seguro.
- Employees se migra a Parties + Employees; Name deja de ser otra fuente de verdad.
- AppUsers recibe PartyId opcional; no se fusiona solo por nombre o correo.
- ExternalCommerceCustomers permanece como mapa de integración enlazado al Party/Customer canónico; no es otro maestro.

## 11. Migración desde Xion

1. Extraer Personas y tablas especializadas.
2. Normalizar por país/tipo.
3. detectar coincidencias seguras, posibles, conflictos e inválidas.
4. Crear una Party por coincidencia segura.
5. Crear todos sus roles.
6. Convertir PersonaSucursal en PartySites.
7. Convertir configuraciones por empresa/sucursal en configuraciones por Business.
8. Crear LegacyEntityMap.
9. Conciliar cantidades y referencias.

No se fusiona automáticamente si difieren tipo/país, los nombres contradicen la identidad, el documento es vacío/genérico o la relación de sede es inconsistente. Se genera informe de intervención. Las tablas especializadas prevalecen sobre flags contradictorios. Las contraseñas no se migran.

## 12. Restricciones y permisos

Restricciones mínimas:

- identidad única por tenant, país y tipo;
- Customer/Supplier/Employee/Seller únicos por Party y Business;
- código de sede único y una sede principal;
- lista/canal excluyentes;
- creación rápida idempotente y concurrente sin duplicados;
- ninguna referencia cruza negocios indebidamente.

Permisos mínimos:

- parties.read/create/update;
- parties.identifications/contacts/sites.manage;
- customers.read/create/update y customers.pricing.manage;
- suppliers.read/manage;
- employees/sellers/carriers/drivers.manage;
- security.users.manage;
- pos.customer.create y pos.customer.site.create.

La API siempre revalida permiso y alcance.

## 13. Pruebas obligatorias

- formatos equivalentes de NIT producen una Party;
- tipos documentales y tenants no se mezclan;
- alta concurrente produce una Party;
- conflictos no se fusionan silenciosamente;
- Party puede ser Customer y Supplier;
- agregar/desactivar un rol no altera otros;
- Supplier no obtiene UserAccount;
- varias sedes comparten identidad y agregar sede no crea Party;
- Party puede ser Customer de varios Businesses con condiciones independientes;
- lista/canal de otro Business se rechaza y sin ambos se usa precio base;
- creación offline sobrevive reinicio y dos cajas convergen en una Party;
- snapshot fiscal permanece inmutable;
- permisos se validan en API;
- migración concilia conteos/referencias y reporta conflictos;
- no quedan maestros paralelos;
- DACPAC compila, despliega y SQL Server real valida restricciones/concurrencia.

## 14. Orden de implementación

Esta decisión se implementa en rebanada propia:

1. Parties y normalización.
2. Evolución de clientes y precios.
3. Sedes y contactos.
4. Administración y creación rápida POS.
5. Sincronización offline.
6. Proveedores.
7. Empleados y vendedores.
8. Transportadores y conductores.
9. Asociación opcional de AppUsers.
10. Migración y conciliación Xion.

No se declara terminada una etapa con tablas o contratos aislados. Requiere consumidor, API autorizada, interfaz, SQL Server, pruebas y evidencia.

## 15. Decisión final

Se conserva lo mejor de Xion —una identidad común con múltiples capacidades— y se mejora con composición, roles especializados como única fuente de verdad, usuario separado, identidad normalizada, sedes hijas, configuración por negocio, snapshots inmutables y límites modulares.

CommerceCustomers, CommerceSellers, Employees y los datos personales duplicados en AppUsers evolucionarán hacia este modelo sin compatibilidad legacy permanente.
