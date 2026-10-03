using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuanLyBenhVien.Application.Common.Auditing;
using QuanLyBenhVien.Application.Common.Authorization;
using QuanLyBenhVien.Domain.Common;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.IntegrationTests.Helpers;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Authorization;

[Collection(IntegrationCollection.Name)]
public class DeniedAccessRecorderTests
{
    private readonly ContainersFixture _containers;
    public DeniedAccessRecorderTests(ContainersFixture containers) => _containers = containers;

    [Fact]
    public async Task Denied_AuditSurvivesRollback_BusinessChangeDoesNot()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers);
        var roleId = await TestData.QueryAsync(factory, async db =>
        {
            var role = Role.Create("rec-test-" + Guid.NewGuid().ToString("N")[..8], "Vai trò gốc");
            db.Roles.Add(role);
            await db.SaveChangesAsync();
            return role.Id;
        });
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var roles = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
            var recorder = scope.ServiceProvider.GetRequiredService<IDeniedAccessRecorder>();

            await using (var tx = await uow.BeginTransactionAsync())
            {
                var role = await roles.GetForUpdateAsync(roleId);
                role!.Rename("Đổi trong transaction sẽ rollback");
                await uow.SaveChangesAsync();
            }

            await recorder.RecordAsync(AuditActions.ResourceAccessDenied, new ResourceRef("Role", roleId), "test_denied", default);
        }

        var name = await TestData.QueryAsync(factory, db => db.Roles.Where(r => r.Id == roleId).Select(r => r.Name).SingleAsync());
        Assert.NotEqual("Đổi trong transaction sẽ rollback", name);
        Assert.True(await TestData.QueryAsync(factory, db => db.AuditRecords.AnyAsync(a =>
            a.Action == AuditActions.ResourceAccessDenied && a.ResourceId == roleId.ToString() && a.Reason == "test_denied")));
    }

    [Fact]
    public async Task Denied_RecordedInsideTransaction_SurvivesRollback()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers);
        var roleId = await TestData.QueryAsync(factory, async db =>
        {
            var role = Role.Create("rec-in-" + Guid.NewGuid().ToString("N")[..8], "Vai trò gốc");
            db.Roles.Add(role);
            await db.SaveChangesAsync();
            return role.Id;
        });
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var roles = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
            var recorder = scope.ServiceProvider.GetRequiredService<IDeniedAccessRecorder>();

            await using (var tx = await uow.BeginTransactionAsync())
            {
                var role = await roles.GetForUpdateAsync(roleId);
                role!.Rename("Đổi trong transaction sẽ rollback");
                await uow.SaveChangesAsync();
                await recorder.RecordAsync(AuditActions.ResourceAccessDenied, new ResourceRef("Role", roleId), "in_tx_denied", default);
            }
        }

        var name = await TestData.QueryAsync(factory, db => db.Roles.Where(r => r.Id == roleId).Select(r => r.Name).SingleAsync());
        Assert.Equal("Vai trò gốc", name);
        Assert.True(await TestData.QueryAsync(factory, db => db.AuditRecords.AnyAsync(a =>
            a.Action == AuditActions.ResourceAccessDenied && a.ResourceId == roleId.ToString() && a.Reason == "in_tx_denied")));
    }

    [Fact]
    public async Task RecorderFailure_DoesNotThrow()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers);
        await using var scope = factory.Services.CreateAsyncScope();
        var recorder = scope.ServiceProvider.GetRequiredService<IDeniedAccessRecorder>();

        var ex = await Record.ExceptionAsync(() => recorder.RecordAsync(
            AuditActions.ResourceAccessDenied, new ResourceRef(new string('x', 500), Guid.NewGuid()), "r", default));

        Assert.Null(ex);
    }
}
