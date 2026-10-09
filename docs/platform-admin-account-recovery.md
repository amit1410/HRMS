# Platform administrator account recovery

The platform login now provides username reminder, password reset request, and password reset pages:

- `/platform/forgot-username`
- `/platform/forgot-password`
- `/platform/reset-password?token=...`

API endpoints are `POST /api/platform/auth/forgot-username`, `POST /api/platform/auth/forgot-password`, and `POST /api/platform/auth/reset-password`. Recovery responses are generic. Reset tokens are 32 random bytes, stored only as SHA-256 hashes, expire after 15 minutes, are single-use, and revoke platform refresh sessions after a successful reset.

Configure `Frontend:PlatformWebUrl` to the exact trusted public platform origin. SMTP uses the existing `Email` settings: `Email:Provider`, `Email:Smtp:Host`, `Email:Smtp:Port`, `Email:Smtp:Username`, `Email:Smtp:Password`, `Email:Smtp:EnableSsl`, `Email:FromEmail`, and `Email:FromName`. Store secrets in environment variables, user secrets, or a secret manager; do not commit values. Production should use HTTPS and valid certificate validation.

The catalog schema requires migration `20261008130000_AddPlatformPasswordRecovery` in the SQL Server catalog migrations assembly or the matching MySQL catalog migrations assembly. Review and apply it through the existing migration host with explicit deployment approval; this implementation did not apply it.

Local verification: configure a fake email provider, set `Frontend:PlatformWebUrl` to `http://platform.localhost:5173`, apply the catalog migration to an isolated local database, open the platform login, submit both recovery forms, and verify that unknown identifiers receive the same generic response. Use the fake sender or a test double; do not send real mail.
