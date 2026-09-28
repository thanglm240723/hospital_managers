using Carter;
using QuanLyBenhVien.API.Composition;
using QuanLyBenhVien.API.Middleware;
using QuanLyBenhVien.API.Security;
using QuanLyBenhVien.Persistence;
using Serilog;

namespace QuanLyBenhVien.API;

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

        builder.AddApiServices();

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "QuanLyBenhVien API v1");
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

        app.UseAuthentication();
        app.UseMiddleware<UserLogContextMiddleware>();
        app.UseAuthorization();

        app.MapCarter();
        app.MapHealthChecks("/health").AllowAnonymous();

        await app.Services.InitializeDatabaseAsync();

        await app.RunAsync();
    }
}
