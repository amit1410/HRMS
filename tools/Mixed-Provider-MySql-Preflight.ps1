[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$ShardKey,

    [string]$Server = "127.0.0.1",
    [int]$Port = 3306,
    [string]$RetainedDatabase = "HRMS_MySqlAdapter_Test_20260905",
    [string]$User = "root"
)

$ErrorActionPreference = "Stop"
$expectedRetainedDatabase = "HRMS_MySqlAdapter_Test_20260905"
$requiredMigration = "20260905172008_InitialMySqlTenantSchema"
$requiredTables = @("Users", "Roles", "Permissions", "Tenants")
$oldMysqlPassword = $env:MYSQL_PWD
$securePassword = $null
$plainPassword = $null

function Invoke-MySqlRead {
    param(
        [Parameter(Mandatory = $true)][string]$DatabaseName,
        [Parameter(Mandatory = $true)][string]$Query
    )

    $arguments = @(
        "--host=$Server", "--port=$Port", "--user=$User", "--database=$DatabaseName",
        "--batch", "--raw", "--skip-column-names", "--execute=$Query"
    )

    $output = @(& mysql @arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "mysql read-only query failed for the requested database."
    }

    @(
        $output |
            ForEach-Object { [string]$_ } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { $_.Trim() }
    )
}

