# Órdenes y recepciones de compra

Fecha de actualización: 2026-09-02

## Lenguaje de producto

- **Orden de compra**: compromiso planificado con un proveedor. Antes se había denominado “orden de pedido”.
- **Recepción de compra**: registro de lo que físicamente llegó. Reemplaza en la interfaz “entrada/recepción de mercancía”.
- Los nombres técnicos `PurchaseOrder` y `GoodsReceipt` permanecen estables en contratos y persistencia para evitar una ruptura artificial.

## Propietarios y flujo

```text
Orden de compra (Purchasing, sin efecto físico)
  -> borrador recuperable
  -> confirmación inmutable OCP
  -> cumplimiento acumulado por línea

Recepción de compra (Purchasing)
  -> puede recuperar una OCP abierta o parcial
  -> el usuario registra la cantidad real
  -> confirmación inmutable EMC
  -> DocumentProcessingEngine / handler GoodsReceipt
  -> inventario, costo, cuentas por pagar, contabilidad y cumplimiento OCP

Venta/devolución procesada
  -> SalesReportingProcessingCoordinator
  -> hechos de reporting
  -> ProductRotationSnapshots por negocio, bodega y producto
```

Purchasing es el propietario de la orden y su cumplimiento. Inventario sólo cambia dentro del handler canónico de `GoodsReceipt`. Reporting es el único propietario del cálculo persistido de rotación; compras y catálogo sólo leen su snapshot.

La fecha física de recepción la registra el sistema al guardar o confirmar y no se edita en la interfaz. El usuario sí informa la fecha de emisión del documento de compra. Esa fecha gobierna la causación contable, las retenciones y el inicio de la cuenta por pagar. `Suppliers.DefaultPaymentDueDays` es la condición comercial canónica del proveedor y propone el vencimiento como fecha de emisión más ese plazo. El usuario puede ajustarlo para reflejar la condición pactada en el documento concreto; el servidor exige que no sea anterior a la emisión.

La retención se calcula exclusivamente en `WithholdingEngine` por medio de
`WithholdingService` al confirmar. El documento transporta ese resultado y el
procesador lo persiste como snapshot inmutable. El detalle de la recepción lee
ese snapshot para mostrar reglas, tarifas, deducciones y neto por pagar; no
recalcula valores históricos en Purchasing ni en la interfaz.

Las tablas de órdenes pertenecen al esquema `purchasing`. Las consultas y mutaciones nuevas se versionan en el proyecto de base de datos mediante procedimientos de los esquemas `purchasing` y `reporting`; los stores y handlers de C# sólo hacen invocaciones parametrizadas y no contienen SQL embebido.

## Ciclo de vida de la orden

`Draft -> Open -> PartiallyReceived -> Received`

Una orden abierta o parcial también puede pasar a `Closed` cuando un usuario autorizado cierra explícitamente el saldo con un motivo. No se permite cerrar si una recepción aceptada aún espera procesamiento. Una orden confirmada no se edita: la realidad se registra en recepciones separadas y auditables.

## Cantidades reales

Recuperar una orden propone el saldo pendiente, pero no bloquea la cantidad:

- orden 10, llegan 8: la recepción registra 8 y quedan 2;
- luego llegan 2: la orden queda recibida;
- orden 10, llegan 12: se conservan las 12 reales, pero el excedente exige motivo y el permiso `purchasing.goods-receipts.over-receive`;
- sin motivo o permiso, el servidor rechaza el excedente;
- el cumplimiento puede superar 100 %, mientras el pendiente nunca es negativo.

La recepción congela `PurchaseOrderId`, `PurchaseOrderLineId`, motivo y autorización del excedente. El handler bloquea orden y líneas, acumula lo recibido y deriva el estado en la misma transacción que aplica inventario. Esto evita dobles aplicaciones por reintentos.

Una recepción confirmada en estado `Accepted` cuenta como cantidad pendiente de aplicar mientras espera el motor. La recuperación descuenta ese pendiente y una confirmación concurrente lo valida bajo aislamiento serializable. Esto no reserva ni modifica inventario: evita que dos capturas consuman silenciosamente el mismo saldo documental de la orden.

## Rotación persistida

`reporting.ProductRotationSnapshots` guarda ventanas móviles de 30 y 90 días, ventas brutas, devoluciones, venta neta y demanda diaria de 90 días. La clave es `BusinessId + WarehouseId + ProductId`; por tanto, “Ver producto” muestra la rotación de cada bodega/sede del negocio y no mezcla existencias.

