using Microsoft.EntityFrameworkCore;
using QuanLyBenhVien.Domain.Catalog.Facilities;
using QuanLyBenhVien.Domain.Exceptions;
using QuanLyBenhVien.IntegrationTests.Helpers;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using QuanLyBenhVien.Persistence.Repositories.Catalog;
using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Persistence;

[Collection(IntegrationCollection.Name)]
public class FacilitiesPersistenceTests
{
    private readonly ContainersFixture _containers;

    public FacilitiesPersistenceTests(ContainersFixture containers) => _containers = containers;

    [Fact]
    public async Task SaveAndReload_Branch_Department_Room()
    {
        var cs = await TestDb.CreateMigratedDatabaseAsync(_containers);
        var branch = Branch.Create("CS1", "Cơ sở 1");
        var dept = Department.Create(branch.Id, "NOI", "Khoa Nội", DepartmentKind.Clinical);
        var room = Room.Create(dept.Id, "P101", "Phòng 101");
        await using (var db = TestDb.Create(cs))
        {
            var repo = new FacilityRepository(db);
            await repo.AddAsync(branch);
            await repo.AddAsync(dept);
            await repo.AddAsync(room);
            await db.SaveChangesAsync();
        }

        await using var read = TestDb.Create(cs);
        var repo2 = new FacilityRepository(read);
        Assert.Equal("Cơ sở 1", (await repo2.GetBranchAsync(branch.Id))!.Name);
        var d = (await repo2.GetDepartmentAsync(dept.Id))!;
        Assert.Equal(DepartmentKind.Clinical, d.Kind);
        Assert.Equal(branch.Id, d.BranchId);
        Assert.Equal(dept.Id, (await repo2.GetRoomAsync(room.Id))!.DepartmentId);
        Assert.Single(await repo2.GetDepartmentsAsync([dept.Id]));
    }

    [Fact]
    public async Task DuplicateDepartmentCode_InSameBranch_Throws_ButOtherBranchOk()
    {
        var cs = await TestDb.CreateMigratedDatabaseAsync(_containers);
        var b1 = Branch.Create("CS1", "Cơ sở 1");
        var b2 = Branch.Create("CS2", "Cơ sở 2");
        await using (var db = TestDb.Create(cs))
        {
            db.Branches.AddRange(b1, b2);
            db.Departments.Add(Department.Create(b1.Id, "NOI", "Nội", DepartmentKind.Clinical));
            await db.SaveChangesAsync();
        }

        await using (var db = TestDb.Create(cs))
        {
            db.Departments.Add(Department.Create(b1.Id, "NOI", "Nội 2", DepartmentKind.Clinical));
            var ex = await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => db.SaveChangesAsync());
            Assert.Equal("IX_Departments_BranchId_Code", ex.ConstraintName);
        }

        await using (var db = TestDb.Create(cs))
        {
            db.Departments.Add(Department.Create(b2.Id, "NOI", "Nội", DepartmentKind.Clinical));
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task RowVersion_Increases_AfterRename()
    {
        var cs = await TestDb.CreateMigratedDatabaseAsync(_containers);
        var branch = Branch.Create("CS1", "Cơ sở 1");
        await using (var db = TestDb.Create(cs))
        {
            db.Branches.Add(branch);
            await db.SaveChangesAsync();
        }

        uint before;
        await using (var db = TestDb.Create(cs))
        {
            var b = await db.Branches.SingleAsync(x => x.Id == branch.Id);
            before = b.RowVersion;
            b.Rename("Cơ sở mới");
            await db.SaveChangesAsync();
            Assert.NotEqual(before, b.RowVersion);
        }

        await using var read = TestDb.Create(cs);
        Assert.NotEqual(before, (await read.Branches.SingleAsync(x => x.Id == branch.Id)).RowVersion);
    }
}
