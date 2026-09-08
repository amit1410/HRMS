# MySQL-only Development runtime

Development is explicitly configured with `Database:CatalogProvider=MySql`. It does not use the SQL Server
catalog, shared SQL Server tenant fallback, or demo tenant startup seeding. The catalog is `hrms_catalog`; every
MySQL tenant is `hrms_<shardKey>`.

## Local configuration

Keep credentials outside Git. In the PowerShell session used to start the API, provide these exact environment
variables (the double underscore maps to `:` in .NET configuration):

```powershell
$env:DOTNET_ENVIRONMENT = 'Development'
$env:Database__CatalogProvider = 'MySql'
$env:ConnectionStrings__Catalog = 'Server=127.0.0.1;Port=3306;Database=hrms_catalog;User ID=<local-user>;Password=<local-password>;'
$env:Sharding__MySqlConnectionStringTemplate = 'Server=127.0.0.1;Port=3306;Database=hrms_{shardKey};User ID=<local-user>;Password=<local-password>;'
```

Use a local secret manager or replace the placeholders only in the current shell/session. Do not put the values in
`appsettings*.json`, scripts committed to Git, or command-line arguments. The checked-in Development settings leave
both connection-string values empty so startup fails closed when the required local configuration is missing.

Create the catalog database without putting the password in shell history:

```powershell
.\tools\Initialize-MySqlDevelopment.ps1 -MySqlHost 127.0.0.1 -MySqlPort 3306 -MySqlUser <local-user>
```

Start the API once the variables are set. Its normal Development initializer applies the complete MySQL catalog
migration chain and seeds only platform reference identity data; it does not create DEMO01 or DEMO02:

```powershell
dotnet run --project Backend/HRMS.API
```

The catalog migration history table is `__EFMigrationsHistory` and must contain, in order:

1. `20260905181639_InitialMySqlCatalogSchema`
2. `20260906130913_AddPlatformIdentity`

The resulting catalog tables are `Tenants`, `TenantBranding`, `PlatformUsers`, `PlatformRoles`,
`PlatformPermissions`, `PlatformUserRoles`, `PlatformRolePermissions`, and `PlatformRefreshTokens`.

## Bootstrap and onboarding

After the API initializer has completed and the catalog still has zero tenant rows, bootstrap the platform admin.
The password is entered interactively and is never a command-line argument or log value:

```powershell
$env:DOTNET_ENVIRONMENT = 'Development'
dotnet run --project tools/PlatformAdminBootstrap -- --confirm-platform-bootstrap
```

Open the platform UI at `http://platform.localhost:5173`, sign in with that administrator, and create the first
tenant with provider `MySQL`. The UI defaults to MySQL and previews `hrms_<shardKey>`. Onboarding inserts the catalog
row as `Inactive`, creates and migrates the shard, seeds required tenant reference data, creates the initial
TenantAdmin, and changes both tenant copies to `Active` only after success. A failed attempt leaves the catalog row
`Inactive` for a safe retry.

The frontend runs with:

```powershell
cd Frontend/HRMS.Web
npm run dev
```

## Cleanup before first onboarding

Verify the databases are obsolete and no active catalog row references them, then run these commands manually from a
MySQL client. They are intentionally not part of application startup:

```sql
DROP DATABASE IF EXISTS hrms_anevratechnologies;
DROP DATABASE IF EXISTS hrms_mysqladapter_test_20260905;
DROP DATABASE IF EXISTS hrms_mysqlcatalogadapter_test_20260906;
DROP DATABASE IF EXISTS hrms_platformcatalog_rehearsal_20260906;
DROP DATABASE IF EXISTS hrms_platformcatalog_rehearsal_20260906_r2;
DROP DATABASE IF EXISTS hrms_platformcatalog_rehearsal_20260906_r3;
```

Do not drop `hrms_catalog` or MySQL system/sample databases (`information_schema`, `mysql`,
`performance_schema`, `sys`, `sakila`, `world`). SQL Server HRMS databases used by provider tests can remain until
the SQL Server-off acceptance has passed; they must not be referenced by Development configuration.
