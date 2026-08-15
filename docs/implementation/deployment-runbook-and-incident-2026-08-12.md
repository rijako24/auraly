# Despliegue reproducible de Auraly y lecciones del primer DEV

Fecha: 2026-08-12  
Estado: procedimiento obligatorio para DEV y producción

## Resultado que se protege

Un despliegue no comienza subiendo archivos. Primero demuestra que el mismo
release inmutable tiene binarios, esquema, infraestructura y configuración de
runtime compatibles. DEV es la compuerta obligatoria de producción: el mismo
`manifest.json` y los mismos hashes se promueven; nunca se recompila para
producción.

El procedimiento evita la secuencia lenta que ocurrió en el primer despliegue:
publicar, esperar el arranque, descubrir una clave con nombre incorrecto,
corregir y volver a publicar.

## Topología de mensajes: operación, contabilidad y fiscalidad desacopladas

Auraly usa carriles con responsabilidades explícitas; ningún botón de la UI escribe asientos directamente:

- `auraly-document-processing`: cola operacional ordenada. `SessionId = BusinessId`, `MessageId = MovementId`, un documento por mensaje. Procesa inventario, costos, caja, cuentas por cobrar y cuentas por pagar. Al confirmar sus efectos publica de forma durable el trabajo contable requerido.
- `auraly-accounting-processing`: cola del motor contable. Genera asientos balanceados e idempotentes, PUC, centros de costo, retenciones y proyecciones contables. Un fallo contable se reintenta o va a intervención sin duplicar ni revertir el documento operacional confirmado.
- `auraly-fiscal-processing`: generación, firma, envío y consulta DIAN. Está separada para que la latencia de un proveedor externo no bloquee operación ni contabilidad.
- `auraly-external-customer-reconciliation`: convierte clientes externos del bot en `Party/Customer`; no mueve documentos, inventario ni asientos.

No se crean colas por cada tipo documental. Ventas, entradas, conteos, ajustes, traslados, conversiones, averías, devoluciones, entradas y salidas de caja entran por la cola operacional; solo los efectos contables pasan a la cola contable.
## Causas encontradas y corrección permanente

| Falla observada | Causa raíz | Protección permanente |
| --- | --- | --- |
| La API no encontraba SQL | Solo existía `ConnectionStrings:DefaultConnection`; el host canónico exige `ConnectionStrings:Auraly` | Bicep y App Configuration publican la clave canónica |
| Una configuración remota vacía anulaba App Service | Azure App Configuration se cargaba después del entorno | Las variables de entorno se vuelven a agregar al final y una prueba de arquitectura protege el orden |
| La protección fiscal detenía el arranque | Faltaba una clave Base64 de 32 bytes | Parámetro Bicep seguro y preflight que valida formato/longitud sin mostrar el valor |
| JWT no era reconocido | IaC usaba `Jwt__*`; la API usa `Authentication__Jwt__*` | Un único contrato canónico y prueba que prohíbe los nombres anteriores |
| Service Bus exigía cadena de conexión | El host no reutilizaba la fábrica de identidad administrada | API usa `ServiceBusConnection__fullyQualifiedNamespace` y `clientId`; no se versionan secretos |
| Las entidades del motor no existían | La infraestructura solo declaraba colas heredadas del bot | Bicep declara las colas operacional, contable y fiscal, más reconciliación externa, con sesiones y DLQ |
| `az webapp deploy` falló por el certificado del proxy local | Problema del cliente local, no del paquete | La publicación puede usar Kudu autenticado; nunca se desactiva la validación TLS |
| El primer diagnóstico tomó varios ciclos | No existía contrato de preflight de extremo a extremo | `Test-AuralyDeploymentReadiness.ps1` falla antes de publicar y lista exactamente el dato faltante |

## Contrato canónico de runtime de la API

El preflight exige, sin imprimir valores:

- `AppConfiguration__Endpoint`;
- `AZURE_CLIENT_ID`;
- `ServiceBusConnection__fullyQualifiedNamespace`;
- `ServiceBusConnection__clientId`;
- los nombres de las colas operacional, contable y fiscal, más reconciliación externa;
- `Auraly__Fiscal__SecretProtectionKey` (Base64, 32 bytes);
- `Authentication__Jwt__Issuer`;
- `Authentication__Jwt__Audience`;
- `Authentication__Jwt__SigningKey` (mínimo 32 bytes);
- `Auraly__PosSynchronization__WebPubSub__Endpoint`;
- `Auraly__PosSynchronization__WebPubSub__ManagedIdentityClientId`;
- `Auraly__PosSynchronization__WebPubSub__Hub`;
- `Release__Version`.

Los secretos no se pasan por `appsettings.json`, el repositorio, el manifiesto,
la salida de CI ni la documentación.

Web PubSub se declara en el mismo resource group de cada ambiente. DEV usa
`Free_F1`; producción usa `Standard_S1` únicamente cuando se aprueba y aplica la
promoción productiva. La API publica con la identidad administrada de Auraly y
el rol `Web PubSub Service Owner`; la autenticación local del recurso permanece
deshabilitada.

## Flujo rápido de DEV

