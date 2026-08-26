# Conteo físico y consolidación de inventario

Fecha: 2026-08-25

Estado: diseño cerrado para implementación

## Decisiones cerradas

1. Cada producto debe pasar por `Preconteo` y después por `Conteo`. No existe
   conteo directo ni una opción para omitir el preconteo.
2. La captura de una persona es un recorrido de dos etapas en una sola
   superficie. No tiene pestañas de contados, pendientes o conflictos.
3. `Consolidar inventario` es una acción independiente ubicada junto a
   `Nueva operación` en el encabezado de Inventario.
4. Las pestañas existen únicamente dentro de la experiencia de consolidación.
5. Cada usuario puede guardar varios borradores con nombre, tanto durante el
   preconteo como durante el conteo.
6. La consolidación muestra los borradores guardados por todos los usuarios de
   la misma sesión, no sólo los del usuario actual.
7. Vacío significa `Pendiente de conteo`; cero es una cantidad contada válida y
   explícita.
8. Guardar, consolidar y aplicar inventario son acciones distintas. Guardar y
   consolidar nunca modifican existencias.
9. El único efecto definitivo es un documento canónico `StockCount`, aceptado y
   procesado por el motor ordenado.

## Qué significa captura lineal

`Lineal` no significa una pantalla rígida ni que el usuario deba terminar todo
de una vez. Significa que el trabajo individual tiene un orden inequívoco:

```text
Preconteo  ->  Guardar borrador  ->  Conteo  ->  Guardar o finalizar
```

El usuario puede guardar, salir, reabrir y volver a una etapa anterior. Lo que
no puede hacer es registrar el conteo final de un producto que no tenga antes
un preconteo en esa misma captura.

Las pestañas `Capturas`, `Productos contados`, `Pendientes de conteo` y
`Conflictos` no ayudan a capturar; ayudan a coordinar el trabajo de varias
personas. Por eso aparecen sólo al abrir `Consolidar inventario`.

## Vocabulario visible

| Concepto | Nombre en la interfaz | Definición |
|---|---|---|
| Jornada o alcance común | Inventario físico | Conteo abierto para una bodega y un alcance congelado. |
| Trabajo individual | Captura | Preconteo y conteo realizados por una persona. |
| Avance editable | Borrador | Captura guardada que se puede reabrir. |
| Unión controlada | Consolidación | Revisión conjunta de capturas seleccionadas. |
| Producto sin conteo final | Pendiente de conteo | No tiene una cantidad final; nunca equivale a cero. |
| Resultados incompatibles | Conflicto | Dos o más conteos finales comparables no coinciden. |

Se eliminan del lenguaje visible `lista`, `equipo` y `no contado`.

## Navegación

El encabezado de Inventario tendrá estas acciones hermanas:

```text
Inventario                         [Nueva operación] [Consolidar inventario · 3]
```

- `Nueva operación` conserva `Conteo físico` como uno de sus tipos. Elegirlo
  permite iniciar un inventario físico, unirse a uno abierto o continuar un
  borrador propio.
- `Consolidar inventario` abre el espacio de coordinación de todos los
  inventarios físicos abiertos. El indicador muestra cuántos requieren
  atención.
- `Operaciones > Inventarios` muestra únicamente el historial inmutable de los
  documentos `StockCount` aplicados. No contiene captura ni consolidación.

No habrá un tercer centro global llamado `Conteos físicos` ni una pestaña de
consolidación dentro de `Nueva operación`.

## Inicio o continuación

Al elegir `Conteo físico` desde `Nueva operación`, el usuario puede:

1. `Continuar borrador`: muestra sólo sus borradores editables.
2. `Unirme a inventario abierto`: crea una captura personal dentro de una
   sesión compatible.
3. `Iniciar inventario físico`: crea la sesión común y su primera captura.

Crear una sesión solicita nombre, bodega, alcance `General` o `Parcial`, motivo
y observaciones opcionales. Si es parcial, también solicita categorías o
productos.

