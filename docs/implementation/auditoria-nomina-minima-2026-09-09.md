# Auditoría del módulo de nómina — 9 de septiembre de 2026

Alcance: hacer utilizables las funciones existentes, unificar su presentación con
Contabilidad y verificar la integración contable y fiscal. La decisión propietaria
continúa siendo [Nómina electrónica integrada](../decision-nomina-electronica-integrada.md).

## Recorridos y correcciones

| Sección | Evidencia y comportamiento revisado |
| --- | --- |
| Liquidaciones | Crear, calcular y aprobar; reglas que cubren todo el período; bloqueo de novedades ya consumidas y acuerdos modificados después del cálculo. |
| Trabajadores | Crear y reabrir contratos; consultas paginadas aisladas por tenant y sede; cambio de sede y actualización de la lista. |
| Novedades y descuentos | Crear acuerdos y novedades, exigir autorización cuando corresponde y conservar el beneficiario al desactivar un acuerdo. |
| Pagos | Confirmar una liquidación aprobada; identificador estable al reintentar; asiento balanceado mediante el motor contable existente. |
| Electrónica DIAN | Preparación mensual y estado por trabajador; conservación del software, PIN seguro y TestSet propios al editar la serie; snapshot laboral para nuevos cálculos. |
| Reportes | Generación desde el catálogo nativo; diez reportes contrastados con las cifras de la liquidación y del pago en SQL Server. |
| Configuración | Crear conceptos y reglas, aprobar reglas, permisos de edición y enlace a centros de costo y configuración fiscal existentes. |

El encabezado, las tarjetas y las siete pestañas siguen la presentación de
Contabilidad. Las pestañas funcionan con teclado y en móvil. Los errores de carga
del workspace y de contratos tienen reintento y no aparecen como listas vacías.

## Centros de costo

La causación, los ajustes y los pagos ya entran por
`AccountingProcessingCoordinator` y `SqlAccountingPostingProcessor`. Para nómina,
el resolvedor existente utiliza la asignación `All` sin bodega o el centro activo
predeterminado de la sede. Cuando existe asignación, las partidas conservan el
centro y su código y nombre históricos. El auxiliar contable se filtra por centro.

La prueba vertical comprueba el centro tanto en causación como en pago, consulta
el auxiliar y vuelve a aprobar con la misma clave después de renombrar el centro:
se conserva el asiento original. La clasificación requiere la asignación o el
predeterminado en Contabilidad; esta auditoría no crea centros en datos operativos.

## Auditoría posterior del cambio

- Propietarios: las escrituras laborales siguen en `SqlPayrollStore`; los asientos
  se escriben únicamente por el motor contable y la transmisión sigue en el motor
  fiscal. Se reutilizan tablas, colas, contratos y catálogos existentes.
- Aislamiento y permisos: claves de caché y formularios por tenant y sede; sin
  permiso de lectura no se consulta el módulo. La autorización del servidor se
  conserva. No se exponen PIN, certificados ni datos personales en la nueva alerta.
- Concurrencia e idempotencia: aprobación serializable, bloqueo de novedades y
  acuerdos, conservación de rowversion y claves existentes. Los conflictos de
  aprobación revierten la transacción antes de crear fuentes contables o consumos.
- Compatibilidad: el snapshot laboral se amplía dentro del JSON existente. La
  excepción para registros antiguos está documentada en la decisión propietaria y
  emite `PayrollLegacyEmployeeSnapshot`; no se reconstruyen datos históricos.
- Dependencias: se añade únicamente la abstracción de logging 8.0.3 ya utilizada
  en la solución, necesaria para hacer observable esa excepción.
- Despliegue: los nuevos catálogos de tipos y etiquetas se publican con
  `SeedPayrollCatalogs.sql` antes de utilizar la nueva versión del admin/backend.
  Las adiciones son compatibles con la versión anterior y no modifican asientos.

## Evidencia reproducible

- `admin/e2e/payroll-workspace.spec.ts`: ocho recorridos de interfaz, con respuestas
  API controladas; cubre creación, edición, permisos, errores, sedes y móvil.
- `PayrollVerticalSliceTests`: API real y SQL Server en base aislada; descarta reglas
  parcialmente vigentes y descuentos no autorizados, verifica centros, balance,
  replay, reportes y salario histórico al consolidar después de editar el contrato.
- Foundation: nómina, XML/XSD/CUNE, transporte fiscal, políticas contables y
  restricciones de arquitectura.
- Compilación de solución y DACPAC, `npm run build`, `npm run lint` y
  `npm run test:pos` como comprobaciones generales.

Resultado ejecutado: ocho recorridos Playwright aprobados; prueba vertical con
SQL Server aprobada; 113 pruebas Foundation aprobadas, sin omisiones; 185 pruebas
del conjunto POS aprobadas. Solución .NET y DACPAC Release compilan sin errores
ni advertencias; el build de producción del admin y su validación TypeScript
terminan correctamente. Lint: cero errores y nueve advertencias previas en
componentes ajenos al cambio. `git diff --check` sin errores.

Estas pruebas no representan un envío real ni aceptación por la DIAN. La puesta
en operación requiere el despliegue y una validación con la configuración fiscal
habilitada. Los períodos antiguos sin snapshot laboral completo requieren revisión
antes de transmitir; los cálculos nuevos conservan sus datos históricos.
