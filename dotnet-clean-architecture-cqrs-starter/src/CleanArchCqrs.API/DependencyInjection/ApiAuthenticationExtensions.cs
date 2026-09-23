using System.Text;
using CleanArchCqrs.API.Errors;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CleanArchCqrs.API.DependencyInjection;

public static class ApiAuthenticationExtensions
{
    /// API validate lại JWT (chữ ký, alg, iss, aud, exp, nbf) làm lớp phòng thủ thứ hai sau Gateway.
    /// Không tra phiên ở đây — Gateway đã làm; API chỉ tin chữ ký.
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services)
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
                        await ProblemResponseWriter.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized,
                            ErrorCodes.Unauthenticated, "Chưa đăng nhập hoặc phiên đã hết hạn.");
                    }
                };
            });
        return services;
    }
}
