using QuanLyBenhVien.Application.Features.Users.Common;
using QuanLyBenhVien.Application.Features.Roles.Common;
using QuanLyBenhVien.Application.Features.Permissions.Common;
using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Application.Common.Auditing;
using QuanLyBenhVien.Application.Common.Caching;
using QuanLyBenhVien.Persistence.Caching;
using QuanLyBenhVien.Application.Features.Auth.Common;
using QuanLyBenhVien.Domain.Common;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.Domain.Identity.Sessions;
using QuanLyBenhVien.Persistence.Interceptors;
using QuanLyBenhVien.Persistence.ReadServices.Identity;
using QuanLyBenhVien.Persistence.Repositories.Common;
using QuanLyBenhVien.Persistence.Repositories.Identity;
using QuanLyBenhVien.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace QuanLyBenhVien.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<AuditSaveChangesInterceptor>();

        void ConfigureDb(IServiceProvider sp, DbContextOptionsBuilder options)
        {
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"));
            options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
        }

        services.AddDbContext<AppDbContext>(ConfigureDb);
        // Factory dùng chung DbContextOptions đã đăng ký bởi AddDbContext; truyền ConfigureDb lần nữa sẽ gắn interceptor audit hai lần.
        services.AddDbContextFactory<AppDbContext>(_ => { }, ServiceLifetime.Scoped);

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<QuanLyBenhVien.Domain.Catalog.Facilities.IFacilityRepository, QuanLyBenhVien.Persistence.Repositories.Catalog.FacilityRepository>();
        services.AddScoped<QuanLyBenhVien.Domain.Identity.Staff.IStaffProfileRepository, QuanLyBenhVien.Persistence.Repositories.Identity.StaffProfileRepository>();
        services.AddScoped<QuanLyBenhVien.Application.Common.Authorization.IAccessContext, QuanLyBenhVien.Persistence.Authorization.AccessContext>();
        services.AddScoped<QuanLyBenhVien.Application.Common.Authorization.IResourceAuthorizer, QuanLyBenhVien.Persistence.Authorization.ResourceAuthorizer>();
        services.AddScoped<QuanLyBenhVien.Application.Common.Authorization.IDeniedAccessRecorder, QuanLyBenhVien.Persistence.Authorization.DeniedAccessRecorder>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IAuthReadService, AuthReadService>();
        services.AddScoped<IUserAccessReadService, UserAccessReadService>();
        services.AddScoped<IRolesReadService, RolesReadService>();
        services.AddScoped<QuanLyBenhVien.Application.Features.Facilities.Common.IFacilitiesReadService, QuanLyBenhVien.Persistence.ReadServices.Catalog.FacilitiesReadService>();
        services.AddScoped<QuanLyBenhVien.Application.Features.StaffProfiles.Common.IStaffProfilesReadService, QuanLyBenhVien.Persistence.ReadServices.Identity.StaffProfilesReadService>();
        services.AddScoped<IUsersReadService, UsersReadService>();
        services.AddScoped<IPermissionsReadService, PermissionsReadService>();
        services.AddScoped<IRefreshSessionLookup, RefreshSessionLookup>();
        services.AddScoped<ICacheInvalidationStore, CacheInvalidationStore>();

        services.AddScoped<IdentitySeeder>();
        services.AddScoped<RoleDefaultsApplier>();
        services.Configure<SeedOptions>(configuration.GetSection("Seed"));

        return services;
    }

    public static Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
        => DbInitializer.InitializeAsync(services, ct);
}