El alcance se congela al crear la sesión. Un inventario general incluye todos
los productos activos que manejan inventario, incluso los de saldo cero. Los
productos que no manejan inventario permanecen visibles en la consulta general
de existencias con esa indicación, pero no generan líneas de `StockCount`.

No se mezclan capturas de bodegas, sesiones o alcances distintos.

## Conteo: una sola superficie de dos etapas

La captura tiene un indicador de etapa, no pestañas:

```text
Conteo físico · Bodega principal
Inventario general · Captura “Pasillo norte”

1 Preconteo  ─────────  2 Conteo

[Buscar o escanear producto]
[tabla de la etapa actual]

[Guardar borrador]                         [Continuar al conteo]
```

El encabezado, buscador, filtros y tabla permanecen en el mismo lugar. En móvil
se conserva el foco de escaneo y se muestra una tarjeta por producto; en
escritorio se usa una tabla compacta con captura por teclado.

### Etapa 1: Preconteo obligatorio

- El usuario busca, escanea o recorre los productos del alcance.
- Registra una cantidad de preconteo por producto.
- Puede guardar el borrador en cualquier momento, aunque esté incompleto.
- `Continuar al conteo` cambia a la segunda etapa.
- Pasar de etapa no inventa cantidades para los faltantes. Un producto sólo se
  puede contar en la segunda etapa si ya tiene preconteo en esa captura.
- Los productos sin preconteo permanecen pendientes y se completan al volver a
  la primera etapa.

### Etapa 2: Conteo obligatorio

- El usuario realiza una segunda captura física para cada producto precontado.
- No ve la existencia del sistema, cantidades de otros usuarios ni su propio
  preconteo mientras registra el conteo final.
- Puede guardar el borrador y reabrirlo en esta etapa.
- `Finalizar captura` declara que terminó su trabajo. No aplica inventario.
- Si se corrige un preconteo después de registrar el conteo final, se invalida
  ese conteo final y se exige contar el producto otra vez. La acción queda
  auditada.

No existe estado, endpoint ni botón que cree una cantidad final sin un
preconteo previo para el mismo `CaptureId + ProductId`.

### Guardado visible

Durante ambas etapas se muestran siempre:

- nombre editable de la captura;
- `Guardar borrador`;
- estado `Cambios sin guardar`, `Guardando…` o `Guardado hace …`;
- progreso separado `precontados / alcance` y `contados / alcance`;
- buscador por nombre, referencia, código o código de barras;
- acción `Dejar pendiente` con motivo.

Los motivos iniciales son `No localizado`, `Sin acceso`, `Requiere
verificación` y `Otro`. Explican una omisión temporal, pero no excluyen el
producto ni lo convierten en cero.

Cada guardado usa concurrencia optimista. Si la captura cambió desde otra
ventana, no se sobrescribe: se permite recargar o guardar los cambios como una
captura nueva.

## Consolidar inventario

El botón del encabezado abre una vista amplia o un panel de pantalla completa,
no una pestaña de Conteo. Primero lista los inventarios físicos abiertos con
bodega, alcance, usuarios, borradores, avance, pendientes, conflictos y última
actividad.

Al seleccionar uno, aquí sí aparecen las pestañas:

```text
Consolidar inventario · Bodega principal
Inventario general · 4 usuarios · 6 borradores

[Capturas] [Productos contados] [Pendientes de conteo] [Conflictos]
```

### Capturas

Muestra todos los borradores de todos los usuarios de la sesión: nombre, autor,
etapa, productos precontados y contados, estado y última modificación.

El administrador selecciona las capturas y usa `Preparar consolidado`. Por
defecto se seleccionan las finalizadas. Un borrador en progreso también se puede
incluir, pero requiere decisión explícita y sus líneas incompletas quedan
pendientes. Una captura que sólo tiene preconteos no aporta cantidades finales.

`Preparar consolidado` congela las versiones seleccionadas. Los cambios
posteriores no alteran silenciosamente la revisión; para incluirlos se actualiza
el consolidado y se vuelven a validar sus resoluciones.

