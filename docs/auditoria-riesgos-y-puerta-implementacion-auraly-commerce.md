# Auditoría de riesgos y puerta de implementación de Auraly Commerce

**Fecha:** 27 de julio de 2026  
**Rama:** `design/auraly-commerce-mvp`  
**Decisión:** `GO CONDICIONADO`

**Alcance revisado:** diseño general, POS, caja, pedidos, catálogo, precios, inventario, compras, CxC, CxP, devoluciones, averías, conversión, reportes, motor documental, sincronización, facturación electrónica, seguridad y despliegue Cloud/On-Premise.

---

## 1. Dictamen ejecutivo

La arquitectura es viable y la decisión de construir Auraly Commerce dentro de Auraly como monolito modular es correcta.

No conviene iniciar todavía todos los módulos en paralelo. Sí se puede empezar inmediatamente:

1. la Fase 0 ejecutable;
2. el renombrado controlado a Auraly;
3. las pruebas de arquitectura;
4. el proyecto SQL como autoridad del esquema;
5. `ReferenceData`, `Parties`, `Identity` y `Authorization`;
6. un primer corte vertical online de catálogo, precio, bodega, caja, venta y motor;
7. pruebas técnicas aisladas de DIAN y POS Edge.

La implementación completa queda condicionada a cerrar cinco puertas:

| Puerta | Condición de salida |
|---|---|
| G1. Especificación canónica | Una matriz vigente de alcance, reglas y precedencias, sin contradicciones |
| G2. Perfil fiscal del piloto | Documento, impuestos, contingencias, resolución y casos DIAN definidos |
| G3. Contrato del motor | Estados, atomicidad, idempotencia, reintentos y reversión por documento |
| G4. Prueba POS Edge/offline | Catálogo, periféricos, outbox, actualización y recuperación demostrados |
| G5. Matriz financiera | Cálculo y efectos de venta, devolución, entrada, cartera, costo y conversión conciliados |

```text
Arquitectura general                         GO
Fundaciones y primer corte vertical online  GO
Implementación paralela de todo el ERP       NO-GO temporal
Piloto comercial online                      GO tras G1, G2, G3 y G5
Piloto con promesa offline                   GO tras G4 y pruebas de caos
Salida fiscal productiva                     GO tras habilitación DIAN
On-Premise comercial                         GO tras certificar instalación y restauración
```

No hace falta rediseñar la plataforma. Hace falta convertir el diseño en contratos ejecutables antes de multiplicar código.

---

## 2. Fortalezas del diseño

- monolito modular antes de microservicios;
- una API y una base SQL para el MVP;
- librerías `Domain`, `Application`, `Infrastructure` y `Contracts` por contexto;
- proyecto SQL/DACPAC como autoridad del esquema;
- motor servidor como autoridad sobre documentos definitivos;
- transacciones cortas y outbox para efectos externos;
- IDs internos separados de consecutivos y números fiscales;
- idempotencia para operaciones confirmables;
- catálogo y precios locales en POS, sin inventario local;
- snapshot inicial, deltas, revisiones y checkpoints;
- validación online al capturar cuando la bodega bloquea negativos;
- revalidación dentro de la transacción final;
- política de negativos por bodega heredada por sus cajas;
- pedidos online protegidos contra doble facturación;
- permisos en API y limitados por alcance;
- reversión en vez de eliminar documentos confirmados;
- reportes conciliables;
- adaptadores Cloud y On-Premise;
- pruebas como condición de terminación;
- Xion como conocimiento y oráculo de comportamiento, no código para copiar.

---

## 3. Registro priorizado

Escala: probabilidad e impacto de 1 a 5; nivel = probabilidad × impacto.

