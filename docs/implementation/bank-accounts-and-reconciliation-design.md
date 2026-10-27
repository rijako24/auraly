# Cuentas bancarias y conciliación mínima

> **Ampliación de diseño 2026-10-20, en implementación local, sin autorizar despliegue:**
> la sección final sustituye el vínculo de un banco a un solo auxiliar por varios
> auxiliares configurados. Las autorizaciones y evidencias siguientes pertenecen
> al alcance histórico de septiembre; no autorizan publicar esta ampliación.

**Estado:** mínimo funcional implementado y validado localmente; publicación
autorizada mediante el release inmutable del repositorio.
Diseño mínimo unificado y cerrado el 2026-09-08 para cobros, bancos y conciliación.
**Fecha de cierre del diseño:** 2026-09-08

**Instrucción histórica de septiembre:** implementación y publicación del mínimo
de ese corte. **Instrucción vigente para la ampliación:** implementar, probar,
auditar y levantar local; no desplegar en DEV ni PROD.

## Decisión

La cuenta bancaria es un maestro contable del tenant. La relación inicial de un
solo auxiliar queda sustituida por la ampliación final: los auxiliares se vinculan
desde el PUC a una cuenta bancaria real. La cuenta principal sirve solo
como valor inicial en la captura: el usuario puede cambiarla en cada operación.

Una venta por transferencia conserva cuenta, referencia, nota y valor. Una
devolución por transferencia conserva la misma evidencia y acredita la cuenta
seleccionada. Si `AccountingTenantSettings.Status` no es `Ready`, cuenta no se
muestra ni se exige; referencia sí es obligatoria y nota es opcional. La
activación posterior no reasigna operaciones históricas ni inventa una cuenta.

La conciliación pertenece al módulo Accounting. No modifica ventas,
devoluciones o extractos y no escribe directamente en `AccountingEntries`. Los
ajustes confirmados entran como documentos inmutables al procesador contable
canónico y reutilizan sus periodos, validación de balance, idempotencia,
numeración, observabilidad y writer.

## Auditoría de adaptación al contable actual — 2026-09-08

La línea base tenía `accounting.BankAccounts`, su API y la sección
**Contabilidad → Cuentas bancarias**, pero no tenía importaciones, cruces, cierre
ni pantalla de conciliación. Este corte agrega esas capacidades al mismo módulo
Accounting y reutiliza el comprobante manual y el libro existentes. Las
correcciones siguientes describen el mínimo implementado, sin añadir un segundo
motor contable.

| Punto | Evidencia actual e implicación | Mínimo necesario |
|---|---|---|
| Banco y PUC | `SqlAccountingStore.SaveBankAccountAsync` valida tenant, auxiliar activo contabilizable de activo y sin tercero obligatorio. El número bancario es diferente del código PUC. | Mantener esa relación configurable; no imponer un código numérico universal ni inferirlo del nombre del banco. |
| Identidad del banco | Dos cuentas pueden apuntar al mismo auxiliar y el vínculo puede editarse. `AccountingEntryLines` conserva el auxiliar, pero no `BankAccountId`. Una consulta por auxiliar puede mezclar bancos. | Para la primera conciliación, exigir un auxiliar exclusivo por cuenta y bloquear el cambio de auxiliar una vez usada. Detectar los datos existentes incompatibles y pedir su saneamiento explícito; no reasignar historia. Si se necesita compartir auxiliares, requiere una decisión de dimensión bancaria adicional antes de conciliar. |
| Documento pendiente | `ResolveAccountsAsync` resuelve el auxiliar desde el maestro vigente. Cambiarlo antes de contabilizar puede cambiar el destino de una operación aceptada. | Proteger el vínculo desde el primer uso aceptado, incluidos pendientes. No basta proteger únicamente asientos ya contabilizados. |
| Sedes | El banco pertenece al tenant; el auxiliar actual en `GetAccountMovementsAsync` filtra una sola sede. | Conciliar el banco completo, con todas las sedes que aportan partidas al corte. Exigir los permisos de conciliación correspondientes en cada una mediante la autorización existente; si falta acceso, bloquear el proceso completo, sin mostrar un saldo parcial como total. El ajuste usa la sede seleccionada y autorizada; si afecta varias, se separan sus partidas por documento/sede. |
| Ajustes | `ConfirmManualAccountingVoucherRequest.Lines` y el procesador admiten múltiples partidas; la pantalla actual captura dos. | Reutilizar el comprobante manual y completar su captura para los ajustes bancarios que realmente se necesiten. Comisión más IVA requiere al menos tres partidas; rendimientos con retención también. No crear otro writer ni otra clase de asiento solo para bancos. |
| Versiones | El maestro tiene `RowVersion`, pero su API permite actualizar sin token. | Exigir versión al editar y conservar quién hizo el cambio. Los cruces y el cierre también necesitan control de concurrencia. |
| Evidencia | Referencia y nota del pago se conservan; no equivalen a un extracto importado ni a un cruce. | Conservar archivo original, hash, líneas y asignaciones, además del documento de ajuste. El hash evita repetir el mismo archivo; no detecta por sí solo archivos distintos con movimientos solapados. |
| Navegación | La sección de bancos configura cuentas y la sección de conciliación importa, cruza, cierra y reabre extractos. | Mantener ambos accesos dentro de Contabilidad y no duplicar el maestro. |

