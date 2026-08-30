# Decisión: aprovisionamiento, suscripciones, cupos y Wompi

Estado: aprobado para implementación incremental
Fecha: 2026-08-30

## Resultado

Una empresa se crea una sola vez y únicamente después de verificar el pago o de registrar una exención autorizada. El mismo contrato gobierna el alta inicial, las ampliaciones, los cupos visibles para el tenant y su aplicación concurrente.

Se extienden los propietarios existentes: `ITenantProvisioningStore` sigue creando físicamente el tenant; el ciclo de pagos existente sigue siendo el único motor de pagos; usuarios, enrolamiento POS, nómina y fiscal conservan sus casos de uso. No se crean writers, catálogos ni landing paralelos.

## Catálogos comerciales separados

La oferta de gestión empresarial y la de agentes de IA son familias distintas dentro de una sola landing y una sola experiencia comercial. Los planes, créditos y precios actuales de agentes de IA se conservan.

Planes de gestión empresarial, en COP por mes:

| Código | Nombre | Precio | Usuarios completos | Cajas | Documentos DIAN/mes | Empleados de nómina |
|---|---|---:|---:|---:|---:|---:|
| `essential` | Esencial | $119.900 | 3 | 1 | 500 | 10 |
| `business` | Negocio | $299.900 | 8 | 3 | 1.500 | 30 |
| `company` | Empresa | $449.900 | 12 | 5 | 3.000 | 100 |
| `corporate` | Corporativo a medida | Cotizado | Configurable | Configurable | Configurable | Configurable |

`Negocio` aparece como recomendado. Adicionales:

| Código | Unidad | Precio |
|---|---|---:|
| `full_user` | usuario completo/mes | $30.000 |
| `seller_user` | usuario vendedor/mes | $10.000 |
| `pos_device` | caja/mes | $20.000 |
| `dian_document_pack` | paquete de 1.000 documentos/mes | $20.000 |
| `same_nit_site` | sede del mismo NIT | $0 |
| `payroll_employee` | empleado de nómina | pendiente de tarifa aprobada |

No se inventa el precio del empleado de nómina adicional. El catálogo persistido es la única fuente de verdad. Landing y wizard lo consultan por API; el frontend nunca calcula un precio autoritativo. El catálogo existente se migra y versiona, sin duplicarlo. Cada cotización guarda snapshot de nombres, cantidades, precios, impuestos, moneda y versión. Los documentos adicionales solo se venden en bloques enteros de 1.000; el API rechaza cantidades fraccionarias y recalcula el total como `bloques * 1.000 * $20`.

La periodicidad comercial admite `Annual` y `Monthly`. `Annual` es la selección predeterminada tanto en el aprovisionamiento como en una renovación iniciada por el cliente y aplica 15 % de descuento sobre doce meses del subtotal recurrente elegible: `total anual antes de impuestos = subtotal mensual * 12 * 0,85`. La interfaz presenta juntos el precio mensual equivalente, el total anual, el valor sin descuento y el ahorro exacto en COP (`subtotal mensual * 12 * 0,15`) con una etiqueta visible “Ahorras 15 %”. `Monthly` cobra un mes sin ese descuento. Impuestos, redondeos, conceptos elegibles y cualquier promoción adicional son calculados y devueltos por el servidor; el navegador solo proyecta la cotización y nunca combina descuentos por su cuenta.

## Wizard y pago

1. `/register` crea o recupera un borrador; todavía no crea tenant.
2. El wizard captura empresa, sede, administrador y plan.
3. El comprador puede sumar usuarios completos, vendedores, cajas, paquetes de 1.000 documentos DIAN y empleados. La UI recalcula visualmente y muestra la capacidad total resultante.
4. El paso de plan ofrece `Anual` y `Mensual`, con `Anual` preseleccionado. El resumen anual destaca el 15 % y el ahorro exacto en pesos, sin ocultar el total que se debitará hoy ni el valor mensual equivalente.
5. El servidor recibe periodicidad y cantidades, vuelve a cotizar desde el catálogo vigente y crea una cotización inmutable con expiración. La periodicidad, porcentaje y ahorro quedan en el snapshot.
6. Pago es el último paso. Se crea un intento Wompi de uso único.
7. El retorno del checkout solo consulta y muestra estado; nunca aprovisiona.
8. El callback del Widget envía inmediatamente el `transaction.id` al backend. El backend consulta Wompi y, si confirma `APPROVED`, ejecuta el mismo comando idempotente de confirmación y aprovisionamiento. Webhook y conciliador temporizado son rutas de recuperación que convergen en ese comando; ninguno mantiene una implementación propia de creación.
9. El primer consumidor que reclama el borrador pagado llama a `ITenantProvisioningStore`; los demás reciben el resultado ya creado.
10. Suscripción y entitlements se crean durablemente y luego el outbox envía la invitación al administrador.

