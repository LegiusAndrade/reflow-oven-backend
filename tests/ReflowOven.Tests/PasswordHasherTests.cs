using ReflowOven.Infrastructure.Auth;
using Xunit;

namespace ReflowOven.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_then_verify_round_trips()
    {
        var hasher = new BcryptPasswordHasher();
        var hash = hasher.Hash("reflow1234");

        Assert.True(hasher.Verify("reflow1234", hash));
        Assert.False(hasher.Verify("wrong", hash));
        Assert.False(hasher.Verify("reflow1234", "not-a-bcrypt-hash"));
    }
}
