# Ejecutar la fundación de Auraly Commerce

## Requisitos actuales

- .NET SDK 8.0.405 compatible con `global.json`.

## Compilar

Desde la raíz del repositorio:

```powershell
dotnet build Auraly.Commerce.sln --configuration Release
```

## Probar

```powershell
dotnet test tests\Auraly.Foundation.Tests\Auraly.Foundation.Tests.csproj `
  --configuration Release
```

Las pruebas incluyen dominio, UUIDv7, outbox, idempotencia, CUFE, concurrencia
de numeración y reglas de referencias entre proyectos.

## Solución

`Auraly.Commerce.sln` contiene únicamente la fundación canónica nueva. La
solución histórica permanece temporalmente disponible para ejecutar la línea
base mientras sus capacidades útiles se reconstruyen por rebanadas. No se deben
crear dependencias desde los módulos Auraly hacia proyectos históricos.