La experiencia elegida es el Widget oficial de Wompi abierto como modal desde el último paso, no un iframe propio ni una pestaña nueva. Así el comprador permanece visualmente en el wizard y Wompi presenta sus medios de pago. La public key, referencia, COP, valor en centavos, expiración y firma de integridad provienen de la cotización del servidor; la private key y los secretos nunca llegan al navegador.

El callback del Widget entrega el identificador de transacción y cambia la pantalla a “Verificando pago”. La primera acción es `POST /provisioning/{draftId}/payments/{transactionId}/verify`: el servidor consulta Wompi y puede completar pago y aprovisionamiento en esa misma interacción. El navegador nunca afirma que pagó por sí solo. Si Wompi todavía devuelve `PENDING`, el frontend consulta con backoff el estado propio del borrador; webhook y conciliador continúan en segundo plano. Cuando queda `Provisioned`, la pantalla confirma que la empresa fue creada y que el correo fue programado. Para PSE, Nequi u otros medios asíncronos puede existir espera; cerrar la página no pierde el proceso y el comprador puede retomarlo. SSE puede añadirse para progreso, pero no es requisito ni autoridad del pago.

Estados: `Draft`, `Quoted`, `PaymentPending`, `Paid`, `Provisioning`, `Provisioned`, `PaymentFailed`, `Expired`, `Waived`, `Failed`. Las transiciones son monotónicas y auditadas. Doble clic, retorno, webhook y poller concurrentes producen un tenant y una invitación.

## Wompi

Existen dos alcances:

- Wompi de negocio vive en `IntegrationConnections` de una sede ya creada y cobra a sus clientes.
- Wompi de facturación Auraly es configuración de plataforma y cobra antes de que exista el tenant.

La cuenta de plataforma se resuelve mediante `IPlatformBillingMerchantProvider`. Los secretos viven en el almacén seguro del entorno; la base conserva identificador no secreto, modo, versión y estado. Cambiar cuenta crea una versión; cada intento guarda la versión original para verificar webhooks históricos correctamente.

Se reutilizan transporte, firma, consulta de transacción, polling e idempotencia de la integración actual de Mimos, no sus secretos ni su fila de negocio. Para habilitar Wompi se exigen private key, public key, events secret e integrity secret del mismo ambiente. Solo se aceptan endpoints oficiales HTTPS. Un secreto ausente siempre falla cerrado.

Un pago se acepta únicamente si pasan firma dinámica, ambiente/cuenta, consulta Wompi, estado aprobado, referencia, COP, valor exacto y unicidad de transacción. La rotación de cuenta no invalida intentos anteriores.

El endpoint de eventos es un router, no un aprovisionador genérico. La referencia resuelve el tipo de pago (`TenantProvisioning`, `EntitlementExpansion`, reserva u otro handler registrado). Antes de consultar Wompi inserta/consulta un recibo con clave única `Provider + MerchantConfigurationVersion + TransactionId`. Si el recibo ya terminó o el borrador está `Provisioned`, responde `200` sin volver a consultar, aprovisionar ni publicar correo. Si otro proceso tiene la transacción reclamada, devuelve éxito reintentable y deja que el propietario termine. Solo un intento conocido, de la cuenta correcta y todavía pendiente puede entrar al comando de confirmación.

## Exención

“Omitir pago” exige `tenants.provisioning.payment.waive`, sembrado únicamente para el rol administrador general de Auraly en el tenant de plataforma y marcado como no delegable. El backend comprueba tenant de plataforma, rol y permiso aunque la UI oculte el botón. Requiere motivo y auditoría, y crea settlement `Waived`; no falsifica una transacción Wompi. La exención aprovisiona inmediatamente y publica una sola invitación al correo de administrador capturado en el wizard. Sin ese permiso, la única transición válida es pago confirmado; después se aprovisiona y se publica la misma invitación. Nunca se invita antes de `Provisioned`.

## Entitlements y permisos

Los cupos pertenecen al tenant y cubren todas sus sedes. El tenant solo puede ver su uso e iniciar una compra. No escribe límites ni precios. Plataforma con `tenants.capacity.update` puede ajustar cualquier tenant con motivo y auditoría.

