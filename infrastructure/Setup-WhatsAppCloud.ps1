#Requires -Version 7.0

<#
.SYNOPSIS
    Configura WhatsApp Cloud API y el webhook para Mimos Baby Spa

.DESCRIPTION
    Este script ayuda a configurar WhatsApp Cloud API mediante la Graph API de Meta:
    - Verifica la aplicación de WhatsApp
    - Obtiene el Phone Number ID
    - Configura el webhook
    - Suscribe a eventos de mensajes
    - Valida la configuración

.PARAMETER AppId
    ID de la aplicación de WhatsApp en Meta for Developers

.PARAMETER AppSecret
    App Secret de la aplicación de WhatsApp

.PARAMETER AccessToken
    Access Token permanente de la aplicación (opcional, se generará si no se proporciona)

.PARAMETER WebhookUrl
    URL completa del webhook de la Azure Function (incluyendo el código de función)

.PARAMETER VerifyToken
    Token de verificación para el webhook (debe coincidir con el configurado en la Function App)

.PARAMETER FunctionAppName
    Nombre de la Azure Function App (para obtener automáticamente la URL del webhook)

.PARAMETER ResourceGroupName
    Nombre del Resource Group de Azure (para obtener automáticamente la URL del webhook)

.PARAMETER FunctionKey
    Function Key del webhook (si se proporciona, se usará para construir la URL)

.PARAMETER PhoneNumber
    Número de teléfono existente con WhatsApp Business (formato: +1234567890 o 1234567890)
    Si se proporciona, el script buscará este número específico en lugar de usar el primero disponible

.EXAMPLE
    .\Setup-WhatsAppCloud.ps1 `
        -AppId "1234567890123456" `
        -AppSecret "tu-app-secret" `
        -WebhookUrl "https://mimosbabyspa-functions.azurewebsites.net/api/WhatsAppWebhook?code=abc123" `
        -VerifyToken "mi-token-secreto"

.EXAMPLE
    .\Setup-WhatsAppCloud.ps1 `
        -AppId "1234567890123456" `
        -AppSecret "tu-app-secret" `
        -FunctionAppName "mimosbabyspa-functions" `
        -ResourceGroupName "MimosBabySpa-RG" `
        -VerifyToken "mi-token-secreto" `
        -PhoneNumber "+1234567890"

.EXAMPLE
    Usar número existente de WhatsApp:
    .\Setup-WhatsAppCloud.ps1 `
        -AppId "1234567890123456" `
        -AppSecret "tu-app-secret" `
        -PhoneNumber "+1234567890" `
        -FunctionAppName "mimosbabyspa-functions" `
        -ResourceGroupName "MimosBabySpa-RG" `
        -VerifyToken "mi-token-secreto"
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory=$true)]
    [string]$AppId,
    
    [Parameter(Mandatory=$true)]
    [SecureString]$AppSecret,
    
    [Parameter(Mandatory=$false)]
    [string]$AccessToken,
    
    [Parameter(Mandatory=$false)]
    [string]$WebhookUrl,
    
    [Parameter(Mandatory=$true)]
    [string]$VerifyToken,
    
    [Parameter(Mandatory=$false)]
    [string]$FunctionAppName,
    
    [Parameter(Mandatory=$false)]
    [string]$ResourceGroupName,
    
    [Parameter(Mandatory=$false)]
    [string]$FunctionKey,
    
    [Parameter(Mandatory=$false)]
    [string]$PhoneNumber
)

$ErrorActionPreference = "Stop"

