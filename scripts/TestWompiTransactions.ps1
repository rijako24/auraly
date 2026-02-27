# Script para consultar transacciones de un payment link en Wompi sandbox
# Uso: .\scripts\TestWompiTransactions.ps1 [payment_link_id]
#   Sin argumentos usa test_Co6eaT por defecto
# Si falla por política de ejecución: powershell -ExecutionPolicy Bypass -File .\scripts\TestWompiTransactions.ps1

$baseUrl = "https://sandbox.wompi.co/v1"
$privateKey = "prv_test_RJKAG9S0lm8tJTuFCmdao8FhXqmXrm0t"

$paymentLinkId = if ($args[0]) { $args[0] } else { "test_Co6eaT" }

# Parámetros: from_date, until_date, page, page_size. La API ignora payment_link_id; el filtrado se hace en la app.
$fromDate = (Get-Date).AddDays(-30).ToString("yyyy-MM-dd")
$untilDate = (Get-Date).ToString("yyyy-MM-dd")
$page = 1
$pageSize = 50

$queryParams = "from_date=$fromDate&until_date=$untilDate&page=$page&page_size=$pageSize"
$uri = "$baseUrl/transactions?$queryParams"

$headers = @{
    "Authorization" = "Bearer $privateKey"
    "Content-Type"  = "application/json"
}

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

try {
    Write-Host "Consultando transacciones para payment_link_id: $paymentLinkId ($fromDate -> $untilDate, página $page)" -ForegroundColor Cyan
    $response = Invoke-RestMethod -Uri $uri -Headers $headers -Method Get
    Write-Host "`n=== Respuesta de Wompi GET /v1/transactions ===" -ForegroundColor Green
    $response | ConvertTo-Json -Depth 6
}
catch {
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.ErrorDetails.Message) {
        Write-Host $_.ErrorDetails.Message
    }
    exit 1
}