La capacidad aplicada siempre sale de las líneas de la cotización efectivamente pagada (o exenta), nunca del formulario del navegador. Al aprovisionar se comparan referencia, versión de catálogo, moneda, importe y cada cantidad comprada antes de materializar los límites. La vista del tenant muestra, por recurso, `usado / contratado / disponible`, fecha de corte y compras pendientes. Las ampliaciones solo aumentan capacidad cuando su propio pago queda confirmado.

Capacidad efectiva = incluida en plan + ampliaciones pagadas + ajustes vigentes. Una compra agrega un ajuste; nunca sobrescribe historia. La consulta muestra límite, usado, disponible, porcentaje y renovación.

- `FullUser`: cuenta activa completa. Se reserva en el caso de uso de usuarios.
- `SellerUser`: cuenta activa creada/vinculada desde Terceros con rol `ORDER_SELLER`.
- `PosDevice`: se reserva en `PosEnrollmentService`.
- `PayrollEmployee`: se reserva al activar un empleo, no al crear una Party.
- `DianDocument`: bolsa mensual compartida por facturas electrónicas, documentos soporte y documentos electrónicos de nómina emitidos.

`DianDocument` se renueva en cada fecha de corte de la suscripción: el consumo del nuevo periodo empieza en cero y la capacidad contratada se conserva; el saldo no usado no se acumula. Cada emisión reserva una unidad antes de numerar o enviar a DIAN y confirma el consumo con el resultado autoritativo. Borradores, consultas, reintentos técnicos y reenvíos del mismo documento conservan la misma reserva y no consumen otra unidad. Cuando no hay saldo, factura electrónica, documento soporte y nómina electrónica quedan bloqueados antes de emitir.

La validación tiene dos niveles y ambos son obligatorios. El preflight de experiencia consulta el saldo al abrir cada flujo y evita que el usuario invierta trabajo en un documento que no podrá emitir: facturación electrónica muestra un modal bloqueante y deja únicamente comprobantes no electrónicos; recepción de mercancía deshabilita la selección de documento soporte y explica cómo ampliar; nómina marca desde el periodo que el envío DIAN de fin de mes no se programará por falta de saldo. El segundo nivel reserva atómicamente al confirmar la emisión para cubrir consumo concurrente entre pestañas, sedes y cajas. Un preflight exitoso nunca sustituye esa reserva final.

Para cajas desconectadas, el servidor entrega una concesión de cupo (`offline lease`) por dispositivo, con identificador y saldo monotónico, tomada de la misma bolsa mensual. La caja puede emitir offline solamente contra ese saldo reservado. Al sincronizar reporta los documentos e intercambia la concesión por un saldo actualizado. Si el servidor confirma agotamiento, se persiste localmente el bloqueo y ninguna pérdida posterior de conexión vuelve a habilitar la emisión. Una caja que aún no recibió esa confirmación puede continuar únicamente con su concesión ya reservada; nunca inventa saldo ni duplica el concedido a otra caja.

Si una caja alcanzó a cerrar una factura fiscal estando desconectada sin concesión suficiente, la venta y su snapshot fiscal no se pierden. El `Outbox` SQLite y `PosEdgeOutboxUploader` existentes la suben con la misma clave idempotente; el servidor acepta y procesa los efectos comerciales, pero deja su único `FiscalDocumentProcesses` en `PendingCapacity`, sin invocar DIAN. La respuesta y la sincronización fiscal muestran “Pendiente por documentos DIAN”. Ese estado no usa backoff de red ni genera intentos repetidos.

Una ampliación pagada o la apertura de un nuevo subperiodo mensual de capacidad publica una invalidación y reclama, en orden de emisión, los procesos `PendingCapacity` para los que ahora exista saldo. La transición reserva la unidad y devuelve el mismo proceso a `PendingGeneration`; desde allí continúan `FiscalGenerationWorker`, `IFiscalSubmissionWorkStore` y `FiscalSubmissionWorker`. No se crea otra factura, numeración, movimiento, outbox, worker ni cola. El `DocumentId`, snapshot, consecutivo y clave de idempotencia originales permanecen inmutables. Si varias cajas esperan, el reclamo serializado por tenant evita sobreconsumo y deja el excedente todavía pendiente.

### Reutilización canónica para la recuperación offline

| Necesidad | Propietario existente que se extiende |
|---|---|
| conservar la venta local | tabla SQLite `Outbox` y `PosEdgeSaleStore` |
| reconectar y subirla | `PosSynchronizationWork` → `PosUnifiedOutboxDispatcher` → `PosEdgeOutboxUploader` |
| recepción idempotente | `ReceivePosSaleService` y `SqlPosSaleServerStore` |
| estado fiscal durable | única fila de `FiscalDocumentProcesses` |
| generación y envío DIAN | `FiscalGenerationWorker`, `IFiscalSubmissionWorkStore` y `FiscalSubmissionWorker` |
| despertar tras compra/renovación | outbox de entitlements publica señal al motor fiscal e invalidación `FiscalProvisioning` al POS |