| ID | Riesgo | P | I | Nivel | Puerta |
|---|---|---:|---:|---:|---|
| R01 | Documentos vigentes se contradicen | 5 | 5 | 25 Crítico | G1 |
| R02 | El MVP intenta salir como ERP completo en un corte | 5 | 5 | 25 Crítico | G1 |
| R03 | Alcance fiscal no limitado a un perfil real | 4 | 5 | 20 Crítico | G2 |
| R04 | Cambios DIAN dejan obsoleto el diseño | 4 | 5 | 20 Crítico | G2 |
| R05 | Offline comercial se confunde con expedición fiscal | 4 | 5 | 20 Crítico | G2/G4 |
| R06 | Atomicidad ambigua entre API y Worker | 4 | 5 | 20 Crítico | G3 |
| R07 | Reintentos duplican efectos | 3 | 5 | 15 Alto | G3 |
| R08 | Fórmulas difieren entre POS, API, XML, PDF y reportes | 4 | 5 | 20 Crítico | G5 |
| R09 | Reversos dejan efectos parciales | 4 | 5 | 20 Crítico | G3/G5 |
| R10 | POS Edge cuesta más de lo estimado | 4 | 4 | 16 Crítico | G4 |
| R11 | Push se vuelve dependencia dura | 3 | 4 | 12 Alto | G4 |
| R12 | Dos cajas venden la última existencia | 4 | 4 | 16 Crítico | G3 |
| R13 | Precio o impuesto local desactualizado | 3 | 5 | 15 Alto | G4/G5 |
| R14 | Fuga multitenant o fuera de alcance | 3 | 5 | 15 Alto | G1 |
| R15 | Permisos offline sobreviven una revocación | 3 | 5 | 15 Alto | G4 |
| R16 | Un `DbContext` erosiona fronteras | 4 | 4 | 16 Crítico | G1 |
| R17 | Renombrado rompe funcionalidad actual | 4 | 4 | 16 Crítico | G1 |
| R18 | DACPAC causa cambios destructivos o deriva de EF | 3 | 5 | 15 Alto | G1 |
| R19 | Migración Xion no concilia saldos | 4 | 5 | 20 Crítico | G5 |
| R20 | Costo promedio/utilidad incorrectos | 4 | 5 | 20 Crítico | G5 |
| R21 | Conversión crea o destruye valor sin explicación | 3 | 5 | 15 Alto | G5 |
| R22 | CxC/CxP no concilian con documentos | 4 | 5 | 20 Crítico | G5 |
| R23 | Reportes degradan tablas operativas | 4 | 4 | 16 Crítico | G1 |
| R24 | On-Premise se vuelve instalación artesanal | 4 | 4 | 16 Crítico | G4 |
| R25 | Pruebas no representan producción | 4 | 4 | 16 Crítico | Todas |
| R26 | No existe envolvente de capacidad | 5 | 4 | 20 Crítico | G1/G4 |
| R27 | Dependencias externas detienen operación | 3 | 5 | 15 Alto | G3/G4 |
| R28 | Exposición de datos, certificados o XML | 3 | 5 | 15 Alto | G2/G4 |
| R29 | Errores se detectan tarde | 3 | 4 | 12 Alto | G3 |
| R30 | Funcionalidades excluidas reaparecen | 5 | 4 | 20 Crítico | G1 |

---

## 4. Alcance y gobernanza

### R01. Contradicciones

Se encontraron decisiones históricas todavía redactadas como vigentes:

- negativos por caja frente a la decisión final por bodega;
- validar inventario al confirmar frente a validar al capturar y revalidar al confirmar;
- esquema y `DbContext` por módulo frente a `dbo` y un `AuralyDbContext`;
- migraciones EF frente al proyecto SQL como única autoridad;
- POS sin socket en el MVP frente a Web PubSub desde el MVP;
- lotes, seriales, retenciones y fletes presentes en flujos donde ya no aplican;
- Conversión excluida en la auditoría anterior, pero incluida posteriormente.

El índice de prevalencia ayuda, pero obliga a cada desarrollador a reconstruir la regla.

Mitigación:

- especificación canónica con `In`, `Later` y `Out`;
- glosario;
- estados y efectos por documento;
- propietario de cada tabla;
- marcar secciones históricas como `SUPERSEDED`;
- enlazar cada historia a su regla vigente.

