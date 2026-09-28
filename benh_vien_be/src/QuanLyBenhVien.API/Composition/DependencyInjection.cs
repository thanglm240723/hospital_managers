using System.Net;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using QuanLyBenhVien.API.Middleware;
using QuanLyBenhVien.API.Security;
using QuanLyBenhVien.Application;
using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Infrastructure;
using QuanLyBenhVien.Persistence;
using QuanLyBenhVien.Presentation;

namespace QuanLyBenhVien.API.Composition;

/// Composition root: chỉ đăng ký DI, không logic.
public static class DependencyInjection
{
    public static WebApplicationBuilder AddApiServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddApplication();
        builder.Services.AddPersistence(builder.Configuration);
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddPresentation(builder.Configuration);
        builder.Services.AddApiSecurity(builder.Configuration);

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, CurrentUser>();
        builder.Services.AddScoped<IRequestContext, HttpRequestContext>();

        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddProblemDetails();

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

        // Mặc định Minimal API chỉ ném BadHttpRequestException khi bind lỗi (JSON hỏng/thiếu body) ở Development;
        // ở môi trường khác request bị "nuốt" thành 400 rỗng, không qua GlobalExceptionHandler. Bật ở mọi môi trường
        // để luôn ra Problem Details (`code: validation_failed`).
        builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

        // Chỉ tin X-Forwarded-* từ proxy đã khai báo (mặc định: loopback). Production: thêm IP Gateway vào cấu hình.
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
                options.KnownProxies.Add(IPAddress.Parse(proxy));
        });

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "QuanLyBenhVien API",
                Version = "v1",
                Description = "Hệ thống quản lý khám chữa bệnh (HMS)",
            });
        });

        return builder;
    }
}