`PendingCapacity` es una espera de negocio recuperable, no un error permanente ni una falla de transporte. La compra no recorre cajas ni envía directamente a DIAN: solo aumenta el ledger, publica el evento y el propietario fiscal reclama trabajo. Esto conserva el orden, la idempotencia y una única ruta de emisión.

Todas las reservas bloquean por tenant para impedir que dos solicitudes consuman el último cupo. Creación, activación o cambio de rol valida por separado `FullUser` y `SellerUser`; activar empleados valida `PayrollEmployee`. El backend devuelve contador actual y límite en el error de capacidad para que la interfaz muestre cuánto lleva y ofrezca ampliar.

`ORDER_SELLER` abre únicamente Pedidos y sus acciones mínimas de captura. No incluye rutas, terceros, reportes, configuración, facturación, caja ni plataforma. Una Party sin acceso no consume usuario. Reintentos y reenvíos DIAN no vuelven a consumir documento.

## Alertas y compra de ampliaciones

Al cruzar 70 %, 85 %, 95 % y 100 %, una política idempotente publica aviso in-app y correo al administrador; Web Push se usa si hay suscripción válida. La deduplicación incluye tenant, periodo, entitlement y umbral. El aviso muestra uso/límite, renovación y “Ampliar capacidad”. La ampliación solo se aplica después del pago verificado.

## Ciclo de suscripción, facturas, cartera y suspensión

El pago inicial cubre el periodo elegido desde el instante de aprovisionamiento: un mes para `Monthly` o doce meses para `Annual`. Esa fecha queda como `BillingAnchor`; no se deriva del día en que después se pague tarde. Para anclas 29, 30 o 31, los meses cortos usan su último día y el siguiente ciclo vuelve al día original. La zona horaria de facturación es configurable por plataforma y por defecto `America/Bogota`.

La facturación recurrente se agrega al ciclo de pagos existente; no se reutiliza el auto-renovado silencioso de `UsageBillingService`. Las `BusinessSubscriptions` actuales siguen siendo la fuente de capacidad/créditos de agentes por negocio, pero su siguiente periodo solo se abre desde un cobro de tenant pagado o dentro de la gracia explícita. Un `TenantBillingAccount` consolida en un cobro todas las líneas recurrentes del tenant: plan empresarial, usuarios, vendedores, cajas, paquetes DIAN, empleados y planes de agentes activos. Para `Monthly` crea el siguiente cobro cada mes; para `Annual`, una vez pagado el año, no crea doce cobros mensuales y genera el siguiente únicamente al acercarse el aniversario. Cada cobro conserva snapshot del catálogo, periodicidad, cantidades, impuestos, descuentos, ahorro, moneda y periodo; cambios posteriores no lo reescriben.

Pagar anual modifica el calendario de cobro, no la semántica operativa de los cupos mensuales. Los documentos DIAN y cualquier capacidad expresada “por mes” abren doce subperiodos mensuales dentro del término anual prepagado, reinician su consumo en cada subancla y no acumulan saldo. Esa apertura mensual no genera una cuenta por cobrar ni otra factura fiscal. Usuarios, vendedores, cajas y empleados permanecen contratados durante todo el término, sujetos a las ampliaciones o disminuciones programadas.

El cobro se genera idempotentemente algunos días antes de la siguiente ancla (configurable; recomendado 5), vence en la ancla e identifica el siguiente periodo mensual o anual. La página Suscripción muestra periodicidad, estado, periodo, creación, vencimiento, total, descuento, ahorro, saldo, factura electrónica posterior al pago, pagos y botón “Pagar con Wompi”. El Widget usa una referencia única de ese cobro. La verificación inmediata, webhook y conciliador aplican el pago al mismo cobro una sola vez. Pagos parciales no renuevan ni reactivan salvo política futura explícita; un excedente no se aplica silenciosamente a otro cobro.

Para Auraly, el cobro constituye la cuenta por cobrar de suscripción. El pago Wompi entra por el caso de uso existente de recaudo, la cancela y dispara la factura de venta de contado desde la empresa facturadora de plataforma mediante el motor documental/fiscal y contable existente. El tenant accede a una proyección autorizada de su propio cobro y documento; nunca recibe permisos ni consultas sobre la contabilidad del tenant plataforma. Un fallo fiscal posterior al pago deja el envío DIAN en el proceso recuperable habitual y no crea otra factura, recaudo ni cartera.

