namespace CleanArchCqrs.Application.Common.Models;

/// Token gửi cho trình duyệt (cookie) và hash lưu DB. Không bao giờ lưu Token.
public sealed record GeneratedRefreshToken(string Token, string Hash);
