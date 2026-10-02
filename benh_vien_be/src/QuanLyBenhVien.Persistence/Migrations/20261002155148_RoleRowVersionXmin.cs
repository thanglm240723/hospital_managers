using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuanLyBenhVien.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoleRowVersionXmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // xmin là cột hệ thống có sẵn của PostgreSQL: chỉ cập nhật model snapshot, không tạo cột vật lý.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Không có gì để hoàn tác (xmin là cột hệ thống).
        }
    }
}
