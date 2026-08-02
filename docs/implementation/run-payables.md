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
requiere `payables.read` y `payables.payments.create`.

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
