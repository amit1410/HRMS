namespace HRMS.Application.Abstractions;

/// <summary>
/// Abstraction over secure password hashing so the Application layer never depends on a concrete
/// crypto implementation. Backed by ASP.NET Core Identity's PBKDF2 hasher in Infrastructure.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Produces a salted, iterated hash suitable for storage.</summary>
    string Hash(string password);

    /// <summary>Verifies a plaintext password against a stored hash in constant time.</summary>
    bool Verify(string hashedPassword, string providedPassword);
}
