# Evidencia de implementación DIAN

Fecha: 2026-07-28.

## Resultado actual

- CUFE validado con vector oficial y truncamiento monetario.
- UBL determinístico y validado por XSD oficial.
- XAdES-EPES verificada criptográficamente; alterar el XML invalida la firma.
- Certificado vencido, de otro emisor o sin clave privada es rechazado.
- Transporte clasifica recepción, pendiente, aceptación, rechazo y timeout ambiguo.
- Cada venta o conflicto crea exactamente un proceso fiscal en la transacción de recepción.
- API fiscal protegida por permisos, aislada por `BusinessId` y paginada en SQL Server.
- DACPAC compilado y desplegado en SQL Server real.

## Ejecuciones aprobadas

- `Auraly.Foundation.Tests`: 88/88.
- `Auraly.ServerSlice.IntegrationTests`: 19/19 con SQL Server real y DACPAC.
- `Auraly.Pos.Edge.Host.Tests`: 3/3.
- Frontend: `npx tsc --noEmit` y `npm run build` correctos; ruta `/pos` generada.
- `Auraly.Commerce.sln` Release: 0 errores, 0 advertencias.
- El despliegue manual de `Auraly.Database.dacpac` terminó correctamente; la base temporal se eliminó.

## No aprobado todavía

- Conectividad real con habilitación DIAN: faltan certificado, software, PIN, `TestSetId` y configuración válida.
- Worker fiscal end-to-end y persistencia efectiva de XML/intentos: el esquema existe, el procesador aún no.
- Sincronización del resultado hacia POS Edge y reimpresión según estado.
- Nota crédito/débito y matriz ejecutable de contingencia.

No se declara la rebanada completa mientras estos puntos sigan pendientes.