using System.Security.Cryptography;

namespace HRMS.Infrastructure.Persistence;

public sealed class MySqlConcurrencyTokenGenerator
{
    public byte[] Create() => RandomNumberGenerator.GetBytes(16);
}
