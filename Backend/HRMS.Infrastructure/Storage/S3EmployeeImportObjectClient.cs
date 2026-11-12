using Amazon.S3;
using Amazon.S3.Model;

namespace HRMS.Infrastructure.Storage;

/// <summary>AWS SDK adapter configured for AWS S3 or an S3-compatible private endpoint.</summary>
public sealed class S3EmployeeImportObjectClient(IAmazonS3 client) : IEmployeeImportObjectClient
{
    private const int MaximumEncryptedObjectBytes = 10 * 1024 * 1024 + 128;

    public async Task PutIfAbsentAsync(string bucket, string key, byte[] content, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            await client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = bucket,
                Key = key,
                InputStream = stream,
                ContentType = "application/octet-stream",
                AutoCloseStream = false,
                IfNoneMatch = "*"
            }, cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.PreconditionFailed || exception.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            throw new InvalidOperationException("Workbook artifact already exists for this operation.");
        }
    }

    public async Task<byte[]?> GetAsync(string bucket, string key, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetObjectAsync(new GetObjectRequest { BucketName = bucket, Key = key }, cancellationToken);
            await using var output = new MemoryStream();
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await response.ResponseStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (output.Length + read > MaximumEncryptedObjectBytes)
                    throw new InvalidDataException("Stored workbook artifact exceeds the configured size limit.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            return output.ToArray();
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound ||
                                                   string.Equals(exception.ErrorCode, "NoSuchKey", StringComparison.Ordinal))
        {
            return null;
        }
    }

    public async Task<bool> ExistsAsync(string bucket, string key, CancellationToken cancellationToken)
    {
        try
        {
            await client.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = bucket, Key = key }, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound ||
                                                   string.Equals(exception.ErrorCode, "NoSuchKey", StringComparison.Ordinal))
        {
            return false;
        }
    }

    public async Task DeleteAsync(string bucket, string key, CancellationToken cancellationToken) =>
        _ = await client.DeleteObjectAsync(new DeleteObjectRequest { BucketName = bucket, Key = key }, cancellationToken);
}
