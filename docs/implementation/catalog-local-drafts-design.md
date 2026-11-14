# Recuperación local de productos y terceros

Los formularios de creación y edición de productos y terceros conservan la captura incompleta en IndexedDB. La clave incluye tipo de formulario, usuario, negocio y, en edición, la entidad; por eso un borrador no puede aparecer en otro negocio, usuario o registro.

La recuperación local cubre los campos editables que todavía no se han confirmado, incluidas imágenes pendientes de productos, sedes, precios, impuestos, proveedores, cartera, horarios y roles. Las contraseñas de acceso, claves de autorización y sus confirmaciones nunca se escriben en IndexedDB y se dejan vacías al restaurar.

En la ficha de un hijo, el vínculo al producto principal es información vigente
del servidor, administrada desde la familia del principal. Restaurar un borrador
no repone un padre eliminado ni borra uno agregado después: conserva la captura
editable y toma `link` de la consulta canónica de merchandising. Si el vínculo
comparte inventario, el control propio aparece deshabilitado; al desvincularlo
queda editable, sin inventar un saldo ni activar inventario automáticamente.
Un vínculo sólo de costo no bloquea el inventario independiente. Recuperar el
borrador tampoco cambia el bloqueo vigente de costos, que pertenece a Pricing.

La regresión `admin/e2e/product-family-recovery.spec.ts` abre una recuperación
anterior al cambio de familia y repite la apertura después de recargar. Cubre
desvinculación, vínculo nuevo, vínculo conservado, costo vinculado con inventario
independiente y error de carga. Conserva nombre y descripción pendientes; prueba
que el inventario desvinculado se puede activar/desactivar y que el costo queda
editable aunque su origen histórico siga siendo `LinkedProduct`. Usa una lectura
de merchandising y una de precios por apertura, sin lecturas de reconciliación.
Si falla la consulta vigente, muestra el error y bloquea el guardado; el borrador
local no sustituye esa autoridad ni genera escrituras con el vínculo antiguo.

La comprobación con API y SQL reales `product-family-unlink-sql-regression`
aprobó 2/2: desvincula desde el guardado completo del principal y desde su
comando de merchandising. Después vuelve a leer ambos productos y Pricing,
comprueba que desaparecen el vínculo y el origen de costo vigente, y permite
editar inventario/costo propios del hijo sin cambiar las existencias del padre.
La desvinculación no activa inventario ni transfiere saldos automáticamente.
Los cinco escenarios de navegador forman parte de los 98/98 aprobados en
`accounting-critical-browser-regression`; esa evidencia visual se complementa
con estas dos pruebas reales, no la sustituye.

Una recarga, pulsar **Cerrar** o cerrar el diálogo con la X conserva el borrador. Guardar correctamente o pulsar **Cancelar** elimina la copia local. Un error del servidor conserva la captura para que la persona pueda corregirla o reintentar.

El borrador no es propietario de fechas técnicas. `CreatedAt`, `UpdatedAt`, `AcceptedAt` y los instantes equivalentes los genera el servidor durante la escritura o confirmación. Las fechas comerciales que la persona sí captura, como emisión, vencimiento o inicio de vigencia, se conservan como campos de negocio independientes. En recepción de compra, la interfaz vuelve a calcular `ReceivedAt` al confirmar; inventario envía `OccurredAt` al ejecutar la operación.

La implementación reutiliza un único store (`auraly-catalog-work/catalog-drafts`) y el hook `useCatalogDraft`. No crea tablas, endpoints, workers ni otra ruta de escritura del catálogo.

Recepciones de compra e inventario aplican la misma regla de cierre: la X y una recarga conservan la recuperación automática. **Descartar borrador** elimina la captura y **Confirmar** elimina la recuperación después de completar la operación. **Guardar borrador** es una acción explícita: en recepciones y conteos físicos utiliza el borrador canónico del servidor; en las demás operaciones de inventario conserva el documento en el almacenamiento durable del dispositivo hasta que exista un contrato servidor equivalente.