El administrador autorizado de plataforma también puede registrar desde Auraly un recaudo manual de una cuenta abierta cuando el dinero llegó por transferencia, consignación u otro medio aprobado. Esta acción reutiliza el caso de uso canónico de pagos manuales y exige el permiso no delegable `tenants.billing.payment.confirm_manual`; el usuario del tenant nunca lo recibe. Antes de confirmar muestra tenant, cobro, periodo, saldo y capacidad que se activará; exige medio, fecha efectiva, importe total exacto, referencia bancaria o comprobante único y observación. No acepta fechas futuras, monedas distintas, cobros pagados/anulados, importes parciales ni una referencia ya utilizada. El soporte se guarda en el almacén documental autorizado, no como datos libres o secretos en el asiento.

Wompi y recaudo manual convergen después de validar el medio en un único comando idempotente `SettleTenantBillingCharge`. Ese comando registra el `PaymentTransaction`, aplica el recaudo a la CxC de Auraly, marca el cobro `Paid`, actualiza la proyección de Suscripción del tenant, activa o renueva el periodo y sus entitlements, y publica la creación de la única factura electrónica correspondiente. La clave de idempotencia incluye cobro y transacción/referencia externa. No existe un botón que cambie directamente el estado de la suscripción o del cobro; revertir un recaudo exige el flujo contable de anulación/devolución y su impacto fiscal, con motivo y auditoría.

El periodo de gracia es configuración de plataforma, 10 días por defecto. En la ancla un cobro con saldo pasa a `PastDue`; el tenant conserva operación durante los días 1 a 9 y la capacidad mensual se abre como crédito de gracia trazable. Los avisos previos son configurables (por defecto 5 días antes). La frecuencia vencida también es configurable (por defecto cada 3 días): con gracia 10 se avisa los días 3, 6 y 9 de mora; el día 9 anuncia la fecha de suspensión. Al inicio local del día 10 pasa a `Suspended`, se envía aviso final y se bloquea la operación.

Cada evento genera notificación in-app visible en la campanita únicamente para cuentas activas con el rol administrador del tenant; vendedores, cajeros, empleados y demás roles no son destinatarios. El correo es un canal global opcional: habilitado significa que todos los tenants reciben los recordatorios; deshabilitado significa que ninguno los recibe, mientras la campanita permanece obligatoria. No existen excepciones ni interruptores por tenant. Cada entrega tiene clave única `ChargeId + RecipientUserId + Channel + NotificationKind + DelinquencyDay`, de modo que múltiples administradores reciban su propio aviso y los reintentos no creen duplicados. El outbox registra programado, entregado y error seguro sin guardar contenido sensible innecesario.

En el módulo transversal `Tenants` se agrega una pestaña global `Política de cobranza`, protegida por el permiso de plataforma no delegable `tenants.billing.policy.manage`. Solo el administrador general de Auraly lo recibe. Configura `EmailRemindersEnabled` (`true` por defecto), días previos, días de gracia, frecuencia de mora, zona horaria y versiones de plantilla para todos los tenants. `InAppEnabled` no se expone porque la campanita siempre se entrega. El administrador de un tenant ve en Suscripción el calendario efectivo y sus facturas, pero nunca esta pestaña ni sus endpoints. Cada cambio exige motivo y registra actor, valor anterior/nuevo y vigencia; apagar correo no borra avisos ya entregados ni altera campanita o suspensión.

La campanita y el botón “Pagar ahora” del correo abren una ruta opaca de Auraly que, después de autenticar, lleva a `/dashboard/subscription?charge={ChargeId}` y enfoca el detalle autorizado. No se incrusta en el correo una URL estática de checkout Wompi: podría ser reenviada, expirar, pertenecer a una versión anterior de la cuenta o estar ya pagada. Al abrir Auraly se comprueban tenant, rol, saldo y estado actuales y recién entonces el backend crea un intento de pago de uso único.

Suscripción contiene una tabla paginada en servidor, ordenada por creación descendente, con filtros por `Todas`, `Pendientes`, `Vencidas`, `Pagadas`, estado, rango y periodo cobrado. Cada fila muestra número de cobro, periodo, creación, vencimiento, estado, total, saldo, fecha de pago y, cuando exista, número de factura electrónica; el detalle ofrece factura/contenedor descargable, líneas y trazabilidad de pagos. “Pagar” solo aparece para saldo abierto. Tanto el CTA del correo como el botón de la tabla abren el mismo componente de pago: el Widget oficial de Wompi como modal dentro de Auraly, con verificación inmediata y recuperación por webhook/conciliador. No se crea una segunda integración, iframe propio ni pestaña obligatoria.

