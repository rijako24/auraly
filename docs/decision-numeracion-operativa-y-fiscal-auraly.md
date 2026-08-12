# Decisión: numeración operativa Auraly y numeración fiscal DIAN

**Fecha:** 28 de julio de 2026
**Estado:** aprobada para implementación
**Prevalencia:** reemplaza cualquier diseño que use un único prefijo o consecutivo para representar simultáneamente el documento Auraly y la numeración autorizada por la DIAN.

## 1. Hallazgo validado en Xion

Xion separa dos conceptos útiles:

- `SSalidaDeMercancia.NoDocumento`: número operativo usado para relacionar el documento y sus detalles.
- `SFactura.Prefijo`, `SFactura.Resolucion` y `Consecutivo`: datos fiscales de la factura.

También construye el número operativo a partir del tipo documental, el equipo y un consecutivo. Auraly conserva la separación conceptual, pero no copia sus abreviaturas, claves de texto, longitud, entidades ni implementación.

## 2. Tres identidades distintas

Todo documento confirmado diferencia:

1. `DocumentId`: UUIDv7 interno, global y no visible como consecutivo.
2. `DocumentNumber`: número operativo propio de Auraly.
3. Numeración fiscal: prefijo, consecutivo y autorización definidos por la DIAN cuando el documento sea fiscal.

El número operativo nunca se usa para calcular el CUFE. El CUFE y el QR usan exclusivamente el número fiscal autorizado.

## 3. Formato operativo

Para documentos emitidos por una caja:

```text
<prefijo Auraly><código de caja>-<consecutivo de 8 dígitos>
```

Ejemplo:

```text
VTA03-00000042
```

- `VTA`: tipo semántico de documento.
- `03`: código de caja único dentro del negocio.
- `00000042`: consecutivo independiente de esa caja y tipo documental.
- Ocho dígitos permiten hasta 99.999.999 documentos por serie de caja y tipo.

Solo existe un guion. La caja no incorpora el código de sede porque la relación caja-sede ya está modelada y el código de caja es único por `BusinessId`.

## 4. Alcance de la secuencia

### Documentos POS offline

Cada caja posee una serie exclusiva por tipo documental. En un negocio con dos sedes y diez cajas, los códigos pueden ser `01` a `10`; no se reinician por sede.

Esto permite que todas las cajas emitan offline sin:

- consultar `MAX + 1`;
- coordinarse entre ellas;
- reservar bloques de una secuencia compartida;
- incluir un identificador largo de sede en el número.

El servidor provisiona y valida la serie. Reinstalar o clonar una caja no crea una serie nueva ni permite reutilizar números.

### Documentos creados en línea

Los documentos que no nacen en una caja usan una serie administrada por el servidor. Su `SeriesCode` se define según el contexto emisor, sin fingir que existe una caja.

## 5. Catálogo canónico inicial

| Documento | Tipo canónico | Prefijo Auraly |
|---|---|---:|
| Factura de venta | `SalesInvoice` | `VTA` |
| Pedido de venta | `SalesOrder` | `PED` |
| Devolución de venta | `SalesReturn` | `DVT` |
| Entrada de mercancía | `GoodsReceipt` | `EMC` |
| Orden de compra | `PurchaseOrder` | `OCP` |
| Compra | `Purchase` | `CMP` |
| Devolución de compra | `PurchaseReturn` | `DCP` |
| Traslado entre bodegas | `WarehouseTransfer` | `TRB` |
| Entrada de inventario | `InventoryEntry` | `EIN` |
| Salida de inventario | `InventoryExit` | `SIN` |
| Ajuste de inventario | `InventoryAdjustment` | `AJI` |
| Avería | `Damage` | `AVE` |
| Conversión de producto | `ProductConversion` | `CNV` |
| Arqueo de caja | `CashCount` | `ARQ` |
| Cargue de aduana | `CustomsLoad` | `ADU` |
| Ingreso de caja | `CashReceipt` | `ING` |
| Egreso de caja | `CashDisbursement` | `EGR` |
| Recaudo de cuenta por cobrar | `ReceivablePayment` | `RCC` |
| Pago de cuenta por pagar | `PayablePayment` | `PGP` |

Los borradores y facturas temporales no consumen número. Apartados, cotizaciones, remisiones, domicilios, puntos, bonos, cheques posfechados, producción, lotes, seriales y otros tipos excluidos del MVP no reciben una serie por adelantado.

## 6. Persistencia y restricciones

`DocumentSeries` conserva como mínimo:

- `DocumentSeriesId`;
- `BusinessId`;
- contexto emisor;
- `DocumentType`;
- `Prefix`;
- `SeriesCode`;
- `Padding`;
- rango y estado.

`SalesDocuments` conserva por separado:

- componentes y texto del número Auraly;
- componentes y texto del número fiscal;
- autorización fiscal;
- `DocumentId`.

SQL Server impide duplicar:

- una serie por negocio, tipo, prefijo y código;
- un número Auraly por negocio, tipo, prefijo, serie y consecutivo;
- un número fiscal por negocio, tipo, autorización, prefijo y consecutivo.

El servidor reconstruye el número operativo desde sus componentes y rechaza una representación que no coincida.

## 7. Emisión POS

Al confirmar una factura, POS Edge realiza en una única transacción SQLite:

1. recupera el `DocumentId` estable del borrador;
2. consume el siguiente número de la serie Auraly de la caja;
3. consume el siguiente número de la serie fiscal DIAN;
4. congela el snapshot fiscal;
5. calcula CUFE y QR con el número DIAN;
6. persiste factura y outbox.

Un reintento del mismo `DocumentId` devuelve ambos números ya asignados y no consume nuevos consecutivos.

La tirilla muestra ambos:

- `DOCUMENTO AURALY: VTA03-00000042`;
- `NUMERO DIAN: <prefijo autorizado><consecutivo>`.

## 8. Reglas que no se negociarán

- Los prefijos Auraly no son configuraciones libres: cada tipo tiene un prefijo canónico.
- El código de caja es único dentro del negocio.
- No se reciclan números anulados.
- Una caja offline no inventa otra serie al agotarse.
- El servidor no reemplaza silenciosamente ninguno de los dos números.
- Una serie DIAN conserva exactamente el prefijo autorizado, aunque coincida visualmente con otro negocio.
