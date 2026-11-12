$ErrorActionPreference = 'Stop'
$setup = Join-Path $PSScriptRoot 'Initialize-HRMSWeb1BackupConfiguration.ps1'
& $setup -SelfTest