### Productos contados

Muestra una fila por producto con cantidad propuesta, capturas y autores,
coincidencias, movimientos ocurridos durante la captura, existencia comparable,
diferencia y valoración.

La vista permite alternar entre `Precio costo` y `Costo promedio` y muestra el
valor total de sobrantes, faltantes y efecto neto. El selector sólo cambia la
valorización, no las cantidades.

### Pendientes de conteo

Incluye los estados `Sin preconteo`, `Precontado sin conteo final`, `Captura en
progreso no seleccionada`, `Dejado pendiente` y `Recuento solicitado`.

Desde aquí se puede `Crear captura de pendientes` o `Solicitar recuento`. Ambas
acciones crean una captura limitada a esos productos y vuelven a exigir
preconteo y conteo. El administrador puede excluir un producto con motivo
obligatorio, pero nunca convertir pendientes masivamente en cero.

### Conflictos

Muestra lado a lado cantidad, usuario, captura, hora y movimientos usados para
normalizar cada resultado. Las opciones son:

- `Usar este conteo`;
- `Registrar cantidad verificada`, con nota obligatoria;
- `Solicitar recuento`, que vuelve a exigir preconteo y conteo.

La decisión queda auditada con usuario, fecha, método, fuentes y observación.

## Regla de unión y conflictos

La consolidación produce una fila por `ProductId`. Las cantidades de capturas
distintas no se suman: hoy cada cantidad representa el total físico del producto
en la bodega, no una porción asignada.

Antes de comparar resultados hechos en momentos distintos, cada conteo final se
normaliza a una misma secuencia:

```text
cantidad comparable = conteo físico final
                    + entradas procesadas después del conteo
                    - salidas procesadas después del conteo
                    hasta el snapshot de consolidación
```

| Evidencia seleccionada | Resultado |
|---|---|
| Un conteo final elegible | Se propone su valor normalizado. |
| Varios conteos finales iguales | Coincidencia automática; se conservan las fuentes. |
| Varios conteos finales distintos | Conflicto obligatorio. |
| Sólo existen preconteos | Pendiente de conteo final. |
| No existe captura | Pendiente sin captura. |

Nunca se usa `el último gana`. Sólo se podrán sumar capturas cuando exista una
dimensión formal que pruebe que corresponden a ubicaciones distintas. En ese
caso la llave será `bodega + ubicación + producto + lote/serie`.

## Guardar, consolidar y aplicar

| Acción | Resultado | ¿Modifica inventario? |
|---|---|---|
| Guardar borrador | Persiste el avance editable. | No |
| Preparar consolidado | Congela capturas y calcula resultados. | No |
| Resolver consolidado | Registra decisiones auditables. | No |
| Aplicar inventario | Genera y acepta un único `StockCount`. | Sí, mediante el motor ordenado |

`Aplicar inventario` permanece deshabilitado hasta que no existan conflictos,
cada producto tenga cantidad resuelta o exclusión aprobada, las versiones del
snapshot sigan vigentes y el usuario tenga permiso de confirmación.

Al aplicar, se incorporan los movimientos posteriores al snapshot y se acepta
de forma idempotente un solo `StockCount`. El handler existente actualiza
`InventoryBalances`, escribe `InventoryMovements` y cierra el inventario físico.

## Estados

### Inventario físico

`Abierto -> En consolidación -> Aplicando -> Cerrado`

También existe `Cancelado`. Volver a captura invalida el snapshot y exige
preparar uno nuevo.

### Captura

`Borrador de preconteo -> Borrador de conteo -> Finalizada`

También puede quedar `Descartada` con motivo. Siempre inicia en preconteo.

### Consolidación

`Preparando -> En revisión -> Aplicada`

También puede quedar `Invalidada` al incorporar versiones nuevas.

## Modelo canónico objetivo

