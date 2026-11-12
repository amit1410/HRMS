#!/bin/sh
set -eu
set +x

export AWS_ACCESS_KEY_ID="$(tr -d '\r\n' < /run/secrets/minio_root_user)"
export AWS_SECRET_ACCESS_KEY="$(tr -d '\r\n' < /run/secrets/minio_root_password)"
endpoint='http://minio:9000'

ready=false
attempt=0
while [ "$attempt" -lt 60 ]; do
  if aws --endpoint-url "$endpoint" s3api list-buckets >/dev/null 2>&1; then
    ready=true
    break
  fi
  attempt=$((attempt + 1))
  sleep 2
done

if [ "$ready" != true ]; then
  echo 'MinIO did not become reachable with the configured test-only credentials.' >&2
  exit 1
fi

if aws --endpoint-url "$endpoint" s3api head-bucket --bucket "$HRMS_F1_BUCKET" >/dev/null 2>&1; then
  echo 'The run-specific bucket already exists; preserving it.'
else
  aws --endpoint-url "$endpoint" s3api create-bucket --bucket "$HRMS_F1_BUCKET" >/dev/null
fi

# A new S3 bucket has no public ACL or policy. Refuse to continue if a public ACL or any
# pre-existing bucket policy is found instead of silently changing persistent test data.
public_grants="$(aws --endpoint-url "$endpoint" s3api get-bucket-acl --bucket "$HRMS_F1_BUCKET" \
  --query 'length(Grants[?Grantee.URI==`http://acs.amazonaws.com/groups/global/AllUsers` || Grantee.URI==`http://acs.amazonaws.com/groups/global/AuthenticatedUsers`])' \
  --output text)"
if [ "$public_grants" != 0 ]; then
  echo 'The run-specific bucket has public ACL grants; refusing to use it.' >&2
  exit 1
fi

if aws --endpoint-url "$endpoint" s3api get-bucket-policy --bucket "$HRMS_F1_BUCKET" >/dev/null 2>&1; then
  echo 'The run-specific bucket has a policy; refusing unreviewed access rules.' >&2
  exit 1
fi

echo 'Run-specific bucket is reachable and has no public ACL grants or bucket policy.'
