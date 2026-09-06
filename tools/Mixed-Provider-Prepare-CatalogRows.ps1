[CmdletBinding()]
param([string]$Server = "lpc:.")

$ErrorActionPreference = "Stop"
$database = "HRMS_Catalog"
$sqlTenantId = "77777777-7777-7777-7777-777777777701"
$mySqlTenantId = "77777777-7777-7777-7777-777777777702"
$sqlShardKey = "adapter-sql-tenant-test-20260906"
$mySqlShardKey = "mysqladapter_test_20260905"
$query = @"
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() <> N'HRMS_Catalog' THROW 51001, 'Catalog identity gate failed.', 1;
IF (SELECT COUNT(*) FROM dbo.Tenants WHERE Id = '11111111-1111-1111-1111-111111111111' AND TenantCode = N'DEMO01' AND Host = N'demo01.localhost' AND ShardKey = N'demo01' AND Status = 1 AND DatabaseProvider = N'SqlServer') <> 1 THROW 51002, 'DEMO01 verification failed.', 1;
IF (SELECT COUNT(*) FROM dbo.Tenants WHERE Id = '22222222-2222-2222-2222-222222222222' AND TenantCode = N'DEMO02' AND Host = N'demo02.localhost' AND ShardKey = N'demo02' AND Status = 3 AND DatabaseProvider = N'SqlServer') <> 1 THROW 51003, 'DEMO02 verification failed.', 1;
IF EXISTS (SELECT 1 FROM dbo.Tenants WHERE Id IN ('$sqlTenantId', '$mySqlTenantId') OR TenantCode IN (N'ADAPTERSQL', N'ADAPTERMYSQL') OR Host IN (N'adaptersql.localhost', N'adaptermysql.localhost') OR ShardKey IN (N'$sqlShardKey', N'$mySqlShardKey')) THROW 51004, 'Disposable catalog identity collision.', 1;
BEGIN TRANSACTION;
INSERT INTO dbo.Tenants (Id, TenantCode, Host, ShardKey, TenantName, Email, Phone, Address, Status, CreatedDate, ModifiedDate, DatabaseProvider)
VALUES ('$sqlTenantId', N'ADAPTERSQL', N'adaptersql.localhost', N'$sqlShardKey', N'Adapter SQL Validation', NULL, NULL, NULL, 1, SYSUTCDATETIME(), NULL, N'SqlServer');
INSERT INTO dbo.Tenants (Id, TenantCode, Host, ShardKey, TenantName, Email, Phone, Address, Status, CreatedDate, ModifiedDate, DatabaseProvider)
VALUES ('$mySqlTenantId', N'ADAPTERMYSQL', N'adaptermysql.localhost', N'$mySqlShardKey', N'Adapter MySQL Validation', NULL, NULL, NULL, 1, SYSUTCDATETIME(), NULL, N'MySql');
COMMIT TRANSACTION;
SELECT CONVERT(nvarchar(36), Id), TenantCode, Host, ShardKey, DatabaseProvider FROM dbo.Tenants WHERE Id IN ('$sqlTenantId', '$mySqlTenantId') ORDER BY TenantCode;
"@

if ($Server -ne "lpc:.") { throw "Only the approved local Shared Memory server is permitted." }
& sqlcmd -S $Server -d $database -E -C -b -W -s "|" -Q $query
if ($LASTEXITCODE -ne 0) { throw "Catalog row preparation failed; the transaction was rolled back on error." }
Write-Host "Inserted only the two approved disposable catalog rows."