### Alcance mínimo cerrado

1. Cuenta bancaria y rango, inicialmente COP. Mostrar su número bancario,
   auxiliar PUC y alcance de sedes antes de importar.
2. Un formato CSV normalizado con previsualización y validación de saldos.
   XLSX y perfiles por banco pueden esperar; no son necesarios para demostrar
   el circuito contable. No reinterpretar silenciosamente signos o separadores.
3. Extracto y movimientos contabilizados, con pendientes de períodos anteriores.
   Marcar cruces manuales por importes exactos; un grupo puede vincular varias
   líneas completas de un lado con varias del otro. Repartos parciales y
   sugerencias por proximidad pueden quedar fuera del primer corte.
4. Ajuste soportado mediante el comprobante manual existente. Un ajuste
   aceptado o pendiente no se cuenta como conciliado hasta quedar `Posted`.
   Un reintento reutiliza el mismo ID y no crea otro comprobante.
5. Cierre reproducible con saldos, usuario, fecha y versión. El primer mínimo
   deja el periodo abierto mientras existan partidas en tránsito: no permite
   clasificarlas para forzar un cierre. Reapertura explícita con motivo; no
   borrar cruces cerrados.

Las tablas definidas abajo son persistencia del proceso de conciliación,
no una segunda contabilidad. Antes de crearlas, definir claves compuestas por
tenant/cuenta, FK de cada asignación a `EntryId + LineNumber`, índices de
pendientes y unicidad de importación. No crear una cola o job propietario nuevo.

### Criterio de cierre del primer corte

Una diferencia entre el saldo bancario y el libro puede corresponder a cheques o
depósitos en tránsito. El mínimo implementado no clasifica esas partidas: deja la
conciliación abierta. Solo permite cerrar cuando la ecuación del extracto cuadra,
todas las líneas bancarias y contables del auxiliar están cruzadas por completo,
no hay trabajos contables pendientes y el saldo final del banco coincide con el
saldo del libro. Un cargo que falta en libros se registra primero mediante el
comprobante manual canónico; una nota libre nunca convierte una diferencia en
gasto, ingreso o tránsito.

Normalizar entradas y salidas según la perspectiva del titular: un ingreso de
1.000 en el extracto corresponde a débito de 1.000 en el auxiliar de activo.
Las columnas «débito/crédito» del banco no se copian mecánicamente al libro.
El cierre debe comprobar la ecuación del propio extracto, asignaciones sin
exceso, ausencia de doble cruce y el corte de asientos. Un asiento retroactivo
obliga a revisar la conciliación afectada; no altera silenciosamente un cierre.

### Contraste con Siigo y DIAN

