# Script para crear un payment link en Wompi sandbox
# Uso: .\scripts\TestWompiCreateLink.ps1
# Si falla por política de ejecución: powershell -ExecutionPolicy Bypass -File .\scripts\TestWompiCreateLink.ps1

$baseUrl = "https://sandbox.wompi.co/v1"
$privateKey = "prv_test_RJKAG9S0lm8tJTuFCmdao8FhXqmXrm0t"
$sku = "86ea4bec64084668b24dbff118949ef7"

$body = @{
    name             = "Test - Ver datos"
    description      = "Link de prueba para verificar respuesta"
    single_use       = $true
    collect_shipping = $false
    currency         = "COP"
    amount_in_cents  = 150000
    sku              = $sku
} | ConvertTo-Json

$headers = @{
    "Authorization" = "Bearer $privateKey"
    "Content-Type"  = "application/json"
}

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

try {
    $response = Invoke-RestMethod -Uri "$baseUrl/payment_links" -Method Post -Headers $headers -Body $body
    Write-Host "`n=== Respuesta de Wompi POST /v1/payment_links ===" -ForegroundColor Green
    $response | ConvertTo-Json -Depth 5
    Write-Host "`nLink de checkout: https://checkout.wompi.co/l/$($response.data.id)" -ForegroundColor Cyan
} catch {
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.ErrorDetails.Message) {
        Write-Host $_.ErrorDetails.Message
    }
}
