using CleanArchCqrs.Gateway.Errors;
using StackExchange.Redis;

namespace CleanArchCqrs.Gateway.Middleware;

/// Chặn dò mật khẩu/spam refresh theo IP trước khi request tới backend. Bộ đếm ở Redis nên dùng được nhiều instance.
public sealed class IpRateLimitMiddleware
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private static readonly (string Path, string Name, int Limit)[] Rules =
    [
        ("/api/v1/auth/login", "login", 10),
        ("/api/v1/auth/refresh", "refresh", 30),
    ];

    private readonly RequestDelegate _next;
    private readonly ILogger<IpRateLimitMiddleware> _logger;

    public IpRateLimitMiddleware(RequestDelegate next, ILogger<IpRateLimitMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IConnectionMultiplexer redis)
    {
        var rule = HttpMethods.IsPost(context.Request.Method)
            ? Rules.FirstOrDefault(r => context.Request.Path.Equals(r.Path, StringComparison.OrdinalIgnoreCase))
            : default;
        if (rule.Path is null)
        {
            await _next(context);
            return;
        }

        var key = $"rl:ip:{rule.Name}:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
        try
        {
            var db = redis.GetDatabase();
            // INCR + EXPIRE(NX) phải nguyên tử: nếu crash xảy ra giữa hai lệnh riêng lẻ, key mất TTL
            // và bị coi như khoá vĩnh viễn (không bao giờ tự hết hạn).
            var transaction = db.CreateTransaction();
            var countTask = transaction.StringIncrementAsync(key);
            _ = transaction.KeyExpireAsync(key, Window, ExpireWhen.HasNoExpiry);
            await transaction.ExecuteAsync();
            var count = await countTask;
            if (count > rule.Limit)
            {
                var retryAfter = await db.KeyTimeToLiveAsync(key) ?? Window;
                context.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                await ProblemResponseWriter.WriteAsync(context, StatusCodes.Status429TooManyRequests, "rate_limited",
                    "Bạn đã gửi quá nhiều yêu cầu. Vui lòng thử lại sau.");
                return;
            }
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            _logger.LogWarning(ex, "IP rate limiter unavailable; allowing request");
        }

        await _next(context);
    }
}