# Convertir SecureString a String
$BSTR = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($AppSecret)
$AppSecretPlain = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR)
[System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($BSTR)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Configuración de WhatsApp Cloud API" -ForegroundColor Cyan
Write-Host "  Mimos Baby Spa" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Construir URL del webhook si no se proporciona
if ([string]::IsNullOrEmpty($WebhookUrl)) {
    if ([string]::IsNullOrEmpty($FunctionAppName) -or [string]::IsNullOrEmpty($ResourceGroupName)) {
        Write-Host "Error: Debes proporcionar WebhookUrl o FunctionAppName + ResourceGroupName" -ForegroundColor Red
        exit 1
    }
    
    Write-Host "[1/7] Obteniendo URL del webhook..." -ForegroundColor Yellow
    
    # Obtener Function Key si no se proporciona
    if ([string]::IsNullOrEmpty($FunctionKey)) {
        try {
            $functionKeys = az functionapp function keys list `
                --name $FunctionAppName `
                --resource-group $ResourceGroupName `
                --function-name WhatsAppWebhook `
                --output json 2>$null | ConvertFrom-Json
            
            if ($null -ne $functionKeys -and $functionKeys.default) {
                $FunctionKey = $functionKeys.default
                Write-Host "  ✓ Function Key obtenida" -ForegroundColor Green
            } else {
                Write-Host "  ⚠ No se encontró Function Key, solicitando manualmente..." -ForegroundColor Yellow
                $FunctionKey = Read-Host "Ingresa el Function Key del webhook"
            }
        } catch {
            Write-Host "  ⚠ Error al obtener Function Key: $($_.Exception.Message)" -ForegroundColor Yellow
            $FunctionKey = Read-Host "Ingresa el Function Key del webhook"
        }
    }
    
    # Obtener URL de la Function App
    try {
        $functionApp = az functionapp show `
            --name $FunctionAppName `
            --resource-group $ResourceGroupName `
            --output json 2>$null | ConvertFrom-Json
        
        if ($null -ne $functionApp) {
            $WebhookUrl = "https://$($functionApp.defaultHostName)/api/WhatsAppWebhook?code=$FunctionKey"
            Write-Host "  ✓ URL del webhook construida: $WebhookUrl" -ForegroundColor Green
        } else {
            Write-Host "  Error: No se pudo obtener información de la Function App" -ForegroundColor Red
            exit 1
        }
    } catch {
        Write-Host "  Error: No se pudo obtener información de la Function App: $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }
    Write-Host ""
}

# Obtener Access Token si no se proporciona
if ([string]::IsNullOrEmpty($AccessToken)) {
    Write-Host "[2/7] Obteniendo Access Token..." -ForegroundColor Yellow
    
    try {
        $tokenUrl = "https://graph.facebook.com/oauth/access_token"
        $tokenParams = @{
            client_id = $AppId
            client_secret = $AppSecretPlain
            grant_type = "client_credentials"
        }
        
        $tokenResponse = Invoke-RestMethod -Uri $tokenUrl -Method Get -Body $tokenParams
        $AccessToken = $tokenResponse.access_token
        
        Write-Host "  ✓ Access Token obtenido" -ForegroundColor Green
    } catch {
        Write-Host "  ✗ Error al obtener Access Token: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "  Por favor, proporciona un Access Token permanente manualmente" -ForegroundColor Yellow
        $AccessToken = Read-Host "Ingresa el Access Token" -AsSecureString
        $BSTR = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($AccessToken)
        $AccessToken = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR)
        [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($BSTR)
    }
    Write-Host ""
}

# Verificar aplicación
Write-Host "[3/7] Verificando aplicación de WhatsApp..." -ForegroundColor Yellow
try {
    $appUrl = "https://graph.facebook.com/v18.0/$AppId"
    $appHeaders = @{
        "Authorization" = "Bearer $AccessToken"
    }
    
    $appInfo = Invoke-RestMethod -Uri $appUrl -Method Get -Headers $appHeaders
    Write-Host "  ✓ Aplicación verificada: $($appInfo.name)" -ForegroundColor Green
} catch {
    Write-Host "  ✗ Error al verificar aplicación: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "  Verifica que el App ID y Access Token sean correctos" -ForegroundColor Yellow
    exit 1
}
Write-Host ""

# Obtener Phone Number ID
Write-Host "[4/7] Obteniendo Phone Number ID..." -ForegroundColor Yellow
try {
    $phoneNumbersUrl = "https://graph.facebook.com/v18.0/$AppId/phone_numbers"
    $phoneNumbersHeaders = @{
        "Authorization" = "Bearer $AccessToken"
    }
    
    $phoneNumbers = Invoke-RestMethod -Uri $phoneNumbersUrl -Method Get -Headers $phoneNumbersHeaders
    
    if ($phoneNumbers.data.Count -gt 0) {
        $phoneNumberId = $null
        
        # Si se proporcionó un número específico, buscarlo
        if (-not [string]::IsNullOrEmpty($PhoneNumber)) {
            # Normalizar el número (remover espacios, guiones, paréntesis)
            $normalizedPhone = $PhoneNumber -replace '\s|-|\(|\)', ''
            if (-not $normalizedPhone.StartsWith('+')) {
                $normalizedPhone = "+$normalizedPhone"
            }
            
            Write-Host "  Buscando número: $normalizedPhone" -ForegroundColor Cyan
            
            # Buscar el número en la lista
            $foundNumber = $phoneNumbers.data | Where-Object {
                $displayNumber = $_.display_phone_number -replace '\s|-|\(|\)', ''
                $verifiedNumber = if ($_.verified_name) { $_.verified_name -replace '\s|-|\(|\)', '' } else { "" }
                $displayNumber -eq $normalizedPhone -or $verifiedNumber -eq $normalizedPhone -or $_.display_phone_number -like "*$normalizedPhone*"
            } | Select-Object -First 1
            
            if ($null -ne $foundNumber) {
                $phoneNumberId = $foundNumber.id
                Write-Host "  ✓ Número encontrado: $($foundNumber.display_phone_number)" -ForegroundColor Green
                Write-Host "  ✓ Phone Number ID: $phoneNumberId" -ForegroundColor Green
                
                if ($foundNumber.verified_name) {
                    Write-Host "  Nombre verificado: $($foundNumber.verified_name)" -ForegroundColor Gray
                }
            } else {
                Write-Host "  ⚠ No se encontró el número $normalizedPhone en la aplicación" -ForegroundColor Yellow
                Write-Host "  Números disponibles:" -ForegroundColor Yellow
                foreach ($num in $phoneNumbers.data) {
                    Write-Host "    - $($num.display_phone_number) (ID: $($num.id))" -ForegroundColor Gray
                }
                
                $useFirst = Read-Host "¿Usar el primer número disponible? (S/N)"
                if ($useFirst -eq "S" -or $useFirst -eq "s") {
                    $phoneNumberId = $phoneNumbers.data[0].id
                    Write-Host "  ✓ Usando número: $($phoneNumbers.data[0].display_phone_number)" -ForegroundColor Green
                } else {
                    $phoneNumberId = Read-Host "Ingresa el Phone Number ID manualmente"
                }
            }
        } else {
            # Usar el primer número disponible
            $phoneNumberId = $phoneNumbers.data[0].id
            Write-Host "  ✓ Phone Number ID obtenido: $phoneNumberId" -ForegroundColor Green
            Write-Host "  Número: $($phoneNumbers.data[0].display_phone_number)" -ForegroundColor Gray
            
            if ($phoneNumbers.data[0].verified_name) {
                Write-Host "  Nombre verificado: $($phoneNumbers.data[0].verified_name)" -ForegroundColor Gray
            }
            
            # Mostrar todos los números disponibles
            if ($phoneNumbers.data.Count -gt 1) {
                Write-Host "  Otros números disponibles:" -ForegroundColor Gray
                for ($i = 1; $i -lt $phoneNumbers.data.Count; $i++) {
                    Write-Host "    - $($phoneNumbers.data[$i].display_phone_number) (ID: $($phoneNumbers.data[$i].id))" -ForegroundColor Gray
                }
            }
        }
    } else {
        Write-Host "  ⚠ No se encontraron números de teléfono en la aplicación" -ForegroundColor Yellow
        Write-Host "  Debes agregar un número de teléfono en Meta for Developers:" -ForegroundColor Yellow
        Write-Host "  1. Ve a Meta for Developers > Tu App > WhatsApp > API Setup" -ForegroundColor Gray
        Write-Host "  2. Haz clic en 'Add phone number'" -ForegroundColor Gray
        Write-Host "  3. Sigue las instrucciones para verificar el número" -ForegroundColor Gray
        Write-Host ""
        
        if (-not [string]::IsNullOrEmpty($PhoneNumber)) {
            Write-Host "  Nota: El número $PhoneNumber no está asociado a esta aplicación." -ForegroundColor Yellow
            Write-Host "  Debes agregarlo primero en Meta for Developers." -ForegroundColor Yellow
        }
        
        $phoneNumberId = Read-Host "Ingresa el Phone Number ID manualmente (o presiona Enter para salir)"
        if ([string]::IsNullOrEmpty($phoneNumberId)) {
            Write-Host "  Operación cancelada." -ForegroundColor Yellow
            exit 0
        }
    }
} catch {
    Write-Host "  ✗ Error al obtener Phone Number ID: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.ErrorDetails.Message) {
        Write-Host "  Detalles: $($_.ErrorDetails.Message)" -ForegroundColor Gray
    }
    
    if (-not [string]::IsNullOrEmpty($PhoneNumber)) {
        Write-Host "  Intentando buscar el número $PhoneNumber manualmente..." -ForegroundColor Yellow
    }
    
    $phoneNumberId = Read-Host "Ingresa el Phone Number ID manualmente"
    if ([string]::IsNullOrEmpty($phoneNumberId)) {
        Write-Host "  Error: Phone Number ID es requerido" -ForegroundColor Red
        exit 1
    }
}
Write-Host ""

# Configurar webhook
Write-Host "[5/7] Configurando webhook..." -ForegroundColor Yellow
try {
    $webhookUrl = "https://graph.facebook.com/v18.0/$AppId/subscriptions"
    $webhookHeaders = @{
        "Authorization" = "Bearer $AccessToken"
        "Content-Type" = "application/json"
    }
    $webhookBody = @{
        object = "whatsapp_business_account"
        callback_url = $WebhookUrl
        verify_token = $VerifyToken
        fields = @("messages")
    } | ConvertTo-Json
    
    # Primero, obtener suscripciones existentes
    $existingSubscriptions = Invoke-RestMethod -Uri "https://graph.facebook.com/v18.0/$AppId/subscriptions" -Method Get -Headers $webhookHeaders
    
    if ($existingSubscriptions.data.Count -gt 0) {
        Write-Host "  ⚠ Ya existe una suscripción. Actualizando..." -ForegroundColor Yellow
        $subscriptionId = $existingSubscriptions.data[0].id
        $updateUrl = "https://graph.facebook.com/v18.0/$AppId/subscriptions"
        
        $updateBody = @{
            callback_url = $WebhookUrl
            verify_token = $VerifyToken
            fields = @("messages")
        } | ConvertTo-Json
        
        Invoke-RestMethod -Uri $updateUrl -Method Post -Headers $webhookHeaders -Body $updateBody | Out-Null
        Write-Host "  ✓ Webhook actualizado" -ForegroundColor Green
    } else {
        Invoke-RestMethod -Uri $webhookUrl -Method Post -Headers $webhookHeaders -Body $webhookBody | Out-Null
        Write-Host "  ✓ Webhook configurado" -ForegroundColor Green
    }
} catch {
    Write-Host "  ✗ Error al configurar webhook: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "  Puedes configurarlo manualmente en Meta for Developers" -ForegroundColor Yellow
    Write-Host "  URL: $WebhookUrl" -ForegroundColor Gray
    Write-Host "  Verify Token: $VerifyToken" -ForegroundColor Gray
}
Write-Host ""

# Suscribirse a eventos
Write-Host "[6/7] Suscribiéndose a eventos de mensajes..." -ForegroundColor Yellow
try {
    # La suscripción ya se hizo en el paso anterior con "fields": ["messages"]
    Write-Host "  ✓ Suscrito a eventos de mensajes" -ForegroundColor Green
} catch {
    Write-Host "  ⚠ Advertencia: $($_.Exception.Message)" -ForegroundColor Yellow
}
Write-Host ""

# Validar configuración
Write-Host "[7/7] Validando configuración..." -ForegroundColor Yellow
try {
    $validationUrl = "https://graph.facebook.com/v18.0/$AppId/subscriptions"
    $validationHeaders = @{
        "Authorization" = "Bearer $AccessToken"
    }
    
    $subscriptions = Invoke-RestMethod -Uri $validationUrl -Method Get -Headers $validationHeaders
    if ($subscriptions.data.Count -gt 0) {
        $sub = $subscriptions.data[0]
        Write-Host "  ✓ Configuración validada:" -ForegroundColor Green
        Write-Host "    Callback URL: $($sub.callback_url)" -ForegroundColor Gray
        Write-Host "    Objeto: $($sub.object)" -ForegroundColor Gray
        Write-Host "    Campos: $($sub.fields -join ', ')" -ForegroundColor Gray
    } else {
        Write-Host "  ⚠ No se encontraron suscripciones activas" -ForegroundColor Yellow
    }
} catch {
    Write-Host "  ⚠ No se pudo validar la configuración: $($_.Exception.Message)" -ForegroundColor Yellow
}
Write-Host ""

# Resumen final
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Configuración Completada" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Información de configuración:" -ForegroundColor Cyan
Write-Host "  App ID: $AppId" -ForegroundColor White
Write-Host "  Phone Number ID: $phoneNumberId" -ForegroundColor White
if (-not [string]::IsNullOrEmpty($PhoneNumber)) {
    Write-Host "  Número configurado: $PhoneNumber" -ForegroundColor White
}
Write-Host "  Webhook URL: $WebhookUrl" -ForegroundColor White
Write-Host "  Verify Token: $VerifyToken" -ForegroundColor White
Write-Host ""
Write-Host "Configura estos valores en tu Azure Function App:" -ForegroundColor Yellow
Write-Host "  WhatsApp:PhoneNumberId = $phoneNumberId" -ForegroundColor Gray
Write-Host "  WhatsApp:AccessToken = [tu-access-token-permanente]" -ForegroundColor Gray
Write-Host ""
Write-Host "Nota: El Access Token usado aquí puede ser temporal." -ForegroundColor Yellow
Write-Host "Para producción, usa un Access Token permanente o System User Token." -ForegroundColor Yellow
Write-Host ""

# Limpiar variables sensibles
$AppSecretPlain = $null
$AccessToken = $null
$FunctionKey = $null
