using HRMS.Infrastructure.Security;

namespace HRMS.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_then_verify_succeeds_and_rejects_wrong_password()
    {
        var hasher = new IdentityPasswordHasher();
        var hash = hasher.Hash("Passw0rd!");

        Assert.False(string.IsNullOrWhiteSpace(hash));
        Assert.NotEqual("Passw0rd!", hash); // never stored in plaintext
        Assert.True(hasher.Verify(hash, "Passw0rd!"));
        Assert.False(hasher.Verify(hash, "Passw0rd?"));
    }

    [Fact]
    public void Hashes_are_salted_so_the_same_password_yields_different_hashes()
    {
        var hasher = new IdentityPasswordHasher();
        Assert.NotEqual(hasher.Hash("Passw0rd!"), hasher.Hash("Passw0rd!"));
    }
}