1. Partir de `main` limpio y actualizado.
2. Crear una versión nueva con `New-AuralyRelease.ps1`. El script compila y
   ejecuta las regresiones antes de producir artefactos.
3. Ejecutar preflight local:

```powershell
$version = '0.2.0-rc4'
.\infrastructure\azure\Test-AuralyDeploymentReadiness.ps1 `
    -Environment Dev -ReleaseVersion $version -LocalOnly
```

4. Aplicar IaC mediante `Validate`, `WhatIf` y `Apply`; revisar el resultado de
   `WhatIf`, especialmente SKU, identidades, configuración y recursos nuevos.
5. Publicar DACPAC, API, Function y frontend desde los artefactos del mismo
   manifiesto. No recompilar entre componentes.
6. Ejecutar el preflight remoto:

```powershell
.\infrastructure\azure\Test-AuralyDeploymentReadiness.ps1 `
    -Environment Dev -ReleaseVersion $version
```

7. Ejecutar las regresiones UI contra DEV y conservar el informe como evidencia
   del release.

## Promoción segura a producción

Producción se bloquea si falta cualquiera de estos puntos:

- PR aprobado y commit presente en `main`;
- árbol limpio;
- release inmutable creado con Node.js 20.19 o superior;
- hashes y tamaños coincidentes;
- DEV desplegado con exactamente la misma versión;
- preflight DEV saludable;
- DACPAC probado contra una base aislada;
- regresión UI de DEV aprobada;
- `WhatIf` de producción revisado;
- respaldo y plan de reversión identificados;
- aprobación explícita para producción.

Después se promueven los mismos ZIP/DACPAC y se ejecuta:

```powershell
.\infrastructure\azure\Test-AuralyDeploymentReadiness.ps1 `
    -Environment Prod -ReleaseVersion $version
```

No se modifica `RG-AURALY-DEV` ni recursos heredados durante esta promoción.

## Diagnóstico rápido

Si la API no está saludable:

1. no volver a desplegar inmediatamente;
2. ejecutar el preflight y corregir el primer contrato incumplido;
3. consultar el evento de arranque y el log de contenedor, sanitizando secretos;
4. distinguir configuración, infraestructura, base de datos y binario;
5. volver a publicar solo si cambió el binario; una corrección puramente de
   infraestructura no requiere regenerar el ZIP;
6. crear una nueva versión si cambió código. Nunca sobrescribir una carpeta de
   release existente.

## Evidencia del ciclo actual

- Release previo construido: `0.2.0-rc3`, commit
  `0dd0ef87a2d4a947a0f5b7a66c00fe31e65b2130`.
- Build .NET: 0 errores y 0 advertencias.
- Pruebas legacy: 730 correctas.
- Foundation: 166 correctas después de la protección de precedencia.
- POS Edge: 23 correctas.
- Integración SQL/RabbitMQ: 129 correctas.
- Frontend: 58 rutas generadas.
- DACPAC: compilado y desplegado en DEV.
- Function DEV `0.2.0-rc2`: saludable.
- Frontend DEV: desplegado correctamente.
- API `0.2.0-rc3`: el binario fue publicado, y el diagnóstico reveló los
  contratos de autenticación/Service Bus que ahora quedan protegidos por IaC y
  preflight. No se considera una validación remota completa hasta que una nueva
  versión incorpore estas correcciones y pase salud.

## Regla de cierre

“Desplegado” significa: artefactos verificados, infraestructura compatible,
base actualizada, servicios saludables y regresiones ejecutadas. Una carga ZIP
exitosa por sí sola no completa un despliegue.

### Cierre remoto de `0.2.0-rc4` en DEV

Fecha de verificacion: 2026-08-12 (America/Bogota).

- API `api-auraly-dev-w5usmo6w`: `Running`, `/health` devuelve `200 Healthy`.
- Worker `func-auraly-dev-w5usmo6w`: `Running`, publicado mediante OneDeploy y
  etiquetado con `Release__Version=0.2.0-rc4`.
- Frontend `proud-moss-0d9cf540f.7.azurestaticapps.net`: `/login` devuelve 200.
- Service Bus: cuatro colas activas, con sesiones obligatorias y `MaxDeliveryCount=10`.
- Web PubSub `wps-auraly-dev-w5usmo6w`: `Free_F1`, aprovisionado y con
  autenticacion local deshabilitada.
- El paquete temporal de OneDeploy y el rol temporal de carga se eliminaron al
  terminar.
- El preflight remoto pasa y ahora valida API, worker, versiones, configuracion,
  colas, sesiones, Web PubSub y salud HTTP.

La infraestructura de produccion declara Web PubSub `Standard_S1`, pero no se
aplico ninguna mutacion en `RG-AURALY-PROD` durante esta promocion a DEV.

### Incidente de conectividad del login BFF

- Sintoma: la pagina y la API respondian 200, pero el login devolvia
  `Error de conexion con el servidor`.
- Causa: el Static Web App no tenia `AURALY_API_URL`; la ruta BFF no podia
  construir la URL del backend.
- Correccion: `main.bicep` administra ese App Setting con el hostname real de la
  API. El preflight publica credenciales deliberadamente invalidas y exige 401,
  demostrando que el navegador alcanza la API sin depender de un usuario real.