La suspensión se aplica por una política transversal de estado de tenant, no desactivando filas de empresa o usuario. Solo permanecen habilitados autenticación mínima, recuperación de contraseña, Suscripción, consulta/pago de cobros y facturas, soporte y administración de plataforma. Se rechazan nuevas operaciones API, enrolamientos y nuevas concesiones offline; se revocan sesiones operativas y leases de autenticación, y se publican invalidaciones `Security`/`FiscalProvisioning` a cajas conectadas. Una caja físicamente desconectada no puede recibir una suspensión instantánea: el lease local acotado y su fecha de expiración limitan esa ventana; al primer contacto persiste el bloqueo y ya no opera offline. Esta limitación debe mostrarse en plataforma y medirse, no ocultarse con una promesa imposible.

Un pago total confirmado, por Wompi o por recaudo manual autorizado, mueve el cobro a `Paid`, aplica la CxC, crea/reutiliza su factura fiscal, cambia la suscripción a `Active`, abre o confirma el periodo correspondiente y publica las mismas invalidaciones. La reactivación es idempotente. Si había documentos `PendingCapacity`, el motor fiscal los reclama contra la nueva capacidad en orden. Pagar tarde no desplaza `BillingAnchor` ni genera dos periodos. La vista del tenant refleja el mismo resultado y medio de pago sin exponer comprobantes internos ni datos contables de otros tenants.

Ampliaciones durante un periodo se cobran de inmediato prorrateadas por los días restantes y solo se activan tras el pago; el siguiente cobro de renovación incluye su precio recurrente completo según la periodicidad vigente. En anual, el prorrateo usa los días restantes del término anual y conserva el descuento anual solo cuando la política comercial versionada lo declare elegible. Disminuciones y cancelaciones se programan para la siguiente ancla y no borran capacidad ya pagada. Cambios de plan o periodicidad entran en la siguiente renovación, salvo una ampliación inmediata pagada; conservan snapshots y trazabilidad. Créditos o devoluciones requieren documento y aplicación contable, no edición de la factura.

## Factura electrónica de Auraly por servicios pagados

Cada pago total confirmado de aprovisionamiento, renovación o ampliación crea exactamente una factura electrónica de venta de Auraly al cliente por el servicio adquirido. El pago anual emite al confirmarse una factura por el término anual efectivamente comprado y no doce facturas mensuales; la próxima factura de renovación nace cuando se pague el cobro del siguiente año. El pago mensual emite una factura por cada mes efectivamente pagado. No se crea un segundo modelo de factura ni una tabla de detalle paralela. `SalesDocuments`, `SalesDocumentLines`, snapshots comerciales/UBL, `FiscalDocumentProcesses`, generación, firma, envío y recepción DIAN siguen siendo los propietarios canónicos.

Para evitar duplicidad entre la “factura pendiente” que el usuario paga y la factura fiscal que nace del pago, el objeto previo al pago se denomina internamente `TenantBillingCharge` y en la interfaz “Cobro de suscripción”. Conserva periodo, vencimiento, líneas, impuestos, saldo y CxC, y es lo que aparece como pendiente o vencido. Al aprobar Wompi, el fulfillment aplica el recaudo al cobro y publica `TenantBillingChargePaid`. El consumidor documental reclama `ChargeId` con índice único, crea la factura fiscal una sola vez y guarda `SalesDocumentId`; webhook, verificación inmediata, conciliador y redelivery reciben el mismo resultado. La vista puede seguir agrupando cobros y facturas bajo “Suscripción”, pero distingue `Cobro pendiente` de `Factura electrónica emitida`. Un cobro no pagado no inventa una factura fiscal; un cobro pagado nunca genera dos.

La exención administrativa no equivale a un pago. Por defecto provisiona y registra el beneficio autorizado sin factura de venta ni recaudo; si contabilidad define que debe existir una operación gratuita, se modelará mediante el tratamiento fiscal aprobado y no mediante una transacción Wompi falsa. Pagos parciales continúan fuera de alcance: no disparan factura hasta completar el cobro.

### Una misma línea para productos y servicios

