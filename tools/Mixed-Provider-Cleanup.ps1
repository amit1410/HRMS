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
IF DB_NAME() <> N'HRMS_Catalog' THROW 52001, 'Catalog identity gate failed.', 1;
IF (SELECT COUNT(*) FROM dbo.Tenants WHERE Id = '11111111-1111-1111-1111-111111111111' AND TenantCode = N'DEMO01' AND Host = N'demo01.localhost' AND ShardKey = N'demo01' AND Status = 1 AND DatabaseProvider = N'SqlServer') <> 1 THROW 52002, 'DEMO01 verification failed.', 1;
IF (SELECT COUNT(*) FROM dbo.Tenants WHERE Id = '22222222-2222-2222-2222-222222222222' AND TenantCode = N'DEMO02' AND Host = N'demo02.localhost' AND ShardKey = N'demo02' AND Status = 3 AND DatabaseProvider = N'SqlServer') <> 1 THROW 52003, 'DEMO02 verification failed.', 1;
IF (SELECT COUNT(*) FROM dbo.Tenants WHERE Id = '$sqlTenantId' AND TenantCode = N'ADAPTERSQL' AND Host = N'adaptersql.localhost' AND ShardKey = N'$sqlShardKey' AND Status = 1 AND DatabaseProvider = N'SqlServer') <> 1 THROW 52004, 'SQL disposable row mismatch.', 1;
IF (SELECT COUNT(*) FROM dbo.Tenants WHERE Id = '$mySqlTenantId' AND TenantCode = N'ADAPTERMYSQL' AND Host = N'adaptermysql.localhost' AND ShardKey = N'$mySqlShardKey' AND Status = 1 AND DatabaseProvider = N'MySql') <> 1 THROW 52005, 'MySQL disposable row mismatch.', 1;
BEGIN TRANSACTION;
DELETE FROM dbo.TenantBranding WHERE TenantId = '$sqlTenantId';
DELETE FROM dbo.TenantBranding WHERE TenantId = '$mySqlTenantId';
DELETE FROM dbo.Tenants WHERE Id = '$sqlTenantId' AND TenantCode = N'ADAPTERSQL' AND Host = N'adaptersql.localhost' AND ShardKey = N'$sqlShardKey' AND Status = 1 AND DatabaseProvider = N'SqlServer';
DELETE FROM dbo.Tenants WHERE Id = '$mySqlTenantId' AND TenantCode = N'ADAPTERMYSQL' AND Host = N'adaptermysql.localhost' AND ShardKey = N'$mySqlShardKey' AND Status = 1 AND DatabaseProvider = N'MySql';
IF EXISTS (SELECT 1 FROM dbo.Tenants WHERE Id IN ('$sqlTenantId', '$mySqlTenantId')) THROW 52006, 'Disposable catalog cleanup did not remove exact rows.', 1;
COMMIT TRANSACTION;
"@

if ($Server -ne "lpc:.") { throw "Only the approved local Shared Memory server is permitted." }
& sqlcmd -S $Server -d $database -E -C -b -Q $query
if ($LASTEXITCODE -ne 0) { throw "Exact disposable catalog cleanup failed." }
Write-Host "Removed only the two exact disposable catalog tenants."
