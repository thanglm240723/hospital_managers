using System.Text;
using CleanArchCqrs.Gateway.Errors;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;

namespace CleanArchCqrs.Gateway.Auth;

public static class GatewayAuthExtensions
{
    private const string DependencyUnavailableItem = "gateway.auth.dependency-unavailable";

    private const int MinKeyLength = 32;

    public static IServiceCollection AddGatewayAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        // ValidateOnStart: khoá/URL rỗng trước đây chỉ lộ ra ở request đầu tiên (500 hoặc lỗi kết nối).
        // Thất bại ngay khi Gateway khởi động an toàn hơn để lọt ra production.
        services.AddOptions<GatewayJwtOptions>()
            .Bind(configuration.GetSection("Jwt"))
            .Validate(o => !string.IsNullOrWhiteSpace(o.SigningKey) && o.SigningKey.Length >= MinKeyLength,
                $"Jwt:SigningKey must be at least {MinKeyLength} characters.")
            .ValidateOnStart();
        services.AddOptions<IdentityServiceOptions>()
            .Bind(configuration.GetSection("Identity"))
            .Validate(o => !string.IsNullOrWhiteSpace(o.InternalBaseUrl) && Uri.IsWellFormedUriString(o.InternalBaseUrl, UriKind.Absolute),
                "Identity:InternalBaseUrl must be an absolute URL.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.InternalApiKey), "Identity:InternalApiKey must not be empty.")
            .ValidateOnStart();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(configuration.GetConnectionString("Redis") ?? "localhost:6379");
            options.AbortOnConnectFail = false;
            options.ConnectTimeout = 2000;
            options.SyncTimeout = 1000;
            options.AsyncTimeout = 1000;
            return ConnectionMultiplexer.Connect(options);
        });
        services.AddHttpClient(SessionValidator.HttpClientName, (sp, client) =>
        {
            var identity = sp.GetRequiredService<IOptions<IdentityServiceOptions>>().Value;
            client.BaseAddress = new Uri(identity.InternalBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(2);
            client.DefaultRequestHeaders.Add("X-Internal-Key", identity.InternalApiKey);
        });
        services.AddSingleton<ISessionValidator, SessionValidator>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<GatewayJwtOptions>>((options, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "sub",
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = ValidateSessionAsync,
                    OnChallenge = WriteChallengeAsync,
                };
            });

        // Mọi route mặc định phải xác thực; route public khai báo AuthorizationPolicy = "anonymous" trong YARP.
        services.AddAuthorization(options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        return services;
    }

    private static async Task ValidateSessionAsync(TokenValidatedContext context)
    {
        var principal = context.Principal!;
        if (!Guid.TryParse(principal.FindFirst("fid")?.Value, out var familyId)
            || !int.TryParse(principal.FindFirst("sv")?.Value, out var securityVersion))
        {
            context.Fail("Token is missing session claims.");
            return;
        }

        var result = await context.HttpContext.RequestServices.GetRequiredService<ISessionValidator>()
            .ValidateAsync(familyId, securityVersion, context.HttpContext.RequestAborted);
        if (result == SessionCheck.Valid) return;

        if (result == SessionCheck.Unavailable)
            context.HttpContext.Items[DependencyUnavailableItem] = true;
        context.Fail("Session is no longer valid.");
    }

    private static async Task WriteChallengeAsync(JwtBearerChallengeContext context)
    {
        context.HandleResponse();
        if (context.HttpContext.Items.ContainsKey(DependencyUnavailableItem))
            await ProblemResponseWriter.WriteAsync(context.HttpContext, StatusCodes.Status503ServiceUnavailable,
                "dependency_unavailable", "Dịch vụ xác thực tạm thời không khả dụng.");
        else
            await ProblemResponseWriter.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized,
                "unauthenticated", "Chưa đăng nhập hoặc phiên đã hết hạn.");
    }
}
