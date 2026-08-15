# Plantillas WhatsApp en Meta

Guia rapida para consultar, crear y validar plantillas de WhatsApp Cloud API usando el token guardado en Azure/BD.

## Datos necesarios

- `BusinessId` del negocio.
- WebApp de Azure con `ConnectionStrings:DefaultConnection`.
- El registro activo en `dbo.BusinessWhatsAppNumbers` debe tener:
  - `WhatsAppBusinessAccountId` (WABA ID)
  - `WhatsAppPhoneNumberId`
  - `WhatsAppAccessToken`

Para Vinos Artesanales Solorzano:

```powershell
$businessId = 'FCEE3BA9-E6BF-43E2-8C1A-560CB724688B'
$resourceGroup = 'RG-AURALY-DEV'
$webAppName = 'api-auraly-dev-w5usmo6w'
```

## Obtener token y WABA desde Azure

No imprimas el token en consola. Cargalo en variables y muestra solo datos no sensibles.

```powershell
$app = Get-AzWebApp -ResourceGroupName $resourceGroup -Name $webAppName
$conn = ($app.SiteConfig.ConnectionStrings | Where-Object { $_.Name -eq 'DefaultConnection' }).ConnectionString

$query = @"
SELECT TOP (1)
    WhatsAppAccessToken,
    WhatsAppBusinessAccountId,
    WhatsAppPhoneNumberId
FROM dbo.BusinessWhatsAppNumbers
WHERE BusinessId = '$businessId'
  AND IsActive = 1
"@

$dt = New-Object System.Data.DataTable
$cn = New-Object System.Data.SqlClient.SqlConnection($conn)
$cmd = New-Object System.Data.SqlClient.SqlCommand($query, $cn)
$cn.Open()
$dt.Load($cmd.ExecuteReader())
$cn.Close()

$token = [string]$dt.Rows[0].WhatsAppAccessToken
$waba = [string]$dt.Rows[0].WhatsAppBusinessAccountId
$phone = [string]$dt.Rows[0].WhatsAppPhoneNumberId
$headers = @{ Authorization = "Bearer $token"; 'Content-Type' = 'application/json' }

[pscustomobject]@{
    WabaId = $waba
    PhoneNumberId = $phone
    TokenLength = $token.Length
} | ConvertTo-Json
```

## Consultar plantillas

```powershell
$name = 'delivery_request'
$uri = "https://graph.facebook.com/v25.0/$waba/message_templates?name=$name&fields=id,name,status,category,language,components"
$res = Invoke-RestMethod -Method Get -Uri $uri -Headers @{ Authorization = "Bearer $token" }
$res.data | ConvertTo-Json -Depth 30
```

Estados comunes:

- `APPROVED`: lista para enviar.
- `PENDING`: en revision de Meta.
- `REJECTED`: rechazada; normalmente se puede editar por API.

Nota: Meta puede devolver plantillas cuyo nombre empieza igual. Verifica `name` exacto en la respuesta.

## Crear una plantilla

Meta usa `{{1}}`, `{{2}}`, etc. Los ejemplos son obligatorios cuando hay variables.

```powershell
function Invoke-MetaJson($Method, $Uri, $BodyObj) {
    $body = $BodyObj | ConvertTo-Json -Depth 30 -Compress
    try {
        Invoke-RestMethod -Method $Method -Uri $Uri -Headers $headers -Body $body
    } catch {
        $errBody = $_.ErrorDetails.Message
        if (-not $errBody -and $_.Exception.Response) {
            $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
            $errBody = $reader.ReadToEnd()
        }
        throw "Meta API error for $Uri :: $errBody"
    }
}

$bodyText = @'
Equipo, no fue posible asignar un domicilio despues de intentar contactar a los domiciliarios configurados para esta orden. Por favor revisen la solicitud y coordinen manualmente la entrega con el cliente.

*Pedido:* {{1}}
*Cliente:* {{2}}
*Celular:* {{3}}
*Ciudad:* {{4}}
*Direccion de entrega:* {{5}}
*Productos:* {{6}}
*Total registrado:* ${{7}} {{8}}
*Codigo de asignacion:* {{9}}

Este mensaje es automatico y sirve para seguimiento interno cuando se agotan los intentos de asignacion.
'@

$payload = @{
    name = 'delivery_unavailable'
    language = 'es_CO'
    category = 'UTILITY'
    components = @(
        @{
            type = 'BODY'
            text = $bodyText.Trim()
            example = @{
                body_text = @(,@(
                    'A1B2C3D4',
                    'Maria Perez',
                    '573001112233',
                    'Valledupar',
                    'Cra 10 #20-30',
                    'Producto x2',
                    '85000',
                    'COP',
                    'PED-12345'
                ))
            }
        }
    )
}

$created = Invoke-MetaJson 'Post' "https://graph.facebook.com/v25.0/$waba/message_templates" $payload
$created | ConvertTo-Json
```

## Crear plantilla con header y botones

Si el header tiene variable, el backend debe enviar `headerParameters` en el seed/configuracion.

