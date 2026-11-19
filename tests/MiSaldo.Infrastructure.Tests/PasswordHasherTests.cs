using MiSaldo.Infrastructure.Auth;
using Xunit;

namespace MiSaldo.Infrastructure.Tests;

public class PasswordHasherTests
{
    // Cheap parameters keep the suite fast; the production defaults are tested separately.
    private static PasswordHasher FastHasher(int memoryKiB = 1024, int iterations = 1, int parallelism = 1) =>
        new(new Argon2Options { MemorySizeKiB = memoryKiB, Iterations = iterations, Parallelism = parallelism });

    [Fact]
    public void A_hashed_password_verifies()
    {
        var hasher = FastHasher();
        var hash = hasher.Hash("correct horse battery");

        Assert.True(hasher.Verify(hash, "correct horse battery"));
    }

    [Fact]
    public void A_wrong_password_does_not_verify()
    {
        var hasher = FastHasher();
        var hash = hasher.Hash("correct horse battery");

        Assert.False(hasher.Verify(hash, "correct horse batterY"));
        Assert.False(hasher.Verify(hash, string.Empty));
    }

    [Fact]
    public void The_same_password_gets_a_different_hash_each_time_because_of_the_salt()
    {
        var hasher = FastHasher();

        var first = hasher.Hash("same password 123");
        var second = hasher.Hash("same password 123");

        Assert.NotEqual(first, second);
        Assert.True(hasher.Verify(first, "same password 123"));
        Assert.True(hasher.Verify(second, "same password 123"));
    }

    [Fact]
    public void The_hash_is_argon2id_with_its_parameters_and_never_contains_the_password()
    {
        var hash = FastHasher(2048, 2, 1).Hash("plain-text-secret");

        Assert.StartsWith("$argon2id$v=19$m=2048,t=2,p=1$", hash);
        Assert.DoesNotContain("plain-text-secret", hash);
    }

    [Fact]
    public void Verification_uses_the_parameters_stored_in_the_hash()
    {
        var oldHash = FastHasher(1024, 1, 1).Hash("migrating password");

        // A hasher configured with other costs still verifies the older hash.
        var newerHasher = FastHasher(4096, 2, 2);

        Assert.True(newerHasher.Verify(oldHash, "migrating password"));
    }

    [Fact]
    public void Unicode_passwords_work()
    {
        var hasher = FastHasher();
        var hash = hasher.Hash("contraseña-ñandú-日本語-🔐");

        Assert.True(hasher.Verify(hash, "contraseña-ñandú-日本語-🔐"));
        Assert.False(hasher.Verify(hash, "contrasena-nandu"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a hash")]
    [InlineData("$argon2i$v=19$m=1024,t=1,p=1$c2FsdHNhbHRzYWx0$aGFzaGhhc2hoYXNoaGFzaA")]
    [InlineData("$argon2id$v=19$m=abc,t=1,p=1$c2FsdHNhbHRzYWx0$aGFzaGhhc2hoYXNoaGFzaA")]
    [InlineData("$argon2id$v=19$m=1024,t=1,p=1$!!!notbase64!!!$aGFzaGhhc2hoYXNoaGFzaA")]
    [InlineData("$argon2id$v=19$m=999999999,t=1,p=1$c2FsdHNhbHRzYWx0$aGFzaGhhc2hoYXNoaGFzaA")]
    [InlineData("$argon2id$v=19$m=1024,t=1,p=1$c2FsdHNhbHRzYWx0")]
    public void A_malformed_hash_never_verifies_and_never_throws(string malformed)
    {
        Assert.False(FastHasher().Verify(malformed, "whatever password"));
    }

    [Fact]
    public void Hashing_an_empty_or_null_password_is_rejected()
    {
        var hasher = FastHasher();
        Assert.ThrowsAny<ArgumentException>(() => hasher.Hash(string.Empty));
        Assert.ThrowsAny<ArgumentException>(() => hasher.Hash(null!));
    }

    [Fact]
    public void Invalid_cost_options_are_rejected()
    {
        // Argon2 needs at least 8 KiB of memory per lane.
        Assert.Throws<ArgumentOutOfRangeException>(() => FastHasher(memoryKiB: 8, parallelism: 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => FastHasher(iterations: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => FastHasher(parallelism: 0));
    }

    [Fact]
    public void The_production_defaults_hash_and_verify()
    {
        var hasher = new PasswordHasher(new Argon2Options());
        var hash = hasher.Hash("production defaults 1");

        Assert.StartsWith("$argon2id$v=19$m=65536,t=3,p=4$", hash);
        Assert.True(hasher.Verify(hash, "production defaults 1"));
    }
}
