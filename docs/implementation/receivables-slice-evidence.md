# Evidencia de la rebanada de cuentas por cobrar

Fecha de ejecución: 2026-08-03

## Resultado funcional

Se comprobó el flujo real:

```text
venta online a crédito
  -> documento durable y motor ordenado
  -> cuenta por cobrar y apertura del libro
  -> asiento contable de cartera
  -> consulta autenticada y paginada
  -> recaudo parcial idempotente
  -> movimiento de sesión de trabajo
  -> aplicación y actualización del saldo
  -> asiento contable de recaudo
  -> evento durable de salida
```

La prueba de integración usa SQL Server real, despliega el DACPAC y verifica además aislamiento por negocio, permisos, replay, un solo asiento y una carrera de dos abonos que intentan exceder el saldo. Solamente uno puede ser aceptado.

También se verificó que una venta totalmente financiada no crea un medio de pago ficticio y que una alteración fiscal sigue produciendo `FiscalIntegrityConflict` en lugar de quedar escondida por la validación de liquidación.

## Comandos y resultados

| Comprobación | Resultado |
| --- | --- |
| `dotnet build Auraly.Commerce.sln --configuration Release` | correcto, 0 errores, 0 advertencias |
| build de `Auraly.Database.sqlproj` | correcto, DACPAC generado, 0 errores, 0 advertencias |
| `Auraly.Foundation.Tests` | 152/152 |
| `Auraly.ServerSlice.IntegrationTests` | 98/98 con SQL Server real |
| `Auraly.Pos.Edge.Host.Tests` | 16/16 |
| `MimosBabySpa.Tests` | 701/701 |
| `npm run test:pos` | 25/25 |
| `npx tsc --noEmit` | correcto |
| `npm run build` | correcto; ruta `/dashboard/receivables` generada |

El primer intento de compilar simultáneamente la solución y el proyecto SQL produjo una colisión en `database/Auraly.Database/obj`. Ambos builds se repitieron en secuencia y aprobaron; por eso la secuencia documentada evita ejecutarlos en paralelo.

## Hallazgos corregidos por las pruebas

- La financiación completa se confundía con ausencia inválida de pagos.
- Los pagos concurrentes podían entrar en deadlock sin reintento acotado.
- Un chequeo prematuro del total podía ocultar un conflicto de integridad fiscal.
- Una lectura SQL de política de crédito devolvía un tipo incompatible.
- La asignación del consecutivo dependía del orden físico de columnas de una tabla con `rowversion`.
- El contrato TypeScript de detalle no coincidía exactamente con la respuesta de API.

## Pendiente explícito

- crédito offline y sincronización del perfil/cupo hacia POS Edge;
- selección y captura de crédito dentro de la interfaz POS;
- edición del perfil de crédito dentro de la pantalla administrativa del cliente;
- compensaciones, castigos, intereses y cuotas;
- antigüedad y estado de cuenta como reporte dedicado;
- aplicar notas crédito al saldo CxC con reglas explícitas;
- configuración automatizada de ESLint.

Ninguno de esos puntos se presenta como implementado en esta entrega.
