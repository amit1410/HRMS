# WEB1 read-only inspection findings

Collected: 2026-10-17 (Asia/Calcutta)

## Result

The temporary .NET 10 utility used the existing MySql.Data 26.7.0 package and the existing Development User Secrets configuration. It did not print connection strings or credentials.

The catalog connection could not be established, so WEB1 host, shard key, provider, physical database, migration history, Welcome Invitation schema, and permission IDs/names remain unverified. No database query succeeded and no database state was changed.

## Exact connection error

`System.Security.Authentication.AuthenticationException: Authentication failed, see inner exception.`

Inner error: `System.ComponentModel.Win32Exception (0x8009030E): No credentials are available in the security package.`

Failure occurred during TLS client authentication (`MySql.Data.Common.Ssl.StartSSLAsync` / `SslStream`).

## Findings unavailable

- Verified WEB1 database identity: unavailable because catalog connection failed.
- Missing migrations: unavailable.
- Missing Welcome Invitation permissions: unavailable.

## Remaining approvals

- Approve or provide the required TLS client credential for the existing catalog connection, if a repeat read-only inspection is desired.
- Separately approve any database changes that inspection may identify: applying migrations, adding permissions, or changing role grants. None of those changes is authorized by the read-only inspection request.