### R02. MVP demasiado grande

El alcance ya es un ERP comercial. Debe liberarse por cortes:

1. identidad, permisos, empresa, sede, bodega, caja y catálogo;
2. producto → precio → venta → pago → inventario → arqueo;
3. venta → documento fiscal → entrega → nota;
4. entrada → costo → inventario → CxP → pago;
5. venta a crédito → CxC → abono;
6. devolución/reversión → inventario → caja/cartera → fiscal;
7. conteo, traslado, avería y conversión;
8. offline certificado;
9. On-Premise certificado.

Cada corte debe ser ejecutable y conciliado.

### R30. Exclusiones que reaparecen

| Capacidad | Estado final |
|---|---|
| Balanza | MVP |
| Devoluciones de venta y compra | MVP |
| Conversión `Split`/`Merge` | MVP |
| Canales de precio | MVP |
| Lotes y seriales | Fuera |
| Producción | Fuera |
| Redondeo comercial configurable | Fuera |
| Retenciones | Fuera |
| Fletes y descargues | Fuera |
| Cotizaciones, remisiones y apartados | Fuera |
| Domicilios, puntos, bonos especializados y cheques posfechados | Fuera |

Los redondeos técnicos de dinero e impuestos siguen siendo obligatorios. Lo excluido es configurar políticas comerciales de redondeo.

---

## 5. Fiscal y DIAN

La arquitectura fiscal propuesta sigue siendo válida: software propio, habilitación, numeración, UBL, firma, CUFE/CUDE, validación, notas, contingencia, entrega y almacenamiento.

El riesgo está en congelar la normativa. El diseño no debe basarse solo en la Resolución 000165 de 2023. La DIAN publicó la Resolución 000227 de 2025 como compilación única y ha expedido disposiciones posteriores. La matriz normativa debe partir de la compilación y anexos vigentes al liberar.

Fuentes oficiales:

