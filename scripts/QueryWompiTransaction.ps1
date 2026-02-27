$transactionId = "12040200-1771985403-85800"
$privateKey = "prv_test_RJKAG9S0lm8tJTuFCmdao8FhXqmXrm0t"
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
