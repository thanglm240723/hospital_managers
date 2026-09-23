using CleanArchCqrs.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchCqrs.Infrastructure.Persistence;

public static class DbInitializer
{
    /// Production: để MigrateOnStartup = false và chạy migration như một bước deploy riêng.
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        if (configuration.GetValue<bool>("Database:MigrateOnStartup"))
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(ct);

        await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync(ct);
    }
}
