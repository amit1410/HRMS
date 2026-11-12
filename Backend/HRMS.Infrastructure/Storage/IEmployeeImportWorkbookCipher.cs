namespace HRMS.Infrastructure.Storage;

/// <summary>Shared authenticated-encryption envelope for employee-import workbook artifacts.</summary>
public interface IEmployeeImportWorkbookCipher
{
    byte[] Encrypt(Guid tenantId, Guid operationId, ReadOnlySpan<byte> plaintext);
    byte[] Decrypt(Guid tenantId, Guid operationId, ReadOnlySpan<byte> envelope);
}
