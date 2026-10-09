Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$userSecretsId = 'af221e70-58f4-4c5b-bbf0-034f30422395'
$secretsDirectory = Join-Path $env:APPDATA "Microsoft\UserSecrets\$userSecretsId"
$secretsPath = Join-Path $secretsDirectory 'secrets.json'

$securePassword = Read-Host 'Hostinger SMTP password (input is hidden)' -AsSecureString
$passwordPointer = [IntPtr]::Zero

try {
    $passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
    $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)

    New-Item -ItemType Directory -Path $secretsDirectory -Force | Out-Null
    $secrets = [ordered]@{}
    if (Test-Path -LiteralPath $secretsPath) {
        $existing = Get-Content -LiteralPath $secretsPath -Raw | ConvertFrom-Json
        foreach ($property in $existing.PSObject.Properties) {
            $secrets[$property.Name] = [string]$property.Value
        }
    }

    $secrets['Email:Smtp:Password'] = $password
    [System.IO.File]::WriteAllText($secretsPath, (($secrets | ConvertTo-Json -Depth 5) + [Environment]::NewLine))
    Write-Host 'SMTP password stored in the local .NET user-secrets store.'
}
finally {
    if ($passwordPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
    }
    $password = $null
    $securePassword = $null
}
