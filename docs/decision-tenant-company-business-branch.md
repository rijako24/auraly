# Decisión definitiva: Tenant es empresa y Business es sede

Fecha: 2026-07-30

Esta decisión prevalece sobre documentos anteriores que presenten `Business`
como empresa y un `Location` adicional como sede.

## Modelo canónico

```text
Tenant (empresa y frontera SaaS)
└── Business (sede o sucursal)
    ├── Warehouse (bodega)
    └── CashRegister (caja)
        └── WarehouseId (bodega asignada)
```

- Un `Tenant` agrupa una empresa dentro de Auraly y puede tener varios
  `Business`.
- Cada `Business` representa una sede operativa. En la interfaz se muestra
  como **Sede**.
- Una bodega pertenece directamente a un `Business`.
- Una caja pertenece directamente a un `Business` y referencia una bodega del
  mismo `Business`.
- La política de negativos continúa perteneciendo a la bodega y la caja la
  hereda mediante `WarehouseId`.

No existe `BusinessLocation`, `LocationId` ni un tercer nivel organizacional
equivalente dentro de Auraly Commerce.

## Aislamiento y claves

`TenantId` continúa en identidad, autenticación y en la raíz `Businesses`.
Las operaciones nunca confían solamente en un `BusinessId` enviado por el
cliente: el servidor comprueba que ese `Business` pertenece al `Tenant`
autenticado.

Las tablas cuyo dueño es una sede usan `BusinessId`. No duplican `TenantId`
cuando puede resolverse de forma inequívoca desde `Businesses`.

SQL Server protege la coherencia de caja y bodega con claves compuestas:

- `Warehouses (BusinessId, WarehouseId)`;
- `CashRegisters (BusinessId, WarehouseId, RegisterId)`;
- `PosDevices` referencia ese alcance completo.

Así una caja o dispositivo no puede quedar conectado a una bodega de otra
sede, incluso si una validación de aplicación falla.

## Configuración del POS

El instalador de Auraly POS es único, firmado y versionado. No se compila un
binario distinto por cliente.

Desde el panel de la empresa se genera un paquete de enrolamiento de un solo
uso ligado al `Tenant` y al servidor. No contiene secretos permanentes. En la
primera ejecución:

1. se valida el paquete y la identidad contra el servidor;
2. se listan únicamente los `Business` permitidos, mostrados como sedes;
3. se elige la caja;
4. la bodega, política de negativos, series y resolución se obtienen de la
   caja;
5. si se enrola como POS Edge, se provisiona el dispositivo y se inicia la
   sincronización local.

El modo online usa la misma selección de sede y caja, pero no descarga el
catálogo ni crea inventario local.

## Ubicaciones de clientes y proveedores

Esta decisión no elimina las direcciones o establecimientos de una `Party`.
Una `Party` puede seguir teniendo múltiples `PartyLocations` para facturación,
entrega y rutas. Esas ubicaciones describen a la persona o tercero; no forman
parte de la jerarquía organizacional de Auraly y no sustituyen `Business`.

## Persistencia histórica

Los documentos conservan `BusinessId`, `WarehouseId` y `RegisterId`, además de
sus snapshots comercial y fiscal. No necesitan `LocationId`: `BusinessId`
identifica la sede que emitió el documento.

La eliminación de `BusinessLocations` se realiza en el proyecto DACPAC dueño
del esquema. Para una base con datos anteriores, el despliegue debe validar
antes que las relaciones de caja, bodega y documento coincidan por
`BusinessId`. El despliegue no debe aceptar pérdida de datos silenciosa.

## Regla de arquitectura

Las pruebas automáticas deben impedir que `BusinessLocations`, `LocationId` o
`locationId` reaparezcan en código ejecutable, contratos, SQL o frontend de
Commerce.
