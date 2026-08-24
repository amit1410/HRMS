using HRMS.Application.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace HRMS.Infrastructure.Security;

/// <summary>
/// <see cref="IPasswordHasher"/> backed by ASP.NET Core Identity's <see cref="PasswordHasher{TUser}"/>,
/// which produces salted PBKDF2 (HMAC-SHA512) hashes and verifies them in constant time. We use the
/// hasher directly rather than the full Identity stack because the multi-tenant User/Role schema is
/// custom (email is unique per tenant, roles carry tenant scope).
/// </summary>
public class IdentityPasswordHasher : IPasswordHasher
{
    // The generic user argument is unused by the default hashing algorithm, so a shared sentinel is fine.
    private static readonly object Sentinel = new();
    private readonly PasswordHasher<object> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(Sentinel, password);

    public bool Verify(string hashedPassword, string providedPassword)
    {
        var result = _hasher.VerifyHashedPassword(Sentinel, hashedPassword, providedPassword);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