function Assert-MySqlDatabase {
    param(
        [Parameter(Mandatory = $true)][string]$DatabaseName,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $identity = @(
        Invoke-MySqlRead `
            -DatabaseName $DatabaseName `
            -Query "SELECT DATABASE(), @@lower_case_table_names;"
    )

    if ($identity.Count -ne 1) {
        throw "$Label identity result was unexpected."
    }

    $identityRow = [string]$identity[0]
    $parts = $identityRow -split "`t", 2
    if (
        ($parts.Count -ne 2) -or
        [string]::IsNullOrWhiteSpace($parts[0]) -or
        [string]::IsNullOrWhiteSpace($parts[1])
    ) {
        throw "$Label identity result was unexpected."
    }

    $actualDatabase = [string]$parts[0]
    $lowerCaseTableNames = [string]$parts[1]

    Write-Host "$Label configured database: $DatabaseName"
    Write-Host "$Label actual DATABASE(): $actualDatabase"
    Write-Host "$Label lower_case_table_names: $lowerCaseTableNames"

    if ($lowerCaseTableNames -notin @("0", "1", "2")) {
        throw "$Label returned unexpected lower_case_table_names value '$lowerCaseTableNames'."
    }

    if ($lowerCaseTableNames -eq "0") {
        if ($actualDatabase -cne $DatabaseName) {
            throw "$Label returned database '$actualDatabase' instead of the case-sensitive requested database identity."
        }
    }
    else {
        if ($actualDatabase -ine $DatabaseName) {
            throw "$Label returned database '$actualDatabase' instead of the requested database identity."
        }
    }

    $history = @(
        Invoke-MySqlRead -DatabaseName $DatabaseName -Query @"
SELECT MigrationId
FROM __EFMigrationsHistory
ORDER BY MigrationId;
"@
    )

    if ($history -notcontains $requiredMigration) {
        throw "$Label required tenant migration is absent."
    }

    foreach ($table in $requiredTables) {
        $count = @(
            Invoke-MySqlRead `
                -DatabaseName $DatabaseName `
                -Query "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = '$table';"
        )

        if ($count.Count -ne 1) {
            throw "$Label required table '$table' count result was unexpected."
        }

        $parsedCount = [int64]::Parse(
            [string]$count[0],
            [Globalization.CultureInfo]::InvariantCulture
        )

        if ($parsedCount -ne 1) {
            throw "$Label required table '$table' is absent."
        }
    }

    Write-Host "$Label migration/schema: PASS"

    [pscustomobject]@{
        ConfiguredDatabase = $DatabaseName
        ActualDatabase = $actualDatabase
        LowerCaseTableNames = $lowerCaseTableNames
    }
}

function Assert-RetainedSchemaIdentity {
    $matches = @(
        Invoke-MySqlRead -DatabaseName $RetainedDatabase -Query @"
SELECT SCHEMA_NAME
FROM information_schema.SCHEMATA
WHERE LOWER(SCHEMA_NAME) = LOWER('HRMS_MySqlAdapter_Test_20260905')
ORDER BY SCHEMA_NAME;
"@
    )

    if ($matches.Count -eq 0) {
        throw "MYSQL TEMPLATE DATABASE IDENTITY BLOCKED - retained schema was not found."
    }

    if ($matches.Count -gt 1) {
        throw "MYSQL TEMPLATE DATABASE IDENTITY BLOCKED - CASE-VARIANT SCHEMA COLLISION"
    }

    return [string]$matches[0]
}

try {
    if (-not (Get-Command mysql.exe -ErrorAction SilentlyContinue)) {
        throw "mysql.exe was not found."
    }

    if ($RetainedDatabase -cne $expectedRetainedDatabase) {
        throw "This preflight only permits $expectedRetainedDatabase."
    }

    if ($ShardKey -cnotmatch '^[a-z0-9][a-z0-9_-]{0,63}$') {
        throw "ShardKey must be 1-64 lowercase ASCII letters, digits, '-' or '_', starting with a letter or digit."
    }

    $templateDatabase = "HRMS_$ShardKey"
    Write-Host "Template-produced database name: $templateDatabase"

    $securePassword = Read-Host "MySQL password" -AsSecureString
    $plainPassword = [System.Net.NetworkCredential]::new("", $securePassword).Password
    if ([string]::IsNullOrWhiteSpace($plainPassword)) {
        throw "Password was empty; preflight was not started."
    }

    $env:MYSQL_PWD = $plainPassword

    $retained = Assert-MySqlDatabase `
        -DatabaseName $RetainedDatabase `
        -Label "RETAINED-NAME CHECK"

    $uniquePhysicalSchema = Assert-RetainedSchemaIdentity

    try {
        $template = Assert-MySqlDatabase `
            -DatabaseName $templateDatabase `
            -Label "PRODUCTION-TEMPLATE-NAME CHECK"
    }
    catch {
        throw "MYSQL TEMPLATE DATABASE IDENTITY BLOCKED - $($_.Exception.Message)"
    }

    if ($template.LowerCaseTableNames -cne $retained.LowerCaseTableNames) {
        throw "MYSQL TEMPLATE DATABASE IDENTITY BLOCKED - lower_case_table_names mismatch"
    }

    if ($retained.ActualDatabase -ine $template.ActualDatabase) {
        throw "MYSQL TEMPLATE DATABASE IDENTITY BLOCKED - retained and template connections resolved to different database identities"
    }

    if ($retained.LowerCaseTableNames -eq "0") {
        if ($retained.ActualDatabase -cne $uniquePhysicalSchema) {
            throw "MYSQL TEMPLATE DATABASE IDENTITY BLOCKED - retained case-sensitive physical schema identity mismatch"
        }

        if ($template.ActualDatabase -cne $templateDatabase) {
            throw "MYSQL TEMPLATE DATABASE IDENTITY BLOCKED - case-sensitive template database identity mismatch"
        }
    }
    else {
        if ($retained.ActualDatabase -ine $uniquePhysicalSchema) {
            throw "MYSQL TEMPLATE DATABASE IDENTITY BLOCKED - retained physical schema identity mismatch"
        }

        if ($template.ActualDatabase -ine $uniquePhysicalSchema) {
            throw "MYSQL TEMPLATE DATABASE IDENTITY BLOCKED - template physical schema identity mismatch"
        }
    }

    Write-Host "Configured retained DB: $($retained.ConfiguredDatabase)"
    Write-Host "Template-produced DB: $($template.ConfiguredDatabase)"
    Write-Host "Template actual DATABASE(): $($template.ActualDatabase)"
    Write-Host "RUNNER EXPECTED MYSQL DATABASE: $($template.ActualDatabase)"
    Write-Host "MYSQL TENANT PREFLIGHT PASS"
}
finally {
    if ($null -eq $oldMysqlPassword) {
        Remove-Item Env:MYSQL_PWD -ErrorAction SilentlyContinue
    }
    else {
        $env:MYSQL_PWD = $oldMysqlPassword
    }

    $plainPassword = $null

    if ($null -ne $securePassword) {
        $securePassword.Dispose()
        $securePassword = $null
    }
}
