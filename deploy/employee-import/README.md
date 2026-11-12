# Employee import object storage

## Local MinIO

This Compose file is for isolated development only. It binds MinIO ports to loopback and stores data in the named `minio_data` volume. Create two local secret files outside the repository and set `MINIO_ROOT_USER_FILE` and `MINIO_ROOT_PASSWORD_FILE` to their absolute paths before starting it. Do not commit those files. MinIO root credentials are for provisioning only; create a restricted application user and a private bucket, then give the API only the bucket-scoped read, write, head, and delete permissions it needs.

Compose secrets are mounted under `/run/secrets`. MinIO supports the `_FILE` root credential variables. The API may load its storage settings from a separately mounted JSON secret file by setting `EmployeeImportStorage__ConfigurationFile=/run/secrets/employee-import-storage.json`. That JSON contains configuration, not a checked-in sample. It must include a persistent 32-byte Base64 key ring and the active key ID. For MinIO development, set provider `S3`, the service endpoint, a signing region, `UseTls=false`, and the explicit `AllowInsecureDevelopmentEndpoint=true`. This HTTP exception is rejected outside Development.

Example configuration names (values intentionally omitted):

- `EmployeeImportStorage:Provider`
- `EmployeeImportStorage:RetentionDays`
- `EmployeeImportStorage:Encryption:ActiveKeyId`
- `EmployeeImportStorage:Encryption:Keys:<KeyId>`
- `EmployeeImportStorage:S3:Bucket`
- `EmployeeImportStorage:S3:Endpoint`
- `EmployeeImportStorage:S3:Region`
- `EmployeeImportStorage:S3:UseTls`
- `EmployeeImportStorage:S3:Credentials:Mode`
- `EmployeeImportStorage:S3:Credentials:AccessKey` and `SecretKey` only when using externally supplied static MinIO credentials
- `EmployeeImportStorage:S3:Credentials:SessionToken` when the external credential set is temporary

The AWS SDK default credential chain is preferred for production identity. Static credentials must come from a mounted secret or managed secret provider, never source control or ordinary image build arguments. Keep old encryption key IDs configured until every artifact encrypted under those IDs has expired and been deleted. Rotating the active key does not re-encrypt existing objects.

## Production controls

Use a private bucket with public access blocked at the account and bucket levels. Do not enable website hosting, public ACLs, anonymous policy access, or presigned download URLs. The provider sends no ACL and exposes no object URL; access is server-side through the configured SDK identity. Require HTTPS and validate the TLS certificate. Use a least-privilege service identity limited to the configured bucket and `employee-import-workbooks/*` prefix.

Configure the storage secret file/secret provider before API startup. Explicitly selecting a provider triggers startup validation of endpoint, bucket, region, TLS, credential mode, retention and encryption-key configuration. Runtime readiness should additionally verify bucket access with the deployed identity without listing unrelated buckets; storage service health must be monitored separately from API liveness.

Back up the private bucket and the encryption key material through separate controlled systems. A bucket backup without the matching historical encryption keys is unrecoverable; key backup without object controls risks disclosure. Test restores in an isolated environment and verify workbook hashes after decrypting. Configure provider-native lifecycle expiration to match `RetentionDays`; object deletion through this API is idempotent, but object versioning/replication backups may retain older encrypted versions. Encrypted client-side artifacts still require lifecycle cleanup of noncurrent versions and backup retention planning.

This repository currently has no production object-storage account, key-management integration, or MinIO container acceptance environment configured. The provider is wired and mock-tested, but production credentials, private bucket policy, TLS, backup/restore and lifecycle behavior require deployment validation. The Compose health check is a local MinIO readiness check only; it does not prove API credentials or bucket policy.
