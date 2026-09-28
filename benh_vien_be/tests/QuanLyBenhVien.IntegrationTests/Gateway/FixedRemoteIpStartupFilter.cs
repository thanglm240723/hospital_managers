using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace QuanLyBenhVien.IntegrationTests.Gateway;

/// TestServer không có IP client. Mỗi factory một IP riêng để bộ đếm rate limit IP (Redis dùng chung) không lẫn nhau.
public sealed class FixedRemoteIpStartupFilter : IStartupFilter
{
    private readonly IPAddress _ip;

    public FixedRemoteIpStartupFilter(IPAddress ip) => _ip = ip;

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            context.Connection.RemoteIpAddress = _ip;
            await nextMiddleware();
        });
        next(app);
    };
}
