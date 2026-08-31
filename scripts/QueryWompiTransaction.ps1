$transactionId = $args[0]
$privateKey = $env:WOMPI_TEST_PRIVATE_KEY
if ([string]::IsNullOrWhiteSpace($transactionId)) {
    throw 'Uso: .\scripts\QueryWompiTransaction.ps1 <transaction-id>'
}
if ([string]::IsNullOrWhiteSpace($privateKey) -or -not $privateKey.StartsWith('prv_test_')) {
    throw 'Configura WOMPI_TEST_PRIVATE_KEY con una llave privada sandbox vigente. No guardes llaves en el repositorio.'
}
$urls = @(
    "https://sandbox.wompi.co/v1/transactions/$transactionId",
    "https://production.wompi.co/v1/transactions/$transactionId"
)
foreach ($url in $urls) {
    Write-Host "`n=== $url ==="
    try {
        $headers = @{ "Authorization" = "Bearer $privateKey" }
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $response = Invoke-RestMethod -Uri $url -Headers $headers -Method Get
        $response | ConvertTo-Json -Depth 10
        break
    } catch {
        Write-Host "Error: $($_.Exception.Message)"
    }
}
