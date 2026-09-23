using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace CleanArchCqrs.Infrastructure.Security;

public sealed class PasswordHasher : IPasswordHasher
{
    private readonly Microsoft.AspNetCore.Identity.PasswordHasher<User> _hasher = new();

    private static readonly string DummyHash =
        new Microsoft.AspNetCore.Identity.PasswordHasher<User>().HashPassword(default!, "timing-equalizer-not-a-real-password");

    public string Hash(string password) => _hasher.HashPassword(default!, password);

    public bool Verify(string password, string passwordHash)
    {
        var result = _hasher.VerifyHashedPassword(default!, passwordHash, password);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }

    public void SimulateVerify(string password) => _hasher.VerifyHashedPassword(default!, DummyHash, password);
}
