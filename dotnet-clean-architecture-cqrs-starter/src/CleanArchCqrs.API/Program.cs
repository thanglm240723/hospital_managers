using System.Net;
using CleanArchCqrs.API.Auth;
using CleanArchCqrs.API.DependencyInjection;
using CleanArchCqrs.API.Errors;
using CleanArchCqrs.API.Logging;
using CleanArchCqrs.API.Middleware;
using CleanArchCqrs.API.Services;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.DependencyInjection;
using CleanArchCqrs.Infrastructure.DependencyInjection;
using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Serilog;

namespace CleanArchCqrs.API;

/// <summary>
/// Application startup - wires up all layers, middleware, and Swagger.
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Host.UseSerilog((context, services, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Destructure.With<SensitiveDataDestructuringPolicy>());

        builder.Services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
                options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            });
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddProblemDetails();
        builder.Services.Configure<ApiBehaviorOptions>(options =>
            options.InvalidModelStateResponseFactory = context =>
                ProblemResponseWriter.ToResult(context.HttpContext, 400, ErrorCodes.ValidationFailed, "Dữ liệu không hợp lệ.",
                    context.ModelState.Where(e => e.Value?.Errors.Count > 0)
                        .ToDictionary(e => e.Key, e => e.Value!.Errors.Select(x => x.ErrorMessage).ToArray())));

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
                Title = "Clean Architecture CQRS Starter",
                Version = "v1",
                Description = "Starter template for Clean Architecture with CQRS and MediatR in ASP.NET Core 10"
            });
        });

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, CurrentUser>();
        builder.Services.AddScoped<IRequestContext, HttpRequestContext>();
        builder.Services.AddScoped<AuthCookieWriter>();
        builder.Services.AddApiAuthentication();

        builder.Services.AddApplicationServices();
        builder.Services.AddInfrastructureServices(builder.Configuration);

        var app = builder.Build();
        await DbInitializer.InitializeAsync(app.Services);

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "CleanArchCqrs API v1");
                c.RoutePrefix = string.Empty; // Swagger at root
            });
        }

        app.UseForwardedHeaders();
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseExceptionHandler();
        app.UseSerilogRequestLogging(options => options.EnrichDiagnosticContext = (diagnostics, http) =>
        {
            if (http.User.Identity?.IsAuthenticated != true) return;
            diagnostics.Set("UserId", http.User.FindFirst("sub")?.Value);
            diagnostics.Set("SessionFamilyId", http.User.FindFirst("fid")?.Value);
        });

        app.UseHttpsRedirection();
        app.UseAuthentication();
        app.UseMiddleware<UserLogContextMiddleware>();
        app.UseAuthorization();
        app.MapControllers();
        app.MapHealthChecks("/health");

        await app.RunAsync();
    }
}
