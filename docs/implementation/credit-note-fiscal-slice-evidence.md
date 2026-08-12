# Evidencia: devolución y nota crédito fiscal conectadas

Fecha: 1 de agosto de 2026  
Rama: `feature/auraly-commerce-accounting-engine`

## Resultado

Una devolución de venta procesada por el motor crea en la misma transacción:

- sus efectos de inventario y devolución de dinero o crédito;
- el snapshot inmutable de la nota crédito;
- la raíz fiscal genérica `FiscalDocuments`;
- el trabajo durable `PendingGeneration`;
- el evento comercial de outbox.

El worker fiscal consume ese trabajo, calcula una sola vez el CUDE, construye
UBL `CreditNote`, valida el XML, firma, persiste ambos XML y sus hashes, y deja
el documento listo para transmisión. El worker de envío conserva intentos,
respuesta y resultado DIAN simulado. Repetir generación o envío no duplica
artefactos, intentos ni eventos.

## Inmutabilidad

La nota crédito no consulta nombres, direcciones, impuestos ni códigos actuales
del catálogo. `SalesReturnFiscalSnapshots` conserva:

- payload definitivo de la devolución;
- adquirente histórico de la factura;
- identificación y configuración del emisor;
- metadatos tributarios de las líneas originales;
- número, CUFE y fecha de la factura referenciada;
- moneda, ambiente y URL de QR;
- hash SHA-256 del JSON exacto.

Si la factura original no congeló los metadatos UBL obligatorios, el motor no
inventa datos y no crea una nota crédito fiscal incompleta.

## Raíz fiscal común

`FiscalDocuments` representa facturas y notas crédito sin duplicar los procesos,
artefactos e intentos de transmisión. Distingue:

- documento fuente: `SalesInvoice` o `SalesReturn`;
- documento fiscal: `Invoice` o `CreditNote`;
- código único: `CUFE` o `CUDE`;
- número Auraly y número fiscal;
- estado fiscal y fecha de emisión.

La API administrativa consulta ambos tipos y filtra por `uniqueCode`. El parámetro
histórico `cufe` continúa como alias compatible, pero la respuesta usa
`UniqueCodeType` y `UniqueCode` para no llamar CUFE a un CUDE.

## Prueba vertical principal

`FiscalGenerationSqlTests.Processed_return_generates_signs_and_submits_credit_note_once`
comprueba con SQL Server real y DACPAC:

1. factura electrónica original con snapshot UBL;
2. devolución parcial confirmada y procesada;
3. creación transaccional del trabajo fiscal;
4. XML raíz `CreditNote`;
5. CUDE de 96 caracteres persistido;
6. referencia exacta al número y CUFE originales;
7. dos artefactos de generación, sin duplicados;
8. ZIP e intento de transmisión;
9. aceptación determinística;
10. estado `DianAccepted` en raíz y devolución;
11. un solo evento de outbox;
12. consulta autenticada y paginada por CUDE.

El test captura y restaura el saldo compartido del producto de fixture para no
contaminar otras pruebas por orden de ejecución.

## Evidencia ejecutada

- solución Release: 0 errores, 0 advertencias;
- `Auraly.Foundation.Tests`: 136 aprobadas;
- `Auraly.Pos.Edge.Host.Tests`: 15 aprobadas;
- `Auraly.ServerSlice.IntegrationTests`: 78 aprobadas con SQL Server real;
- RabbitMQ real en modo obligatorio: 1 aprobada;
- DACPAC incluido en el build de la solución: 0 errores, 0 advertencias.

No se declara conectividad real con habilitación DIAN: el resultado fiscal de esta
prueba usa el transporte determinístico existente. Certificado, `TestSetId` y
credenciales válidas siguen siendo requisito para marcar habilitación real.
