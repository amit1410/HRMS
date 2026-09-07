# Platform identity and tenant onboarding

## Security domains

Platform administration and tenant application identity are separate domains:

```text
platform.localhost
  -> PlatformBearer
  -> PlatformUser / PlatformSuperAdmin in the catalog
  -> platform permissions checked live in the catalog

demoXX.localhost
  -> tenant bearer token
  -> TenantShardResolutionMiddleware
  -> Tenant user in the selected tenant database
  -> TenantSuperAdmin / TenantAdmin permissions
```

`PlatformSuperAdmin != TenantSuperAdmin`. A tenant role named `SuperAdmin` is
tenant-scoped and cannot authorize `/api/platform/*`. A tenant JWT is not a
platform JWT, and a platform JWT carries no tenant identity claims.

Platform requests are accepted only on configured platform hosts. The platform
branch runs before tenant shard resolution; no fake `PLATFORM` tenant is used.
Customer hosts are rejected for platform routes, and platform hosts do not
resolve as customer tenants.

## Catalog identity schema

The catalog owns `PlatformUsers`, `PlatformRoles`, `PlatformPermissions`,
`PlatformUserRoles`, `PlatformRolePermissions`, and `PlatformRefreshTokens`.
None of these tables has `TenantId`. `PlatformSuperAdmin` and its three tenant
lifecycle permissions are seeded as reference data by the catalog migration;
the first user is created only by the operator-controlled bootstrap tool.

Both catalog migration streams are required and provider-neutral:

- SQL Server: `20260906130913_AddPlatformIdentity`
- MySQL: `20260906130913_AddPlatformIdentity`

They must be reviewed and applied separately in an approved deployment window.
This change does not apply either migration.

## Onboarding flow

`PlatformSuperAdmin` submits a validated provider enum, tenant identity and
initial tenant administrator details to `POST /api/platform/tenants`.

The service generates the tenant id, inserts the catalog row as `Inactive`,
provisions the selected SQL Server or MySQL tenant using trusted server-side
configuration, creates the initial tenant administrator, and activates the
catalog/shard copies only after provisioning succeeds. Catalog and tenant
operations are not treated as one distributed transaction. A provisioning
failure leaves the catalog tenant inactive for operator diagnosis/retry; no
database is dropped automatically.

In Development only, a cryptographically generated temporary administrator
password may be returned once. It is hashed before persistence and is not
logged or stored by the frontend. Non-Development onboarding remains closed
until an invitation/password-setup provider is configured.

### Failed onboarding recovery

An `Inactive` tenant is retained when provisioning fails. Platform operators
with `PlatformTenant.Create` may correct its name/host through
`PUT /api/platform/tenants/{id}` and retry the existing row through
`POST /api/platform/tenants/{id}/retry-provisioning`; neither operation inserts
another catalog tenant. Development host validation requires the supported
`<workspace>.localhost` form. Active and Suspended tenants cannot be retried.

MySQL retry state is fail-closed: an absent database is created from the
trusted server-side template, while an existing database must have the
complete expected migration history and schema before provisioning continues.
No database is dropped automatically. Initial administrator creation is
idempotent for an existing active user with the TenantAdmin role; conflicting
or incomplete administrator state leaves the tenant Inactive. Failure logs
contain only tenant identity, provider, phase, and exception type.

SQL Server onboarding respects the current shared-database mode when no SQL
Server sharding template is configured: `ConnectionStrings:SqlServer` is used
and tenant row filters provide isolation. MySQL onboarding uses the trusted
`Sharding:MySqlConnectionStringTemplate`; no credential is accepted from the
frontend or catalog.

## Operator bootstrap

`tools/PlatformAdminBootstrap` is an interactive, configuration-driven console
tool for creating the first platform user. It supports the configured SQL
Server or MySQL catalog, prompts for the password without command-line input,
hashes it, assigns `PlatformSuperAdmin`, and refuses duplicate normalized
emails. It is not executed automatically by API startup.

## Follow-up

Persistent platform audit storage and non-Development invitation/password setup
remain deployment work. Structured logs record platform lifecycle events without
passwords, hashes, tokens, or connection strings.
