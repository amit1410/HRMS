# HRMS Database Setup

## Overview

The HRMS application uses **two SQL Server databases**:

| Database | Purpose | Tables |
|----------|---------|--------|
| `HRMS_Catalog` | Host-to-tenant routing | 2 (Tenants, TenantBranding) |
| `HRMS` | Application data | 22 (Employees, Departments, etc.) |

The application uses **EF Core migrations** to create and maintain the schema.
On startup, it automatically:
1. Applies migrations to both databases
2. Seeds permissions, roles, demo tenants, departments, designations, and employees

## Prerequisites

- **SQL Server**: LocalDB, Express, Developer, or full edition
- **sqlcmd** (optional): For running the SQL script manually

## Option A — Automatic (Recommended)

The application creates the schema automatically on first startup.

### Steps

1. Start SQL Server and ensure it is accessible.

2. Create two empty databases:

```sql
CREATE DATABASE HRMS_Catalog;
CREATE DATABASE HRMS;
```

3. (Optional) If your SQL Server instance is not `(localdb)\MSSQLLocalDB`, update the connection strings in `Backend/HRMS.API/appsettings.json`:

```json
"ConnectionStrings": {
    "Catalog": "Server=YOUR_SERVER;Database=HRMS_Catalog;Trusted_Connection=True;TrustServerCertificate=True",
    "SqlServer": "Server=YOUR_SERVER;Database=HRMS;Trusted_Connection=True;TrustServerCertificate=True"
}
```

4. Start the backend:

```powershell
cd Backend\HRMS.API
dotnet run
```

The application will automatically apply migrations and seed all required data.

5. Verify by opening `http://localhost:5080/api/auth/login` (should return 401, confirming the API is running).

## Option B — Manual SQL Script

Run the complete setup script if you prefer to create the schema via SQL:

### Using sqlcmd

```powershell
# Windows Authentication
sqlcmd -S (localdb)\MSSQLLocalDB -E -i Database\HRMS-Database.sql

# Or with a specific server
sqlcmd -S .\SQLEXPRESS -E -i Database\HRMS-Database.sql
```

### Using SQL Server Management Studio (SSMS)

1. Open SSMS and connect to your SQL Server instance.
2. Open `Database/HRMS-Database.sql`.
3. Press **F5** or click **Execute**.
4. Verify both databases exist in Object Explorer.

## Option C — Individual Migration Scripts

If you need the raw migration SQL:

```powershell
# Shard database (HRMS) — all 5 migrations
dotnet ef migrations script \
    --project Backend/HRMS.Infrastructure \
    --startup-project Backend/HRMS.API \
    --context HrmsDbContext \
    --idempotent \
    --output Database/HRMS-Shard.sql

# Catalog database (HRMS_Catalog) — 1 migration
dotnet ef migrations script \
    --project Backend/HRMS.Infrastructure \
    --startup-project Backend/HRMS.API \
    --context HrmsCatalogDbContext \
    --idempotent \
    --output Database/HRMS-Catalog.sql
```

## Tables Created

### HRMS_Catalog (2 tables)

| Table | Purpose |
|-------|---------|
| `Tenants` | Tenant organizations with Host and ShardKey routing |
| `TenantBranding` | Login page branding per tenant |

### HRMS (22 tables + 1 migration history)

| Table | Purpose |
|-------|---------|
| `Tenants` | Tenant record (shard-side copy) |
| `Permissions` | 20 permission definitions |
| `Roles` | 6 role definitions |
| `RolePermissions` | Role-to-permission grants |
| `Users` | User accounts |
| `UserRoles` | User-to-role assignments |
| `RefreshTokens` | JWT refresh tokens |
| `Departments` | Organization departments |
| `Designations` | Job titles/designations |
| `Employees` | Employee master records |
| `EmployeeContacts` | Phone, email, emergency contact |
| `EmployeeAddresses` | Structured addresses |
| `EmployeeFamilyMembers` | Family/dependents |
| `EmployeeEducationRecords` | Education history |
| `EmployeeEmploymentHistory` | Effective-dated employment changes |
| `EmployeePreviousEmployments` | Prior employer records |
| `EmployeeBankDetails` | Bank account information |
| `EmployeeDocuments` | Document metadata |
| `EmployeeSupervisors` | Reporting hierarchy |
| `EmployeeAdditionalInfo` | Division, PA/PSA, etc. |
| `EmployeeAuditLogs` | Change audit trail |
| `ImportBatches` | Bulk import tracking |

## Seed Data

The application seeds the following on startup:

| Data | Count |
|------|-------|
| Permissions | 20 |
| Roles | 6 (SuperAdmin, TenantAdmin, HRAdmin, HRManager, Manager, Employee) |
| Demo tenants | 2 (DEMO01, DEMO02) |
| Demo users | 4 (2 per tenant) |
| Departments | 3 per tenant (ENG, HR, FIN) |
| Designations | 6 per tenant (CTO, EM, SE, HRM, ACC, TL) |
| Employees | 6 per tenant |

## Default Credentials

| Tenant | Email | Password |
|--------|-------|----------|
| DEMO01 | admin@demo01.com | Passw0rd! |
| DEMO01 | hr@demo01.com | Passw0rd! |
| DEMO02 | admin@demo02.com | Passw0rd! |
| DEMO02 | hr@demo02.com | Passw0rd! |

## Troubleshooting

### SQL Server connection failed
- Verify SQL Server is running: `sqlcmd -S <SERVER> -E -Q "SELECT @@VERSION"`
- Check connection strings in `appsettings.json`
- Ensure `TrustServerCertificate=True` for local instances

### Database not found
- Create the databases first (see Option A, step 2)
- Or let the application create them automatically

### Login fails (401)
- Verify the tenant host header matches (`demo01.localhost`)
- Check that seed data was applied (restart the application)
- Default password is `Passw0rd!`

### CORS errors
- Frontend must run on `localhost:5173`
- Check `Cors:AllowedOrigins` in `appsettings.json`
