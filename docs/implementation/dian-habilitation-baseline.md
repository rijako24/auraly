# Línea base técnica DIAN para habilitación

Fecha de verificación: 2026-07-28 (America/Bogota).

## Alcance confirmado

La rebanada usa Factura Electrónica de Venta, anexo técnico 1.9 y UBL 2.1. La firma de documento es XAdES-EPES en servidor. En habilitación, el envío se realiza con el servicio oficial y el `TestSetId` asignado al software; el resultado puede requerir consulta posterior. Esta línea base no autoriza envíos a producción ni constituye por sí sola certificación legal.

## Fuentes oficiales

| Artefacto | Fuente DIAN | SHA-256 verificado |
|---|---|---|
| Portal de documentación técnica | https://micrositios.dian.gov.co/sistema-de-facturacion-electronica/documentacion-tecnica/ | Página dinámica; consultada 2026-07-28 |
| Anexo técnico FEV 1.9 | Descarga oficial enlazada por el portal técnico | `1B4022AC112232CD525A455432B2BDFC977D2EDCF14C1C7AA26F8BA93FE47DED` |
| Toolbox DIAN 2026 con XSD y ejemplos | Descarga oficial enlazada por el portal técnico | `2D6002D0A446ED9016CEB584CD18AE9B45EE532EB49D45CD999610D01B7266B3` |
| Guía oficial de servicios web | Descarga oficial enlazada por el portal técnico | `0F78626FBBA95700E178178369B187E39660878D5313859324E0F83A09471DF7` |
| Política de firma v2 | https://facturaelectronica.dian.gov.co/politicadefirma/v2/politicadefirmav2.pdf | `74CA0CBED706E5A233818A34B48B1241E5490439D49DF48E7C1A715EB9A8AF46` |
| Resolución 000165 de 2023 compilada | https://normograma.dian.gov.co/dian/compilacion/docs/resolucion_dian_0165_2023.htm | Página oficial consultada 2026-07-28 |
| Proceso de registro y habilitación | https://micrositios.dian.gov.co/sistema-de-facturacion-electronica/proceso-de-registro-y-habilitacion-como-facturador-electronico/ | Página oficial consultada 2026-07-28 |
| Inconvenientes tecnológicos | https://micrositios.dian.gov.co/sistema-de-facturacion-electronica/inconvenientes-tecnologicos/ | Página oficial consultada 2026-07-28 |

La huella SHA-256 codificada en Base64 de la política de firma usada por XAdES es `dMoMvtcG5aIzgYo0tIsSQeVJBDnUnfSOfBpxXrmor0Y=`.

## Decisiones aplicadas

- El CUFE monetario usa truncamiento a dos decimales, no redondeo.
- El XML se construye con APIs XML y namespaces explícitos; no se concatenan cadenas.
- Los XSD oficiales se incorporan como artefactos versionados y se usan realmente en pruebas.
- El certificado y la clave privada permanecen en el servidor.
- La firma XAdES cubre el documento y `SignedProperties`; usa SHA-256 y RSA-SHA256.
- La política de firma, el certificado, la hora de firma y el rol del firmante quedan dentro de `SignedProperties`.
- `SendTestSetAsync` y `GetStatusZip` son operaciones distintas: ante un timeout con posible recepción se consulta estado antes de retransmitir.
- El endpoint es configuración del ambiente entregada por DIAN. No se toma una URL copiada de ejemplos como valor normativo inmutable.

## Diferencias corregidas frente al diseño previo

1. El cálculo anterior de CUFE redondeaba valores monetarios; la regla oficial exige truncarlos.
2. Una factura local pendiente por falta de Internet no se etiqueta automáticamente como contingencia DIAN. La causa y el tratamiento deben clasificarse según la regla oficial aplicable.
3. El snapshot actual de la rebanada anterior no contiene por sí solo todos los datos obligatorios del UBL. Los documentos históricos incompletos pasarán a `MissingMandatoryFiscalData`; el servidor no inventará ni corregirá datos ya emitidos.
4. Generar XML válido no prueba habilitación real. La conectividad real solo se aprobará con software registrado, PIN, `TestSetId`, certificado y configuración entregados por DIAN.

## Artefactos temporales

Las descargas usadas para verificar hashes viven únicamente en `.tmp-dian-official` durante el desarrollo y no se versionan. Solo los XSD estrictamente requeridos por el validador forman parte del proyecto `Auraly.Fiscal.Ubl`.