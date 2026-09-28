namespace QuanLyBenhVien.IntegrationTests.Helpers;

// CacheInvalidationWorker chưa được port sang khung mới (chờ slice refresh-logout, xem
// Infrastructure.csproj <Compile Remove>) — RemoveCacheInvalidationWorker tạm bỏ theo Q2.
// Test dùng helper này (Caching/RedisServicesTests.cs) đang bị loại tạm ở IntegrationTests.csproj.
public static class TestServices
{
}
