using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using QuanLyBenhVien.Infrastructure.Security;
using QuanLyBenhVien.Presentation.Http;

namespace QuanLyBenhVien.API.Security;

public static class JwtAuthenticationSetup
{
    /// API validate lại JWT (chữ ký, alg, iss, aud, exp, nbf) làm lớp phòng thủ thứ hai sau Gateway.
    /// Không tra phiên ở đây — Gateway đã làm; API chỉ tin chữ ký.
    public static IServiceCollection AddApiSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((options, jwtOptions) =>
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
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        var result = ProblemResponses.Create(context.HttpContext, StatusCodes.Status401Unauthorized,
                            "unauthenticated", "Chưa đăng nhập hoặc phiên đã hết hạn.");
                        await result.ExecuteAsync(context.HttpContext);
                    },
                    OnForbidden = async context =>
                    {
                        var result = ProblemResponses.Create(context.HttpContext, StatusCodes.Status403Forbidden,
                            "forbidden", "Không đủ quyền.");
                        await result.ExecuteAsync(context.HttpContext);
                    },
                };
            });

        // Deny-by-default: mọi endpoint mặc định cần đăng nhập trừ khi tự khai .AllowAnonymous().
        services.AddAuthorization(options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }
}
