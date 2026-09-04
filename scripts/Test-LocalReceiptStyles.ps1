param(
    [string]$EdgeUrl = "http://127.0.0.1:47831"
)

$ErrorActionPreference = "Stop"

function Get-ActiveEdgeSession {
    $history = Join-Path $env:LOCALAPPDATA "Auraly\PosEdge\webview2\EBWebView\Default\History"
    $historyCopy = Join-Path $env:TEMP ("auraly-history-{0}.db" -f [guid]::NewGuid().ToString("N"))
    Copy-Item -LiteralPath $history -Destination $historyCopy
    try {
        $urls = & python -c @'
import sqlite3
import sys

connection = sqlite3.connect(sys.argv[1])
try:
    for row in connection.execute(
        "select url from urls where url like '%edgeToken=%' order by last_visit_time desc limit 20"
    ):
        print(row[0])
finally:
    connection.close()
'@ $historyCopy
    }
    finally {
        Remove-Item -LiteralPath $historyCopy -Force
    }

    foreach ($url in $urls) {
        if ($url -notmatch "edgeToken=([^&]+)") { continue }
        $candidate = [uri]::UnescapeDataString($Matches[1])
        try {
            $response = Invoke-WebRequest -UseBasicParsing `
                -Uri "$EdgeUrl/edge/v1/health" `
                -Headers @{ "X-Auraly-Edge-Session" = $candidate } `
                -TimeoutSec 5
            if ($response.StatusCode -eq 200) { return $candidate }
        }
        catch {
            continue
        }
    }

    throw "No se encontró una sesión local activa."
}

function Send-PrintJob {
    param(
        [string]$Name,
        [string]$Path,
        [hashtable]$Payload,
        [string]$Session
    )

    $response = Invoke-WebRequest -UseBasicParsing `
        -Method Post `
        -Uri "$EdgeUrl$Path" `
        -Headers @{ "X-Auraly-Edge-Session" = $Session } `
        -ContentType "application/json" `
        -Body ($Payload | ConvertTo-Json -Depth 8) `
        -TimeoutSec 120
    "{0}: HTTP {1}" -f $Name, $response.StatusCode
}

$session = Get-ActiveEdgeSession
$now = [DateTimeOffset]::Now

$sale = @{
    documentId = [guid]::NewGuid()
    documentType = "SalesInvoice"
    documentNumber = "VTA09-00000017"
    fiscalNumber = "VTA09-00000017"
    issuedAt = $now
    customerIdentification = "222222222222"
    customerName = "Consumidor final"
    companyName = "Auraly"
    businessName = "Auraly"
    warehouseName = "Bodega de venta"
    lines = @(@{
        productCode = "300983122"
        description = "Producto regresión 300983122 editado"
        quantity = 1
        unitPrice = 13025
        discount = 0
        tax = 2475
        total = 15500
        taxCode = "01"
        taxRate = 19
    })
    payments = @(@{ methodCode = "Cash"; amount = 15500 })
    untaxedAmount = 13025
    taxAmount = 2475
    payableAmount = 15500
    cufe = "14fcc1322962cae04fad6938c7d9dfadc9a8d0d550f60be060931e8975f2e993b461ff8ed55e0db5d58fcaa026dff229b5f"
    qrPayload = "https://catalogo-vpfe-hab.dian.gov.co/document/searchqr?documentkey=14fcc1322962cae04fad6938c7d9dfadc9a8d0d550f60be060931e8975f2e993b461ff8ed55e0db5d58fcaa026dff229b5f"
}

$salesReceipt = $sale.Clone()
$salesReceipt.documentId = [guid]::NewGuid()
$salesReceipt.documentType = "SalesReceipt"
$salesReceipt.documentNumber = "CVI00-00000001"
$salesReceipt.fiscalNumber = $null
$salesReceipt.cufe = $null
$salesReceipt.qrPayload = $null

$entry = @{
    documentId = [guid]::NewGuid()
    direction = "In"
    reasonName = "Fondo de caja"
    amount = 10000
    occurredAt = $now
    reference = "PRUEBA LOCAL"
    notes = "Verificación de diseño"
    responsibleName = "Richard Jacome"
}

$exit = @{
    documentId = [guid]::NewGuid()
    direction = "Out"
    reasonName = "Gasto operativo"
    amount = 5000
    occurredAt = $now
    reference = "PRUEBA LOCAL"
    notes = "Verificación de diseño"
    responsibleName = "Richard Jacome"
}

$closure = @{
    workSessionClosureId = [guid]::NewGuid()
    workSessionId = [guid]::NewGuid()
    businessId = [guid]::NewGuid()
    businessName = "Auraly"
    warehouseId = [guid]::NewGuid()
    warehouseName = "Bodega de venta"
    userId = [guid]::NewGuid()
    userName = "Richard Jacome"
    deviceId = [guid]::NewGuid()
    openedAt = $now.AddHours(-8)
    closedAt = $now
    totalSales = 15500
    totalRefunds = 0
    totalOther = 5000
    netAmount = 20500
    expectedCash = 20500
    countedCash = 20500
    cashDifference = 0
    note = "Prueba local de formato"
    paymentTotals = @(
        @{ paymentMethodCode = "Cash"; salesAmount = 15500; refundAmount = 0; otherAmount = 5000; netAmount = 20500; countedAmount = 20500; difference = 0; requiresCount = $true },
        @{ paymentMethodCode = "DebitCard"; salesAmount = 0; refundAmount = 0; otherAmount = 0; netAmount = 0; countedAmount = 0; difference = 0; requiresCount = $true },
        @{ paymentMethodCode = "Transfer"; salesAmount = 0; refundAmount = 0; otherAmount = 0; netAmount = 0; countedAmount = 0; difference = 0; requiresCount = $true }
    )
    salesCount = 1
    creditSalesCount = 0
    creditSalesAmount = 0
    returnCount = 0
}

Send-PrintJob "Factura" "/edge/v1/print/receipt?workflow=pos" $sale $session
Send-PrintJob "Comprobante" "/edge/v1/print/receipt?workflow=pos" $salesReceipt $session
Send-PrintJob "Entrada" "/edge/v1/print/cash-movement" $entry $session
Send-PrintJob "Salida" "/edge/v1/print/cash-movement" $exit $session
Send-PrintJob "Cierre" "/edge/v1/print/work-session-closure" $closure $session
Send-PrintJob "Pedido factura" "/edge/v1/print/receipt?workflow=orders" $sale $session
Send-PrintJob "Pedido comprobante" "/edge/v1/print/receipt?workflow=orders" $salesReceipt $session
