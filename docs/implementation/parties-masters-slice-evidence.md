# Evidencia de Terceros y Maestros

Fecha: 2026-08-02
Rama: `feature/auraly-commerce-parties-masters`

## Resultado conectado

La rebanada conecta SQL Server, API, administración web y POS Edge:

1. un cliente y un proveedor con la misma identificación comparten una Party;
2. la repetición de una operación de proveedor devuelve el mismo Supplier;
3. la edición usa rowversion y rechaza una copia obsoleta;
4. el cambio de estado afecta los roles del Business sin borrar la identidad;
5. las consultas son paginadas, filtrables y limitadas al Business autenticado;
6. país, departamento/estado y ciudad alimentan formularios dependientes;
7. el barrio permanece como texto libre;
8. alta, edición y desactivación de Customer escriben una notificación durable del
   stream `Customers` en la misma transacción;
9. POS Edge crea el cliente por la API autenticada como dispositivo y descarga la
   proyección actualizada a SQLite antes de devolverlo a facturación;
10. sin servidor, el POS conserva búsqueda local pero no permite crear una
    identidad potencialmente duplicada.

No se agregó polling ni una segunda tabla de clientes o proveedores.

## Pruebas ejecutadas

```text
dotnet build Auraly.Commerce.sln --configuration Release --disable-build-servers
aprobado: 0 errores, 0 advertencias; incluye Auraly.Database.dacpac
```

```text
dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj --configuration Release --no-build
149 aprobadas, 0 fallidas
```

```text
dotnet test tests/Auraly.Pos.Edge.Host.Tests/Auraly.Pos.Edge.Host.Tests.csproj --configuration Release --no-build
16 aprobadas, 0 fallidas
```

```text
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release --no-build
91 aprobadas, 0 fallidas; SQL Server real y DACPAC desplegado
```

```text
dotnet build database/Auraly.Database/Auraly.Database.sqlproj --configuration Release --disable-build-servers
aprobado: 0 errores, 0 advertencias
```

```text
npx tsc --noEmit
aprobado

npm run test:pos
25 aprobadas, 0 fallidas
```

La prueba SQL específica comprobó además:

- normalización de `901.777.333-1` y búsqueda mediante `9017773331`;
- un único Party, Customer, Supplier y código de sede principal;
- conservación del dígito de verificación;
- permisos denegados;
- Business incorrecto denegado;
- conflicto de concurrencia con rowversion obsoleta;
- al menos tres mensajes durables `Customers` por alta, edición y desactivación.

La prueba de POS Edge usa una base SQLite física y comprueba:

- autenticación de dispositivo en geografía y alta;
- descarga de país, departamento y ciudad;
- alta remota del cliente;
- descarga de la proyección de precios/clientes;
- persistencia local antes de devolver el cliente al consumidor.

## Validación frontend

TypeScript, las 25 pruebas POS y el build de producción fueron ejecutados. Las
rutas nuevas forman parte del artefacto de Next.js:

- `/dashboard/parties`;
- `/dashboard/settings/masters`.

El controlador de navegador del entorno no pudo iniciar por una restricción ACL
del sandbox. Por eso no se registra una inspección visual interactiva como
aprobada en esta ejecución; no se sustituyó por una afirmación basada únicamente
en compilación.

## Compatibilidad de proveedores

`Suppliers.PartyId` es canónico desde esta rebanada. La migración de despliegue
crea Parties incompletas para proveedores existentes. Las columnas anteriores de
nombre e identificación se conservan temporalmente porque compras, cuentas por
pagar y reportes aún pueden consumirlas. Se retirarán en una migración posterior,
cuando pruebas de consumidores demuestren que ya no se leen.

## Pendiente deliberado

- reconciliar `ExternalCommerceCustomers` con Party/Customer;
- administrar múltiples sedes y contactos secundarios desde la ficha web;
- agregar Employee, Seller, Carrier y Driver cuando sus módulos los consuman;
- editar y desactivar geografía con reglas de dependencia;
- conectar el árbol de clasificación al editor de Producto;
- retirar columnas transitorias de Supplier después del corte de consumidores;
- repetir la inspección visual interactiva en un entorno donde el navegador pueda
  acceder al worktree.

Nada de ese alcance se representa mediante servicios vacíos, tablas sin consumidor
o `TODO` permanentes.
