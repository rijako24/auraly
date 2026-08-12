# Modos de documento de venta

## Decisión

El punto de venta ofrece siempre dos tipos de documento por negocio, sin una tabla de habilitación adicional:

| Tipo canónico | Nombre visible | Prefijo operativo | Etapa fiscal |
| --- | --- | --- | --- |
| `SalesInvoice` | Factura electrónica | `VTA` | Sí |
| `SalesReceipt` | Comprobante de venta | `CVI` | No |

La selección pertenece a la venta actual y se bloquea después de agregar la primera línea para impedir que el tipo cambie a mitad del documento.

## Numeración

Las series operativas son independientes. En línea, el servidor asigna el siguiente consecutivo de la serie del negocio de forma transaccional. En POS Edge, el enrolamiento provisiona una serie `VTA` y otra `CVI`, ambas ligadas al dispositivo y con cursores independientes. Reintentar el mismo `DocumentId` no consume un consecutivo nuevo.

La factura electrónica usa, además, su serie fiscal DIAN. El comprobante no tiene número DIAN, autorización fiscal, CUFE, QR, snapshot fiscal, UBL ni estado DIAN.

## Motor documental

No existen dos motores comerciales. Ambos tipos ingresan al mismo motor documental durable, ordenado e idempotente. El tipo determina las etapas que se ejecutan:

1. Persistencia del documento y sus líneas.
2. Resúmenes de impuestos para información operativa.
3. Movimiento de inventario.
4. Registro de los medios de pago.
5. Actualización de informes operativos mediante el evento de salida.
6. Solamente para `SalesInvoice`: cartera y trabajo de contabilización.
7. Solamente para `SalesInvoice`: creación del trabajo fiscal que genera UBL, firma y transmisión.

`SalesReceipt` nunca se enruta al worker fiscal y tampoco crea cartera ni trabajo contable. Esto se valida en SQL, no únicamente en la interfaz.

## Impresión

Ambos documentos imprimen tirilla. La factura muestra número DIAN, CUFE y QR. El comprobante imprime únicamente el número operativo `CVI`, los productos, impuestos informativos, totales y pagos. La plantilla no genera elementos fiscales ocultos para retirarlos después: construye directamente el documento correcto según su tipo.

## Operación online y offline

La misma selección funciona con la API online y con POS Edge. En Edge, el comprobante se guarda en SQLite y en la outbox durable, puede reimprimirse y subirse después sin inventar datos fiscales. Al llegar al servidor entra al mismo motor documental y conserva su `DocumentId` y número `CVI`.

## Invariantes probadas

- Series `VTA` y `CVI` independientes.
- Emisión y reintento offline durables sin renumeración.
- El comprobante no contiene CUFE, QR, número DIAN ni snapshot UBL.
- El comprobante procesa una sola vez inventario, pago y evento operativo.
- El comprobante genera cero artefactos fiscales, cero cartera y cero trabajos contables.
- La factura electrónica conserva el flujo fiscal existente.
