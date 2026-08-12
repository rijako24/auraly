# Digital Shop: propiedad de reglas

| Regla | Propietario |
| --- | --- |
| Identidad de Catalina y tono de vendedora | `persona` y `conversationOpening` |
| Direccion y horario reales del local | plantilla autoritativa `store_location` |
| Distinguir ubicacion de servicio tecnico | accion global `store_location`, señal `store_location_requested` y prioridad superior a `technical_service` |
| Bienvenida inicial breve | `conversationOpening.enabled`, `allowQuestions=false` y `guidance`; la primera pregunta pertenece a la etapa `discover` |
| Pregunta natural para escoger nuevo o usado | guía de la etapa `discover` y opciones canónicas A/B de `product_condition`, como en CJ |
| Respuestas breves, estructuradas y con emojis ocasionales | `persona`; las etapas y acciones son dueñas del contenido de cada respuesta |
| Recomendacion humana, contextual y sin frases prefabricadas | `persona`, `policies`, historial reciente y resultados autoritativos |
| Comparar explícitamente dos modelos distintos | acción global `compare_two_iphone_models` y señal `two_iphone_models_comparison_requested` |
| Ficha técnica por modelo | `Products.Description`, alimentada por el catálogo del tenant |
| Render de cualquier producto actual o futuro | plantillas genéricas `new_product_offers` y `used_product_offers`, con `{{product_name}}`, `{{unit_price}}` y `{{description}}`; no existen plantillas por referencia |
| Especificaciones usadas para recomendar | resultados autoritativos de `commerce.search_products` y plantilla `technical_comparison_model` |
| Precios de los dos modelos bajo la misma condición | `commerce.search_product_offers`, condicionado por `product_condition` |
| Resolver todo modelo diferente pedido despues del primero | `product_condition` se vuelve a solicitar si el cliente no la indica; con condición explícita, la acción global `compare_different_iphone_models` consulta de inmediato, compara automáticamente solo el segundo y presenta individualmente los siguientes |
| Comparar el mismo modelo como nuevo y usado | acción global `compare_new_and_used`; su señal excluye mensajes con dos modelos distintos |
| Cambiar nuevo/usado sin conservar el precio anterior | acciones globales `switch_used_to_new` y `switch_new_to_used` |
| Recomendar cargador compatible sin inventarlo | `ProductRecommendationRules`, acción `recommend_charger_after_phone` y plantilla `phone_accessory_recommendation` |
| Máximo una pregunta cuando falta un dato | guía de la etapa `discover`; no se duplica en la política global |
| Venta de cargadores y cables | acción global `accessory_sales`, catálogo y `accessory_product_offers` |
| Únicos teléfonos vendidos: iPhone; listado general de modelos | acción global `iphone_catalog_sales`, `commerce.search_products` y `iphone_model_catalog` |
| Responder sólo colores ante una consulta puntual | acción global `show_product_colors` y plantilla `product_color_options` |
| Modelo solicitado y condición nuevo/usado | `factSchema` |
| La condición pertenece a cada modelo y no se hereda al cambiar de iPhone | `product_condition.dependsOn=["device_model"]`; una condición explícita en el mismo mensaje se conserva por la mutación atómica del motor |
| Volver a preguntar la condición al cambiar de modelo | `discover.reentryOnFactChanged=["device_model"]`; `quote.reentryOnFactChanged` limpia el checkpoint `offer_presented` y el selector vuelve a la primera etapa incompleta |
| Capacidad sin sobrescribir el modelo | fact `storage_gb` y guía de extracción |
| Precio, capacidad, variante, batería mínima y vigencia | `ProductOffers` |
| Varias imágenes por producto u oferta | `ProductImages` |
| Enviar una imagen principal por producto consultado, si existe | efectos de `commerce.search_product_offers` |
| Formato y alojamiento de imagenes | PNG real del modelo en `business-d1617a10-0000-0000-0000-000000000010`, referenciado por `ProductImages.MediaUrl` |
| Garantía de equipos nuevos directamente con la marca | `policies` y plantillas de ofertas nuevas |
| Batería usada superior al 90%; valor exacto en tienda | política y datos de `ProductOffers` |
| Servicio técnico en el local | acción global `technical_service` y plantilla `technical_service_local` |
| Terminar la venta en el local | etapa `visit` y `request.complete` |
| Actualizar precios desde texto, PDF o imagen | agente `Operaciones Digital Shop` |
| Interpretar listas y rechazar ambigüedades | `internal.update_product_offer_prices` |
| Retomas empáticas | `conversationFollowUp` por agente y renderer determinista |

La identidad, las especificaciones, los precios, las imágenes, la disponibilidad y la
salud mínima de batería permanecen en catálogo. Las señales sólo seleccionan la
operación adecuada: una comparación entre modelos nunca cambia de eje para comparar
condiciones, y la condición vigente se aplica de forma idéntica a ambos precios.
