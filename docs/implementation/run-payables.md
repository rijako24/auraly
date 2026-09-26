# Ejecutar cuentas por pagar localmente

Fecha: 2 de agosto de 2026

## Requisitos

- .NET SDK compatible con la solución.
- Node y npm compatibles con `admin/package.json`.
- SQL Server accesible por la configuración de pruebas.
- `sqlpackage` disponible para desplegar el DACPAC.
- RabbitMQ local para la prueba explícita del transporte.

No se usa EF InMemory, `EnsureCreated` ni una segunda base del servidor.

## Compilar

```powershell
dotnet build Auraly.Commerce.sln --configuration Release
dotnet build database/Auraly.Database/Auraly.Database.sqlproj --configuration Release
```

## Probar dominio y aplicación

```powershell
dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj `
  --configuration Release
```

## Probar SQL Server y la API

La fixture crea una base aislada, despliega `Auraly.Database.dacpac`, ejecuta la
API de pruebas y elimina exclusivamente esa base al finalizar.

```powershell
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj `
  --configuration Release
```

Para ejecutar solo cartera:

```powershell
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj `
  --configuration Release `
  --filter FullyQualifiedName~PayablesVerticalSliceTests
```

## Probar RabbitMQ real

La prueba no considera aprobada una omisión cuando se exige explícitamente el
broker.

```powershell
$env:AURALY_TEST_RABBITMQ='amqp://<usuario>:<clave>@127.0.0.1:5672/'
$env:AURALY_REQUIRE_RABBITMQ_TEST='1'
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj `
  --configuration Release --no-build --no-restore `
  --filter FullyQualifiedName~PayablesRabbitMqIntegrationTests
```

La prueba crea colas con nombres efímeros y las elimina al terminar. No imprime
la cadena de conexión.

## Frontend

```powershell
cd admin
npx tsc --noEmit
npm run build
npm run dev
```

Después de autenticarse con permisos, abrir:

```text
http://localhost:3000/dashboard/payables
```

El proxy del admin debe apuntar a `Auraly.Api`. Para registrar pagos el usuario
requiere `payables.read` y `payables.payments.create`. En punto de venta, la selección y el pago usan `pos.payables.payments.create`; al iniciar la operación se abre o retoma automáticamente la sesión operativa del usuario, aunque no exista una venta previa. En la caja preparada, Edge confirma que esa sesión ya está registrada en el servidor antes de consultar o pagar cartera. Este permiso no abre la vista administrativa de cuentas por pagar.

## Recorrido de verificación manual

1. Confirmar una entrada de mercancía a crédito.
2. Esperar su procesamiento por el motor.
3. Abrir Cuentas por pagar y localizar el documento `EMC`.
4. Abrir el detalle y registrar un abono.
5. Comprobar que la API devuelve aceptación `PGP`.
6. Comprobar que el saldo pasa a `PartiallyPaid` o `Paid`.
7. Consultar el movimiento de cartera y el asiento contable.
8. Repetir la misma solicitud con igual clave y comprobar que no duplica.

No hay polling. Una vista abierta hace su consulta normal e invalida una vez al
aceptar su propia mutación.
# Totales por moneda

La cartera de proveedores admite obligaciones en distintas monedas. `GET /commerce/v1/payables` y
`GET /commerce/v1/payables/suppliers` devuelven `currencyTotals` por moneda del conjunto filtrado;
los importes escalares `totalOutstanding` y `totalOverdue` conservan la semántica COP para
compatibilidad. En la pestaña de proveedores cada fila corresponde a proveedor y moneda,
incluida su paginación. Nunca se suman saldos USD y COP como si tuvieran la misma unidad.
Los saldos a favor del proveedor permanecen en COP y se asignan a una sola fila del tercero:
la fila COP si existe en el filtro o, de lo contrario, la primera moneda visible. Así se muestran
también para proveedores con obligaciones únicamente en moneda extranjera sin duplicarlos en el total.
