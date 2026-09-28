using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using QuanLyBenhVien.Application.Features.Auth.Common;
using Microsoft.Extensions.Options;

namespace QuanLyBenhVien.Infrastructure.Security;

public sealed class CsrfTokenService : ICsrfTokenService
{
    private const int MinKeyLength = 32;
    private readonly byte[] _key;

    public CsrfTokenService(IOptions<AuthOptions> options)
    {
        var key = options.Value.CsrfKey;
        if (string.IsNullOrEmpty(key) || key.Length < MinKeyLength)
            throw new InvalidOperationException($"Auth:CsrfKey must be at least {MinKeyLength} characters.");
        _key = Encoding.UTF8.GetBytes(key);
    }

    public string Create(Guid sessionFamilyId) => Base64Url.EncodeToString(Compute(sessionFamilyId));

    public bool IsValid(Guid sessionFamilyId, string? token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        byte[] provided;
        try
        {
            provided = Base64Url.DecodeFromChars(token);
        }
        catch (FormatException)
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(provided, Compute(sessionFamilyId));
    }

    private byte[] Compute(Guid sessionFamilyId) => HMACSHA256.HashData(_key, sessionFamilyId.ToByteArray());
}
