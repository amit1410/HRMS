# HRMS Database Migration & Setup Guide

## Prerequisites

- **SQL Server** (Express, Developer, or Standard) running on `localhost` (default instance)
- **.NET 10 SDK** (`dotnet --version` should show 10.x)
- **Node.js** (v18+) and **npm**
- **EF Core CLI** (installed via `dotnet tool install --global dotnet-ef` if not present)

## Database Setup

The HRMS uses **two** SQL Server databases managed by EF Core migrations:

| Database | Purpose | Context | Migrations |
|---|---|---|---|
| `HRMS_Catalog` | Tenant routing + branding | `HrmsCatalogDbContext` | `Catalog/Migrations/` |
| `HRMS` | Employee data per tenant | `HrmsDbContext` | `Persistence/Migrations/` |

### Apply Migrations

```powershell
# From the repo root (D:\HRMS)

# 1. Catalog database (routing + branding)
dotnet ef database update --context HrmsCatalogDbContext --project Backend/HRMS.Infrastructure --startup-project Backend/HRMS.API

# 2. Tenant/employee database
dotnet ef database update --context HrmsDbContext --project Backend/HRMS.Infrastructure --startup-project Backend/HRMS.API
```

Seed data (tenants, roles, permissions, users, departments, designations, employees) is applied automatically at application startup.

### List Existing Migrations

```powershell
dotnet ef migrations list --context HrmsDbContext --project Backend/HRMS.Infrastructure --startup-project Backend/HRMS.API
dotnet ef migrations list --context HrmsCatalogDbContext --project Backend/HRMS.Infrastructure --startup-project Backend/HRMS.API
```

## Backend

```powershell
cd D:\HRMS\Backend\HRMS.API
dotnet run
```

The API starts on `http://localhost:5080` (see `Properties/launchSettings.json`).

## Frontend

```powershell
cd D:\HRMS\Frontend\HRMS.Web
npm install   # first time only
npm run dev
```

The frontend starts on `http://localhost:5173` and points to the API at `http://localhost:5080`.

## Verification

### Database

```powershell
# Check databases exist
sqlcmd -S localhost -E -C -Q "SELECT name FROM sys.databases WHERE name LIKE 'HRMS%'"

# Check HRMS_Catalog tables (should show Tenants, TenantBranding, __EFMigrationsHistoryCatalog)
sqlcmd -S localhost -E -C -d HRMS_Catalog -Q "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE'"

# Check HRMS tables (should show 22 tables including Employees, Departments, etc.)
sqlcmd -S localhost -E -C -d HRMS -Q "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE'"

# Check migration history
sqlcmd -S localhost -E -C -d HRMS -Q "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId"
```

### API

```powershell
# Health check (should return 200 with status "Healthy")
curl http://localhost:5080/health

# Login (tenant resolved from Host header)
curl -X POST http://localhost:5080/api/auth/login -H "Content-Type: application/json" -H "Host: demo01.localhost" -d '{"email":"admin@demo01.com","password":"Passw0rd!"}'
```

### Frontend

Open `http://localhost:5173` in a browser. Navigate to `http://demo01.localhost:5173` to sign in as the Demo Organization.

## Seed Data

The application automatically seeds:

| Data | Count |
|---|---|
| Tenants (catalog) | 2 (DEMO01, DEMO02) |
| Tenant Branding | 2 |
| Roles | 6 (SuperAdmin, TenantAdmin, HRAdmin, HRManager, Manager, Employee) |
| Permissions | 20 |
| Role-Permission grants | All mapped |
| Users | 4 (2 per tenant) |
| Departments | 5 (3 in DEMO01, 2 in DEMO02) |
| Designations | 8 (6 in DEMO01, 2 in DEMO02) |
| Employees | 8 (6 in DEMO01, 2 in DEMO02) |

### Login Credentials

All seeded users share the password `Passw0rd!`:

| Email | Tenant | Role |
|---|---|---|
| admin@demo01.com | DEMO01 (Demo Organization) | TenantAdmin |
| hr@demo01.com | DEMO01 (Demo Organization) | HRManager |
| admin@demo02.com | DEMO02 (Sample Organization) | TenantAdmin |
| hr@demo02.com | DEMO02 (Sample Organization) | HRManager |

## Connection String Configuration

Connection strings are in:

- `Backend/HRMS.API/appsettings.json` (line 5-9)
- `Backend/HRMS.API/appsettings.Development.json` (overrides provider)

```json
{
  "Database": { "Provider": "SqlServer" },
  "ConnectionStrings": {
    "Catalog": "Server=localhost;Database=HRMS_Catalog;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True",
    "SqlServer": "Server=localhost;Database=HRMS;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
  }
}
```

If your SQL Server instance is not `localhost`, update the `Server=` portion in all connection strings:

1. `appsettings.json` - Catalog and SqlServer connection strings
2. `appsettings.json` - Sharding:ConnectionStringTemplate (if using per-tenant databases)
3. `Backend/HRMS.Infrastructure/Persistence/HrmsDbContextFactory.cs` (design-time factory)
4. `Backend/HRMS.Infrastructure/Persistence/Catalog/HrmsCatalogDbContextFactory.cs` (design-time factory)

## Resetting the Development Database

> **DEVELOPMENT ONLY** - Never run this against production data.

```powershell
# Drop both databases
sqlcmd -S localhost -E -C -Q "DROP DATABASE IF EXISTS HRMS; DROP DATABASE IF EXISTS HRMS_Catalog"

# Re-apply migrations (creates databases from scratch)
dotnet ef database update --context HrmsCatalogDbContext --project Backend/HRMS.Infrastructure --startup-project Backend/HRMS.API
dotnet ef database update --context HrmsDbContext --project Backend/HRMS.Infrastructure --startup-project Backend/HRMS.API

# Seed data will be re-applied on next application startup
cd D:\HRMS\Backend\HRMS.API
dotnet run
```

## Architecture Notes

- **Multi-tenant**: Two databases — a shared catalog for routing, per-tenant databases for employee data
- **Sharding modes**: No template = shared database (query filters isolate tenants). Template set = one DB per tenant
- **Schema management**: SQL Server uses EF Core migrations (`MigrateAsync`). SQLite dev fallback uses `EnsureCreated`
- **Migrations assembly**: `HRMS.Infrastructure` (both contexts share the same assembly)