El writer canónico de reporting recalcula sólo los productos afectados al proyectar una venta o devolución. Una transacción histórica no puede hacer retroceder `WindowEndDate`. Las órdenes leen el snapshot para apoyar abastecimiento junto con stock actual y cantidades en camino; nunca recalculan ventas desde la pantalla ni desde Purchasing.

## Experiencia web

Las bandejas usan la grilla compartida, paginación de servidor y filtros por estado. Órdenes y recepciones usan el mismo selector paginado de productos del proveedor:

1. buscar o escanear por nombre, códigos, referencia o código del proveedor;
2. seleccionar agrega la línea y enfoca su cantidad;
3. flechas arriba/abajo recorren cantidades;
4. Enter en cantidad devuelve el foco al buscador.

El detalle de producto presenta rotación sólo como información de lectura. La edición del producto no puede escribirla.

## Contratos principales

- `GET /api/commerce/v1/purchase-orders`
- `GET /api/commerce/v1/purchase-orders/{id}`
- `GET /api/commerce/v1/purchase-orders/{id}/receipt-source`
- `PUT /api/commerce/v1/purchase-orders/{id}/draft`
- `POST /api/commerce/v1/purchase-orders/confirm`
- `POST /api/commerce/v1/purchase-orders/{id}/close`
- `GET /api/commerce/v1/goods-receipts/products`
- `PUT /api/commerce/v1/goods-receipts/drafts/{id}`
- `POST /api/commerce/v1/goods-receipts/confirm`
- `GET /api/commerce/v1/products/{id}/rotation`

Todos derivan tenant y negocio de la identidad autenticada y validan que bodega, proveedor, producto y orden pertenezcan al mismo alcance. En una compra a crédito también validan que el vencimiento coincida con el plazo del proveedor, para que un cliente desactualizado no pueda alterar la condición calculada.

## Rollback y compatibilidad

Las columnas nuevas de recepción son opcionales; las recepciones sin orden conservan el comportamiento anterior. El despliegue puede revertir código sin perder documentos: las tablas y columnas agregadas son compatibles. No se eliminan ni reescriben recepciones confirmadas; cualquier reversión económica o física se realiza mediante los documentos compensatorios canónicos.

## Diseño propuesto: múltiples documentos de costo y costo puesto en bodega

**Estado:** slice de recepción, costos adicionales e importación implementado; pagos en moneda
extranjera y costos conocidos después de confirmar permanecen fuera de este alcance.
**Fecha:** 2026-09-03.

### Problema que resuelve

Una recepción física puede originar obligaciones independientes:

- factura de la mercancía, normalmente del proveedor principal;
- factura de flete, seguro, descargue u otro servicio, emitida por otro proveedor;
- declaración de importación y tributos aduaneros;
- factura de agencia de aduanas u otros servicios de nacionalización;
- vencimientos, monedas, retenciones, soportes e IVA diferentes por cada documento.

