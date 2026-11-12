Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot '..\backup\HrmsTlsMaintenanceDiagnostics.ps1')
$passed = 0
function Assert-Hrms { param([bool]$Condition, [string]$Name) if (-not $Condition) { throw "FAIL: $Name" }; $script:passed++ }
$exec = [pscustomobject]@{ Execute='C:\tools\mysqld.exe'; Arguments='--diagnostic' }
$com = [pscustomobject]@{ ClassId='{12345678-1234-1234-1234-123456789abc}'; Data='handler payload' }
Assert-Hrms (Test-HrmsTaskActionMatch -Action $exec -Pattern 'mysqld') 'Exec action matching'
Assert-Hrms (-not (Test-HrmsTaskActionMatch -Action $com -Pattern 'mysqld')) 'COM action safely ignored'
$propertyException = [System.Management.Automation.PropertyNotFoundException]::new('synthetic private detail')
$propertyError = [System.Management.Automation.ErrorRecord]::new($propertyException, 'synthetic', [System.Management.Automation.ErrorCategory]::InvalidOperation, $null)
$d = Get-HrmsSafeFailureDiagnostic -ErrorRecord $propertyError -Stage 'scheduled_task_inventory' -MutationStarted $false
Assert-Hrms ($d.Stage -eq 'scheduled_task_inventory' -and $d.ExceptionType -eq 'System.Management.Automation.PropertyNotFoundException' -and $d.SafeErrorCategory -eq 'powershell.property_not_found') 'Property error classification'
Assert-Hrms ($d.HResult -eq '0x80131501' -and -not $d.MutationStarted -and -not $d.CanProceed) 'Fail closed with HRESULT and mutation state'
Assert-Hrms (-not ($d | ConvertTo-Json -Compress).Contains('synthetic private detail')) 'No raw message in diagnostic'
$accessException = [System.UnauthorizedAccessException]::new('private detail')
$accessError = [System.Management.Automation.ErrorRecord]::new($accessException, 'access', [System.Management.Automation.ErrorCategory]::PermissionDenied, $null)
$d2 = Get-HrmsSafeFailureDiagnostic -ErrorRecord $accessError -Stage 'file_check' -MutationStarted $false
Assert-Hrms ($d2.SafeErrorCategory -eq 'windows.access_denied') 'Access denied classification'
$unknownException = [System.InvalidOperationException]::new('secret text')
$unknownError = [System.Management.Automation.ErrorRecord]::new($unknownException, 'unknown', [System.Management.Automation.ErrorCategory]::NotSpecified, $null)
$d3 = Get-HrmsSafeFailureDiagnostic -ErrorRecord $unknownError -Stage 'configuration_read' -MutationStarted $false
Assert-Hrms ($d3.ExceptionType -eq 'System.InvalidOperationException' -and $d3.SafeErrorCategory -eq 'exception.InvalidOperationException') 'Concrete unknown exception retained'
Assert-Hrms (-not ($d3 | ConvertTo-Json -Compress).Contains('secret text')) 'Unknown message suppressed'
Write-Output "PASS=$passed; FAIL=0; TESTS=8"