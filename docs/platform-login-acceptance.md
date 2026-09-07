# Platform Login Acceptance

This is a controlled local acceptance of the already-created
`PlatformSuperAdmin`. It does not create tenants or modify tenant data. Login,
refresh, and logout intentionally create/revoke platform refresh-token rows.

## Required configuration

The API requires these `PlatformJwt` settings:

- `PlatformJwt:Issuer`
- `PlatformJwt:Audience`
- `PlatformJwt:SecretKey` (at least 32 characters, secret, not committed)
- `PlatformJwt:AccessTokenMinutes`
- `PlatformJwt:RefreshTokenDays`
- `PlatformJwt:ClockSkewSeconds`

Development JSON supplies every non-secret value, but does not contain the
signing key. If `PlatformJwt__SecretKey` is not already supplied by the local
operator environment, set it only for the API process using the operator's
existing local development secret. Never put it in this document, source, or
the command line.

## Terminal 1: API

From `D:\HRMS`, use a process-scoped secret and disable startup initialization
for this acceptance:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Database__SkipInitialization = 'true'
$jwtKeySecure = Read-Host 'Platform JWT signing key' -AsSecureString
$jwtKeyPtr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($jwtKeySecure)
try {
    $env:PlatformJwt__SecretKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($jwtKeyPtr)
    dotnet run --project .\Backend\HRMS.API\HRMS.API.csproj --no-launch-profile --urls http://localhost:5080
}
finally {
    if ($jwtKeyPtr -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($jwtKeyPtr) }
    Remove-Item Env:PlatformJwt__SecretKey -ErrorAction SilentlyContinue
    $jwtKeySecure.Dispose()
}
```

The API is reached by `http://platform.localhost:5080` with the platform host
boundary. `Database__SkipInitialization=true` prevents startup initialization
from writing to the already-validated databases.

## Terminal 2: frontend

From `D:\HRMS\Frontend\HRMS.Web`:

```powershell
npm run dev -- --host 0.0.0.0
```

The platform API client defaults to `http://platform.localhost:5080`; no
frontend secret or token configuration is required.

## Terminal 3: API acceptance runner

From `D:\HRMS`, after the API is healthy:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-PlatformLogin-Acceptance.ps1
```

The runner prompts for the PlatformSuperAdmin email and hidden password. It
keeps tokens in memory only and does not print them. It verifies health,
platform login, `/me`, tenant listing, customer/unknown-host rejection,
platform-token rejection on the tenant API, refresh rotation/replay, and
logout revocation. It deliberately defers the tenant-token-to-platform check
because it will not invent or accept tenant credentials.

## Browser acceptance

Open:

`http://platform.localhost:5173/platform/login`

Sign in with the PlatformSuperAdmin credentials. Confirm that the page redirects
to `/platform/tenants`, shows DEMO01 and DEMO02 as `SqlServer`, and retains the
separate platform session after refresh. Do not create DEMO03/DEMO04 yet.

Platform session storage is separate from tenant session storage:
`hrms.platform.refreshToken.v1` versus the tenant session key. Platform API
requests use `platformApi` and `PlatformBearer`; tenant requests use the tenant
API client and tenant bearer.

## Expected effects

- `PlatformUsers` remains 1.
- `PlatformRefreshTokens` may contain active/revoked rows from acceptance.
- No tenant user or tenant is created.
- DEMO01, DEMO02, and tenant business data are not modified by the runner.