Esos documentos no se deben sumar como si fueran nuevas líneas gravadas de la factura de mercancía. Cada uno conserva su realidad fiscal y financiera, mientras sólo la porción que sea directamente atribuible a poner el inventario en su ubicación y condición actuales incrementa su costo. IAS 2 incluye precio de compra, aranceles, impuestos no recuperables, transporte y manejo directamente atribuibles; excluye del costo los impuestos recuperables. Fuente: [IFRS Foundation, IAS 2](https://www.ifrs.org/issued-standards/list-of-standards/ias-2-inventories/).

### Decisión de dominio

Se mantienen separados tres conceptos:

```text
Recepción de compra       = qué producto y cantidad llegaron físicamente
Documento de costo       = quién cobra, qué soporte existe, moneda, impuesto y vencimiento
Asignación de costo       = cuánto del valor capitalizable impacta a cada línea recibida
```

`GoodsReceipt` continúa siendo el único documento físico y el propietario de cantidades, bodega, cumplimiento de la orden y entrada al inventario. No se crea otra recepción, motor, cola ni writer.

Para conservar lo que ya funciona, `GoodsReceipts` y `GoodsReceiptLines` siguen siendo la fuente de verdad de la **factura principal de mercancía**. Una recepción tendrá cero o más **documentos de costo adicionales** para flete, seguro, nacionalización u otros cobros. La API compone ambos orígenes en un único read model para que visualmente todos aparezcan como documentos de la recepción, pero no copia la factura principal en tablas nuevas.

Este límite evita una migración innecesaria y, sobre todo, evita dos propietarios para precio, descuento e IVA de la mercancía. El proveedor del encabezado sigue siendo el proveedor comercial y de la orden; cada documento adicional conserva su propio proveedor y vencimiento.

La recepción completa se confirma una sola vez. El payload inmutable lleva documentos, líneas y asignaciones; el handler `GoodsReceipt` aplica el costo total reconocido mediante `SqlInventoryLedgerWriter` y conserva idempotencia por recepción.

La factura principal conserva el `AccountingPostingJob` vigente. Cada documento adicional genera su propia fuente y otro job en el mismo motor contable. Así conserva proveedor, fecha de causación, período, moneda, vencimiento y eventual reversión propios. El processor produce un asiento balanceado y una cuenta por pagar por documento, usando las asignaciones congeladas para debitar inventario, gasto e IVA. No se crean otra tabla de jobs, cola, worker ni posting service. La suma de débitos a inventario de la factura principal y los documentos adicionales debe conciliar con el valor reconocido por el único movimiento operativo de la recepción.

`Payables` se vincula al `CostDocumentId` inmutable y conserva también `GoodsReceiptId` para navegación. Su unicidad deja de ser una sola obligación por recepción y pasa a una obligación por documento de costo. La reversión posterior referencia ambos identificadores.

Los documentos adicionales no generan `DocumentProcessingJobs` ficticios. El handler de la recepción crea sus `AccountingSourceDocuments` y `AccountingPostingJobs` directamente en las tablas canónicas del motor contable, dentro de la misma transacción que deja procesada la recepción.

### Modelo mínimo

No se crea un agregado genérico de importaciones en la primera entrega. `GoodsReceiptId` es el expediente de costeo.

#### `purchasing.GoodsReceiptCostDocuments`

Un registro sólo por factura, declaración o soporte adicional; la factura principal no se duplica aquí:

- `CostDocumentId`, `GoodsReceiptId`, `BusinessId`;
- `SupplierId` o contraparte pagable del mismo tenant/business;
- tipo de soporte del catálogo canónico;
- número normalizado, fecha de emisión y vencimiento;
- condición contado/crédito y evidencia de liquidación cuando sea contado;
- moneda original;
- en el slice de importación: tasa de reconocimiento contable, fecha, fuente y equivalente en COP;
- subtotal, impuestos, retenciones, total y neto por pagar en moneda original y COP;
- estado y snapshot inmutable al confirmar.

Unicidad mínima: `BusinessId + SupplierId + número normalizado + tipo de soporte`. Un número vacío sólo es admisible para el soporte que legalmente no lo requiera.

#### `purchasing.GoodsReceiptCostLines`

Una línea económica del documento:

- tipo de costo desde catálogo: flete, seguro, arancel, servicio aduanero, manejo u otro costo directo;
- descripción y conjunto de líneas físicas elegibles para recibir la asignación;
- importe antes de impuesto, base gravable, código/tarifa e importe del impuesto;
- tratamiento tributario ya vigente: `DeductibleInputVat`, `CapitalizedCost` o `NotApplicable`;
- tratamiento del importe base: capitalizable o gasto;
- método de distribución y, cuando aplique, criterio manual;
- importes originales y COP congelados.

La base gravable y el impuesto se guardan por separado. Esto es obligatorio para importaciones: el IVA aduanero puede tener como base el valor en aduana más el arancel y no se obtiene aplicando la tarifa al cobro del agente.

#### `purchasing.GoodsReceiptCostAllocations`

Un registro por línea de costo y línea recibida:

- importe asignado en COP;
- factor usado y snapshot de su unidad: valor, cantidad, kg, m³ o porcentaje manual;
- residuo de redondeo aplicado;
- versión de fórmula.

La suma asignada debe ser exactamente igual al importe capitalizable de la línea de costo. La asignación es calculada por backend, previsualizada en UI y congelada al confirmar.

El borrador recuperable forma parte del mismo slice: guardar la recepción persiste documentos, líneas y criterios manuales adicionales bajo el `GoodsReceiptDraftId` y su `RowVersion`. Reabrir un borrador no puede perder facturas ni recalcular silenciosamente una distribución manual. La confirmación elimina el borrador sólo después de crear el snapshot definitivo de manera atómica.

#### Snapshot logístico en la línea recibida

Para no depender de maestros que luego cambien, cada `GoodsReceiptLine` congela opcionalmente:

- peso bruto total en kg;
- volumen total en m³.

El formulario puede proponerlos desde la presentación del proveedor, pero el usuario los valida contra packing list o documento de transporte. No se inventa peso o volumen faltante.

### Catálogos, no listas quemadas

Los siguientes selectores se sirven desde `reference.Options` y API:

- `purchase-cost-kind`;
- `purchase-cost-allocation-method`;
- `exchange-rate-source`, únicamente cuando se implemente moneda extranjera.

El tipo de soporte amplía el catálogo existente `purchase-evidence-type`; no se crea un segundo catálogo con la misma responsabilidad. Los nuevos códigos necesarios para importación son, como mínimo, factura comercial extranjera y declaración de importación.

Los tipos técnicos inmutables pueden conservar constantes para tipado, pero la tabla sigue siendo la fuente de opciones, label, activación y orden.

### Métodos de distribución

Los ERP revisados ofrecen combinaciones de igual, cantidad, valor, peso y volumen. Business Central también conserva el cargo como valor separado ligado al movimiento original; Odoo muestra valor original, costo adicional y nuevo valor antes de validar. Fuentes: [Microsoft Business Central](https://learn.microsoft.com/en-au/dynamics365/business-central/payables-how-assign-item-charges), [Odoo Landed Costs](https://www.odoo.com/documentation/17.0/applications/inventory_and_mrp/inventory/product_management/inventory_valuation/landed_costs.html) y [Oracle Landed Cost Charges](https://docs.oracle.com/en/cloud/saas/supply-chain-and-manufacturing/25c/fapma/landed-cost-charges.html).

| Método | Uso recomendado | Regla |
| --- | --- | --- |
| Valor | seguro, arancel agregado o costos ligados al valor | valor COP de mercancía de la línea / valor COP total elegible |
| Cantidad | cargo realmente cotizado por unidad homogénea | unidades base / unidades base totales |
| Peso | flete facturado por kg y manejo físico | kg brutos de la línea / kg brutos totales |
| Volumen | flete donde manda el espacio ocupado | m³ de la línea / m³ totales |
| Igual por línea | cargo administrativo uniforme | 1 / número de líneas elegibles |
| Manual | declaración o proveedor entrega reparto exacto | porcentajes o importes que sumen 100 % / total |

No existe un método universal de “flete”. Si el transportador cobra por peso se usa peso; si cobra por espacio, volumen; si entrega peso cobrable o reparto por guía, se registra como asignación manual soportada. No se quema un factor de peso volumétrico, porque cambia por modo y transportador.

Defaults propuestos, siempre visibles y editables antes de confirmar:

- flete: peso; volumen si el documento indica cobro volumétrico;
- seguro: valor;
- arancel: asignación directa desde cada ítem/subpartida de la declaración; valor sólo como ayuda cuando el soporte no trae el reparto;
- honorarios de agencia: valor o igual por línea según la forma real del cobro;
- manejo/descargue: peso o manual;
- mercancía: asignación directa a su línea recibida.

El algoritmo usa decimal y método de mayor residuo: calcula proporciones a alta precisión, redondea a moneda y entrega los centavos residuales en orden determinista a las mayores fracciones. Así nunca se pierde ni se duplica un peso.

### Flujo doméstico

1. Se seleccionan proveedor principal, bodega y productos como hoy.
2. Auraly presenta como primera tarjeta la factura de mercancía que ya captura hoy, sin copiar sus datos.
3. “Agregar documento” permite flete u otro costo con proveedor, soporte, fecha y vencimiento propios.
4. Cada línea de servicio separa base e IVA. La base capitalizable se distribuye; el IVA descontable no se distribuye al costo.
5. La vista previa presenta costo original, cargos asignados y nuevo costo unitario por producto.
6. Confirmar crea una recepción, la obligación principal, las obligaciones adicionales y un único efecto físico idempotente.

Ejemplo: mercancía COP 1.000.000 + IVA 190.000 y flete COP 100.000 + IVA 19.000. Si ambos IVA son descontables, el inventario recibe COP 1.100.000; IVA descontable recibe COP 209.000; las cuentas por pagar suman COP 1.309.000 repartidas entre los dos proveedores. Nunca se vuelve a calcular 19 % sobre COP 1.100.000.

### Flujo importado

Una “factura de nacionalización” no se trata como una sola bolsa. La pantalla propone separar sus componentes porque tienen naturalezas distintas:

1. **Factura comercial extranjera:** mercancía en USD u otra moneda. No se agrega IVA colombiano a esas líneas por defecto.
2. **Declaración de importación:** valor en aduana, arancel por ítem/subpartida e IVA de importación. El arancel incrementa costo; el IVA pagado en importación se registra como descontable cuando se cumplen las condiciones tributarias, de lo contrario se capitaliza.
3. **Flete y seguro:** cada factura conserva su proveedor e IVA. Su base directamente atribuible incrementa inventario; el IVA recuperable permanece impuesto descontable.
4. **Agencia de aduanas/nacionalización:** honorarios y su IVA se separan de anticipos o reembolsos de tributos para no duplicar ni el gasto ni el impuesto.

La DIAN establece que la base del IVA en la importación parte del valor en aduana y adiciona los derechos de aduana; también indica que el flete y gastos conexos hasta el lugar de importación forman parte del valor en aduana cuando no están incluidos. Fuentes: [artículo 459 explicado por DIAN](https://normograma.dian.gov.co/dian/compilacion/docs/oficio_dian_901412_2021.htm) y [preguntas frecuentes de valoración aduanera DIAN](https://www.dian.gov.co/aduanas/aspectecmercancias/valoracion_de_mercancias/Preguntas_frecuentes/Paginas/default.aspx).

La nacionalización no se presume “por kilo”. Los tributos se toman del detalle real de la declaración. Un agente puede cobrar sus honorarios por kilo; sólo ese servicio se distribuye por peso si el soporte lo demuestra. El IVA pagado en importación puede ser descontable bajo las condiciones de los artículos 485 y 488; la clasificación concreta debe quedar validada por el contador del negocio. Fuente: [DIAN, Concepto 8857 de 2025](https://normograma.dian.gov.co/dian/compilacion/docs/oficio_dian_8857_2025.htm).

Ejemplo simplificado con tasa contable de COP 4.000/USD:

| Componente | Producto A | Producto B | Tratamiento |
| --- | ---: | ---: | --- |
| Mercancía USD 600 / USD 400 | COP 2.400.000 | COP 1.600.000 | inventario |
| Flete COP 800.000 por volumen 25 % / 75 % | 200.000 | 600.000 | inventario |
| Arancel según ítems de declaración | 288.000 | 192.000 | inventario |
| Agencia COP 200.000 distribuida por valor 60 % / 40 % | 120.000 | 80.000 | inventario |
| **Costo puesto en bodega** | **3.008.000** | **2.472.000** | **COP 5.480.000** |

En este ejemplo, el valor en aduana es COP 4.800.000 y el arancel es COP 480.000. Si el IVA de importación soportado por la declaración es COP 1.003.200 —19 % sobre COP 5.280.000— y el IVA de la agencia es COP 38.000, ambos se llevan a IVA descontable cuando proceda: COP 1.041.200. No se suman al costo de COP 5.480.000 ni se calcula nuevamente 19 % sobre ese costo. El flete aparece una vez como costo económico y también integra la base fiscal aduanera; eso no lo duplica. El ejemplo presupone que el flete no estaba ya incluido en el precio/Incoterm; si estaba incluido, se concilia y no se capitaliza dos veces.

### Moneda extranjera

La recepción puede confirmar una factura extranjera congelando su moneda original, tasa,
fecha y fuente. El inventario, las retenciones, las propuestas de precio y los asientos se
reconocen en COP; la cuenta por pagar conserva simultáneamente el importe original y su
equivalente funcional. La liquidación posterior de esa obligación y su diferencia en cambio
pertenecen al flujo de pagos, no recalculan ni reescriben esta recepción.

La implementación garantiza:

- moneda funcional COP por tenant, como hoy;
- monto original y equivalente COP en cada documento y línea;
- tasa positiva, fecha y fuente obligatorias;
- tasa bloqueada al confirmar y hash del snapshot;
- inventario, propuestas de precio, retenciones y asientos siempre basados en COP;
- cuenta por pagar conservando saldo original en USD y valor contable COP;
- al pagar, tasa de liquidación y diferencia en cambio en resultado, sin reescribir el costo histórico del inventario.

IAS 21 exige reconocer inicialmente una transacción extranjera en moneda funcional con la tasa spot de la fecha de la transacción. La fecha aduanera y su tasa oficial pueden diferir de la fecha/tasa contable de la factura; se almacenan por separado y nunca se reutiliza una como si fuera la otra. Fuente: [IFRS Foundation, IAS 21](https://www.ifrs.org/content/dam/ifrs/publications/pdf-standards/english/2021/issued/part-a/ias-21-the-effects-of-changes-in-foreign-exchange-rates.pdf?bypass=on).

### Regla de IVA: evitar el doble impuesto

El IVA no se calcula sobre el costo puesto en bodega. Se reconoce desde cada soporte legal:

```text
Costo de inventario por línea
  = mercancía neta en COP
  + bases capitalizables asignadas
  + impuestos no recuperables asignados

IVA descontable
  = suma de impuestos marcados DeductibleInputVat en sus documentos

Cuenta por pagar por documento
  = total de ese documento - sus retenciones
```

Por tanto:

- el IVA de la factura local de mercancía no se vuelve a aplicar después del flete;
- el IVA del flete se reconoce una sola vez desde la factura del transportador;
- el IVA de importación se reconoce desde la declaración, no desde la factura extranjera;
- un impuesto no recuperable usa `CapitalizedCost` y sí se distribuye al inventario;
- una línea sin soporte fiscal válido nunca crea IVA descontable.

Controles obligatorios para impedir doble IVA:

- cada impuesto tiene exactamente un `CostDocumentId`, una base gravable y un importe;
- una asignación de costo nunca genera impuesto: sólo reparte importes ya clasificados como capitalizables;
- la suma de IVA de la recepción se obtiene agregando documentos, no recalculando sobre el costo final;
- el backend rechaza el mismo soporte tributario referenciado en dos documentos;
- anticipos y reembolsos del agente de aduanas deben referenciar el tributo original y no crear otro IVA;
- la UI no propone 19 % para flete, agencia o importación por inferencia: muestra exactamente el impuesto del soporte o exige capturarlo;
- el tratamiento se muestra en lenguaje humano: “IVA descontable — no aumenta el costo” o “IVA no recuperable — aumenta el costo”.

La retención también se calcula por documento, proveedor y fecha. El preview único actual de `GoodsReceipt` no se reparte ni se reutiliza entre proveedores.

### Costos observados, precios y devoluciones

El “último costo del proveedor” conserva exclusivamente la mercancía comprada al proveedor principal, con su tratamiento tributario propio. No incluye flete, arancel ni honorarios de terceros. Mezclarlos dañaría la comparación de futuras listas del proveedor.

El costo utilizado para valorar inventario y preparar precios sí usa el costo puesto en bodega completo. Por tanto se conservan dos cifras visibles y nombradas:

```text
Costo de compra al proveedor != Costo puesto en bodega
```

Reporting atribuye la compra de mercancía al proveedor principal y presenta los cargos adicionales por su proveedor/tipo; no infla las compras del proveedor de mercancía con facturas de terceros.

Una devolución de compra revierte por defecto sólo mercancía, IVA y cuenta por pagar del proveedor principal. No reduce automáticamente la factura ya causada del transportador, la agencia o la DIAN. Los cargos capitalizados no recuperables que salgan con la mercancía se reconocen según la política contable de la devolución; una nota crédito real de un proveedor adicional se registra contra su propio documento. El flujo vigente que asume una sola cuenta por pagar debe modificarse antes de habilitar múltiples documentos.

### Integración contable nativa y configurable

Todo efecto monetario de la recepción entra al motor contable. Purchasing captura, valida y congela la evidencia; no escoge cuentas PUC ni escribe asientos. `AccountingProcessingCoordinator` y `SqlAccountingPostingProcessor` son los únicos propietarios de:

- inventario contable y gastos no capitalizables;
- IVA descontable o mayor valor del inventario;
- cuentas por pagar, anticipos y cuentas de compensación;
- retenciones por pagar;
- aranceles y tributos aduaneros;
- pagos y aplicaciones;
- diferencias en cambio;
- notas crédito, devoluciones y reversos.

La pantalla de recepción no pide una cuenta contable. El usuario elige el tipo económico real —flete, seguro, arancel, agencia, manejo u otro costo directo— y el tratamiento permitido. El motor resuelve la cuenta efectiva mediante el sistema vigente de categorías y `AccountCategoryMappings`, con alcance tenant/business y fechas de vigencia.

Se agregan únicamente las categorías contables que representan comportamientos realmente distintos y no existen hoy:

- gasto de flete de compra no capitalizable;
- gasto de seguro de compra no capitalizable;
- gasto de agencia/manejo no capitalizable;
- gasto de arancel no capitalizable;
- tributos aduaneros por pagar o cuenta de compensación;
- anticipos entregados a proveedores/agencia, si esa forma de liquidación se habilita;
- ganancia y pérdida por diferencia en cambio.

Los importes capitalizables no requieren una cuenta de flete o nacionalización separada en el débito: usan la categoría `Inventory` ya existente y conservan tipo de costo y documento como dimensiones de trazabilidad. IVA usa `InputVat`; la obligación usa `AccountsPayable`; las retenciones reutilizan sus categorías actuales. No se crean categorías duplicadas para esos efectos.

El perfil contable colombiano aprovisiona cuentas y mappings por defecto para todas las categorías nuevas. Un contador autorizado puede reemplazarlos con vigencia, sin cambiar código ni modificar recepciones históricas. Una recepción no puede seleccionar una cuenta ad hoc para evadir esa configuración.

Asientos esperados, simplificados:

```text
Factura de mercancía
  Débito  Inventario                         base neta capitalizable
  Débito  IVA descontable                    IVA recuperable
  Crédito Cuenta por pagar proveedor         neto por pagar
  Crédito Retenciones por pagar              retenciones aplicadas

Factura de flete o agencia capitalizable
  Débito  Inventario                         base distribuida
  Débito  IVA descontable                    IVA recuperable del documento
  Crédito Cuenta por pagar proveedor         neto por pagar
  Crédito Retenciones por pagar              retenciones aplicadas

Declaración de importación
  Débito  Inventario                         arancel y tributos capitalizables
  Débito  IVA descontable                    IVA de importación recuperable
  Crédito Tributos aduaneros por pagar,
          banco, anticipo o compensación      según liquidación soportada

Pago de obligación USD
  Débito  Cuenta por pagar                    valor contable vigente
  Débito/Crédito Diferencia en cambio         variación desde reconocimiento
  Crédito Banco                               COP realmente entregados
```

Cada documento adicional lleva su propia fecha contable y se procesa de forma idempotente. Si falta período o mapping, queda en `AccountingPendingConfiguration`, visible desde la recepción y bloqueando el cierre del período como ocurre hoy. El documento no se marca falsamente como contabilizado, no se crea un fallback a gasto general y un retry posterior usa el snapshot original sin volver a mover inventario.

La capacidad de agregar facturas/costos exige que el tenant tenga el motor contable activado y que todas las categorías necesarias sean resolubles en la fecha de cada documento. La validación previa se realiza mediante un contrato de readiness del módulo Accounting; Purchasing no consulta tablas contables ni duplica reglas de mappings. Si falta configuración, la UI presenta antes de confirmar la lista exacta y el enlace a Contabilidad. Las recepciones simples existentes conservan su compatibilidad durante el cutover, pero ningún documento adicional puede confirmarse por una ruta no contable.

Para compatibilidad con el comportamiento durable existente, una falla transitoria ocurrida después del commit puede quedar pendiente y reintentarse; nunca desaparece ni duplica el efecto operacional.

### Experiencia visual

La pantalla conserva el recorrido actual para la compra normal. Si no se agrega otra factura, el usuario no ve pasos ni campos nuevos obligatorios.

Debajo de los productos aparece una sección de revelado progresivo “Facturas y otros costos”. La factura principal siempre está visible; “Agregar factura o costo” abre un panel lateral corto. La distribución se calcula automáticamente con un default visible y sólo se abre en detalle cuando el usuario desea cambiarla o existe un dato faltante. No se usan pestañas que oculten errores entre una vista y otra.

```text
Recepción EMC-000123                     Costeo completo ✓

Facturas y otros costos
┌ Mercancía · ACME · INV-88 · USD 2.450 · vence 30 sep   ✓ conciliado
├ Flete · Transportes Sur · FE-991 · COP 476.000          ✓ distribuido por peso
└ Importación · Declaración 032... · COP 3.280.000         ! revisar 1 diferencia
                                           [+ Agregar factura o costo]

Resumen por producto
Producto      Mercancía COP   Flete   Arancel/otros   Costo final/u
A             2.000.000       80.000  120.000          22.000
B             3.000.000      320.000  180.000          35.000
```

Cada tarjeta muestra proveedor, soporte, moneda, total, vencimiento, cuánto aumenta inventario y estado. Al expandirla se editan sus líneas. “Ver distribución” abre el detalle de importe original, factor, asignación y nuevo costo; una advertencia explica exactamente qué dato falta. Los totales separan siempre:

- valor de mercancía;
- costos adicionales capitalizados;
- impuestos descontables;
- impuestos capitalizados;
- retenciones;
- total y neto por pagar por proveedor;
- costo final del inventario.

Después de confirmar, cada tarjeta muestra además `Contabilizado`, `Pendiente de configuración` o `Error reintentable`, con enlace al asiento o al mapping faltante para usuarios autorizados. No se expone un editor de débitos y créditos dentro de la recepción.

El botón final dice “Confirmar recepción y N cuentas por pagar”. Se bloquea si un documento no concilia, una asignación no suma, falta tasa de cambio, peso/volumen requerido, soporte, vencimiento o tratamiento tributario.

En el grid de productos se agrega una sola columna condicional “Costo final/u”; permanece oculta cuando no existen costos adicionales. Peso y volumen sólo se solicitan al escoger esos métodos, evitando cargar la compra normal con datos logísticos innecesarios.

### Alcance implementado y límites deliberados

El slice confirmado incluye compras domésticas e importadas, varios documentos,
proveedores y vencimientos, monedas y tasas congeladas, métodos de asignación por valor,
cantidad, peso, volumen, igual o manual, IVA y retenciones por documento, múltiples cuentas
por pagar y contabilización nativa mediante el motor existente.

Todos los documentos deben estar completos antes de confirmar. Se conservan estos límites:

- una recepción conserva un solo proveedor de mercancía; si llegan productos comprados a otro proveedor se crea otra recepción;
- un documento adicional pertenece a una sola recepción;
- no se pueden anexar facturas después de confirmar;
- peso o volumen se captura sólo cuando el método elegido lo requiere;
- no se crea un módulo general de facturas de proveedor ni se reutiliza `Expenses`;
- anticipos, reembolsos de agencia, pagos de obligaciones en moneda extranjera y diferencia en cambio se resuelven en sus flujos financieros propios, no dentro de la recepción.

#### Slice 3 — costos posteriores

Sólo si la operación real exige vender antes de conocer flete/nacionalización: documento compensatorio `GoodsReceiptCostAdjustment` dentro del motor documental existente. Debe ajustar por separado inventario aún disponible y costo de venta ya reconocido, respetar período abierto y no reescribir la recepción. No se implementa como una actualización SQL del promedio.

### Propiedad e integración canónica

| Regla/efecto | Propietario |
| --- | --- |
| documentos, conciliación y asignaciones | `Purchasing` / agregado `GoodsReceipt` |
| cálculo de IVA y retenciones | motores tributarios; Purchasing conserva snapshot |
| cantidad y valor de inventario | handler `GoodsReceipt` → `SqlInventoryLedgerWriter` |
| todo asiento, cuenta por pagar, anticipo, pago, diferencia en cambio y reverso | `AccountingProcessingCoordinator` / `SqlAccountingPostingProcessor` |
| propuestas de precio | costo reconocido final que ya consume el handler |
| opciones visibles | catálogo `reference.Options` + API |

No se reutiliza `Expenses` para las facturas ligadas a la recepción: produciría otro propietario de la obligación y dificultaría conciliar el costo con inventario. Tampoco se crean productos de servicio ficticios para flete o arancel.

### Criterios de aceptación de la recepción

- una recepción con tres proveedores crea tres obligaciones, un movimiento físico y un asiento balanceado sin duplicados al reintentar;
- el costo asignado por cada cargo suma exactamente su base capitalizable;
- IVA descontable nunca incrementa inventario; IVA capitalizado sí;
- flete por peso/volumen falla explícitamente si falta el snapshot correspondiente;
- la declaración distribuye el arancel conforme a sus ítems y no “por kilo” por defecto;
- USD sin tasa/fuente/fecha no puede confirmarse y USD confirmado valora inventario en COP;
- la cuenta por pagar conserva moneda original y valor funcional sin cambiar el costo histórico;
- mercancía, flete, seguro, agencia, arancel, IVA y retenciones producen fuentes contables nativas y asientos balanceados;
- cada categoría nueva tiene mapping efectivo configurable y el perfil colombiano trae un default explícito;
- una configuración contable faltante produce `AccountingPendingConfiguration`, bloquea el cierre y puede reintentarse sin reaplicar inventario;
- duplicidad se valida por negocio, proveedor, tipo y número normalizado;
- tenant, autorización, concurrencia, idempotencia, períodos y rollback tienen regresiones de integración;
- el detalle histórico reproduce exactamente documentos, impuestos, factores, asignaciones y costo final sin consultar maestros mutables.
- guardar, cerrar y recuperar un borrador conserva todas las facturas adicionales y su distribución;
- ningún documento adicional crea un `DocumentProcessingJob` o writer paralelo para simular una segunda recepción.
