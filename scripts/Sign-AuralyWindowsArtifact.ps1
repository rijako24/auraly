[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string[]]$Path,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9A-Fa-f]{40}$')]
    [string]$CertificateThumbprint,

    [Parameter(Mandatory)]
    [ValidatePattern('^https://')]
    [string]$TimestampUrl,

    [ValidateSet('CurrentUser', 'LocalMachine')]
    [string]$CertificateStoreLocation = 'CurrentUser',

    [string]$SignToolPath
)

$ErrorActionPreference = 'Stop'
$normalizedThumbprint = $CertificateThumbprint.ToUpperInvariant()

function Resolve-SignTool {
    if (-not [string]::IsNullOrWhiteSpace($SignToolPath)) {
        $resolved = (Resolve-Path -LiteralPath $SignToolPath -ErrorAction Stop).Path
        if ([IO.Path]::GetFileName($resolved) -ne 'signtool.exe') {
            throw 'SignToolPath debe apuntar a signtool.exe.'
        }
        return $resolved
    }

    $installedRoots = Get-ItemProperty `
        'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots' `
        -ErrorAction SilentlyContinue
    $kitsRoot = $installedRoots.KitsRoot10
    if (-not [string]::IsNullOrWhiteSpace($kitsRoot)) {
        $candidate = Get-ChildItem -LiteralPath (Join-Path $kitsRoot 'bin') `
            -Recurse -Filter signtool.exe -File -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($null -ne $candidate) {
            return $candidate.FullName
        }
    }
    return $null
}

$store = [Security.Cryptography.X509Certificates.X509Store]::new(
    [Security.Cryptography.X509Certificates.StoreName]::My,
    [Security.Cryptography.X509Certificates.StoreLocation]::$CertificateStoreLocation)
try {
    $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
    $matchingCertificates = @(
        $store.Certificates |
            Where-Object { $_.Thumbprint -eq $normalizedThumbprint })
    if ($matchingCertificates.Count -ne 1 -or
        -not $matchingCertificates[0].HasPrivateKey) {
        throw "El certificado $normalizedThumbprint no existe en $CertificateStoreLocation\\My o no tiene clave privada."
    }
    $certificate = $matchingCertificates[0]
    $codeSigningOid = '1.3.6.1.5.5.7.3.3'
    $ekuExtension = $certificate.Extensions |
        Where-Object { $_.Oid.Value -eq '2.5.29.37' } |
        Select-Object -First 1
    $enhancedKeyUsages = if ($null -eq $ekuExtension) {
        @()
    }
    else {
        ([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new(
            $ekuExtension,
            $ekuExtension.Critical)).EnhancedKeyUsages |
            Select-Object -ExpandProperty Value
    }
    if ($enhancedKeyUsages -notcontains $codeSigningOid) {
        throw "El certificado $normalizedThumbprint no permite firma de código."
    }
    $isSelfSigned = $certificate.Subject -eq $certificate.Issuer
}
finally {
    $store.Dispose()
}

$signTool = Resolve-SignTool
foreach ($item in $Path) {
    $resolvedPath = (Resolve-Path -LiteralPath $item -ErrorAction Stop).Path
    if ($null -eq $signTool) {
        if ($isSelfSigned) {
            # Los servicios públicos de timestamp pueden rechazar certificados
            # autofirmados. Esta ruta existe solo para artefactos locales de prueba.
            $signed = Set-AuthenticodeSignature `
                -LiteralPath $resolvedPath `
                -Certificate $certificate `
                -HashAlgorithm SHA256
        }
        else {
            $signed = Set-AuthenticodeSignature `
                -LiteralPath $resolvedPath `
                -Certificate $certificate `
                -HashAlgorithm SHA256 `
                -TimestampServer $TimestampUrl
        }
        if ($signed.Status -ne 'Valid') {
            throw "PowerShell no pudo firmar ${resolvedPath}: $($signed.StatusMessage)"
        }
        continue
    }
    $arguments = @(
        'sign',
        '/sha1', $normalizedThumbprint,
        '/s', 'My',
        '/fd', 'SHA256'
    )
    if (-not $isSelfSigned) {
        $arguments += @('/tr', $TimestampUrl, '/td', 'SHA256')
    }
    if ($CertificateStoreLocation -eq 'LocalMachine') {
        $arguments += '/sm'
    }
    $arguments += $resolvedPath
    & $signTool @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "SignTool no pudo firmar $resolvedPath."
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $resolvedPath
    if ($signature.Status -ne 'Valid' -or
        $signature.SignerCertificate.Thumbprint -ne $normalizedThumbprint) {
        throw "La firma Authenticode de $resolvedPath no es válida o usa otro certificado."
    }
}
