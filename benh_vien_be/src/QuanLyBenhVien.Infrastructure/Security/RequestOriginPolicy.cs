using Microsoft.Extensions.Options;
using QuanLyBenhVien.Application.Features.Auth.Common;

namespace QuanLyBenhVien.Infrastructure.Security;

/// So khớp chính xác (không phân biệt hoa thường) với `Auth:AllowedOrigins`; thiếu Origin ⇒ từ chối.
public sealed class RequestOriginPolicy(IOptions<AuthOptions> options) : IRequestOriginPolicy
{
    public bool IsAllowed(string? origin)
        => !string.IsNullOrWhiteSpace(origin)
           && options.Value.AllowedOrigins.Any(allowed => string.Equals(allowed, origin, StringComparison.OrdinalIgnoreCase));
}
