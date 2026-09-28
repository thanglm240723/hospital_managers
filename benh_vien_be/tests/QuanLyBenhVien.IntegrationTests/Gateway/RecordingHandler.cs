using System.Collections.Concurrent;

namespace QuanLyBenhVien.IntegrationTests.Gateway;

public sealed record RecordedRequest(string Method, string Path, IReadOnlyList<string> HeaderNames);

/// Ghi lại mọi request Gateway gửi xuống API — để chứng minh request bị chặn KHÔNG tới backend.
public sealed class RecordingHandler : DelegatingHandler
{
    private readonly ConcurrentQueue<RecordedRequest> _log;

    public RecordingHandler(HttpMessageHandler inner, ConcurrentQueue<RecordedRequest> log) : base(inner) => _log = log;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        _log.Enqueue(new RecordedRequest(request.Method.Method, request.RequestUri!.AbsolutePath,
            request.Headers.Select(h => h.Key).ToList()));
        return base.SendAsync(request, ct);
    }
}