- `InventoryPhysicalCounts`: sesión, bodega, alcance, estado y auditoría.
- `InventoryPhysicalCountScopeProducts`: productos congelados y saldo base.
- `InventoryPhysicalCountCaptures`: autor, nombre, etapa, estado y versión.
- `InventoryPhysicalCountCaptureLines`: preconteo, conteo final, fechas,
  secuencias y motivo pendiente por captura.
- `InventoryPhysicalCountConsolidations`: snapshot y versiones seleccionadas.
- `InventoryPhysicalCountResolutions`: resultado, exclusión, fuentes y
  auditoría.

La base de datos impone que una línea final no exista sin preconteo. La llave
permite que capturas distintas registren el mismo producto.

Las tablas actuales de listas y líneas se migran en un único cutover. No quedan
dos coordinadores activos ni una ruta legacy paralela.

## Cutover

1. Introducir sesiones, alcance, capturas, snapshots y resoluciones con pruebas
   de concurrencia e idempotencia.
2. Migrar cada lista vigente a una captura sin inventar cantidades ausentes.
3. Reemplazar la captura actual por el recorrido obligatorio de dos etapas.
4. Agregar `Consolidar inventario` junto a `Nueva operación` y ubicar allí las
   pestañas de coordinación.
5. Eliminar contratos basados en `Lists` en el mismo cutover funcional.
6. Certificar aceptación antes de retirar componentes y endpoints legacy.

## Permisos

- `inventory.physical-counts.capture`: crear y editar capturas propias.
- `inventory.physical-counts.manage`: crear sesiones, ver capturas, preparar
  consolidaciones, resolver, excluir y cancelar.
- `inventory.counts.confirm`: aplicar y generar `StockCount`.
- `inventory.costs.read`: ver costos y valorización.

Todos los recursos se validan contra el `BusinessId` y tenant autenticados.

## Criterios de aceptación

1. No existe ruta de UI, API o persistencia para un conteo final sin preconteo.
2. Se puede guardar y reabrir un preconteo incompleto.
3. Se puede guardar y reabrir un conteo incompleto.
4. La captura no contiene pestañas de coordinación.
5. `Consolidar inventario` está junto a `Nueva operación`.
6. La consolidación muestra borradores de todos los usuarios de la sesión.
7. Conteos iguales coinciden; conteos distintos bloquean hasta resolver.
8. Cero es contado y vacío es pendiente.
9. Dejar pendiente no excluye ni convierte en cero.
10. Una captura de pendientes vuelve a exigir las dos etapas.
11. Los movimientos de la jornada se normalizan sin perder operaciones.
12. Se valoriza por precio costo o costo promedio.
13. El cierre genera exactamente un `StockCount` y es idempotente.
14. El conteo final es ciego a existencia, preconteo y capturas ajenas.

## Contraste con Xion local

La referencia revisada es `C:\Proyectos\XiOn`:

- `FrmInventario` tiene `Guardar`, una consulta de `Pendientes`, un botón de
  `Reconteo` y una acción separada `Consolidar Inventario`.
- `Guardar` persiste el inventario temporal sin aplicarlo; después se puede
  buscar y modificar.
- `FrmInventarioGeneralConsolidado` consulta por bodega y fecha, muestra los
  registros de varios equipos, el detalle consolidado y métricas de contados y
  no contados antes de `Actualizar`.
- El repositorio agrupa por producto y suma `ConteoFinal` de los equipos.
- Xion usa `Conteo` y un `Reconteo` opcional; el valor final es el reconteo si
  existe y, si no, el primer conteo.

Se conserva de Xion la separación entre guardar, consolidar y actualizar; la
visibilidad del trabajo distribuido; y el control explícito de pendientes. Se
mejora con borradores por usuario y nombre, dos etapas obligatorias, snapshot,
conflictos auditables y pendientes sin equivalencia implícita a cero.

No se copia la suma automática: Xion identifica aportes por equipo y asume que
son porciones acumulables. Auraly no tiene todavía ubicación o sector como
dimensión verificable, por lo que sumar dos capturas del mismo producto podría
duplicar inventario.