Se reutilizan los dos catálogos canónicos existentes: `Products` para bienes y `Services` para servicios. `Services` se amplía con los datos facturables que hoy no posee —código estable, unidad UBL, perfil tributario, moneda y versión de precio— sin crear `BillingServices` ni insertar servicios ficticios en `Products`. Sus campos de agenda/reserva permanecen bajo el propietario actual y los campos fiscales/comerciales comunes se exponen mediante una proyección de ítem vendible. Los planes, adicionales, prorrateos y ajustes de Auraly son filas `Services` versionadas dentro de la empresa emisora de plataforma.

`SalesDocumentLines` sigue siendo el único detalle. `ProductId` pasa a nullable, se agrega `ServiceId` nullable y una restricción exige exactamente una referencia; `ItemKindSnapshot`, código, nombre, unidad y perfil tributario quedan congelados en la línea. Los documentos históricos mantienen `ProductId` y se backfillean como `Product` sin reescritura de importes. Para producto, el handler conserva kardex, costo, marca y atribución de proveedor; para servicio, lee `Services`, inserta la misma línea comercial y UBL y omite inventario, costo, marca y proveedor. Impuestos, descuentos, retenciones, totales, notas crédito/débito y contabilidad operan sobre importes de línea y no se bifurcan por tipo. La representación gráfica rotula la columna como “Producto o servicio” y puede mostrar el tipo sin cambiar encabezado, totales, CUFE, QR ni flujo DIAN.

El contrato de línea se versiona como referencia discriminada `ProductId | ServiceId`; nunca acepta ambos ni ninguno. Los adaptadores actuales siguen enviando `ProductId` y conservan compatibilidad. El buscador del POS puede incorporar servicios facturables mediante la proyección común solo cuando la sede los habilite; reservas siguen consultando `Services` directamente y no adquieren comportamiento de inventario.

El contrato de recepción se versiona/generaliza sin romper `PosSaleUploadRequest`: el adaptador POS sigue exigiendo dispositivo y sesión; `SourceMode = PlatformSubscription` exige `ChargeId`, cliente y servicio, y no crea una sesión de caja ficticia. Ambos convergen antes de `DocumentProcessingJobs` en el mismo documento aceptado. El handler canónico valida el contexto según origen, inserta las mismas tablas y entrega al proceso fiscal existente. No se crea otra numeración, writer fiscal, cola o worker.

El cliente facturado vive como `Party/Customer` dentro de la empresa emisora de Auraly, vinculado de manera única al tenant y a su identificación legal. El borrador de aprovisionamiento debe capturar antes del pago razón social/nombre, tipo y número de identificación, responsabilidades tributarias, dirección y correo de facturación; el servidor vuelve a validarlos. La factura toma un snapshot inmutable de esos datos. Cambiar el tercero después no reescribe documentos emitidos. La cantidad, precio, impuestos y descripción salen exclusivamente de las líneas del cobro pagado, por lo que lo facturado siempre concuerda con lo comprado.

### Entrega profesional por correo

La factura no se envía al confirmar el pago, sino cuando el proceso fiscal queda `DianAccepted` y existe la respuesta de validación. En esa transición, el outbox crea una entrega única por `DocumentId + RecipientEmailSnapshot + DeliveryKind + ArtifactVersion`. El correo usa la dirección de facturación capturada en el snapshot; si no existe, la factura queda disponible en Suscripción y se notifica al administrador para completar el correo y realizar una entrega posterior explícita, sin alterar el XML emitido.

El mensaje incluye emisor, número, periodo/servicio, total, fecha, CUFE, estado validado y un botón autenticado a la factura. Adjunta el contenedor electrónico exigible: `AttachedDocument` con el `Invoice` XML firmado y el `ApplicationResponse` de validación DIAN, empaquetado conforme al anexo técnico vigente, más el PDF profesional de representación gráfica con QR. El artefacto fiscal es inmutable y se regenera por versión solo si es byte-equivalente a sus fuentes; el correo nunca fabrica un XML desde la vista. Reintentos de SMTP reutilizan los mismos artefactos y no reenvían DIAN ni emiten otra factura. Se registran intentos, entrega, rebote y error seguro; los adjuntos se obtienen del almacén fiscal con enlaces expirables y no se duplican en la base.

