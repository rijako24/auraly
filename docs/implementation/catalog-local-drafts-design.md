# Recuperación local de productos y terceros

Los formularios de creación y edición de productos y terceros conservan la captura incompleta en IndexedDB. La clave incluye tipo de formulario, usuario, negocio y, en edición, la entidad; por eso un borrador no puede aparecer en otro negocio, usuario o registro.

La recuperación local cubre los campos editables que todavía no se han confirmado, incluidas imágenes pendientes de productos, sedes, precios, impuestos, proveedores, cartera, horarios y roles. Las contraseñas de acceso, claves de autorización y sus confirmaciones nunca se escriben en IndexedDB y se dejan vacías al restaurar.

Una recarga, pulsar **Cerrar** o cerrar el diálogo con la X conserva el borrador. Guardar correctamente o pulsar **Cancelar** elimina la copia local. Un error del servidor conserva la captura para que la persona pueda corregirla o reintentar.

El borrador no es propietario de fechas técnicas. `CreatedAt`, `UpdatedAt`, `AcceptedAt` y los instantes equivalentes los genera el servidor durante la escritura o confirmación. Las fechas comerciales que la persona sí captura, como emisión, vencimiento o inicio de vigencia, se conservan como campos de negocio independientes. En recepción de compra, la interfaz vuelve a calcular `ReceivedAt` al confirmar; inventario envía `OccurredAt` al ejecutar la operación.

La implementación reutiliza un único store (`auraly-catalog-work/catalog-drafts`) y el hook `useCatalogDraft`. No crea tablas, endpoints, workers ni otra ruta de escritura del catálogo.

Recepciones de compra e inventario aplican la misma regla de cierre: la X y una recarga conservan la recuperación automática. **Descartar borrador** elimina la captura y **Confirmar** elimina la recuperación después de completar la operación. **Guardar borrador** es una acción explícita: en recepciones y conteos físicos utiliza el borrador canónico del servidor; en las demás operaciones de inventario conserva el documento en el almacenamiento durable del dispositivo hasta que exista un contrato servidor equivalente.