- [Normatividad del sistema de facturación](https://micrositios.dian.gov.co/sistema-de-facturacion-electronica/normatividad/)
- [Resolución DIAN 000227 de 2025](https://www.dian.gov.co/normatividad/Normatividad/Resoluci%C3%B3n%20000227%20de%2023-09-2025.pdf)
- [Documentación técnica](https://micrositios.dian.gov.co/sistema-de-facturacion-electronica/documentacion-tecnica/)
- [Compilación jurídica](https://normograma.dian.gov.co/dian/compilacion/tnt_facturacion_electronica.html)

### R03. Documento fiscal sin cerrar

“Facturación POS” puede implicar:

- factura electrónica de venta;
- documento equivalente electrónico POS;
- nota crédito/débito;
- nota de ajuste;
- contingencia.

El piloto debe elegir FEV, DEE POS o ambos. Implementar ambos se autoriza solo si existe un caso comercial real.

Se requiere una matriz por documento:

```text
obligado
momento
numeración
validación
contingencias
entrega
corrección
eventos
retención
casos de habilitación
```

### R04. Cambio regulatorio

- `FiscalRuleSet` versionado;
- transporte DIAN aislado;
- fixtures y pruebas de contrato;
- vigilancia normativa;
- feature flags por versión;
- reglas DIAN fuera de Sales;
- UAT antes de activar una nueva versión.

### R05. Offline no equivale a factura expedida

Separar siempre:

```text
CommercialStatus
PaymentStatus
SyncStatus
FiscalStatus
```

Definir qué se imprime y entrega en:

- caída de Internet del establecimiento;
- indisponibilidad DIAN;
- caída de Auraly Cloud;
- certificado no disponible;
- rango agotado;
- Edge no disponible;
- On-Premise por LAN sin Internet.

La interfaz nunca debe mostrar “aceptada por DIAN” a una venta solamente local.

---

## 6. Motor, atomicidad e idempotencia

### R06. API frente a Worker

Decisión recomendada:

- confirmación online: caso de uso en API y una transacción SQL;
- operaciones diferidas: Worker ejecuta el mismo procesador;
- DIAN, correo, push y proyecciones: outbox después del commit;
- el Worker jamás reaplica efectos confirmados;
- `Confirmed` significa que efectos internos obligatorios quedaron atómicos;
- `FiscalPending` no invalida la venta comercial.

### R07. Duplicación

Cada comando confirmable:

```text
TenantId
OperationType
ClientOperationId / IdempotencyKey
PayloadHash
ResultId
Status
```

Reglas:

- misma clave y hash: mismo resultado;
- misma clave y hash diferente: conflicto;
- restricción única física;
- respuesta perdida: reintento seguro;
- timeout: estado consultable;
- inbox por consumidor;
- pruebas contra SQL Server real.

### R09. Reversión

Antes de construir cada documento debe existir una ficha:

| Campo | Definición |
|---|---|
| Efectos | libros/tablas que modifica |
| Confirmación | contenido del commit |
| Externos | mensajes de outbox |
| Cancelación | cuándo no hay efectos |
| Reversión | transacción compensatoria |
| Fiscal | nota o ajuste |
| Caja/cartera | reembolso, crédito o saldo |
| Inventario/costo | restauración de cantidad y valor |
| Reintento | prueba de idempotencia |

### R12. Última existencia

Validar al capturar no reserva.

Si la bodega bloquea negativos:

1. consulta al agregar/cambiar;
2. muestra disponibilidad observada;
3. actualización condicional o bloqueo corto al confirmar;
4. una venta gana la carrera;
5. la otra recibe conflicto recuperable;
6. nunca queda saldo negativo.

Si permite negativos, no se consulta y el saldo puede quedar negativo explícitamente.

---

## 7. POS Edge, catálogo y offline

### R10. Edge no es un conector pequeño

Incluye instalación, actualización, servicio Windows, identidad de dispositivo, API local segura, SQLite, lector, balanza, impresión, cajón, catálogo, outbox, recuperación, diagnóstico, versiones, revocación y secretos.

Spike obligatorio en caja real:

1. aprovisionamiento;
2. snapshot grande;
3. escaneo sostenido;
4. balanza;
5. impresión/cajón;
6. venta sin Internet;
7. reinicio abrupto;
8. sincronización repetida;
9. actualización con outbox pendiente;
10. revocación;
11. diagnóstico sanitizado.

### R11. Push

Decisión para desbloquear:

```text
Obligatorio:
  snapshot + change feed + deltas + checkpoints + manifiesto

Acelerador:
  IPushNotificationChannel

Cloud:
  Azure Web PubSub, sujeto a carga/costo

On-Premise:
  SignalR

Fallback:
  ETag y polling adaptativo de baja frecuencia
```

Push puede entrar al MVP, pero no es fuente de verdad ni punto único de fallo.

### R13. Datos locales obsoletos

Definir:

- antigüedad máxima;
- cambios críticos bloqueantes;
- revisión al pagar cuando hay red;
- tratamiento de líneas capturadas;
- versión fotografiada;
- resolución del motor ante versión obsoleta.

La línea de venta conserva producto, código, unidad, canal, regla de precio, precio, descuentos, impuestos y revisiones usadas.

### R15. Permisos offline

La instantánea firmada tendrá vigencia corta, acciones offline explícitas, caja, dispositivo, negocio, bodega y versión. Las acciones sensibles exigen conexión.

Revocar un usuario no puede invalidar mágicamente una caja sin red. Se debe limitar el tiempo offline y permitir revocar el dispositivo al reconectar.

---

## 8. Persistencia y modularidad

Decisión canónica:

```text
una base SQL
dbo
un AuralyDbContext de ejecución
un Auraly.Database.sqlproj
cero migraciones EF para modificar el esquema
configuración EF organizada por módulo
propietario lógico exclusivo por tabla
```

La referencia a migraciones EF en `decision-persistencia-simple-monolito-modular.md` es histórica y no se implementa.

### R16. Acoplamiento por `DbContext`

- `DbContext` solo en Infrastructure;
- repositorios/queries por módulo;
- propietario de tablas documentado;
- prohibición de escritura cruzada;
- contratos entre módulos;
- pruebas de dependencias;
- excepciones con vencimiento.

No hacen falta esquemas ni bases separadas en el MVP.

### R17. Renombrado

El repositorio aún contiene solución, proyectos y rutas `MimosBabySpa`. Renombrar ensamblados, namespaces, SQL, pipelines, recursos y secretos mientras se agregan módulos amplía el fallo.

Hacerlo como iniciativa separada:

1. inventario de nombres;
2. mapa anterior → nuevo;
3. cambio mecánico;
4. build y pruebas;
5. despliegue de prueba;
6. compatibilidad temporal;
7. retiro posterior de alias.

No mezclar funcionalidad Commerce en el mismo commit.

### R18. DACPAC

- generar script en CI;
- bloquear pérdida de datos;
- expansión/contracción;
- backup/restauración;
- prueba sobre copia representativa;
- modelo EF alineado con SQL;
- semillas idempotentes;
- rollback operacional.

---

## 9. Dinero, costo y cartera

### R08. Cálculos divergentes

Un motor autoritativo en servidor y una vista previa compatible deben fijar:

- precisión de cantidad, precio y tasa;
- momento de redondeo técnico;
- descuento de línea/global;
- base de impuesto;
- impuesto incluido/adicionado;
- orden de promociones;
- devolución proporcional;
- costo por línea;
- tolerancia entre preview y servidor.

### R20. Costo

Recomendación inicial: costo promedio permanente, salvo evidencia del piloto.

Probar entrada, compra con descuento, venta, devolución, negativo permitido, entrada posterior a negativo, traslado, avería, conteo, conversión, reversión, fecha retroactiva y concurrencia.

La métrica se llama **utilidad bruta comercial**, no utilidad contable.

### R21. Conversión

Invariantes:

- una bodega;
- `Split` o `Merge`, no muchos-a-muchos;
- inventario suficiente online;
- no aplica política de negativos de venta;
- costo conservado o diferencia explícita;
- merma y autorización;
- kardex doble;
- commit atómico;
- reversión completa;
- sin lotes, seriales ni offline.

Crear vectores dorados de cantidad, merma, costo y reversión. No copiar el fallo de validación de pérdida detectado en Xion.

### R22. CxC/CxP

Definir documento origen, causación, vencimiento, abonos, pagos parciales, anticipos, saldo a favor, reversión, aplicación/desaplicación y diferencia de centavos.

Primer corte recomendado: una obligación, un vencimiento y múltiples aplicaciones. Cuotas complejas solo si el piloto las necesita.

---

## 10. Seguridad y multitenancy

### R14. Aislamiento

- `TenantId` obligatorio;
- filtros y autorización en servidor;
- scopes por empresa, sede, bodega y caja;
- índices únicos con alcance correcto;
- pruebas negativas entre tenants;
- grupos push asignados por servidor;
- archivos autorizados;
- Reporting con los mismos alcances.

Puede usarse aislamiento de aplicación en el MVP si existen pruebas sistemáticas de fuga. Row-Level Security queda como decisión explícita, no supuesto.

### R28. Secretos

- certificado DIAN aislado por tenant;
- Key Vault en Cloud;
- almacén Windows/DPAPI en On-Premise y Edge;
- XML/PDF con acceso auditado;
- SQLite cifrado si guarda clientes/ventas;
- procedimiento por pérdida de caja;
- respaldo cifrado;
- soporte sanitizado.

La firma local solo se activa si el flujo fiscal offline la requiere.

---

## 11. On-Premise

Perfil inicial cerrado:

```text
Windows Server
IIS
SQL Server
Auraly.Worker como Windows Service
almacenamiento protegido
SignalR local
salida HTTPS
```

No prometer Docker, Linux, Kubernetes, alta disponibilidad ni activo-activo.

Certificación:

- instalador repetible;
- preflight y TLS;
- mínimo privilegio;
- DACPAC/semillas;
- actualización;
- rollback;
- backup/restauración;
- salud/logs;
- prueba sin Internet;
- antivirus/firewall;
- versiones soportadas.

Cloud y On-Premise ejecutan la misma suite de contratos.

---

## 12. Reportes y rendimiento

### R23. Carga de reportes

- paginación por cursor para tablas grandes;
- orden determinista;
- límites de exportación;
- exportación grande asíncrona;
- índices por consultas reales;
- proyecciones reconstruibles;
- Reporting nunca escribe;
- zona horaria comercial;
- autorización antes de paginar.

### R26. Capacidad sin definir

Fijar piloto, objetivo y estrés para:

- tenants, negocios y cajas concurrentes;
- productos y códigos;
- líneas por venta;
- ventas/minuto;
- días offline;
- documentos fiscales/día;
- retención de datos.

Sin esos números no se aprueban Web PubSub, snapshots, índices, retención del feed ni costo.

SLO inicial para validar:

- escaneo y recálculo local p95 < 100 ms;
- validación online p95 < 500 ms;
- confirmación interna p95 < 2 s;
- precio con push p95 < 5 s y fallback < 60 s;
- cero duplicación;
- reinicio sin pérdida.

---

## 13. Pruebas y migración

### R25. Pruebas

La estrategia debe cubrir:

1. dominio e invariantes;
2. Application y permisos;
3. SQL real: constraints, locks, idempotencia y DACPAC;
4. contratos Cloud/On-Premise y API/Edge;
5. DIAN, storage, correo y push;
6. UI por teclado, lector, foco y recálculo;
7. hardware certificado;
8. caos: red, reinicio, duplicados y desorden;
9. migración y conciliación;
10. UAT con datos y roles reales.

Definition of Done: reglas trazadas, pruebas, SQL revisado, permisos negativos, telemetría, errores operables, reversión, documentación, migración y UAT.

El backend de la rama pasó `dotnet test MimosBabySpa.sln --no-restore`. El frontend no compiló en el worktree porque no tenía dependencias instaladas; el pipeline debe restaurar y compilar backend, frontend y proyecto SQL desde cero.

### R19. Migración Xion

No migrar todo el historial al MVP.

Orden:

1. catálogos y terceros;
2. productos, códigos, precios y proveedor-costo;
3. existencias y costo inicial;
4. pedidos abiertos;
5. CxC/CxP abiertas;
6. resoluciones y parámetros;
7. historia opcional solo para consulta.

Usar IDs Auraly nuevos, `LegacyEntityMappings`, proceso reanudable, rechazados y conciliación de conteos, dinero, inventario, costo y cartera.

Ensayar dos veces con copia anonimizada.

---

## 14. Decisiones cerradas por esta auditoría

1. Auraly Commerce permanece dentro de Auraly.
2. Monolito modular; no microservicios en el MVP.
3. Una base, `dbo` y un `AuralyDbContext`.
4. Solo `Auraly.Database.sqlproj` modifica el esquema.
5. Negativos pertenecen a bodega.
6. La caja no descarga inventario.
7. Bodega bloqueante: validar al capturar y al confirmar.
8. Bodega bloqueante no vende offline.
9. Bodega permisiva puede vender offline según política fiscal, permisos y pagos.
10. Snapshot, deltas y checkpoints son obligatorios.
11. Push es acelerador reemplazable.
12. Web PubSub/SignalR son adaptadores sujetos a spike.
13. El motor servidor procesa documentos definitivos.
14. Confirmación interna puede ser síncrona en API.
15. Efectos externos usan outbox.
16. Facturación electrónica es propia.
17. El perfil fiscal se cierra antes de construir Fiscal.
18. Pedidos siempre online; POS recupera uno.
19. Facturar seleccionados crea una factura por pedido.
20. Conversión entra; Producción queda fuera.
21. Lotes, seriales, retenciones, fletes y redondeo comercial quedan fuera.
22. Balanza y devoluciones entran.
23. Cloud y On-Premise comparten código.
24. On-Premise se vende solo después de certificarse.

---

## 15. Decisiones abiertas por módulo

| Decisión | Bloquea |
|---|---|
| Tipo de negocio y volumen | estimación, UX, rendimiento |
| FEV, DEE POS o ambos | Fiscal |
| Perfil tributario | cálculo/XML |
| Método de costo | Inventory/Reporting |
| Una fecha o cuotas | CxC/CxP |
| Recepción sin factura | Purchasing/Payables |
| Medios offline | POS/Cash |
| Máxima antigüedad de catálogo/permisos | PosSync |
| Cierre con outbox pendiente | Cash |
| Periféricos certificados | Edge |
| Datos a migrar | cronograma |
| Identidad On-Premise | Identity |
| Retención XML/PDF/logs | Fiscal/seguridad |

Valores recomendados para el primer piloto:

- costo promedio permanente;
- una obligación con un vencimiento y múltiples abonos;
- efectivo offline;
- sin cuotas complejas;
- sin historia profunda;
- catálogo sin imágenes;
- permisos offline cortos;
- cierre con advertencia y política explícita de pendientes;
- pocos modelos de impresora y balanza certificados.

---

## 16. Plan para reducir riesgo

### Semanas técnicas 0–1

- consolidar especificación;
- marcar decisiones reemplazadas;
- elegir piloto;
- fijar capacidad;
- estados/efectos por documento;
- ADR de persistencia y push;
- Definition of Done.

### Semanas técnicas 1–2

- spike DIAN mínimo en habilitación;
- spike Edge con SQLite, lector, balanza e impresión;
- snapshot/delta representativo;
- venta concurrente en SQL;
- idempotencia con respuesta perdida;
- DACPAC sobre copia;
- aislamiento entre tenants.

### Primer corte vertical

```text
Producto -> código -> precio -> caja -> escaneo
-> cálculo servidor -> pago -> inventario -> caja
-> outbox -> reporte conciliado
```

Después:

1. fiscal online;
2. abastecimiento/cartera;
3. devoluciones;
4. inventario operacional y conversión;
5. offline;
6. On-Premise.

---

## 17. Criterio Go/No-Go

Se inicia implementación cuando:

- existe especificación canónica;
- backlog enlaza criterios;
- primer corte está delimitado;
- existe piloto;
- las puertas tienen responsables;
- CI compila desde cero;
- SQL project es autoridad;
- hay pruebas de arquitectura.

No se abre un módulo cuando:

- sus reglas fiscales/financieras están implícitas;
- no tiene reversión;
- no tiene propietario de tablas;
- cruza módulos sin contrato;
- depende de algo excluido;
- no tiene escenario extremo a extremo.

No se promete offline cuando:

- Edge no sobrevivió reinicios/actualizaciones;
- la cola duplica efectos;
- venta local se confunde con factura;
- no hay política para bodega bloqueante;
- no se probó revocación.

No se sale fiscalmente cuando:

- no se validó la norma vigente;
- no se completó habilitación;
- no se controlan certificados/rangos;
- faltan notas/correcciones;
- no hay tablero de rechazados;
- no se probó contingencia.

---

## 18. Recomendación final

Sí podemos pasar a implementación, de forma controlada:

1. aprobar arquitectura;
2. declarar `GO CONDICIONADO`;
3. evitar todos los módulos simultáneos;
4. cerrar las cinco puertas;
5. construir un corte vertical online;
6. desarrollar DIAN y POS Edge como pruebas demostrables;
7. activar offline y On-Premise después de certificarlos;
8. medir progreso por escenarios conciliados, no por pantallas.

El mayor riesgo ya no es si Auraly puede absorber Xion. Puede hacerlo. El mayor riesgo es tratar un ERP completo como una sola funcionalidad y usar documentos contradictorios en lugar de contratos ejecutables.

Con estas puertas, la implementación puede comenzar sin hipotecar la arquitectura ni prometer capacidades todavía no demostradas.
