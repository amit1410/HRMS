# HRMS configuration and secrets

The API uses standard ASP.NET Core precedence: `appsettings.json`, `appsettings.{Environment}.json`, .NET User Secrets in Development, environment variables, then command-line arguments. `HRMS.API.csproj` already has a `UserSecretsId`; no custom configuration loader is used.

## Local development

Committed JSON contains safe defaults only. Development selects MySQL, SMTP, and Fake SMS; SMTP host/port/sender metadata is non-secret. Run these once from `D:\HRMS`:

```powershell
cd D:\HRMS
dotnet user-secrets set --project .\Backend\HRMS.API\HRMS.API.csproj "Jwt:SecretKey" "<development-jwt-secret-at-least-32-bytes>"
dotnet user-secrets set --project .\Backend\HRMS.API\HRMS.API.csproj "PlatformJwt:SecretKey" "<development-platform-jwt-secret-at-least-32-bytes>"
dotnet user-secrets set --project .\Backend\HRMS.API\HRMS.API.csproj "Email:Smtp:Password" "<smtp-password>"
dotnet user-secrets set --project .\Backend\HRMS.API\HRMS.API.csproj "ConnectionStrings:Catalog" "<mysql-catalog-connection-string>"
dotnet user-secrets set --project .\Backend\HRMS.API\HRMS.API.csproj "Sharding:MySqlConnectionStringTemplate" "<mysql-tenant-template-with-{shardKey}>"
```

If the local mailbox differs, set `Email:Smtp:Username` once as a User Secret. Fake SMS does not require MSG91 credentials. Normal startup is then:

```powershell
cd D:\HRMS\Backend\HRMS.API
dotnet run
```

No `$env:` commands are required before each run.

## Configuration inventory

### Shared, non-secret JSON

`Database:Provider`, optional `Database:CatalogProvider`, SQLite fallbacks, sharding cache durations, `Email:FromEmail`, `Email:FromName`, invitation/leave settings, frontend/CORS/forwarded-header/rate-limit settings, logging, allowed hosts, and JWT metadata (`Issuer`, `Audience`, token lifetimes, and clock skew) are safe shared settings.

### Secrets and provider-specific settings

| Key | Type and behavior |
| --- | --- |
| `Jwt:SecretKey` | Required JWT secret; minimum 32 UTF-8 bytes. The application uses this canonical key, not `Jwt:Key`. |
| `PlatformJwt:SecretKey` | Platform JWT secret; minimum 32 characters. |
| `Email:Smtp:Password` | SMTP secret, required only for `Email:Provider=Smtp`. |
| `ConnectionStrings:Catalog` | Catalog credential-bearing connection string for MySQL or SQL Server. |
| `ConnectionStrings:SqlServer` | Shared SQL Server tenant fallback. |
| `Sharding:MySqlConnectionStringTemplate` | MySQL tenant template, including `{shardKey}`. |
| `Sharding:SqlServerConnectionStringTemplate` | SQL Server tenant template, including `{shardKey}`. |
| `Msg91:AuthKey` | MSG91 secret, required only for `Sms:Provider=Msg91`. |
| `Email:Provider` | Preferred email provider selection: `Fake` or `Smtp`. |
| `Sms:Provider` | Preferred SMS provider selection: `Fake` or `Msg91`. |
| `Email:FromEmail`, `Email:FromName`, `Email:Smtp:Host`, `Email:Smtp:Port`, `Email:Smtp:Username`, `Email:Smtp:EnableSsl` | Non-secret sender/SMTP metadata. |
| `Msg91:BaseUrl`, `Msg91:FlowId`, `Msg91:SenderId` | MSG91 provider settings; required when MSG91 is selected. `DefaultCountryCode` and `OtpVariable` are optional. |
| `Database:Provider`, `Database:CatalogProvider` | Provider selection: `MySql`, `SqlServer`, or local `Sqlite`. |
| `Sharding:CacheSeconds`, `Sharding:UnknownHostCacheSeconds` | Non-secret routing cache settings. |

Only the selected provider is required. MySQL tenant provisioning uses the selected MySQL template credentials to inspect/create tenant databases. SQL Server uses its resolved template or shared connection and the permissions of that database account. Existing `{shardKey}` naming is unchanged.

## Legacy compatibility

Nested SMTP keys are preferred, with fallback to `Email:SmtpHost`, `Email:SmtpPort`, `Email:SmtpUsername`, `Email:SmtpPassword`, and `Email:EnableSsl`. `PasswordRecoveryProviders:EmailProvider` and `PasswordRecoveryProviders:SmsProvider` remain fallbacks for the new `Email:Provider` and `Sms:Provider` keys. `Sharding:ConnectionStringTemplate` remains the legacy SQL Server template fallback. No new legacy formats were added.

## Production `/etc/hrms-api.env`

The systemd unit should contain `EnvironmentFile=/etc/hrms-api.env`. The unit is outside this repository; verify it once with `sudo systemctl cat hrms-api`. Install this file once with root-only permissions and never commit it:

```ini
ASPNETCORE_ENVIRONMENT=Production
Jwt__SecretKey=<production-jwt-secret-at-least-32-bytes>
Jwt__Issuer=HRMS.API
Jwt__Audience=HRMS.Client
PlatformJwt__SecretKey=<production-platform-jwt-secret-at-least-32-bytes>
Email__Provider=Smtp
Email__FromEmail=noreply@anevratechnology.com
Email__FromName=Anevra HRMS
Email__Smtp__Host=smtp.hostinger.com
Email__Smtp__Port=587
Email__Smtp__Username=noreply@anevratechnology.com
Email__Smtp__Password=<smtp-password>
Email__Smtp__EnableSsl=true
Sms__Provider=Msg91
Msg91__AuthKey=<msg91-auth-key>
Msg91__BaseUrl=https://control.msg91.com/api/v5/
Msg91__FlowId=<msg91-flow-id>
Msg91__SenderId=<msg91-sender-id>
Database__Provider=MySql
Database__CatalogProvider=MySql
ConnectionStrings__Catalog=<mysql-catalog-connection-string>
Sharding__MySqlConnectionStringTemplate=<mysql-tenant-template-with-{shardKey}>
```

This SMTP adapter uses `System.Net.Mail.SmtpClient` with `EnableSsl=true`; configure port 587 with STARTTLS as the preferred production setting. Port 465 is implicit TLS and is not the preferred pairing for this implementation.

For SQL Server, use `Database__Provider=SqlServer`, provide `ConnectionStrings__Catalog`, and provide `Sharding__SqlServerConnectionStringTemplate` or legacy `Sharding__ConnectionStringTemplate`; `ConnectionStrings__SqlServer` is the shared-tenant fallback. Inactive-provider credentials are not required.

Normal deployment does not overwrite the environment file:

```bash
cd /var/www/HRMS
git pull
dotnet publish Backend/HRMS.API/HRMS.API.csproj -c Release -o /var/www/hrms-api
sudo systemctl restart hrms-api
sudo systemctl status hrms-api --no-pager
```

## Safety

`.env`-style files, `hrms-api.env`, and `.codex-local-env` are ignored by Git. No tracked real secret was found in the reviewed configuration; the previous committed development JWT placeholder was removed. Rotate credentials if an actual secret is ever found in repository history. Startup validation is provider-aware and does not require SMTP, MSG91, MySQL, or SQL Server settings for an unselected provider.