- [Siigo, asistente de conciliación](https://siigopyme.portaldeclientes.siigo.com/basedeconocimiento/tesoreria-asistente-conciliacion-bancaria/)
  compara las partidas del extracto con los registros de la cuenta contable
  parametrizada para el banco. Esto respalda reutilizar el auxiliar; no prueba
  que Auraly ya tenga ese flujo ni elimina las restricciones de identidad/sede.
- [DIAN, artículo 115 en la Ley 2277 de 2022](https://normograma.dian.gov.co/dian/compilacion/docs/ley_2277_2022.htm)
  trata la deducibilidad del GMF. Registrar el débito bancario completo y
  clasificar su tratamiento fiscal por separado conserva la conciliación;
  deducible no significa contabilizar solo una fracción de la salida.
- [DIAN, información del GMF](https://www.dian.gov.co/impuestos/personas/Paginas/gravamen_movimientos_financieros.aspx)
  identifica el gravamen sobre la disposición de recursos. El sistema debe
  usar el cobro real soportado y no aplicar automáticamente 4 por mil a toda
  salida. La procedencia del IVA descontable o de una retención requiere la
  clasificación y soporte correspondientes; conciliar no decide esos impuestos.

Estos contrastes validan criterios concretos, no certifican cumplimiento
tributario integral ni equivalencia de funcionalidades con Siigo.

### Puerta de aceptación antes de declarar conciliación funcional

- Cobro por transferencia y reintegro llegan al auxiliar de la cuenta elegida,
  con aislamiento de tenant y sin duplicados al reintentar.
- Dos cuentas o sedes no mezclan movimientos; los vínculos bancarios usados no
  cambian el destino de fuentes ya aceptadas.
- Un archivo repetido se rechaza; un archivo solapado no duplica sus cruces.
- Un movimiento agrupado se concilia sin exceder ningún importe; dos usuarios
  no pueden asignar el mismo saldo simultáneamente.
- Comisión con IVA y rendimiento con retención quedan balanceados usando el
  comprobante actual; un pendiente contable bloquea su cruce definitivo.
- Una partida en tránsito o cualquier diferencia mantiene abierta la
  conciliación; el primer corte no permite clasificarla para forzar el cierre.
- Lectura no permite importar: separar permisos de consulta y modificación.
  Cierre/reapertura conserva motivo, actor y corte reproducible.

## Una sola configuración de cobros y bancos

La configuración se organiza en Contabilidad con las categorías y maestros
existentes. No crear un segundo catálogo de cuentas de tesorería ni volver a
pedir el PUC en cada venta. `AccountingAccounts` es el PUC; los mappings de
`Cash`, `DebitCardClearing` y `CreditCardClearing` resuelven los medios;
`AccountingAccounts.BankAccountId` vincula los auxiliares de cada banco. El campo
inverso anterior se vacía al completar la migración y no se edita desde runtime.
El catálogo fiscal de medios de pago conserva su función distinta del auxiliar
PUC: un código DIAN no es el número de una cuenta bancaria ni un auxiliar contable.

| Cobro o traslado | Registro mínimo | Cuenta receptora y momento | Estado actual |
|---|---|---|---|
| Efectivo recibido | Débito a la categoría `Cash`; contrapartida de venta o cartera según el documento | Caja física de la sede, según mapping existente; no elegir un banco en la venta | Contabilización existente |
| Transferencia recibida | Débito al auxiliar del banco seleccionado | Elegir del maestro bancario existente cuando contabilidad está activa; su principal solo precarga | Existente en ventas y recaudos conectados |
| Tarjeta débito | Débito a `DebitCardClearing` por el cobro bruto | Cuenta por cobrar al operador hasta que exista abono real | Contabilización inicial existente |
| Tarjeta crédito del cliente | Débito a `CreditCardClearing` por el cobro bruto | Cuenta por cobrar al operador; no es deuda por una tarjeta corporativa | Contabilización inicial existente |
| Abono del operador | Débito al banco por el neto, gastos e impuestos/retenciones soportados; crédito a la cuenta de tarjetas por el bruto liquidado | Banco real seleccionado en el comprobante; la partida queda disponible para el cruce con el extracto | Captura multiparte implementada sobre el comprobante manual canónico |
| Consignación de efectivo | Débito al banco y crédito a caja | Cuando se consigna; no genera otra venta ni otro ingreso | El comprobante manual registra ambas partidas y la línea bancaria queda disponible para conciliación |
| Devolución | Crédito al medio que efectivamente reintegra, con la operación de devolución existente | Banco elegido cuando el reintegro es transferencia; conservar referencia y nota | Transferencias de devolución conectadas y con prueba de integración |

El cierre de caja actual (`WorkSessionClosureReconciliation`) verifica valores
contados, diferencias y reclasificaciones entre medios. **No liquida al operador
de tarjetas ni concilia un extracto bancario**: su payload no tiene cuenta
bancaria de abono, comisión o retención. No ampliarlo para que decida también
ese proceso. El cierre puede detectar una diferencia de captura; no prueba
que el operador ya haya abonado el dinero.

### Captura y repercusiones financieras

- En la pantalla actual mostrar juntos los mappings de efectivo, tarjeta débito
  y tarjeta crédito, y el acceso al maestro bancario. Mostrar código y nombre
  del auxiliar seleccionado. Reutilizar las escrituras existentes y sus permisos.
- Para el mínimo no añadir datáfonos, redes, porcentajes de comisión, cuenta
  receptora obligatoria por tarjeta ni otra configuración por caja. Un negocio
  con varios bancos elige el receptor real al registrar el abono. Si requiere
  control separado por operador, se define después con evidencia de necesidad.
- El cobro con tarjeta aumenta fondos por liquidar; no debe aumentar el saldo
  bancario disponible antes del abono. El extracto puede contener un abono por
  muchas ventas y combinar débito y crédito: permitir varias partidas en el
  comprobante manual, usando ambos auxiliares de tarjetas cuando corresponda.
- Registrar banco por el neto sin cancelar el bruto de tarjetas dejaría un saldo
  pendiente artificial. Cancelar el bruto contra banco por el bruto inflaría
  caja bancaria cuando hubo descuentos. Volver a acreditar ingresos duplicaría
  las ventas. Las tres situaciones deben tener regresiones explícitas.
- Ejemplo ilustrativo sin impuestos: cobros con tarjeta 100.000, abono 97.000 y
  comisión soportada 3.000. El abono registra débito banco 97.000, débito gasto
  3.000 y crédito tarjetas por liquidar 100.000. Ingresos por venta no cambian.
  Si hay IVA o retenciones, agregar sus partidas soportadas y comprobar que
  banco + gastos + impuestos/retenciones = bruto cancelado. No inferir tasas.
- Si una liquidación incluye reversos o contracargos, usar sus documentos y
  evidencia reales. Un faltante de abono no se convierte automáticamente en
  comisión, gasto o devolución. Dejar el importe pendiente hasta clasificarlo.
- Una consignación mueve un activo entre caja y banco: no cambia el resultado.
  Conservar las partidas en tránsito cuando el registro y el banco tengan
  fechas distintas. Su clasificación contable la decide el contador mediante
  el mismo comprobante y las cuentas que ya existen.

### Ajuste bancario por el comprobante existente

Extender la captura actual de `ConfirmManualAccountingVoucherRequest.Lines`
para añadir o quitar partidas, seleccionar el banco y resolver su auxiliar,
seleccionar terceros/centros cuando el auxiliar o la operación lo requieran,
y mostrar bruto, descuentos y neto. Permitir escoger el comprobante ya registrado
para vincularlo; no obligar a crear otro desde el extracto.

La referencia de evidencia y el ID del comprobante se conservan en el proceso
de conciliación. Confirmar se hace por `AccountingService`, fuente inmutable,
`AccountingPostingJobs` y `SqlAccountingPostingProcessor`. Los asientos solo
aparecen al contabilizarse; `PendingConfiguration` debe mostrar la causa y
bloquear el cruce definitivo del ajuste. La conciliación no toca cuentas por
cobrar de clientes, ventas o inventario al liquidar al operador.

## Persistencia y controles mínimos de la conciliación

El modelo durable de la primera entrega queda limitado a encabezado/importación
de conciliación, líneas de extracto y asignaciones a líneas contables. El
encabezado conserva cuenta, tenant, rango, moneda, saldos, archivo/hash,
estado, corte, versión y auditoría; cada asignación enlaza una línea bancaria
con `EntryId + LineNumber`, importe, actor y fecha. La evidencia y el ID del
comprobante de ajuste se enlazan desde ese mismo flujo. No crear otra tabla
de documentos contables ni `BankStatementAdjustments` como writer independiente.
Si un encabezado debe aceptar varios archivos, separar la importación solo
cuando se implemente ese requisito; el primer corte acepta un extracto por rango.

- Comprobar tenant/cuenta en cada escritura y en cada FK compuesta que sea
  necesaria. Un auxiliar no autoriza por sí mismo acceso a todas las sedes.
- Una asignación no puede exceder el valor disponible de ninguna de sus líneas
  ni utilizar una partida ya conciliada. Dos confirmaciones concurrentes no
  pueden consumir el mismo saldo. Una reversión conserva auditoría.
- El rango mensual es un valor inicial, no una obligación fiscal universal.
  Bloquear cierres solapados y conservar pendientes para el siguiente corte.
- Al cerrar, bloquear el encabezado, recalcular importes y verificar que el
  corte de asientos no cambió. No cuadrar diferencias con un asiento automático.
- Permisos: `accounting.bank-reconciliation.read` consulta;
  `accounting.bank-reconciliation.manage` importa y gestiona cruces;
  `accounting.bank-reconciliation.close` cierra/reabre con motivo.
  Crear comprobantes exige además `accounting.manual.create`.
- No incluir conexiones bancarias, OCR, IA, timers, multimoneda, porcentajes
  automáticos o perfiles de importación por banco en esta entrega mínima.

## Centros de costo y compras dentro del mismo diseño

El centro responde **a qué área se atribuye** un movimiento; el auxiliar PUC
responde **qué activo, pasivo, ingreso o gasto representa**; la cuenta bancaria
identifica **dónde está el dinero**. No se sustituye una dimensión por otra.

- Rige el diseño de [centros de costo, sección 8](../decision-contabilidad-minima-colombia-y-cumplimiento.md#8-centros-de-costos):
  código/nombre libres, superior opcional, un predeterminado sustituible,
  reglas actuales por operación y bodega opcional, versiones y protección de
  historia. No crear automáticamente un centro por banco, tarjeta o bodega.
- La resolución se conserva al primer intento contable. Las devoluciones
  heredan la clasificación original; los comprobantes admiten centros por
  línea. Reclasificar centros mueve la distribución y conserva el saldo PUC.
- El extracto se concilia contra el total de la cuenta bancaria: filtrar por
  centro sirve para consultar, pero no reduce el importe que se debe conciliar.
  Una comisión o liquidación puede usar los centros decididos por el contador
  dentro del comprobante existente, sin reparto automático por ventas.
- La recepción conserva separadas la factura principal y sus documentos de
  flete, aduana y otros costos ya admitidos. Mostrar principal, asociados,
  impuestos incluidos, retenciones, bruto y neto total de documentos; indicar
  moneda y utilizar importes funcionales confirmados al consolidar COP.
- El costo puesto de los productos se muestra aparte. Un costo distribuido no
  se suma otra vez a la obligación: ya está dentro de su documento asociado.
  Productos no inventariables van a gasto según su reconocimiento, aunque
  aparezcan en el costo puesto total de productos. IVA descontable no es costo
  de inventario. La clasificación fiscal conserva su propietario actual.
- En la recepción revisada: principal 4.760 + asociados 3.190 = documentos
  7.950; el costo puesto mostrado suma 6.000. Son magnitudes distintas. El
  importe identificado como IVA de importación pero capturado como gasto debe
  revisarse con su soporte; el resumen no lo reclasifica ni inventa impuesto.
- El neto de documentos al confirmar no es el saldo por pagar hoy. Pagos,
  anticipos y devoluciones se consultan en cartera y no se restan silenciosamente
  del total histórico de la recepción. Un pago al proveedor reduce CxP y banco
  o caja; no vuelve a reconocer la compra ni el costo.
- Si falta un importe histórico funcional, mostrarlo como no disponible.
  Nunca convertir con la tasa actual ni sumar monedas distintas para cuadrar.

El detalle de compras permanece en
[el diseño de recepciones](goods-receipts-design.md). Esta integración no añade
pasos obligatorios a ventas, POS o recepción ni amplía tipos de costo.

## Estados, transición y conservación de historia

La conciliación tiene tres estados funcionales: **En preparación**, **Cerrada**
y **Reabierta**. Importar y confirmar cruces requiere permiso de gestión;
cerrar requiere controles completos; reabrir exige motivo y conserva el cierre
anterior. No borrar extractos aceptados, cruces ni versiones de cierre.
Un archivo equivocado se sustituye únicamente con una nueva revisión trazable,
deshaciendo sus cruces en una conciliación abierta; no se pisa su contenido.

El corte incluye únicamente líneas contabilizadas hasta la fecha y versión
registradas. Identificar movimientos repetidos por archivo/hash y comprobar
solapamientos antes de confirmar otra importación. Un solapamiento sin resolver
bloquea la importación, no se deduplica por fecha/valor a ciegas porque dos pagos
reales pueden tener los mismos datos. Los pendientes arrastrados conservan su
identidad original y no se crean como movimientos nuevos.

Antes de habilitar el flujo, comprobar cuentas bancarias que comparten auxiliar,
vínculos cambiados e historia pendiente. Resolver incompatibilidades con una
decisión contable y documentos soportados, sin modificar asientos existentes.
El maestro, mappings y formularios se validan con versión para evitar perder
cambios de otro usuario. La aceptación de un documento y la protección del
vínculo bancario deben ser atómicas frente a una edición concurrente.

La implementación es un slice del módulo Accounting con schema, API, permisos,
UI y pruebas alineados. Un rollback conserva fuentes, extractos,
asignaciones y asientos; no reactiva un writer antiguo ni recalcula historia.

## Condición de listo

Las pruebas automatizadas demuestran los criterios de aceptación anteriores y el circuito
**cobro con tarjeta → abono neto y descuentos soportados → asiento único → cruce
contra extracto → cierre explicado**. También efectivo → consignación y
transferencia directa, sin duplicar ingresos, sin cruzar tenants y sin alterar
ventas ni fuentes ya aceptadas. La regresión multisedes incluye partidas de
períodos anteriores, bloqueo de documentos bancarios pendientes, denegación de
saldo parcial por falta de permisos e identificación de asientos retroactivos.

La organización por forma de pago y cuenta contable coincide con
[Siigo Nube, crear formas de pago](https://siigonube.portaldeclientes.siigo.com/crear-formas-de-pago/).
Este contraste orienta la usabilidad; la fuente de verdad de Auraly sigue siendo
su módulo contable y las reglas fiscales aplicables a cada soporte.

## Ampliación 2026-10-20: banco con auxiliares por sede y medio

Este diseño sustituye para operaciones futuras la cardinalidad banco → un
auxiliar. Una cuenta bancaria real conserva un único `BankAccountId` y puede
vincular varios auxiliares por sede/medio. Cada auxiliar pertenece como máximo a
una cuenta bancaria. No duplicar el maestro del mismo banco/número para representar
tres sedes. La cuenta principal sigue siendo un default de captura, no un selector
automático de auxiliar cuando existan varias combinaciones.

El vínculo canónico nuevo reside en `AccountingAccounts.BankAccountId`, junto a
las dimensiones de sede/medio del auxiliar. La numeración es libre y no exige código contable en el banco ni en la sede;
las propiedades son independientes del código, según la sección 13 de [comprobantes](accounting-open-vouchers-design.md). No crear
además una tabla de vínculos con otra relación editable. El FK actual inverso
`BankAccounts.AccountingAccountId` se conserva solo durante migración compatible:

1. Agregar el vínculo nuevo, poblarlo desde relaciones inequívocas existentes y
   detectar bancos que comparten auxiliar. No asignarles historia arbitrariamente.
2. Migrar resolución de pagos, confirmaciones, POS/configuración y conciliación
   al vínculo nuevo; los consumidores antiguos que exigen un único auxiliar deben
   actualizarse antes de habilitar bancos con más de uno.
3. Congelar banco y auxiliar en cada fuente aceptada y partida contabilizada.
   Validar que la sede/medio de captura coincida con esa cuenta. Reintentos y
   reversos usan el snapshot, aunque cambie la configuración para nuevos documentos.
4. Retirar escritura y después columna/FK inversos cuando no haya consumidores
   antiguos. La transición no admite dos relaciones editables. Después de usar
   la cardinalidad nueva no es seguro volver a un worker que solo entienda un
   auxiliar por banco.

La conciliación selecciona movimientos de **todos** los auxiliares del banco,
con las sedes autorizadas y el mismo corte. Cambiar el filtro de sede no convierte
un saldo parcial en saldo bancario total. Extracto y asignaciones siguen
referenciando `EntryId + LineNumber`; ningún asiento se copia al añadir un vínculo.
Operaciones pendientes del mismo banco se identifican por el snapshot aceptado.

Las cuentas de efectivo no requieren banco. El cobro con tarjeta pendiente del
adquirente conserva su cuenta de compensación; solo el abono real mueve el banco.
Una deuda con tarjeta de crédito requiere función de pasivo diferenciada: la
validación actual de banco como activo no se debe ampliar a cualquier cuenta
por el nombre «tarjeta». La configuración de cada función y sus cuentas permitidas
se valida en Accounting y se presenta mediante catálogos persistidos.

Aceptación: un banco con auxiliares en tres sedes concilia el mismo extracto sin
duplicar saldo; un pago conserva su auxiliar original; configuración ambigua,
cuenta de otro tenant, remapeo de auxiliar usado y acceso parcial se rechazan.
La lectura de movimientos agrega/pagina en SQL; ninguna consulta por auxiliar.
Los casos de cierre ya implementados conservan su regresión. La ampliación sigue
en validación local; no se extiende la autorización histórica de release.

### Adaptación técnica local del 23 de octubre

Los medios de tarjeta en egresos de proveedores usan propósitos de configuración
`SupplierDebitCard` y `SupplierCreditCard`, independientes de las cuentas por
cobrar al adquirente de tarjetas de clientes. Débito requiere el auxiliar bancario
configurado; crédito corporativo, el auxiliar de pasivo con su banco real. Son
defaults de captura asistida del mismo motor, no medios, bancos ni flujos nuevos.
No tienen cuenta sugerida universal: el tenant selecciona sus auxiliares del PUC.
El perfil conserva esas categorías opcionales sin código/nombre de cuenta
sugerida; `accounting.DefaultAccountDefinitions` proyecta solo sugerencias
concretas para los tres accesos existentes de aprovisionamiento, defaults y seed.
No se crean bancos o cuentas ficticios para completar la configuración. El
resolver canónico valida la función, sede, banco y medio de la cuenta ganadora;
una excepción por sede inválida no elige a escondidas la cuenta compartida.
Falta de configuración conserva el pago pendiente y su reserva, explica que no
se realizó y revierte efectos financieros hasta corregir y reintentar el mismo
trabajo. El cambio requiere revisar estas dos configuraciones antes del corte y
drenar trabajos contables anteriores según la transición de comprobantes.
El número de consultas del camino se conserva; la validación usa el mismo lote
de categorías y el aplicador nativo existente. Pasaron los cuatro casos de
tarjetas manuales; los dos de proveedor también prueban el pago nativo contra el
mismo auxiliar, falta de configuración, override inválido sin fallback, mensaje
de pago no realizado, cero efecto parcial, corrección y reintento, y replay con
una sola aplicación. Los intentos intermedios detectaron dos datos faltantes del
fixture —serie nativa y alcance compartido del auxiliar—; se completaron mediante
el aprovisionamiento canónico y el builder, sin relajar las validaciones productivas.
La compilación SQL pasó sin advertencias y backend sin errores, con las cuatro
advertencias previas de la dependencia fiscal. La regresión amplia posterior
aprobó 135 casos SQL/API y la base 862 casos, antes del slice de comprobantes
asociados a compras. Incluye la preservación de mapeos elegidos por el contador
al aprovisionar otro año, y el pago mixto con tarjetas del proveedor sin usar
las cuentas de cobro al adquirente. El rollback del corte debe conservar binarios que comprendan
propósitos sin sugerencia de cuenta; un `EnsureDefaults` antiguo que intente crear
cuentas desde todas las definiciones no es compatible con esas filas opcionales.

- El banco se crea sin exigir auxiliar. Cada auxiliar configura su vínculo desde
  el PUC; el maestro devuelve su cantidad real. Los campos singulares de la API
  compatible solo tienen valor cuando existe exactamente un vínculo.
- El parámetro antiguo `AccountingAccountId` se admite al crear un banco como
  traducción al participante transaccional de edición del PUC. No se escribe el
  FK inverso. Al editar un banco, ese parámetro solo puede confirmar un vínculo
  ya existente; no reasigna cuentas. La compatibilidad de auxiliares antiguos sin
  propiedades conserva su código y su ausencia de padre, sin inventar una
  agrupación. Esta excepción solo admite un auxiliar antiguo sin uso; el PUC
  ordinario conserva sus validaciones y la protección de cuentas utilizadas.
- `accounting.BankPostingAccount` resuelve una transferencia por banco y sede:
  primero sede específica frente a compartida y luego medio específico frente
  a no especificado. Dos candidatos con igual prioridad son una ambigüedad, no
  un `TOP(1)` arbitrario. No se interpreta el código ni el nombre del auxiliar.
- La aceptación canónica llama a `accounting.BankAccountsFreeze` dentro de su
  transacción. Se conserva la selección en el trabajo contable ya existente
  (`ResolvedBankAccountsJson`), sin importes, saldos, cola ni proceso adicional.
  También se protege la configuración de los auxiliares seleccionados.
- Si todavía falta configuración o es ambigua, se conserva la referencia sin
  auxiliar resuelto. El motor deja pendiente el documento con motivo explícito;
  un pago/recaudo no aplica saldo, transacción ni asiento. El reintento existente
  puede completar únicamente referencias sin resolver, antes de aplicar efectos;
  nunca sustituye una cuenta ya fijada. Las fuentes comerciales sin contabilidad
  mantienen su modo original. Las reversiones de propina invierten las partidas
  del asiento original, incluidos banco, pasivo y centro de costo. No dependen
  de un snapshot bancario anterior ni resuelven un auxiliar nuevo: el processor
  y el participante de fijación excluyen esa nueva selección. Esto también
  conserva los asientos históricos previos al snapshot; sin asiento original
  completo, la anulación contable queda pendiente con causa visible.
- `accounting.BankBookAccounts` es la proyección común de auxiliares bancarios
  para lectura, permisos, cruces y cierre. Incluye auxiliares históricos inactivos
  y excluye compensación de tarjetas y pasivos de tarjeta. No copia asientos.
- La migración conserva únicamente vínculos anteriores inequívocos y prueba el
  auxiliar de documentos históricos contabilizados contra su asiento. Un banco
  compartido o una historia no demostrable bloquean la transición con un motivo;
  no se reparten ni reclasifican saldos automáticamente.
  Todo el paso es transaccional. Después de fijar la historia se vacían los
  valores inversos: conservarlos bloquearía una futura reasignación permitida
  de un auxiliar todavía sin uso. El esquema conserva la columna nullable para
  compatibilidad del despliegue; no conserva otra relación editable ni usada.
- La importación del extracto conserva el encabezado y sus líneas en la misma
  transacción, con una sola inserción por conjunto para las líneas. La pantalla
  reutiliza los resultados de los comandos para detalle y resumen, con caché
  acotada por tenant/sede. Cambiar la cuenta principal requiere una lectura del
  catálogo bancario porque cambia también la versión de la cuenta principal
  anterior; no invalida el resto de Contabilidad.

El listado y las cuatro colecciones del detalle ya se paginan en SQL, antes de
enriquecer descripciones. El listado acepta búsqueda por archivo/banco, filtro de
banco y orden estable; extracto y libro aceptan búsqueda sobre el conjunto completo.
Los conteos usan los mismos filtros y los totales de cierre conservan el banco
completo. Los cruces e historial tienen páginas independientes. Tamaño máximo:
100 filas; respuesta de comando: primeras 50 filas de cada colección.

El contrato del listado pasa de arreglo a `items/page/pageSize/totalCount`;
API y admin requieren corte coordinado. No se crea un endpoint paralelo. La
pantalla conserva un propietario por recurso y reutiliza el detalle de cada
comando. Al importar se relee solamente la página activa de períodos: la respuesta
del comando no incluye la pertenencia ni el orden de ese listado filtrado. Los
cruces y el cierre actualizan sus resúmenes ya visibles sin GET adicional. Las
páginas de detalle anteriores se marcan obsoletas sin consultarlas en segundo
plano. No hay polling.

La comodidad de cruces exactos solo se ofrece si ambos conjuntos completos caben
en la respuesta sin filtros (máximo 100). Una coincidencia única en una página no
prueba unicidad en el banco. Cada cruce es una transacción independiente explícita
del comando existente; si falla uno posterior, se conserva en pantalla el último
resultado confirmado. La muestra previa del CSV presenta como máximo 50 filas y
explica que se validan/importan todas. La importación admite hasta 10 MB/100.000
movimientos; rechaza referencias largas e importes fuera de decimal(19,4), sin
truncarlos ni redondearlos silenciosamente.

La comprobación de permisos usa `accounting.BankInaccessibleBusinesses`, con los
mismos roles, tenant y sedes del contrato anterior. Importación, mutaciones y
lectura reutilizan esa proyección. El conjunto de bancos se reduce antes de
revisar sus sedes: la prueba de volumen reprodujo un timeout de 30 s cuando se
repetía el mismo historial por cada período. La versión corregida revisa cada
banco una vez. Las lecturas usan una transacción serializable; un comando obtiene
su respuesta autorizada antes del commit. Una regresión reprodujo antes HTTP 403
con un extracto ya guardado; ahora comprueba cero encabezados y líneas al rechazar.

Evidencia local adicional del 23 de octubre: cuatro casos SQL/API aprobaron el
banco en tres sedes, acceso parcial, cierres/retroactividad, liquidación de tarjetas
y volumen. Con 10.000 partidas y 1.001 períodos, 20 muestras por caso en SQL local
exclusivo, sin compiladores ni diagnósticos concurrentes: p95 503,5 ms primera
página de detalle, 423,2 ms siguiente, 600,1 ms filtrada; 377,7 ms primera página
del listado y 133,2 ms siguiente. Se verifican seis comandos SQL por detalle y dos
por listado, respuestas menores de 120.000 caracteres, páginas sin duplicados,
conteos globales y búsqueda de la fila 10.000. Presupuesto: p95 menor de 2 s.
Dos escenarios de navegador sobre el build real de Next con API simulada pasan,
incluida navegación, unicidad global de candidatos y ausencia de GET tras cruce
y cierre. Schema, API, TypeScript y ESLint pasan. Esto no sustituye la regresión
global ni declara terminados los grupos financieros manuales y cargos de compra.

Evidencia local del slice: schema Release sin errores; seis escenarios SQL/API
dirigidos aprobaron banco en tres sedes, pagos mixtos y reintegro, conservación de
auxiliar tras una nueva regla y desactivación del banco, rechazo explicable de
ambigüedad, reintento sin doble pago y migración inequívoca/repetida frente a una
relación histórica ambigua. También aprobaron seis casos de navegador con API
simulada sobre la compilación real de Next, incluido crear el banco sin auxiliar
y reutilizar las respuestas de los comandos. El contrato paginado posterior
requiere la única lectura de listado tras importar explicada arriba. La corrida
SQL/API dirigida final aprobó siete casos, incluidos los tres rechazos monetarios
sin escrituras, el auxiliar conservado y los cierres existentes. No se presentan
como la regresión completa ni como prueba de navegador con API real.