```powershell
$bodyText = @'
*Codigo:* {{1}}
*Pedido:* {{2}}

*Recogida:* {{3}}
*Direccion recogida:* {{4}}

*Cliente:* {{5}}
*Celular:* {{6}}
*Ciudad:* {{7}}
*Direccion entrega:* {{8}}

*Total:* ${{9}} {{10}}
*Metodo de pago:* {{11}}

Si aceptas, quedaras asignado para coordinar y entregar esta orden.
'@

$payload = @{
    name = 'delivery_request_v2'
    language = 'es_CO'
    category = 'UTILITY'
    components = @(
        @{
            type = 'HEADER'
            format = 'TEXT'
            text = 'Slicitud domicilio {{1}}'
            example = @{ header_text = @('Vinos Artesanales Solorzano') }
        },
        @{
            type = 'BODY'
            text = $bodyText.Trim()
            example = @{
                body_text = @(,@(
                    'PED-12345',
                    'A1B2C3D4',
                    'Vinos Artesanales Solorzano',
                    'Calle 16 # 9-35, Centro, Valledupar',
                    'Maria Perez',
                    '573001112233',
                    'Valledupar',
                    'Cra 10 #20-30',
                    '85000',
                    'COP',
                    'efectivo'
                ))
            }
        },
        @{
            type = 'BUTTONS'
            buttons = @(
                @{ type = 'QUICK_REPLY'; text = 'Aceptar' },
                @{ type = 'QUICK_REPLY'; text = 'No tomar' }
            )
        }
    )
}

$created = Invoke-MetaJson 'Post' "https://graph.facebook.com/v25.0/$waba/message_templates" $payload
$created | ConvertTo-Json
```

## Editar una plantilla

Meta no siempre permite editar plantillas por API. Si la plantilla esta `APPROVED`, puede rechazar la edicion con:

```text
Las plantillas de mensajes solo se pueden editar si se rechazaron.
```

En ese caso, crea una plantilla nueva con otro nombre, por ejemplo `delivery_request_v2`, espera aprobacion y luego cambia `templateName` en `SettingsJson.messageSequences`.

Si Meta permite editarla, usa el `id` de la plantilla:

```powershell
$templateId = '1336284085305102'
$updatePayload = @{
    category = 'UTILITY'
    components = @(
        # HEADER, BODY, BUTTONS...
    )
}

$updated = Invoke-MetaJson 'Post' "https://graph.facebook.com/v25.0/$templateId" $updatePayload
$updated | ConvertTo-Json
```

## Alinear Meta con el backend

Las plantillas se envian desde `Agents.SettingsJson.messageSequences`.

Ejemplo con header:

```json
"delivery_request": {
  "messages": [
    {
      "type": "whatsapp_template",
      "templateName": "delivery_request",
      "language": "es_CO",
      "headerParameters": [
        "{business_name}"
      ],
      "bodyParameters": [
        "{attempt_code}",
        "{order_number}",
        "{pickup_contact_name}",
        "{pickup_address}",
        "{customer_name}",
        "{customer_phone}",
        "{city}",
        "{delivery_address}",
        "{total}",
        "{currency}",
        "{payment_method}"
      ],
      "buttons": [
        {
          "id": "external_interaction:accepted:{external_interaction_id}",
          "title": "Aceptar"
        },
        {
          "id": "external_interaction:declined:{external_interaction_id}",
          "title": "No tomar"
        }
      ]
    }
  ]
}
```

Reglas importantes:

- El numero de `headerParameters` debe coincidir con las variables del componente `HEADER`.
- El numero y orden de `bodyParameters` debe coincidir con `{{1}}`, `{{2}}`, etc. del `BODY`.
- Si agregas o quitas variables en Meta, actualiza el seed/configuracion antes de enviar mensajes reales.
- Los botones quick reply de Meta solo definen el texto visible. El payload interno lo manda el backend desde `buttons.id`.
- Para negrita en WhatsApp usa `*Titulo:*`.
- Evita muchas variables con poco texto. Meta rechaza plantillas con alta proporcion de parametros vs palabras; agrega contexto si aparece ese error.

## Verificacion despues de crear

```powershell
foreach ($name in @('delivery_request', 'delivery_unavailable')) {
    $uri = "https://graph.facebook.com/v25.0/$waba/message_templates?name=$name&fields=id,name,status,category,language,components"
    $res = Invoke-RestMethod -Method Get -Uri $uri -Headers @{ Authorization = "Bearer $token" }
    $res.data | Select-Object id,name,status,category,language | ConvertTo-Json
}
```

## Checklist

- Consultar si la plantilla ya existe.
- Si existe y esta `APPROVED`, preferir crear una version nueva si Meta no deja editar.
- Crear/editar en Meta.
- Esperar `APPROVED`.
- Actualizar `templateName`, `headerParameters` y `bodyParameters` en el seed/configuracion.
- Ejecutar `dotnet build Auraly.Commerce.sln`.
- Probar un envio real o controlado.
