# Preparación de la conexión directa con DIAN

Fecha de revisión: 2026-08-10.

## Estado de esta entrega

Auraly conserva una conexión directa con la DIAN, sin proveedor tecnológico como camino principal. El flujo está conectado desde la configuración del emisor hasta el motor fiscal durable:

1. La administración registra por `BusinessId` los datos del emisor, software, ambiente, `TestSetId`, endpoint y referencia segura del certificado.
2. La resolución fiscal y la clave técnica se administran por el propietario fiscal existente; no se duplican en la configuración del emisor.
3. El motor genera UBL 2.1 para el Anexo Técnico 1.9, valida, firma en el servidor y crea un intento durable.
4. El transporte ejecuta `SendTestSetAsync` y, cuando existe `ZipKey`, continúa mediante `GetStatusZip` sin renumerar ni crear otra factura.
5. La respuesta y los artefactos quedan asociados al documento original.

No se ejecutó una transmisión real en esta entrega. Tener los campos diligenciados significa que los datos requeridos fueron registrados; no demuestra que el software esté habilitado ni que la DIAN haya aceptado un set de pruebas.

## Referencia funcional tomada de Xion

Xion confirmó el flujo funcional directo `SendTestSetAsync -> ZipKey -> GetStatusZip` y la separación entre certificado, software, PIN, resolución, clave técnica y `TestSetId`. Auraly no copia su XML, sus datos quemados, su configuración ni sus secretos.

| Referencia histórica | Propietario canónico en Auraly |
| --- | --- |
| Ruta y contraseña de P12 | Certificado importado en un almacén seguro; la base conserva proveedor, almacén y huella |
| SoftwareId | `FiscalIssuerConfigurations.SoftwareIdentificationCode` |
| SoftwarePin | Secreto externo referenciado como `env://NOMBRE_VARIABLE` |
| TestSetId incorporado en código | Configuración versionada por negocio, nunca constante de código |
| Resolución y prefijo | `FiscalAuthorizations` |
| Clave técnica | `FiscalTechnicalKeySecrets`, cifrada y versionada |

## Datos que deben suministrarse juntos

Antes de una prueba real de habilitación deben verificarse como un conjunto coordinado:

- NIT y datos tributarios exactos del emisor.
- Software registrado ante la DIAN y su `SoftwareIdentificationCode`.
- PIN del mismo software, entregado al proceso mediante una variable segura.
- `TestSetId` vigente asignado al software y al modo de operación correspondiente.
- Certificado vigente, con clave privada y titular compatible con el emisor.
- Resolución/autorización de habilitación, prefijo, rango, vigencia y clave técnica aplicables.
- Endpoint oficial de habilitación y ambiente `2`.

No se deben combinar datos de empresas, certificados o sets distintos aunque individualmente parezcan válidos.

## Configuración segura

La vista **Maestros > Numeración y emisor fiscal** consume:

- `GET /api/commerce/v1/fiscal/configuration/issuer?businessId={businessId}`
- `PUT /api/commerce/v1/fiscal/configuration/issuer?businessId={businessId}`

Los endpoints exigen JWT, contexto autenticado del tenant y permisos `fiscal.configuration.read` o `fiscal.configuration.manage`. El backend comprueba que el negocio pertenece al tenant autenticado.

El PIN nunca se envía ni se persiste en el formulario: solo se registra una referencia `env://...`. El certificado permanece en `CurrentUser/My` o `LocalMachine/My`; Auraly guarda únicamente su huella. No se deben versionar PFX, PEM, contraseñas o valores del PIN.

## Secuencia cuando estén disponibles los datos

1. Importar el certificado en el almacén seguro del usuario que ejecuta Auraly.
2. Crear la variable segura referenciada por `SoftwarePinSecretReference`.
3. Registrar el emisor y el set de pruebas desde la vista administrativa.
4. Registrar la resolución y su clave técnica mediante el formulario fiscal existente.
5. Ejecutar builds y pruebas determinísticas.
6. Habilitar explícitamente el worker fiscal en un entorno controlado.
7. Ejecutar el set real de habilitación y conservar `ZipKey`, respuestas y evidencia.
8. Marcar conectividad real como aprobada únicamente si la DIAN confirmó el resultado.

## Fuentes oficiales

- Micrositio DIAN, documentación técnica del sistema de facturación electrónica: https://micrositios.dian.gov.co/sistema-de-facturacion-electronica/documentacion-tecnica/
- Micrositio DIAN, normatividad: https://micrositios.dian.gov.co/sistema-de-facturacion-electronica/normatividad/
- Resolución DIAN 000165 de 2023: https://normograma.dian.gov.co/dian/compilacion/docs/resolucion_dian_0165_2023.htm
- DIAN, proceso para ser facturador electrónico: https://www.dian.gov.co/impuestos/factura-electronica/como-hacerlo/Paginas/ser-facturador-electronico.aspx

La línea técnica vigente revisada para esta implementación es UBL 2.1 con Anexo Técnico de Factura Electrónica de Venta 1.9. Antes de cada prueba real debe volver a comprobarse la versión vigente y descargarse directamente de la DIAN el paquete técnico aplicable.
