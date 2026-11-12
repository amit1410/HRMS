Set-StrictMode -Version Latest

function Get-HrmsTaskActionProperty {
    param([Parameter(Mandatory)] [object] $Action, [Parameter(Mandatory)] [string] $Name)
    $property = $Action.PSObject.Properties[$Name]
    if ($null -ne $property) { return $property.Value }
    $cimPropertyBag = $Action.PSObject.Properties['CimInstanceProperties']
    $cimProperties = if ($null -ne $cimPropertyBag) { $cimPropertyBag.Value } else { $null }
    if ($null -ne $cimProperties) {
        foreach ($cimProperty in $cimProperties) {
            if ($null -ne $cimProperty -and $cimProperty.Name -eq $Name) { return $cimProperty.Value }
        }
    }
    return $null
}

function Test-HrmsTaskActionMatch {
    param([Parameter(Mandatory)] [object] $Action, [Parameter(Mandatory)] [string] $Pattern)
    foreach ($name in @('Execute', 'Arguments', 'ClassId', 'Data')) {
        $value = Get-HrmsTaskActionProperty -Action $Action -Name $name
        if ($null -ne $value -and ([string]$value) -match $Pattern) { return $true }
    }
    return $false
}

function Get-HrmsSafeFailureDiagnostic {
    param(
        [Parameter(Mandatory)] [System.Management.Automation.ErrorRecord] $ErrorRecord,
        [Parameter(Mandatory)] [string] $Stage,
        [Parameter(Mandatory)] [bool] $MutationStarted
    )
    # Keep the original exception only in memory for classification; never serialize it.
    $script:HrmsLastOriginalException = $ErrorRecord.Exception
    $exception = $ErrorRecord.Exception
    $typeName = $exception.GetType().FullName
    $category = 'exception.' + $exception.GetType().Name
    $nativeCode = $null
    if ($exception -is [System.Management.Automation.PropertyNotFoundException]) {
        $category = 'powershell.property_not_found'
    } elseif ($exception -is [System.UnauthorizedAccessException]) {
        $category = 'windows.access_denied'
        $nativeCode = 5
    } elseif ($exception -is [System.ComponentModel.Win32Exception]) {
        $nativeCode = $exception.NativeErrorCode
        if ($nativeCode -eq 5) { $category = 'windows.access_denied' }
        else { $category = 'windows.native_error' }
    } elseif ($exception -is [System.IO.FileNotFoundException]) {
        $category = 'filesystem.file_not_found'
    } elseif ($exception -is [System.IO.DirectoryNotFoundException]) {
        $category = 'filesystem.directory_not_found'
    } elseif ($exception -is [System.Security.Cryptography.CryptographicException]) {
        $category = 'certificate.cryptographic_error'
    } else {
        # Provider-specific exceptions are classified without depending on assembly load.
        $numberProperty = $exception.PSObject.Properties['Number']
        if ($typeName -match 'MySqlException' -and $null -ne $numberProperty) {
            $nativeCode = [int]$numberProperty.Value
            $category = switch ($nativeCode) {
                1045 { 'mysql.authentication_failed'; break }
                2026 { 'mysql.tls_error'; break }
                default { 'mysql.provider_error' }
            }
        }
    }
    $hresult = '0x{0:X8}' -f ($exception.HResult -band 0xffffffffL)
    return [pscustomobject]@{
        Stage = $Stage
        ExceptionType = $typeName
        SafeErrorCategory = $category
        ErrorCode = $nativeCode
        HResult = $hresult
        MutationStarted = $MutationStarted
        CanProceed = $false
    }
}