Esta entrega sigue la [Resolución DIAN 000165 de 2023](https://www.dian.gov.co/normatividad/Normatividad/Resoluci%C3%B3n%20000165%20de%2001-11-2023.pdf), que contempla entrega por correo del XML, representación gráfica y documento de validación dentro del contenedor electrónico, y el [Anexo técnico de factura electrónica vigente publicado por la DIAN](https://micrositios.dian.gov.co/sistema-de-facturacion-electronica/documentacion-tecnica/). Los nombres, estructura, tamaño y reglas del contenedor se resuelven por la versión activa del generador fiscal, no se hardcodean en la plantilla de correo.

Pruebas adicionales obligatorias: pago, webhook y conciliador concurrentes producen un cobro aplicado y una factura; servicio no genera kardex ni sesión; producto conserva su comportamiento; importes de factura coinciden con el quote pagado; aislamiento del cliente por tenant; DIAN rechazada no envía correo; aceptación crea una entrega; redelivery SMTP no duplica factura; correo ausente conserva descarga; XML/ApplicationResponse/PDF pertenecen al mismo CUFE y `DocumentId`; nota crédito de servicio usa el mismo documento origen.

Estados de cobro: `Draft`, `Open`, `PastDue`, `Paid`, `Void`, `Uncollectible`; `PartiallyPaid` queda reservado aunque no habilita renovación. La factura fiscal usa los estados del motor documental/DIAN existente. Estados de suscripción: `Active`, `PastDue`, `Suspended`, `Cancelled`. `Tenant.IsActive` no se usa para mora porque impediría el portal de pago y mezclaría baja administrativa con cobranza.

El programador canónico es `TimedProcessScheduler`: agrega tipos de proceso para generar renovación, emitir recordatorios y evaluar suspensión. SQL conserva la fecha/clave de cada trabajo y el scheduler solo reclama; no se crea otro timer, tabla de jobs ni worker propietario. El outbox publica correo, fiscal, contabilidad, capacidad y sincronización después de cada commit.

Decisiones comerciales aún requeridas antes de activar cobros reales: confirmar si los precios publicados incluyen IVA, aprobar el precio del empleado de nómina adicional, identificar la empresa/NIT de Auraly que factura y aprobar el texto contractual de prorrateo, gracia, suspensión y no acumulación de documentos. Estas decisiones son datos versionados de catálogo/configuración; no se hardcodean.

## Experiencia

- Empresa: identidad legal.
- Sede: operación inicial.
- Plan: cards Esencial, Negocio, Empresa y Corporativo; “Ver detalles” abre modal accesible con tags de POS, facturación, contabilidad, nómina, soporte y capacidades.
- Adicionales: controles de cantidad y subtotal por concepto.
- Administrador: correo de invitación.
- Pago: selector anual/mensual con anual predeterminado; ahorro anual del 15 % destacado en porcentaje y COP, comparación transparente, resumen, impuestos, Wompi y estado; exención solo con permiso.

El borrador sobrevive recargas. El tenant ve capacidad propia y compra más; plataforma puede ver todas las sedes y editar contratos.

## Persistencia mínima

- borradores versionados de aprovisionamiento;
- precios versionados de plan y adicionales;
- cotizaciones y líneas inmutables;
- intentos de pago con referencia/transacción únicas y versión Wompi;
- suscripción de tenant;
- ajustes de entitlement con origen y vigencia;
- ledger de consumo idempotente.

Todos los índices y consultas posteriores al alta incluyen `TenantId`; acceso transversal exige permiso de plataforma.

## Rollout y evidencia

El despliegue es incremental con feature flag: esquema/catálogos, cotización, sandbox de plataforma, wizard pagado en dev, producción, migración de límites actuales, luego cupos vendedor/nómina/DIAN y alertas. El rollback desactiva el flag y conserva pagos, cotizaciones y ledger.

Pruebas obligatorias: cálculo servidor y manipulación rechazada; anual predeterminado, fórmula del 15 %, ahorro mostrado y total Wompi exacto; cambio mensual/anual y redondeos; renovación mensual frente a aniversario anual sin cobros intermedios; cupos mensuales reiniciados dentro de un anual sin factura adicional; factura anual única y factura mensual por pago; recaudo manual y Wompi convergen en el mismo settlement; permiso de plataforma, referencia manual duplicada, importe parcial y cobro ya pagado rechazados; concurrencia entre webhook y confirmación manual produce un pago aplicado, una suscripción y una factura; anulación no edita estados directamente; paquetes DIAN distintos de múltiplos de 1.000 rechazados; webhook/poller concurrentes; retorno sin aprovisionar; importe/moneda/referencia/cuenta incorrectos; exención; último cupo concurrente en cada recurso; factura, soporte y nómina bloqueados sin saldo; concesiones offline sin sobreasignación y bloqueo persistente al reconectar; reintento fiscal idempotente; aislamiento; avisos deduplicados; ampliación posterior al pago; rol vendedor limitado; recuperación de contraseña de un solo uso; landing/wizard en escritorio, teléfono y movimiento reducido.
